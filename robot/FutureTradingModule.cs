using System;
using OptionBot.Xaml;

namespace OptionBot.robot {
    public class FutureTradingModule : ViewModelBase {
        static readonly Logger _log = new Logger(nameof(FutureTradingModule));
        public FuturesInfo Future {get;}
        readonly StrategyWrapper<HedgeStrategy> _hedgeStrategy;
        int _numCalcModules, _numActiveOptionStrategies;
        bool _isDeltaHedged;

        Scheduler Scheduler => Future.Controller.Scheduler;
        Connector Connector {get {return Future.Controller.Connector;}}
        Robot Robot {get {return Future.Controller.Robot;}}

        public bool DeltaHedgeTradingAllowed => Future.IsActive && Scheduler.RobotPeriod != RobotPeriodType.Pause;
        public StrategyWrapper<HedgeStrategy> HedgeStrategy => _hedgeStrategy;

        public StrategyState ModuleState => _hedgeStrategy.State;

        public int NumActiveOptionStrategies => _numActiveOptionStrategies;
        public bool IsDeltaHedged {get {return _isDeltaHedged;} private set {SetField(ref _isDeltaHedged, value);}}

        public string HedgeStateStr { get {
            var state = HedgeStrategy.State;
            var stateStr = (string)EnumToStringConverter.Instance.Convert(state);
            return state != StrategyState.Active || DeltaHedgeTradingAllowed ? stateStr : "расчет";
        }}

        public event Action<FutureTradingModule> ModuleStateChanged;
        public event Action DeltaHedgedStateChanged;

        // Опционные стратегии и стратегия дельта-хеджа работают в параллельных потоках
        // Это событие служит для синхронизации между ними
        // Сразу после изменения позиции по любому из опционов необходимо последовательно выполнить следующее
        // 1) Пересчитать параметры vega/gamma лимитов с учетом новых позиций
        // 2) Запустить главный модуль расчета у каждого опциона для учета пересчитанных параметров на шаге 1
        // Это событие уведомляет опционные стратегии о том, что шаг 1 завершен и можно приступать к шагу 2
        // Событие выполняется в потоке фьючерса
        public event Action ParamsUpdatedOnPositionChange;

        // событие уведомляет стратегию дельта-хеджирования о том, что изменилась позиция по любому из дочерних опционов
        // это позволяет стратегии дельта-хеджирования вызвать событие ParamsUpdatedOnPositionChange когда необходимо
        public event Action<OptionInfo> ChildOptionStrategyPositionChange;

        public FutureTradingModule(FuturesInfo future) {
            Future = future;

            _hedgeStrategy = new StrategyWrapper<HedgeStrategy>(Robot, Future, w => new HedgeStrategy(w));
            _hedgeStrategy.StateChanged += HedgeStrategyOnStateChanged;

            Robot.RegisterModule(this);
        }

        protected override void DisposeManaged() {
            if(!HedgeStrategy.State.Inactive())
                _log.Dbg.AddErrorLog("Module {0}: disposing in state {1}", Future.Code, HedgeStrategy.State);

            HedgeStrategy.Dispose();

            base.DisposeManaged();
        }

        void HedgeStrategyOnStateChanged(StrategyWrapper wrapper) {
            Robot.RobotThread.ExecuteAsync(() => UpdateState(_numCalcModules, _numActiveOptionStrategies));
        }

        public void UpdateState(int numCalcModules, int numActiveStrategies) {
            _log.Dbg.AddDebugLog($"UpdateState({numCalcModules}, {numActiveStrategies}): state={HedgeStrategy.State}, futActive={Future.IsFutureActive}");

            if(_numActiveOptionStrategies == 0 && numActiveStrategies != 0)
                Future.TimeValuationRun = Connector.GetMarketTime();

            _numCalcModules = numCalcModules;
            _numActiveOptionStrategies = numActiveStrategies;

            if(HedgeStrategy.State == StrategyState.Inactive) {
                if(Future.IsFutureActive && (_numCalcModules > 0 || _numActiveOptionStrategies > 0)) {
                    _log.AddInfoLog("Запуск хеджирующей стратегии на фьючерсе {0}...", Future.Code);
                    HedgeStrategy.Start();
                }
            } else if(HedgeStrategy.State == StrategyState.Failed) {
                _log.AddErrorLog("Хеджирующая стратегия завершилась с ошибкой.");
                Future.ForceDeactivateFuture();
                HedgeStrategy.Reset();
            } else if(HedgeStrategy.State.CanStop()) {
                if(Future.IsFutureActive) {
                    if(!(_numCalcModules > 0 || _numActiveOptionStrategies > 0)) {
                        _log.AddInfoLog("Хеджирующая стратегия на фьючерсе {0} будет остановлена, так как нет ни одного активного расчетного модуля для этого фьючерса", Future.Code);
                        HedgeStrategy.Stop(!Robot.Controller.RobotData.IsConnected);
                    }
                } else {
                    if(!(_numActiveOptionStrategies > 0)) {
                        _log.AddInfoLog("Хеджирующая стратегия на фьючерсе {0} будет остановлена", Future.Code);
                        HedgeStrategy.Stop(!Robot.Controller.RobotData.IsConnected);
                    } else {
                        var msgStart = "Нельзя в данный момент остановить модуль хеджирования";
                        if(LogHelper.CanLogMessage(msgStart))
                            _log.AddWarningLog($"{msgStart}, так как есть {_numActiveOptionStrategies} неостановленных стратегий.");
                    }
                }
            }

            OnPropertyChanged(() => HedgeStateStr);
            ModuleStateChanged?.Invoke(this);
        }

        public void SetDeltaHedgeStatus(bool isDeltaHedged) {
            if(IsDeltaHedged == isDeltaHedged)
                return;

            IsDeltaHedged = isDeltaHedged;
            DeltaHedgedStateChanged?.Invoke();

            Robot.RecheckStrategiesState($"delta hedge status changed ({isDeltaHedged})");
        }

        public void RaiseParamsUpdatedOnPositionChange() {
            ParamsUpdatedOnPositionChange?.Invoke();
        }

        public void HandleChildOptionStrategyPositionChange(OptionInfo option) {
            ChildOptionStrategyPositionChange?.Invoke(option);
        }
    }
}
