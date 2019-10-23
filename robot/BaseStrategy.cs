using System;
using System.Linq;
using System.Threading;
using AsyncHandler;
using Ecng.Collections;
using Ecng.Common;
using OptionBot.Config;
using StockSharp.Algo;
using StockSharp.Algo.Strategies;
using StockSharp.BusinessEntities;
using StockSharp.Logging;
using StockSharp.Messages;

namespace OptionBot.robot {
    public abstract class BaseStrategy : Strategy {
        readonly Robot _robot;
        readonly SecurityInfo _secInfo;
        protected readonly Logger _log;
        readonly ManualResetEventSlim _evStarted;
        PlazaTraderEx _plazaTrader;
        bool _subscribed;
        int _stoppedCounter;

        public Robot Robot {get {return _robot;}}
        protected IConfigGeneral CfgGeneral {get {return ConfigProvider.General.Effective; }}

        public SecurityInfo SecurityInfo {get {return _secInfo;}}
        public PlazaTraderEx PlazaTrader {get {return _plazaTrader ?? (_plazaTrader = (PlazaTraderEx)Connector); }}
        protected TransactionRateController TranRateController => PlazaTrader.TranRateController;
        protected RobotData RobotData {get {return Robot.Controller.RobotData; }}
        protected Connector MyConnector {get {return Robot.Controller.Connector; }}
        protected HandlerThread RobotThread {get {return Robot.RobotThread;}}
        protected Controller Controller {get {return Robot.Controller;}}
        protected ConfigProvider ConfigProvider {get {return Controller.ConfigProvider;}}
        protected bool IsStrategyActive {get {return ProcessState == ProcessStates.Started;}}
        protected ISecurityProcessor SecProcessor {get {return _secInfo.SecProcessor;}}

        protected bool ThereAreUnprocessedOrders {get {return SecProcessor.NumPendingOrderMessages > 0;}}
        protected bool ThereAreUnprocessedDepthMessages {get {return SecProcessor.NumPendingMarketDepthMessages > 0;}}
        protected bool IsInSecurityThread {get {return SecProcessor.IsInSecurityThread;}}

        public ManualResetEventSlim EventStarted {get {return _evStarted;}}

        /// <summary>
        /// Признак того, что стратегия завершена с ошибкой.
        /// </summary>
        public bool StrategyFailed {get; private set;}

        static int _lastStrategyId;
        readonly string _name;
        bool _logRegistered;

        protected virtual bool CanStop => true;
        public bool IsStopping {get; private set;}
        public bool WasStarted {get; private set;}
        protected bool WasStopped => _stoppedCounter > 0;

        public override LogLevels RulesLogLevel {get {return LogLevels.Warning;}}

        public BaseStrategy(Robot robot, SecurityInfo secInfo, string name="") {
            if(robot == null) throw new ArgumentNullException("robot");
            if(secInfo == null) throw new ArgumentNullException("secInfo");

            _robot = robot;
            _secInfo = secInfo;

            var pinfo = RobotData.ActivePortfolio;
            if(pinfo == null) throw new InvalidOperationException("Стратегия не может быть запущена пока не выбран активный портфель.");

            Connector = robot.Trader;

            var sec = PlazaTrader.Securities.FirstOrDefault(s => s.Id == secInfo.Id);
            if(sec == null)
                throw new InvalidOperationException("Инструмент {0} не найден.".Put(secInfo.Id));

            Security = sec;
            Portfolio = PlazaTrader.Portfolios.First(p => p.Name == pinfo.Name);

            var straId = Interlocked.Increment(ref _lastStrategyId);
            _name = name.IsEmpty() ? "{0}-{1}".Put(straId, Security.Code) : "{0}-{1}({2})".Put(straId, name, Security.Code);
            _log = new Logger(_name);

            _evStarted = new ManualResetEventSlim(false);
        }

        protected override void DisposeManaged() {
            if(ProcessState != ProcessStates.Stopped)
                _log.Dbg.AddErrorLog("disposing strategy in state {0}", ProcessState);

            if(!WasStopped)
                _log.Dbg.AddErrorLog("disposing strategy which wasn't stopped ({0})", ProcessState);

            Unsubscribe();

            if(_logRegistered)
                _log.DeregisterSource(this);

            base.DisposeManaged();
        }

        public override sealed void Start() {
            var st = ProcessState;
            if(st != ProcessStates.Stopped) {
                _log.Dbg.AddWarningLog("Unable to Start() in state {0}", st);
                return;
            }

            if(!IsInSecurityThread) {
                SecProcessor.Post(Start, "start strategy");
                return;
            }

            CancelOrdersWhenStopping = false;

            _log.Dbg.AddInfoLog("Start()");
            base.Start();
        }

        protected void CheckStop(string msg) {
            if(!IsStopping) return;

            Stop("checkstop: " + msg);
        }

        bool _inOnStop;

        public override sealed void Stop() {
            Stop(null);
        }

        public void Stop(string msg, bool force = false) {
            if(!IsInSecurityThread) { SecProcessor.Post(() => Stop(msg, force), "stop strategy({0}, {1})".Put(msg, force)); return; }

            if(!IsInSecurityThread) _log.Dbg.AddWarningLog("Stop() executed in wrong thread");

            var st = ProcessState;

            _log.Dbg.AddInfoLog("Stop({0}, force={1}): IsStopping={2}, ProcessState={3}", msg, force, IsStopping, st);
            IsStopping = true;

            if(st != ProcessStates.Started) { _log.Dbg.AddWarningLog("Unable to Stop() in state {0} (WasStarted={1})", st, WasStarted); return; }

            if(CanStop) {
                _log.Dbg.AddDebugLog("calling Strategy.Stop(), clearing rules: " + Rules.SyncGet(c => c.Select(r => "[" + r + "]").Join(", ")));
                Rules.Clear();
                base.Stop();
            } else if(!_inOnStop) {
                _inOnStop = true;
                try {
                    OnStop(force);
                } finally {
                    _inOnStop = false;
                }

                if(force && !CanStop)
                    _log.Dbg.AddErrorLog("ERROR: strategy still returns CanStop==false after OnStop(force==true). Will stop anyway.");

                if(CanStop || force) {
                    _log.Dbg.AddDebugLog("calling Strategy.Stop(), clearing rules: " + Rules.SyncGet(c => c.Select(r => "[" + r + "]").Join(", ")));
                    Rules.Clear();
                    base.Stop();
                }
            } else if(force) {
                _log.Dbg.AddWarningLog("Stop(force=true) called while _inOnStop==true");
            }
        }

        protected sealed override void OnStarted() {
            if(Parent == null) {
                _logRegistered = true;
                _log.RegisterSource(this, LogTarget.Dbg);
            }

            base.OnStarted();
            Name = _name;
            Subscribe();

            OnStarting();
            WasStarted = true;
            OnStarted2();

            EventStarted.Set();
        }

        protected abstract void OnStarting();
        protected abstract void OnStarted2();

        protected override void OnStopping() {
            SecProcessor.CheckThread();

            if(IsStopping == false) {
                _log.Dbg.AddWarningLog("strategy is stopping but IsStopping==false");
                IsStopping = true;
            }
            base.OnStopping();
        }

        protected override void OnStopped() {
            if(Interlocked.Increment(ref _stoppedCounter) > 1) { _log.Dbg.AddWarningLog("OnStopped() called second time. Ignoring..."); return; }

            StrategyFailed |= (ErrorCount > 0);

            OnStopped2();

            if(StrategyFailed) {
                var msg = "Стратегия {0} остановлена с ошибками{1}.".Put(_name, ErrorCount > 0 ? " ({0} ошибок)".Put(ErrorCount) : string.Empty);
                _log.AddWarningLog(msg);
                Controller.SendEmail(msg);
            } else {
                _log.Dbg.AddInfoLog("Стратегия остановлена.");
            }

            Unsubscribe();

            base.OnStopped();
        }

        protected virtual void OnStopped2() {}
        protected abstract void OnStop(bool force); // can be called multiple times

        #region subscribe/unsubscribe

        void Subscribe() {
            if(_subscribed)
                return;
            _subscribed = true;

            OnSubscribe();
        }

        void Unsubscribe() {
            if(!_subscribed)
                return;
            _subscribed = false;

            OnUnsubscribe();
        }

        protected virtual void OnSubscribe() {}
        protected virtual void OnUnsubscribe() {}

        #endregion

        /// <summary>
        /// Метод вызывается, если происходит серьезная ошибка, из которой нельзя автоматически восстановиться.
        /// </summary>
        protected void OnStrategyFail(string msg, Exception e) {
            StrategyFailed = true;

            if(!WasStopped) {
                _log.Dbg.AddErrorLog("Стратегия будет остановлена ({0}). {1}", msg, e);
                Stop("strategy fail");
            }
        }

        #region market order

        protected Order CreateMarketOrder(decimal size, Sides? dir = null) {
            return CreateMarketOrder(CfgGeneral, Portfolio, Security, size, dir);
        }

        /// <summary>
        /// Создать объект рыночной заявки с заданными параметрами. Заявка только создается, но не посылается.
        /// </summary>
        /// <param name="cfg">Объект настроек, из которых берутся параметры рыночной заявки.</param>
        /// <param name="port">Портфель</param>
        /// <param name="sec">Инструмент</param>
        /// <param name="size">Объем заявки. Если dir==null, то отризательный объем означает продажу, положительный - покупку.</param>
        /// <param name="dir">Направление заявки. Если null - то используется знак объема.</param>
        /// <returns></returns>
        public static Order CreateMarketOrder(IConfigGeneral cfg, Portfolio port, Security sec, decimal size, Sides? dir = null) {
            if(size == 0) throw new InvalidOperationException("CreateMarketOrder: size cannot be zero");
            if(dir == null) dir = size.GetDirection();

            var price = CalculateMarketOrderPrice(cfg, sec, dir.Value);

            if(price == 0) throw new Exception("Невозможно определить рыночную цену для инструмента");

            return new OrderEx {
                Connector = sec.Connector,
                Portfolio = port,
                Security = sec,
                //Type = OrderTypes.Limit,
                Direction = dir.Value,
                Volume = Math.Abs(size),
                Price = price,
                TimeInForce = TimeInForce.CancelBalance
            };
        }

        /// <summary>
        /// Рассчитать цену рыночной заявки.
        /// </summary>
        static decimal CalculateMarketOrderPrice(IConfigGeneral cfg, Security sec, Sides dir) {
            var price = dir == Sides.Buy ? sec.MaxPrice : sec.MinPrice;
            if(price != 0 && price < int.MaxValue && price > sec.PriceStep)
                return price;

            var sameDirPrice = GetCurrentPrice(sec, dir);
            var oppositePrice = GetCurrentPrice(sec, dir.Invert());
            if(sameDirPrice == 0 || oppositePrice == 0) return 0;

            var shift = cfg.MarketOrderShift;
            if(shift < 5) shift = 100;

            var delta = shift.Pips(sec);
            if(dir == Sides.Sell) delta = -delta;

            price = sameDirPrice + (decimal)delta;
            if(price < sec.PriceStep)
                price = sec.PriceStep;

            return price;
        }

        /// <summary>
        /// Получить рыночную цену заданного направления для заданного инструмента.
        /// </summary>
        static decimal GetCurrentPrice(Security sec, Sides dir) {
            var q = dir == Sides.Buy ? sec.BestBid : sec.BestAsk;
            return q != null ? q.Price : 0;
        }

        #endregion
    }
}
