using System;
using System.Collections.Generic;
using System.Linq;
using AsyncHandler;
using Ecng.Common;
using MoreLinq;
using OptionBot.Config;
using StockSharp.Algo.Strategies;

namespace OptionBot.robot {
    public class OptionTradingModule : Disposable {
        static readonly Logger _log = new Logger();

        readonly Tuple<StrategyType, StrategyWrapper>[] _optionStrategies;
        readonly OptionInfo _option;
        bool _stopping, _stopForce;

        public bool Stopping {get {return _stopping;}}
        public OptionInfo Option {get {return _option;}}
        Robot Robot {get {return _option.Controller.Robot;}}
        Controller Controller {get {return Robot.Controller;}}
        HandlerThread RobotThread {get {return Robot.RobotThread;}}

        public StrategyState ModuleState {get {return _mainStrategy.State;}}

        readonly StrategyWrapper<OptionMainStrategy> _mainStrategy;
        readonly StrategyWrapper<RegularStrategy> _regularStrategy;
        readonly StrategyWrapper<MMStrategy> _mmStrategy;
        readonly StrategyWrapper<VegaHedgeStrategy> _vegaHedgeStrategy;
        readonly StrategyWrapper<GammaHedgeStrategy> _gammaHedgeStrategy;

        public StrategyWrapper Regular {get {return _regularStrategy;}}
        public StrategyWrapper MM {get {return _mmStrategy;}}
        public StrategyWrapper VegaHedge {get {return _vegaHedgeStrategy;}}
        public StrategyWrapper GammaHedge {get {return _gammaHedgeStrategy;}}

        public IEnumerable<Tuple<StrategyType, StrategyWrapper>> OptionStrategies {get {return _optionStrategies;}}

        public int NumActiveChildren {get {return OptionStrategies.Count(t => !t.Item2.State.Inactive());}}

        public bool CanStartOptionStrategies {get {return _mainStrategy.State == StrategyState.Active && !_mainStrategy.IsStopping;}}
        public string ActiveStrategiesString {get {return string.Join(",", _optionStrategies.Where(t => !t.Item2.State.Inactive()).Select(t => t.Item1)); }}

        public event Action<OptionTradingModule> ModuleStateChanged;

        public OptionTradingModule(OptionInfo option) {
            _option = option;

            _mainStrategy = new StrategyWrapper<OptionMainStrategy>(Robot, Option, w => new OptionMainStrategy(w));

            var startChild = new Action<Strategy>(s => {
                var ms = ModuleState;
                if(ms != StrategyState.Active && ms != StrategyState.Starting) {
                    _log.Dbg.AddErrorLog("Unable to start strategy while module is inactive({0})", ms);
                    return;
                }
                _mainStrategy.StartChild(s);
            });

            _regularStrategy = new StrategyWrapper<RegularStrategy>(Robot, Option, w => new RegularStrategy(w), startChild);
            _mmStrategy = new StrategyWrapper<MMStrategy>(Robot, Option, w => new MMStrategy(w), startChild);
            _vegaHedgeStrategy = new StrategyWrapper<VegaHedgeStrategy>(Robot, Option, w => new VegaHedgeStrategy(w), startChild);
            _gammaHedgeStrategy = new StrategyWrapper<GammaHedgeStrategy>(Robot, Option, w => new GammaHedgeStrategy(w), startChild);

            _mainStrategy.StateChanged += WrapperOnStateChanged;
            _regularStrategy.StateChanged += WrapperOnStateChanged;
            _mmStrategy.StateChanged += WrapperOnStateChanged;
            _vegaHedgeStrategy.StateChanged += WrapperOnStateChanged;
            _gammaHedgeStrategy.StateChanged += WrapperOnStateChanged;

            _optionStrategies = new[] {
                Tuple.Create(StrategyType.Regular, Regular),
                Tuple.Create(StrategyType.MM, MM),
                Tuple.Create(StrategyType.VegaHedge, VegaHedge),
                Tuple.Create(StrategyType.GammaHedge, GammaHedge)
            };

            Robot.RegisterModule(this);
        }

        protected override void DisposeManaged() {
            if(!ModuleState.Inactive())
                _log.Dbg.AddErrorLog("Module {0}: disposing in state {1} ({2})", Option.Code, ModuleState, string.Join(",", OptionStrategies.Select(t => "{0}:{1}".Put(t.Item1, t.Item2.State))));

            OptionStrategies.ForEach(t => t.Item2.Dispose());
            _mainStrategy.Dispose();

            base.DisposeManaged();
        }

        void WrapperOnStateChanged(StrategyWrapper wrapper) {
            if(!RobotThread.InThread()) {
                RobotThread.ExecuteAsync(() => WrapperOnStateChanged(wrapper));
                return;
            }

            _log.Dbg.AddDebugLog("Module {0}: state {1}:{2} (stopping={3})", Option.Code, wrapper.TypeName, wrapper.State, _stopping);

            if(_stopping) {
                if(ChildrenInactive()) {
                    if(ModuleState.Inactive()) {
                        _log.Dbg.AddInfoLog("module {0} stopped", Option.Code);
                        _stopping = false;
                    } else if(ModuleState.CanStop()) {
                        _mainStrategy.Stop(_stopForce);
                    }
                }
            }

            if(wrapper == _mainStrategy && wrapper.State == StrategyState.Failed) {
                var msg = "Основной модуль опциона {0} завершился с ошибкой.".Put(Option.Code);
                _log.AddErrorLog(msg);
                Controller.SendEmail("Ошибка {0}".Put(Option.Code), msg);
                
                wrapper.Reset();
                // Option.ForceDeactivateOption();
            }

            ModuleStateChanged.SafeInvoke(this);
        }

        public StrategyWrapper StrategyByType(StrategyType type) {
            switch(type) {
                case StrategyType.Regular:      return Regular;
                case StrategyType.MM:           return MM;
                case StrategyType.VegaHedge:    return VegaHedge;
                case StrategyType.GammaHedge:   return GammaHedge;
            }

            throw new InvalidOperationException("unexpected type: {0}".Put(type));
        }

        public void Start() {
            if(!RobotThread.InThread()) {
                _log.Dbg.AddWarningLog("Start() called from unexpected thread.");
                RobotThread.ExecuteAsync(Start);
                return;
            }

            _log.Dbg.AddInfoLog("Module {0}: Start()", Option.Code);

            if(!ModuleState.Inactive()) {
                _log.Dbg.AddErrorLog("Unable to start module in state {0}", ModuleState);
                return;
            }

            _stopping = false;
            _mainStrategy.Start();
        }

        public void Stop(bool force) {
            if(!RobotThread.InThread()) {
                _log.Dbg.AddWarningLog("Stop() called from unexpected thread.");
                RobotThread.ExecuteAsync(() => Stop(force));
                return;
            }

            _log.Dbg.AddInfoLog("Module {0}: Stop()", Option.Code);

            if(!ModuleState.CanStop()) {
                _log.Dbg.AddErrorLog("Can't stop module in state {0}", ModuleState);
                return;
            }

            if(_stopping) {
                _log.Dbg.AddErrorLog("module {0}: already stopping", Option.Code);
                return;
            }

            _stopping = true;
            _stopForce = force;

            if(ChildrenInactive()) {
                _mainStrategy.Stop(force);
                return;
            }

            if(_regularStrategy.State.CanStop())    _regularStrategy.Stop(force);
            if(_mmStrategy.State.CanStop())         _mmStrategy.Stop(force);
            if(_vegaHedgeStrategy.State.CanStop())  _vegaHedgeStrategy.Stop(force);
            if(_gammaHedgeStrategy.State.CanStop()) _gammaHedgeStrategy.Stop(force);

            if(ModuleState.CanStop() && ChildrenInactive()) {
                _mainStrategy.Stop(force);
                return;
            }
        }

        bool ChildrenInactive() {
            return OptionStrategies.All(t => t.Item2.State.Inactive());
        }
    }
}
