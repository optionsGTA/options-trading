using System;
using System.Collections.Generic;
using System.Linq;
using Ecng.Common;
using OptionBot.Config;
using OptionBot.robot;
using StockSharp.Messages;

namespace OptionBot {
    public class VMStrategy : ViewModelBase, IRobotDataUpdater {
        static readonly Logger _log = new Logger();
        readonly Controller _controller;
        readonly OptionStrikeShift _shift;
        readonly RobotLogger.StrategyLogger _strategyLogger;
        bool _isActive;
        bool _isDeactivating;
        bool _isRemoving;
        readonly ICfgPairStrategy _config;
        string _id;

        public string Id => _id ?? (_id = "{0}_{1}_{2}_{3}".Put(StrategyType, CfgStrategy.OptionType, CfgStrategy.AtmStrikeShift, CfgStrategy.SeriesId));

        Robot Robot => _controller.Robot;
        RobotData RobotData => _controller.RobotData;
        ConfigProvider ConfigProvider => _controller.ConfigProvider;

        public string FutureCode            => CfgStrategy.SeriesId.FutureCode;
        public OptionStrikeShift Shift      => _shift;
        public OptionInfo Option            => Shift.Option;
        public StrategyType StrategyType    => Config.Effective.StrategyType;
        public OptionTypes OptionType       => Config.Effective.OptionType;
        public StrategyState State          => _shift.Option.Return(o => o.TradingModule.StrategyByType(StrategyType).State, StrategyState.Inactive);
        public ICfgPairVP ConfigVP          => Shift.VP;
        public ICfgPairStrategy Config      => _config;
        public IConfigStrategy CfgStrategy  => _config.Effective;

        public CalculatedStrategyParams CalcParams {get;}

        public bool IsActive { get { return _isActive && !_isDeactivating; }
            set {
                if(SetActive(value))
                    FlagChangedByUser?.Invoke(this, nameof(IsActive));
            }
        }

        public static event Action<VMStrategy, string> FlagChangedByUser;

        static readonly HashSet<string> _flagNames = new HashSet<string> {
            nameof(IsActive), // must be first, see ConfigOnUiEffectiveDifferent
            nameof(IConfigStrategy.IlliquidTrading),
            nameof(IConfigStrategy.IlliquidCurveTrading),
            nameof(IConfigStrategy.CloseRegime),
            nameof(IConfigStrategy.AutoStartStop),
            nameof(IConfigStrategy.CheckOrderIv),
            nameof(IConfigStrategy.AutoObligationsVolume),
            nameof(IConfigStrategy.AutoObligationsSpread),
            nameof(IConfigStrategy.CurveOrdering),
            nameof(IConfigStrategy.CurveControl),
            nameof(IConfigStrategy.MarketControl),
        };

        public VMStrategy(Controller controller, ICfgPairStrategy config) {
            if(config == null)
                throw new ArgumentNullException(nameof(config));

            _controller = controller;
            _config = config;
            CalcParams = new CalculatedStrategyParams();
            _shift = OptionStrikeShift.GetStrikeShift(_controller, CfgStrategy.SeriesId, CfgStrategy.OptionType, CfgStrategy.AtmStrikeShift);
            _shift.ShiftUpdated += ShiftOnShiftUpdated;
            _shift.RegisterStrategy(this);
            ConfigProvider.ConfigSecuritySelection.EffectiveConfigChanged += ConfigSecuritySelectionOnEffectiveConfigChanged;
            _config.EffectiveConfigChanged += ConfigOnEffectiveConfigChanged;
            _config.UIEffectiveDifferent += ConfigOnUiEffectiveDifferent;
            _config.CanUpdateConfig += ConfigOnCanUpdateConfig;

            Robot.RobotStateChanged += OnStateChanged;

            _strategyLogger = _controller.RobotLogger.Strategy(StrategyType);
            _strategyLogger.LogStrategy(this);

            RobotData.RegisterUpdater(this);
        }

        public VMStrategy(Controller controller, StrategyType straType, OptionSeriesId seriesId, OptionTypes otype, int atmShift) :
                            this(controller, controller.ConfigProvider.CreateNewStrategyConfig(straType, seriesId, otype, atmShift)) {}

        protected override void DisposeManaged() {
            _shift.DeregisterStrategy(this);
            _shift.ShiftUpdated -= ShiftOnShiftUpdated;
            Robot.RobotStateChanged -= OnStateChanged;
            ConfigProvider.ConfigSecuritySelection.EffectiveConfigChanged -= ConfigSecuritySelectionOnEffectiveConfigChanged;
            _config.EffectiveConfigChanged -= ConfigOnEffectiveConfigChanged;
            _config.UIEffectiveDifferent -= ConfigOnUiEffectiveDifferent;
            _config.CanUpdateConfig -= ConfigOnCanUpdateConfig;

            RobotData.DeregisterUpdater(this);

            base.DisposeManaged();
        }

        void ConfigOnEffectiveConfigChanged(ICfgPairStrategy pair, string[] names) {
            _strategyLogger.LogStrategy(this);

            if(names.Length == 1 && _flagNames.Contains(names[0]))
                FlagChangedByUser.SafeInvoke(this, names[0]);

            Robot.ForceRecalculate();
        }

        void ConfigOnCanUpdateConfig(IConfigStrategy cfg, CanUpdateEventArgs args) {
            if(args.Names.Length == 1 && _flagNames.Contains(args.Names[0]))
                args.AllowInstantUpdate = true;
        }

        void ConfigOnUiEffectiveDifferent(ICfgPairStrategy pair) {
            RobotData.Dispatcher.MyGuiAsync(() => {
                Util.CopyProperties(pair.Effective, pair.UI, _flagNames.Skip(1).ToArray()); // Skip(1) for IsActive (not cfg property)
            }, true);
        }

        void ConfigSecuritySelectionOnEffectiveConfigChanged(ICfgPairSecSel cfgPairSecSel, string[] strings) {
            OnDependencyChange();
        }

        void ShiftOnShiftUpdated() {
            OnDependencyChange();
        }

        void OnDependencyChange() {
            OnPropertyChanged(() => Option);
            OnPropertyChanged(() => ConfigVP);
            OnStateChanged();
        }

        void OnStateChanged(int numRunning = 0) {
            Config.RealtimeUpdate = State.Inactive();
            OnPropertyChanged(() => State);

            _strategyLogger.LogStrategy(this);
        }

        public bool TryBeginRemove() {
            var state = State;
            if(!state.Inactive()) {
                _log.AddErrorLog("Стратегию в состоянии {0} нельзя удалять.", state);
                return false;
            }

            if(IsActive) {
                _log.AddErrorLog("Перед удалением стратегию необходимо деактивировать.");
                return false;
            }

            _isRemoving = true;
            return true;
        }

        public void ForceDeactivateStrategy() {
            _isDeactivating = true;
            RobotData.Dispatcher.MyGuiAsync(() => {
                try {
                    if(!_isActive) {
                        _log.Dbg.AddWarningLog($"ForceDeactivateStrategy({Id}): already inactive");
                        return;
                    }

                    _log.AddWarningLog($"ForceDeactivateStrategy({Id}): стратегия будет деактивирована");

                    SetActive(false);
                } finally {
                    _isDeactivating = false;
                }
            });
        }

        public bool SetActive(bool isActive) {
            if(isActive && _isRemoving) {
                var msg = "{0}: unable to activate strategy which is being removed.".Put(Id);
                _log.Dbg.AddErrorLog(msg);
                throw new InvalidOperationException(msg);
            }

            // ReSharper disable once ExplicitCallerInfoArgument
            if(!SetField(ref _isActive, isActive, nameof(IsActive)))
                return false;

            OnDependencyChange();
            Robot.RecheckStrategiesState("VMStrategy.IsActive", true);
            _strategyLogger.LogStrategy(this);

            return true;
        }

        public bool CheckStartConfig(List<string> errors) {
            if(RobotData.ActivePortfolio == null)
                errors.Add("портфель не выбран");

            CfgStrategy.VerifyConfig(errors);

            return !errors.Any();
        }

        public void UpdateData() {
            CalcParams.UpdateData();
        }
    }
}
