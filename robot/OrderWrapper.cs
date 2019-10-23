using System;
using System.Linq;
using Ecng.Common;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Messages;
using StockSharp.Plaza;

namespace OptionBot.robot {
    /// <summary>
    /// Класс-обертка для заявки основного робота.
    /// </summary>
    /// <remarks>
    /// Объект OrderWrapper может содержать в себе до 2х заявок:
    /// ProcessingOrder - заявка, по которой был только что сделан запрос (new, move, cancel) и ответ еще не был получен.
    /// CurrentOrder - актуальная заявка на данный момент. Для новой заявки присваивается сразу в момент посылки. В случае перемещения значение присваевается из ProcessingOrder после успешной активации новой заявки.
    /// </remarks>
    public class OrderWrapper : Disposable {
        #region properties

        readonly Logger _log;
        readonly OptionStrategy _parentStrategy;
        readonly Func<IOrderSender> _getSender;
        readonly Sides _direction;
        readonly bool _isOpenPosOrder;

        long _timeoutId;
        ICancellationToken _timeoutAction;

        RobotOptionOrder CancelingOrder;
        RobotOptionOrder _currentOrder;

        public string Name {get; private set;}
        public Sides Direction {get {return _direction;}}
        public bool IsOpenPosOrder {get {return _isOpenPosOrder;}}
        public OptionStrategy ParentStrategy {get {return _parentStrategy;}}
        public OptionInfo Option {get {return _parentStrategy.Option;}}

        public RobotOptionOrder CurrentOrder {get {return _currentOrder; } private set {_currentOrder = value; CurrentOrderChanged.SafeInvoke(this);}}
        public RobotOptionOrder ProcessingOrder {get; private set;}

        public bool IsInactive {get {return ProcessingOrder == null && CurrentOrder == null; }}
        public bool IsProcessing {get { return ProcessingOrder != null; }}
        public bool IsActive {get {return ProcessingOrder == null && CurrentOrder != null; }}
        public bool IsCancelRequested { get; private set; }
        public bool CanCancel {get {return IsActive || (IsProcessing && !IsCancelRequested);}}
        public RobotOptionOrder OrderToMoveOrCancel {get {return CanCancel ? (ProcessingOrder ?? CurrentOrder) : null; }}
        public decimal Price { get { return IsInactive ? 0 : IsProcessing ? ProcessingOrder.Price : CurrentOrder.Price; }}

        public decimal MaxCurrentOrderPrice { get {
            return Math.Max(CurrentOrder.Return(o => o!=CancelingOrder ? o.Price : decimal.MinValue, decimal.MinValue),
                            ProcessingOrder.Return(o => o!=CancelingOrder ? o.Price : decimal.MinValue, decimal.MinValue));
        }}
        public decimal MinCurrentOrderPrice { get {
            return Math.Min(CurrentOrder.Return(o => o!=CancelingOrder ? o.Price : decimal.MaxValue, decimal.MaxValue),
                            ProcessingOrder.Return(o => o!=CancelingOrder ? o.Price : decimal.MaxValue, decimal.MaxValue));
        }}

        PlazaTraderEx PlazaTrader {get {return _parentStrategy.PlazaTrader;}}

        public override string ToString() {
            return  Name + ": proc=" +
                    ProcessingOrder.With(po => "{0}/{1}".Put(po.TransactionId, po.State)) +
                    ", current=" +
                    CurrentOrder.With(co => "{0}/{1}".Put(co.TransactionId, co.State)) +
                    ", canceling=" +
                    CancelingOrder.With(co => "{0}/{1}".Put(co.TransactionId, co.State)) +
                    ", IsCancelRequested=" + IsCancelRequested;
        }

        #endregion

        public event Action<OrderWrapper> CurrentOrderChanged;
        public event Action<OrderWrapper, string> OrderStateChanged;
        public event Action<OrderWrapper> NotEnoughMoney;
        public event Action<OrderWrapper, string> FatalError;

        public OrderWrapper(OptionStrategy parentStrategy, Func<IOrderSender> senderGetter, Sides dir, bool isOpenOrder, string descr) {
            _parentStrategy = parentStrategy;
            _isOpenPosOrder = isOpenOrder;
            _getSender = senderGetter;
            _direction = dir;
            Name = descr;
            _log = new Logger("{0}_{1}".Put(_parentStrategy.Security.Code, Name));
        }

        protected override void DisposeManaged() {
            _log.Dbg.AddInfoLog("Dispose()");
            Util.CancelDelayedAction(ref _timeoutAction);
            base.DisposeManaged();
        }

        #region create delegates

        public void SendNew(RecalculateState.OrderAction action) {
            if(action.Action != RecalculateState.ActionType.New) throw new InvalidOperationException("SendNew: invalid action {0}".Put(action.Action));
            if(!IsInactive) throw new InvalidOperationException("попытка послать новый ордер до завершения старого");

            ProcessingOrder = CurrentOrder = CreateOrder(action);
            IsCancelRequested = false;
            CancelingOrder = null;

            _log.Dbg.AddInfoLog("SendNew: {0}@{1}", action.Size, action.Price);
            _getSender().SendOrder(_parentStrategy, ProcessingOrder);
        }

        public void Move(RecalculateState.OrderAction action) {
            if(action.Action != RecalculateState.ActionType.Move) throw new InvalidOperationException("Move: invalid action {0}".Put(action.Action));
            if(!IsActive) throw new InvalidOperationException("Move: unable to move order. unexpected state.");

            ProcessingOrder = CreateOrder(action);
            CancelingOrder = CurrentOrder;
            IsCancelRequested = false;

            var oldOrder = CurrentOrder;
            var newOrder = ProcessingOrder;

            _log.Dbg.AddInfoLog("Move: {0} {1}/{2}@{3} => {4}@{5}", oldOrder.TransactionId, oldOrder.Volume, oldOrder.Balance, oldOrder.Price, action.Size, action.Price);

            _getSender().MoveOrder(_parentStrategy, oldOrder, newOrder);
            CurrentOrderChanged.SafeInvoke(this);
        }

        public void Cancel() {
            if(!CanCancel)
                throw new InvalidOperationException("Cancel: unable to cancel order. unexpected state. processing={0}, cancelRequested={1}, proc={2}, cur={3}".Put(IsProcessing, IsCancelRequested, ProcessingOrder.Return(o => o.TransactionId, 0), CurrentOrder.Return(o => o.TransactionId, 0)));

            Order orderToCancel;
            var isActive = IsActive;

            if(isActive) {
                orderToCancel = CancelingOrder = ProcessingOrder = CurrentOrder;
            } else {
                orderToCancel = ProcessingOrder;
                CancelingOrder = null;
            }

            IsCancelRequested = true;

            _log.Dbg.AddInfoLog("Cancel({0}): {1} {2}/{3}", isActive?"active":"process", orderToCancel.TransactionId, orderToCancel.Volume, orderToCancel.Balance);
            _getSender().CancelOrder(_parentStrategy, orderToCancel);
            CurrentOrderChanged.SafeInvoke(this);
        }

        public static void MovePair(RecalculateState.OrderAction action1, RecalculateState.OrderAction action2) {
            if(action1.Action != RecalculateState.ActionType.Move) throw new InvalidOperationException("MovePair1: invalid action {0}".Put(action1.Action));
            if(action2.Action != RecalculateState.ActionType.Move) throw new InvalidOperationException("MovePair2: invalid action {0}".Put(action2.Action));
            if(action1.RecalcState != action2.RecalcState) throw new InvalidOperationException("MovePair: action states not equal");

            var w1 = action1.Wrapper;
            var w2 = action2.Wrapper;
            if(!w1.IsActive || !w2.IsActive)
                throw new InvalidOperationException("MovePair: unable to move pair. unexpected state.");

            var newOrder1 = w1.CreateOrder(action1);
            var newOrder2 = w2.CreateOrder(action2);

            w1.ProcessingOrder = newOrder1;
            w1.CancelingOrder = w1.CurrentOrder;
            w1.IsCancelRequested = false;

            w2.ProcessingOrder = newOrder2;
            w2.CancelingOrder = w2.CurrentOrder;
            w2.IsCancelRequested = false;

            var oldOrder1 = w1.CurrentOrder;
            var oldOrder2 = w2.CurrentOrder;

            w1._log.Dbg.AddInfoLog("MovePair: order1 balance={0}, order2 balance={1}", oldOrder1.Balance, oldOrder2.Balance);
            w1._getSender().MoveOrderPair(w1._parentStrategy, w2._parentStrategy, oldOrder1, newOrder1, oldOrder2, newOrder2);
            w1.CurrentOrderChanged.SafeInvoke(w1);
            w2.CurrentOrderChanged.SafeInvoke(w2);
        }

        public void ForceReset() {
            _log.Dbg.AddInfoLog("ForceReset({0})", ToString());
            CurrentOrder = ProcessingOrder = CancelingOrder = null;
            Util.CancelDelayedAction(ref _timeoutAction);
        }

        #endregion

        RobotOptionOrder CreateOrder(RecalculateState.OrderAction action) {
            var order = new RobotOptionOrder(action)
            {
                Direction = _direction,
                Price = action.Price,
                Volume = action.Size,
            };

            order.Balance = order.Volume; // this is necessary for AttachOrder to work correctly
            ApplyOrderRules(order);
            return order;
        }

        /// <summary>
        /// Обработать событие регистрации заявки на бирже.
        /// </summary>
        void OnOrderRegistered(RobotOptionOrder order) {
            Option.SecProcessor.CheckThread();

            _log.Dbg.AddInfoLog("OnOrderRegistered({0}): id={1}, vol={2}, bal={3}", order.TransactionId, order.Id, order.Volume, order.Balance);

            if(order != ProcessingOrder) {
                _log.Dbg.AddErrorLog("OnOrderRegistered: unknown order tranId={0}, id={1}", order.TransactionId, order.Id);
                return;
            }

            if(ProcessingOrder != CurrentOrder) {
                _log.Dbg.AddInfoLog("OnOrderRegistered: Replace successfull. Order {0} registered.", ProcessingOrder.TransactionId);
                CurrentOrder = ProcessingOrder;
            }
            ProcessingOrder = null;

            OrderStateChanged.SafeInvoke(this, "order registered");
        }

        /// <summary>
        /// Обработать событие перехода заявки в конечное состояние.
        /// </summary>
        void OnOrderDone(RobotOptionOrder order, object arg, bool timeout = false) {
            Option.SecProcessor.CheckThread();

            _log.Dbg.AddInfoLog("OnOrderDone({0}): {1}. id={2}, vol={3}, bal={4}", order.TransactionId, 
                            (order.IsMatched() ? "matched" : order.IsCanceled() ? "canceled" : "failed"), order.Id, order.Volume, order.Balance);

            var fail = order.State == OrderStates.Failed ? (arg as OrderFail) : null;

            var curUpdated = false;
            var curOrder = CurrentOrder;
            if(order == CurrentOrder) {
                curUpdated = true;
                CurrentOrder = null;
            }

            try {
                if(order.State == OrderStates.Failed) {
                    if(fail != null) {
                        _log.Dbg.AddInfoLog("fail reason: {0}", fail.Error.Message);
                        var plazaException = fail.Error as PlazaException;
                        if(plazaException != null) {
                            if(PlazaTraderEx.PlazaErrorsFatal.Contains(plazaException.ErrorCode)) {
                                ProcessingOrder = null;
                                FatalError.SafeInvoke(this, plazaException.ToString());
                                return;
                            }
                            if(plazaException.ErrorCode == PlazaTraderEx.PlazaErrTimeout) {
                                if(!timeout) {
                                    _log.AddWarningLog("Шлюз вернул код 'тайм-аут' для заявки {0}. Ожидаю ответа...", order.TransactionId);
                                    _timeoutId = order.TransactionId;
                                    if(curUpdated) CurrentOrder = order;
                                    Util.CancelDelayedAction(ref _timeoutAction);
                                    _timeoutAction = Option.SecProcessor.DelayedPost(() => {
                                        Util.CancelDelayedAction(ref _timeoutAction);

                                        OnOrderDone(order, arg, true);
                                    }, TimeSpan.FromSeconds(5), "plaza timeout handler");

                                    return;
                                }

                                _log.AddWarningLog("Время ожидания заявки истекло. Заявка не была активирована.");
                            } else if(plazaException.ErrorCode == PlazaTraderEx.PlazaErrNotEnoughMoney) {
                                NotEnoughMoney.SafeInvoke(this);
                            }
                        }
                    } else {
                        _log.Dbg.AddWarningLog("OnOrderDone: order failed but can't get fail reason");
                    }

                    if(order == curOrder) { // this means order register failed
                        ProcessingOrder = null;
                        return;
                    }

                    if(order == ProcessingOrder) { // this means order re-register failed
                        _log.Dbg.AddWarningLog("new order registration failed. other order probably was executed.");
                        ProcessingOrder = null;
                        return;
                    }
                }

                if(order == ProcessingOrder) {
                    ProcessingOrder = null;
                } else if(ProcessingOrder != null) {
                    _log.Dbg.AddWarningLog("Unknown order.");
                }
            } finally {
                OrderStateChanged.SafeInvoke(this, "order done");
            }
        }

        /// <summary>
        /// Отмена заявки не удалась. В случае перемещения заявки отмена старой заявки может быть неудачной если старая заявка успела исполниться.
        /// </summary>
        void OnCancelFailed(RobotOptionOrder order, OrderFail fail) {
            Option.SecProcessor.CheckThread();
            _log.Dbg.AddWarningLog("Отмена заявки не удалась: order=({0}), error={1}", order, fail.Error);

            var isNotFound = _parentStrategy.PlazaTrader.IsOrderNotFoundFail(fail);

            if(!isNotFound) {
                var transId = CancelingOrder.Return(co => co.TransactionId, 0);
                _log.Dbg.AddWarningLog("IsCancelRequested={0}, CancelingOrder={1}", IsCancelRequested, transId);

                if(IsCancelRequested) {
                    IsCancelRequested = false;

                    if(fail.Order.TransactionId == transId) {
                        CancelingOrder = ProcessingOrder = null;
                    }
                } else {
                    _log.Dbg.AddWarningLog("CancelFailed, but IsCancelRequested==false. Do nothing.");
                }
            }
            
            if(!isNotFound)
                OrderStateChanged.SafeInvoke(this, "cancel failed");
        }

        void ApplyOrderRules(RobotOptionOrder order) {
            var doneRule = (new IMarketRule[] {order.WhenMatched(), order.WhenRegisterFailed(), order.WhenCanceled()})
                .Or()
                .Do(arg => {
                    try {
                        OnOrderDone(order, arg);
                    } catch(Exception e) {
                        FatalError.SafeInvoke(this, "order done handler error: {0}".Put(e));
                    }
                })
                .Once()
                .Apply(_parentStrategy);

            var cancelFailedRule = order
                .WhenCancelFailed()
                .Do(fail => {
                    try {
                        OnCancelFailed(order, fail);
                    } catch(Exception e) {
                        FatalError.SafeInvoke(this, "OnCancelFailed handler error: {0}".Put(e));
                    }
                })
                .Apply(_parentStrategy);

            doneRule.ExclusiveRules.Add(cancelFailedRule);
                
            order.WhenRegistered()
                    .Do(() => {
                    try{
                        if(order.TransactionId == _timeoutId)
                            _log.AddWarningLog("Заявка с таймаутом({0}) была активирована.", _timeoutId);
                        OnOrderRegistered(order);
                    } catch(Exception e) {
                        FatalError.SafeInvoke(this, "order registered handler error: {0}".Put(e));
                    }
                    })
                    .Once()
                    .Apply(_parentStrategy);
        }
    }
}
