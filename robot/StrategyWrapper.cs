using System;
using AsyncHandler;
using Ecng.Common;
using StockSharp.Algo;
using StockSharp.Algo.Strategies;

namespace OptionBot.robot {
    abstract public class StrategyWrapper : ViewModelBase {
        StrategyState _state;
        readonly SecurityInfo _securityInfo;

        public StrategyState State {get {return _state;} protected set {SetField(ref _state, value);}}
        public SecurityInfo SecurityInfo {get {return _securityInfo;}}
        public Robot Robot {get; protected set;}
        protected Controller Controller {get {return Robot.Controller;}}
        protected HandlerThread RobotThread {get {return Robot.RobotThread;}}

        abstract public string TypeName {get;}

        abstract public event Action<StrategyWrapper> StateChanged;

        protected StrategyWrapper(SecurityInfo secInfo) {
            _securityInfo = secInfo;
        }

        abstract public void Start();
        abstract public void Stop(bool force);
        abstract public void Reset();
    }

    /// <summary>Класс-обертка для торговой стратегии, поддерживающий автоматическое создание/удаление стратегий и обработку ошибок.</summary>
    public class StrategyWrapper<T> : StrategyWrapper where T:BaseStrategy {
        static readonly string _typeName = typeof(StrategyWrapper<T>).ToGenericTypeString();
        static readonly Logger _log = new Logger("Wrapper<{0}>".Put(typeof(T).Name));
        static readonly TimeSpan ForceStopDelay = TimeSpan.FromSeconds(5);
        readonly string _logName;

        T _strategy;

        readonly Func<StrategyWrapper<T>, T> _createStrategy;
        readonly Action<T> _startStrategy;

        public override string TypeName {get {return _typeName;}}

        public bool IsStopping { get {
            var state = State;
            var strategy = _strategy;
            return state == StrategyState.Stopping || (state == StrategyState.Active && strategy.Return(s => s.IsStopping, false));
        }}

        public override event Action<StrategyWrapper> StateChanged; 

        public StrategyWrapper(Robot robot, SecurityInfo securityInfo, Func<StrategyWrapper<T>, T> createStrategy, Action<T> startStrategy = null) : base(securityInfo) {
            State = StrategyState.Inactive;
            Robot = robot;
            _createStrategy = createStrategy;
            _startStrategy = startStrategy ?? (s => Robot.GetSecurityStrategy(s.Security.Id).StartChild(s));
            _logName = "{0}-{1}".Put(typeof(T).Name, securityInfo.Code);
        }

        public override void Start() {
            RobotThread.CheckThread();

            if(!State.Inactive()) {
                _log.AddWarningLog("Start({0}): нельзя стартовать из состояния {1}", _logName, State);
                return;
            }

            _log.Dbg.AddInfoLog("Start({0})", _logName);

            try {
                Reset();
                _strategy = _createStrategy(this);
                SubscribeStrategy();
                State = StrategyState.Starting;

                _startStrategy(_strategy);

            } catch(Exception e) {
                _log.AddErrorLog("{0}: Ошибка при попытке старта стратегии: {1}", _logName, e);
                if(_strategy.Return(s => s.ProcessState, ProcessStates.Stopped) == ProcessStates.Stopped) {
                    DisposeStrategy();
                    State = StrategyState.Inactive;
                }

                throw;
            }

            StateChanged.SafeInvoke(this);
        }

        HTCancellationToken _stopTimeoutToken;

        public override void Stop(bool force) {
            RobotThread.CheckThread();

            if(_strategy == null) {
                _log.Dbg.AddWarningLog("Stop({0}): strategy is null. nothing to stop", _logName);
                return;
            }

            _log.Dbg.AddInfoLog("Stop({0}): force={1} WrapperState={2}, ProcessState={3}, IsStopping={4}, WasStarted={5}", _logName, force, State, _strategy.ProcessState, _strategy.IsStopping, _strategy.WasStarted);

            State = StrategyState.Stopping;
            Util.CancelDelayedAction(ref _stopTimeoutToken);

            if(_strategy.ProcessState == ProcessStates.Stopped && !_strategy.WasStarted) {
                _log.Dbg.AddErrorLog("Stop({0}): strategy was never started. Setting state to stopped", _logName); // shoulnd't happen
                State = StrategyState.Inactive;
            } else {
                _strategy.Stop("wrapper({0})".Put(force), force);

                if(!force)
                    _stopTimeoutToken = RobotThread.DelayedAction(() => Stop(true), ForceStopDelay);
            }
        }

        public override void Reset() {
            RobotThread.CheckThread();

            var s = State;
            if(!s.Inactive())
                throw new InvalidOperationException("Не удалось сбросить состояние стратегии. Неожиданное состояние: {0}".Put(s));

            DisposeStrategy();
            Util.CancelDelayedAction(ref _stopTimeoutToken);
            State = StrategyState.Inactive;
        }

        public void StartChild(Strategy child) {
            RobotThread.CheckThread();

            _log.Dbg.AddInfoLog("{0}: StartChild({1})", _logName, child.GetType().Name);

            var parent = _strategy;

            SecurityInfo.SecProcessor.Post(() => {
                if(parent == null)
                    throw new InvalidOperationException("Невозможно стартовать дочернюю стратегию пока не запущена базовая (_strategy == null)");

                if(parent != _strategy)
                    throw new InvalidOperationException("Невозможно стартовать дочернюю стратегию: родительская стратегия изменилась за время старта");

                var s = State;
                if(s != StrategyState.Active && s != StrategyState.Starting)
                    throw new InvalidOperationException("Невозможно стартовать дочернюю стратегию в состоянии {0}".Put(s));

                if(parent.IsStopping || parent.ProcessState != ProcessStates.Started)
                    throw new InvalidOperationException("Невозможно стартовать дочернюю стратегию. state={0}, stopping={1}.".Put(parent.ProcessState, parent.IsStopping));

                _log.Dbg.AddInfoLog("{0}: starting child {1}...", _logName, child.GetType().Name);
                parent.ChildStrategies.Add(child);
            }, "start child 2");
        }

        void StrategyOnProcessStateChanged(Strategy strategy) {
            if(!RobotThread.InThread()) {
                RobotThread.ExecuteAsync(() => StrategyOnProcessStateChanged(strategy));
                return;
            }

            _log.Dbg.AddInfoLog("{0} ({1}): state {2}", _logName, strategy.Name, strategy.ProcessState);

            if(strategy != _strategy) {
                _log.Dbg.AddErrorLog("Wrong strategy. Current strategy is {0}", _strategy == null ? "null" : _strategy.Name);
                return;
            }

            StrategyState newState;
            switch(strategy.ProcessState) {
                case ProcessStates.Started:
                    newState = StrategyState.Active;
                    break;
                case ProcessStates.Stopping:
                    newState = StrategyState.Stopping;
                    break;
                default:
                    Util.CancelDelayedAction(ref _stopTimeoutToken);
                    var bs = (BaseStrategy)strategy;
                    newState = bs.StrategyFailed ? StrategyState.Failed : StrategyState.Inactive;
                    DisposeStrategy();
                    break;
            }

            if(State == newState) {
                _log.Dbg.AddWarningLog("already in {0}", newState);
                return;
            }

            State = newState;

            StateChanged.SafeInvoke(this);
        }

        #region subscription

        bool _strategySubscribed;

        void SubscribeStrategy() {
            if(_strategySubscribed) return;
            _strategySubscribed = true;

            _strategy.ProcessStateChanged += StrategyOnProcessStateChanged;
        }

        void UnsubscribeStrategy() {
            if(!_strategySubscribed) return;
            _strategySubscribed = false;
            
            _strategy.ProcessStateChanged -= StrategyOnProcessStateChanged;
        }

        #endregion

        void DisposeStrategy() {
            if(_strategy == null) { _log.Dbg.AddWarningLog("DisposeStrategy: nothing to dispose of"); return; }
            if(_strategy.ProcessState != ProcessStates.Stopped) {
                _log.Dbg.AddErrorLog($"Стратегия не была должным образом остановлена. state={_strategy.ProcessState}. Пробую остановить...");
                _strategy.Stop("disposing", true);
            }

            _log.Dbg.AddDebugLog("DisposeStrategy()");
            UnsubscribeStrategy();
            _strategy.Dispose();
            _strategy = null;
            _strategySubscribed = false;
        }

        protected override void DisposeManaged() {
            RobotThread.CheckThread();

            Util.CancelDelayedAction(ref _stopTimeoutToken);
            try {
                DisposeStrategy();
            } catch(Exception exception) {
                _log.AddErrorLog("{0}: Dispose strategy: {1}", _logName, exception);
            }
            base.DisposeManaged();
        }
    }
}
