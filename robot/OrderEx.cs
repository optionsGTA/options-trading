using System;
using System.Threading;
using Ecng.Common;
using StockSharp.BusinessEntities;
using StockSharp.Messages;

namespace OptionBot.robot {
    public class OrderEx : Order {
        static readonly Logger _log = new Logger();
//        public TimeSpan CreatedTime {get; private set;}
        DateTime SendTime {get; set;}
        DateTime CancelSendTime {get; set;}
        public TimeSpan? Latency {get; private set;}
        public TimeSpan? CancelLatency {get; private set;}

        protected Controller Controller => Controller.Instance;
        protected RobotLogger RobotLogger => Controller.RobotLogger;
        protected PlazaTraderEx Trader => Controller.Connector.Trader;

        bool _registeredOrFailed;
        bool _wasActivated;
        bool _isDone;

        public virtual void HandleBeforeSend() {
            SendTime = SteadyClock.Now;
        }

        public void HandleBeforeCancel() {
            CancelSendTime = SteadyClock.Now;
        }

        public void HandleCancelFailed() {
            if(CancelSendTime != default(DateTime)) {
                CancelLatency = SteadyClock.Now - CancelSendTime;
                NotifyChanged(nameof(CancelLatency));
            } else {
                _log.Dbg.AddWarningLog($"HandleCancelFailed({TransactionId}): CancelSendTime is null");
            }
        }

        public virtual void HandleOrderChanged(OrderFail fail = null) {
            var now = SteadyClock.Now;

            if(!_registeredOrFailed && (State == OrderStates.Active || this.IsInFinalState())) {
                _registeredOrFailed = true;
                Latency = now - SendTime;
                RobotLogger.LoggerThread.ExecuteAsync(() => RobotLogger.LatencyOrder.LogOrderLatency(this));

                if(State == OrderStates.Active) {
                    ThreadPool.QueueUserWorkItem(registeredLocalMarketTime =>
                        Controller.TimeKeeper.HandleOrderRegistered((DateTime)registeredLocalMarketTime, this), Controller.Connector.GetMarketTime());
                }

                NotifyChanged(nameof(Latency));
            }

            if(!_wasActivated) {
                if(State == OrderStates.Active) {
                    _wasActivated = true;
                    OnOrderActivated();
                }
            }

            if(!_isDone) {
                if(this.IsInFinalState()) {
                    _isDone = true;
                    OnOrderDone();
                }
            }

            if(fail == null)
                RobotLogger.Orders.LogOrder(this);
            else
                RobotLogger.Orders.LogOrderFail(fail);
        }

        protected virtual void OnOrderActivated() { }

        static readonly TimeSpan _maxCancelTime = TimeSpan.FromSeconds(1);

        protected virtual void OnOrderDone() {
            var trader = Trader;

            if(!_wasActivated || Balance == 0)
                return;

            // order was canceled
            var now = SteadyClock.Now;
            DateTime cancelSentAt;

            if(CancelSendTime != default(DateTime)) {
                cancelSentAt = CancelSendTime;
            } else {
                if(trader == null) {
                    _log.Dbg.AddWarningLog("trader is null");
                    return;
                }

                cancelSentAt = trader.LastOptGroupCancelTime;
            }

            var cancelTime = now - cancelSentAt;
            if(cancelTime > _maxCancelTime) {
                _log.Dbg.AddWarningLog($"OnOrderDone({TransactionId}): cancel time is too large: {cancelTime.TotalSeconds:0.###} sec");
                return;
            }

            CancelLatency = cancelTime;
            NotifyChanged(nameof(CancelLatency));
        }
    }

    public class RobotOptionOrder : OrderEx {
        readonly RecalculateState.OrderAction _orderAction;
        bool _orderLogged;

        public RecalculateState.OrderAction OrderAction {get {return _orderAction;}}
        public OrderWrapper Wrapper {get {return _orderAction.Wrapper;}}
        public OptionStrategy ParentStrategy {get {return _orderAction.Wrapper.ParentStrategy;}}
        public OptionInfo Option {get {return _orderAction.Wrapper.ParentStrategy.Option;}}

        public RobotOptionOrder(RecalculateState.OrderAction orderAction) {
            if(orderAction.Action == RecalculateState.ActionType.Cancel)
                throw new ArgumentException("orderAction");

            _orderAction = orderAction;
            Portfolio = ParentStrategy.Portfolio;
            Security = ParentStrategy.Security;
        }

        public override void HandleOrderChanged(OrderFail fail = null) {
            base.HandleOrderChanged(fail);

            if(_orderLogged)
                return;

            if(Time.IsDefault() && !this.IsInFinalState())
                return;

            _orderLogged = true;
            RobotLogger.OptionOrderAction.LogOrder(this, fail);
        }

        protected override void OnOrderActivated() {
            base.OnOrderActivated();

            Option.Logger.AddStrategyOrder(this, ParentStrategy.StrategyType, Wrapper.Direction, Wrapper.IsOpenPosOrder);
        }

        protected override void OnOrderDone() {
            base.OnOrderDone();

            Option.Logger.RemoveStrategyOrder(this, ParentStrategy.StrategyType, Wrapper.Direction, Wrapper.IsOpenPosOrder);
        }
    }
}
