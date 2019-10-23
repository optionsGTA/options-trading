using System;
using System.Collections.Generic;
using System.Linq;
using Ecng.Common;
using MoreLinq;
using OptionBot.Config;
using StockSharp.Algo;
using StockSharp.Algo.Strategies;
using StockSharp.BusinessEntities;
using StockSharp.Logging;
using StockSharp.Messages;

namespace OptionBot.robot {
    public interface IOrderSender {
        void SendOrder(Strategy s, Order order);
        void MoveOrder(Strategy s, Order oldOrder, Order newOrder);
        void MoveOrderPair(Strategy s1, Strategy s2, Order old1, Order new1, Order old2, Order new2);
        void CancelOrder(Strategy s, Order order);
    }

    /// <summary>
    /// Стратегия основного робота.
    /// </summary>
    public sealed class OptionMainStrategy : BaseRobotStrategy, IOrderSender {
        #region fields/properties

        readonly FilteredMarketDepth _depth;

        PricePair _futPrevQuote;

        CanTradeState _strategyCanTrade;

        static readonly TimeSpan _moneyErrorDelay = TimeSpan.FromSeconds(3);
        static readonly TimeSpan _moneyErrorCalcPeriod = TimeSpan.FromSeconds(30);
        const int MaxMoneyErrorsInPeriod = 3;

        readonly List<DateTime> _moneyErrorTimes = new List<DateTime>();

        ICancellationToken _notEnoughMoneyDelayAction;

        bool _notEnoughMoneyError;
        bool _needRecalculate;
        DateTime _lastRecalculate;

        bool _ordersWereSent;

        readonly RecalculateState _state = new RecalculateState();

        readonly RobotLogger.LatencyLogger _mainWatch;
        readonly RobotLogger.OptionOrderActionLogger _orderActionLogger;

        readonly IntCounter _recalcMessagesCounter = new IntCounter();
        public IntCounter RecalcMessagesCounter {get {return _recalcMessagesCounter;}}

        bool ThereAreRecalculationRequests {get {return _recalcMessagesCounter.Value > 0;}}

        //int _recalcIndex;

        decimal _futureChangeTrigger;

        public OptionInfo Option {get {return (OptionInfo)SecurityInfo;}}
        public FuturesInfo Future {get {return Option.Future; }}
        OptionModel Model {get {return Option.Model; }}
        IConfigFuture CfgFuture {get {return Future.CfgFuture;}}

        IOptionStrategy[] _orderedChildStrategies = new IOptionStrategy[0];

        CanTradeState CanTrade {get { return Robot.CanTradeState & _strategyCanTrade; }}
        protected override bool CanStop {get {return !_orderedChildStrategies.Any();}}

        // параметры робота, переведенные в абсолютные значения

        #endregion

        #region init/deinit

        public OptionMainStrategy(StrategyWrapper<OptionMainStrategy> wrapper) : base(wrapper, "optmain") {
            _mainWatch = Robot.RobotLogger.LatencyMain(Option);
            _orderActionLogger = Robot.RobotLogger.OptionOrderAction;
            _depth = new FilteredMarketDepth(Option);
        }

        protected override void DisposeManaged() {
            _depth.Dispose();

            base.DisposeManaged();
        }

        protected override void OnStarting() {
            SecProcessor.CheckThread();

            PlazaTrader.GetPosition(Portfolio, Security); // all positions (even zeroes) must be in Positions list
            //PlazaTrader.GetPosition(Portfolio, Option.Opposite.NativeSecurity); // need this for OLD hedge calculator to work
            Position = Robot.GetRealPosition(Security.Id);

            _strategyCanTrade = CanTradeState.CanOpenPositions.Normalize();

            _futureChangeTrigger = CfgFuture.FuturesChangeStartTrigger * Future.NativeSecurity.PriceStep;
            _log.AddInfoLog($"Расчетная стратегия запущена. Позиция = {Position}, _futureChangeTrigger = {_futureChangeTrigger}");

            UpdateValuationParams();
            UpdateFutureInfo();
        }

        protected override void OnStarted2() {
            OnOptionFilteredMarketDepthChanged();
        }

        protected override void OnStopped2() {
            base.OnStopped2();

            _log.AddInfoLog("Расчетная стратегия остановлена. Позиция = {0}({1})", Robot.GetRealPosition(Security.Id), Position);
        }

        protected override void OnStopping() {
            base.OnStopping();

            _notEnoughMoneyDelayAction.Do(token => token.Cancel());

            if(Controller.Scheduler.CanCancelOrders()) {
                if(_ordersWereSent) {
                    _log.Dbg.AddInfoLog("sending CancelOrders() to make sure no orders left due to errors...");
                    try {
                        PlazaTrader.CancelOrders(null, Portfolio, null, null, Security);
                    } catch(Exception e) {
                        _log.AddWarningLog("Ошибка при попытке отменить все заявки по инструменту {0}: {1}", Security.Code, e);
                    }
                }
            } else {
                _log.Dbg.AddInfoLog("canceling orders is not allowed now");
            }
        }

        protected override void OnStop(bool force) {
            _orderedChildStrategies.Cast<BaseStrategy>().Where(s => !s.IsStopping).ForEach(s => s.Stop("from base", force));
        }

        #endregion

        void ChildStrategiesOnChanged() {
            var newarr = ChildStrategies.Cast<IOptionStrategy>().OrderBy(s => s.StrategyType).ToArray();

            _log.Dbg.AddInfoLog("ChildStrategiesOnChanged: {0} => {1}", _orderedChildStrategies.Length, newarr.Length);

            _orderedChildStrategies = newarr;

            if(IsStopping)
                _orderedChildStrategies.Cast<BaseStrategy>().Where(s => !s.IsStopping).ForEach(s => s.Stop("from base2"));

            CheckStop("ChildStrategiesOnChanged");
        }

        volatile bool _recalculating;

        /// <summary>
        /// Пересчитать параметры основного робота и отправить заявки при необходимости.
        /// </summary>
        /// <param name="reason">Причина запуска основного робота</param>
        public void Recalculate(RecalcReason reason) {
            Action action = () => {
                if(_recalculating) {
                    _log.Dbg.AddWarningLog($"Ignoring recursive or parallel RecalculateImpl() call. reason={reason}");
                    return;
                }

                try {
                    _recalculating = true;

                    if(!IsStrategyActive || _depth == null) return;

                    if(ThereAreUnprocessedOrders || ThereAreRecalculationRequests)
                        return;

                    if(ThereAreUnprocessedDepthMessages) {
                        _depth.ForceAnyChangeNotificationOnce();
                        return;
                    }

                    _needRecalculate = false;
                    _mainWatch.StartTimer();
                    try {
                        RecalculateImpl(reason);
                    } finally {
                        _mainWatch.StopAndCommit();
                    }

                } catch(Exception e) {
                    OnStrategyFail("ошибка основного робота", e);
                } finally {
                    _lastRecalculate = SteadyClock.Now;
                    _recalculating = false;
                    CheckStop("recalc finally");
                }
            };

            if(!IsInSecurityThread) {
                SecProcessor.Post(action, "recalc_opt("+ reason +")", _recalcMessagesCounter);
            } else {
                action();
            }
        }

        void RecalculateImpl(RecalcReason reason) {
            _state.Init();
            _state.Time = MyConnector.GetMarketTime();
            _state.Position = (int)Position;
            _futPrevQuote = _state.FutQuote;

            UpdateQuotes();

            var children = _orderedChildStrategies;
            _state.RecalcMinMaxPrices(children.SelectMany(c => c.MyOrders));

            // todo может стоит в случае ThereAreUnprocessedOrders форсировать Post(Recalculate), так как нет гарантии вызова Recalculate для ЛЮБОГО ордер инструмента?
            if(ThereAreUnprocessedOrders || ThereAreRecalculationRequests) return;

            children.ForEach(c => c.BeginCalculation());

            _state.FutParams = Future.Params;
            Model.Update(_state, reason, children);
            _state.ModelData = Model.LastData;

            // выставление этого свойства означает, что при последующих изменениях best bid/ask опциона меньше чем на OptionChangeTrigger шагов
            // автоматический пересчет не будет автоматически вызываться.
            // поэтому, если ниже в этом методе происходит отмена пересчета и возврат *с неопределенным последующим пересчетом*, то свойство LastCalcOptQuote нужно сбрасывать, или как-либо еще обрабатывать эту ситуацию.
            _state.LastCalcOptQuote = _state.OptQuote;

            if(ThereAreUnprocessedOrders || ThereAreRecalculationRequests) return;

            // Обновить состояние CanTrade (можно ли делать заявки и если можно, то какие)
            UpdateCanTradeState();
            _state.CanTrade = CanTrade.Normalize();
            _state.UpdateFutureParams(Model, CfgGeneral);

            if(!PlazaTrader.RealtimeMonitor.InRealtimeMode)
                return;

            foreach(var s in children) {
                s.Recalculate(_state, reason);

                if(ThereAreUnprocessedOrders || ThereAreRecalculationRequests)
                    return;
            }

            #region check realtime/disabledReason

            if(!PlazaTrader.RealtimeMonitor.CanCalculate) {
                // в случае сброса реалтайм режима ничего делать не нужно.
                // все ордера будут отменены из Robot.OnRealtimeModeChanged

                _state.OrderActions.ForEach(action => _orderActionLogger.LogCanceledOrderAction(action, RecalculateState.ActionCancelReason.NotInRealtimeMode));
                _state.OrderActions.Clear();
            } else if(TranRateController.IsLimitExceeded) {
                _state.OrderActions.ForEach(action => _orderActionLogger.LogCanceledOrderAction(action, RecalculateState.ActionCancelReason.TransactionsLimit));
                _state.OrderActions.Clear();
            } else if(TranRateController.IsNewOrderLimitExceeded) {
                _state.OrderActions.Where(action => action.Action == RecalculateState.ActionType.New)
                                   .ForEach(action => _orderActionLogger.LogCanceledOrderAction(action, RecalculateState.ActionCancelReason.TransactionsLimit));
                _state.OrderActions.RemoveAll(action => action.Action == RecalculateState.ActionType.New);
            }

            #endregion

            #region Предотвращение посылки ордеров с пересекающимися ценами

            var actions = new List<RecalculateState.OrderAction>(16);
            var crossList = new List<RecalculateState.OrderAction>();
            var minCurrentSellPrice = _state.MinSellPrice;
            var maxCurrentBuyPrice = _state.MaxBuyPrice;
            var canceledActions = false;

            foreach(var action in _state.OrderActions) {
                if(action.CancelThisAction) {
                    canceledActions = true;
                    continue;
                }

                if(action.Wrapper.Direction == Sides.Buy) {
                    if(action.Action == RecalculateState.ActionType.Cancel || action.Price < minCurrentSellPrice) {
                        actions.Add(action);
                        if(action.Action != RecalculateState.ActionType.Cancel && action.Price > maxCurrentBuyPrice)
                            maxCurrentBuyPrice = action.Price;
                    } else {
                        crossList.Add(action);
                    }
                } else {
                    if(action.Action == RecalculateState.ActionType.Cancel || action.Price > maxCurrentBuyPrice) {
                        actions.Add(action);
                        if(action.Action != RecalculateState.ActionType.Cancel && action.Price < minCurrentSellPrice)
                            minCurrentSellPrice = action.Price;
                    } else {
                        crossList.Add(action);
                    }
                }
            }

            if(crossList.Count > 0) {
                _log.Dbg.AddWarningLog("Orders not sent to prevent cross trades: {0}", string.Join("; ", crossList.Select(a => "{0}-{1}-{2}".Put(a.Action, a.Size, a.Price))));

                crossList.ForEach(action => _orderActionLogger.LogCanceledOrderAction(action, RecalculateState.ActionCancelReason.CrossPrice));
            }

            if(canceledActions) {
                var canceledActionsList = _state.OrderActions.Where(a => a.CancelThisAction).ToArray();
                Robot.RobotLogger.LoggerThread.ExecuteAsync(() => {
                    canceledActionsList.ForEach(a => _orderActionLogger.LogCanceledOrderAction(a, RecalculateState.ActionCancelReason.SendConditionFalse));
                });
            }

            #endregion

            if(ThereAreUnprocessedOrders || ThereAreRecalculationRequests)
                return;

            if(actions.Count == 0)
                return;

            #region Посылка ордеров на рынок

            for(var i=0; i<actions.Count; ++i) {
                var action = actions[i];
                if(action == null) continue;

                switch(action.Action) {
                    case RecalculateState.ActionType.New:
                        _ordersWereSent = true;
                        action.Wrapper.SendNew(action);
                        action.LastSecondTransactions = TranRateController.LastSecondTransactionsCached;
                        //++transNum;
                        break;
                    case RecalculateState.ActionType.Move:
                        var secondMoveIndex = -1;

                        for(var j = i + 1; j < actions.Count; ++j) {
                            if(actions[j] == null || actions[j].Action != RecalculateState.ActionType.Move) continue;

                            secondMoveIndex = j;
                                                
                            if(actions[secondMoveIndex].Direction != action.Direction)
                                break; //prefer MovePair with opposite directions
                        }

                        _ordersWereSent = true;

                        if(secondMoveIndex >= 0) {
                            OrderWrapper.MovePair(action, actions[secondMoveIndex]);
                            actions[secondMoveIndex].LastSecondTransactions = action.LastSecondTransactions = TranRateController.LastSecondTransactionsCached;
                            actions[secondMoveIndex] = null;
                        } else {
                            action.Wrapper.Move(action);
                            action.LastSecondTransactions = TranRateController.LastSecondTransactionsCached;
                        }

                        //++transNum;

                        break;
                    case RecalculateState.ActionType.Cancel:
                        action.Wrapper.Cancel();
                        action.LastSecondTransactions = TranRateController.LastSecondTransactionsCached;
                        _orderActionLogger.LogOrderCancel(action);
                        //++transNum;
                        break;
                    default:
                        _log.Dbg.AddErrorLog("unknown action: {0}", action.Action);
                        break;
                }
            }

            #endregion
        }

        void UpdateQuotes() {
            _state.OptQuote = new PricePair(_depth.BestBidPrice, _depth.BestAskPrice);
            _state.OptionCalcBidVol = _depth.BestBidVol;
            _state.OptionCalcAskVol = _depth.BestAskVol;
            _state.GlassBidVolume = _depth.GlassBidVolume;
            _state.GlassOfferVolume = _depth.GlassAskVolume;
        }

        void OnFutureConfigChanged(ICfgPairFuture pair, string[] names) {
            if(names != null) SecProcessor.Post(() => {
                _futureChangeTrigger = CfgFuture.FuturesChangeStartTrigger * Future.NativeSecurity.PriceStep;
                _log.AddInfoLog("OnFutureConfigChanged: _futureChangeTrigger = {0}", _futureChangeTrigger);

                Recalculate(RecalcReason.FutureSettingsUpdated);
            }, RecalcReason.FutureSettingsUpdated.GetRecalcReasonDescription(), _recalcMessagesCounter);
        }

        void GeneralOnEffectiveConfigChanged(ICfgPairGeneral cfgPairGeneral, string[] names) {
            if(names != null) Recalculate(RecalcReason.GeneralSettingsUpdated);
        }

        void ValuationParamsOnListOrItemChanged(bool isListChange) {
            SecProcessor.Post(() => {
                UpdateValuationParams();
                Recalculate(RecalcReason.VPSettingsUpdated);
            }, RecalcReason.VPSettingsUpdated.GetRecalcReasonDescription(), _recalcMessagesCounter);
        }

        public void ResetMarketIv(string comment) {
            _log.Dbg.AddInfoLog("MarketIV reset");
            Model.Reset(comment);
        }

        /// <summary>Поменялось глобальное состояние CanTrade.</summary>
        protected override void RobotOnCanTradeStateChanged() {
            var reason = RecalcReason.CanTradeStateChanged;
            var reasonStr = reason.GetRecalcReasonDescription();
            SecProcessor.Post(() => {
                ResetMarketIv(reasonStr);
                Recalculate(reason);
            }, reasonStr, _recalcMessagesCounter);
        }

        void SeriesOnAtmStrikeChanged(OptionSeriesInfo series) {
            var reason = RecalcReason.ATMStrikeChanged;
            var reasonStr = reason.GetRecalcReasonDescription();
            SecProcessor.Post(() => {
                if(IsStopping)
                    return;

                var shift = Option.AtmShift;
                if(shift == null) {
                    _log.Dbg.AddInfoLog("ATM shift is null. Stopping strategy...");
                    return;
                }

                UpdateValuationParams();
                ResetMarketIv(reasonStr);
                Recalculate(reason);
            }, reasonStr, _recalcMessagesCounter);
        }

        /// <summary>Локальная причина того, что нельзя делать заявки (при том, что глобально заявки разрешены).</summary>
        enum DisabledReason {None, ModelFail, NotRealtime, TranLimitExceeded, NotEnoughMoney, Stopping}
        DisabledReason _disabledReason = DisabledReason.None;

        /// <summary>
        /// Обновить состояние CanTrade.
        /// called from threading pool thread
        /// </summary>
        void UpdateCanTradeState() {
            var oldVal = CanTrade;
            var reason = DisabledReason.None;
            var inRealtime = PlazaTrader.RealtimeMonitor.InRealtimeMode;
            var tranLimitExceeded = TranRateController.IsLimitExceeded;

            if(!Model.LastCalcSuccessful || !inRealtime || tranLimitExceeded || _notEnoughMoneyError || IsStopping) {
                _strategyCanTrade = CanTradeState.TradingDisabled;
                reason = IsStopping ? DisabledReason.Stopping :
                         _notEnoughMoneyError ? DisabledReason.NotEnoughMoney :
                         !inRealtime ? DisabledReason.NotRealtime :
                         tranLimitExceeded ? DisabledReason.TranLimitExceeded : 
                         DisabledReason.ModelFail;
            } else {
                _strategyCanTrade = CanTradeState.CanOpenPositions.Normalize();
            }

            string comment = null;

            if(CanTrade != oldVal || reason != _disabledReason) {
                if(_strategyCanTrade == CanTradeState.TradingDisabled) {
                    _disabledReason = reason;

                    switch(reason) {
                        case DisabledReason.NotRealtime: comment = "режим реального времени сброшен"; break;
                        case DisabledReason.TranLimitExceeded: comment = "лимит частоты транзакций превышен"; break;
                        case DisabledReason.ModelFail:   comment = "расчет цен завершился с ошибкой: {0}".Put(Model.LastError); break;
                        case DisabledReason.NotEnoughMoney: comment = "ошибка нехватки средств"; break;
                        case DisabledReason.Stopping: comment = "остановка стратегии"; break;
                    }

                    var msg = $"заявки нельзя делать: {comment}";
                    (LogHelper.CanLogMessage(msg) ? _log : _log.Dbg).AddWarningLog(msg);
                } else {
                    _disabledReason = DisabledReason.None;
                    var msg = "заявки можно делать.";
                    (LogHelper.CanLogMessage(msg) ? _log : _log.Dbg).AddInfoLog(msg);
                }
            }

//            _logger.AppendComment(comment);
//            _logger.CanTrade(CanTrade);
        }

        /// <summary>
        /// Обновить информацию о котировках фьючерса.
        /// called from datathread
        /// </summary>
        bool UpdateFutureInfo() {
            _state.FutQuote = Future.NativeSecurity.BestPair.GetQuotes();
            return Math.Max(Math.Abs(_state.FutQuote.Bid - _futPrevQuote.Bid), Math.Abs(_state.FutQuote.Ask - _futPrevQuote.Ask)) >= _futureChangeTrigger;
        }

        void UpdateValuationParams() {
            _depth.UpdateValuationParams();
        }

        decimal _previousPosition;

        void OnPositionChanged() {
            SecProcessor.CheckThread();

            _log.Dbg.AddInfoLog($"pos changed: {_previousPosition} ==> {Position}");
            _needRecalculate = true;

            var dealVolume = (int)Math.Abs(Position - _previousPosition);
            _state.LastDealTime = MyConnector.GetMarketTime();

            Option.CfgValuationParams?.Do(vp => {
                if(vp.DealVolumeReset && dealVolume >= vp.DealVolumeResetLimit) {
                    _log.Dbg.AddDebugLog($"deal reset: vol={dealVolume}, limit={vp.DealVolumeResetLimit}");
                    Model.DealReset();
                }
            });

            _previousPosition = Position;

            Future.TradingModule.HandleChildOptionStrategyPositionChange(Option);
        }

        void FutureOnParamsUpdatedOnPositionChange() {
            Recalculate(RecalcReason.FutureRecalculatedOnPositionChange);
        }

        void OnSecurityOrdersDataEnd() {
            if(_needRecalculate) {
                _needRecalculate = false;
                Recalculate(RecalcReason.OrderOrPositionUpdate);
            }
        }

        void OnOptionFilteredMarketDepthChanged() {
            Recalculate(RecalcReason.OptionChanged);
        }

        void PlazaTraderOnMarketDepthChanged(MarketDepth depth) {
            if(depth.Security.Id == Future.Id && UpdateFutureInfo())
                Recalculate(RecalcReason.FutureChanged);
        }

        void RealtimeMonitorOnStateChanged() {
            if(PlazaTrader.RealtimeMonitor.InRealtimeMode)
                Recalculate(RecalcReason.RealtimeMode);
        }

        void RealtimeMonitorCanCalculateChanged() {
            if(PlazaTrader.RealtimeMonitor.CanCalculate)
                Recalculate(RecalcReason.CanCalculateMode);
        }

        void RobotOnRecalculateRequired() {
            Recalculate(RecalcReason.ForcedRecalculate);
        }

        void TranRateControllerOnStateChanged(TransactionControllerState newState) {
            if(newState != TransactionControllerState.LimitExceeded)
                Recalculate(RecalcReason.TranRateControllerStateChanged);
        }

        #region Подписка на события адаптера/робота

        protected override void OnSubscribe() {
            base.OnSubscribe();
            PositionChanged                                     += OnPositionChanged;
            PlazaTrader.MarketDepthChanged                      += PlazaTraderOnMarketDepthChanged;
            PlazaTrader.RealtimeMonitor.RealtimeStateChanged    += RealtimeMonitorOnStateChanged;
            PlazaTrader.RealtimeMonitor.CanCalculateChanged     += RealtimeMonitorCanCalculateChanged;
            Future.Config.EffectiveConfigChanged                += OnFutureConfigChanged;
            Option.Series.AtmStrikeChanged                      += SeriesOnAtmStrikeChanged;
            ConfigProvider.General.EffectiveConfigChanged       += GeneralOnEffectiveConfigChanged;
            ConfigProvider.ValuationParams.ListOrItemChanged    += ValuationParamsOnListOrItemChanged;
            _depth.FilteredMarketDepthChanged                   += OnOptionFilteredMarketDepthChanged;
            Future.TradingModule.ParamsUpdatedOnPositionChange  += FutureOnParamsUpdatedOnPositionChange;
            SecProcessor.OrdersDataEnd                          += OnSecurityOrdersDataEnd;
            ChildStrategies.Changed                             += ChildStrategiesOnChanged;
            Robot.RecalculateRequired                           += RobotOnRecalculateRequired;
            TranRateController.StateChanged                     += TranRateControllerOnStateChanged;
        }

        protected override void OnUnsubscribe() {
            base.OnUnsubscribe();
            PositionChanged                                     -= OnPositionChanged;
            PlazaTrader.MarketDepthChanged                      -= PlazaTraderOnMarketDepthChanged;
            PlazaTrader.RealtimeMonitor.RealtimeStateChanged    -= RealtimeMonitorOnStateChanged;
            PlazaTrader.RealtimeMonitor.CanCalculateChanged     -= RealtimeMonitorCanCalculateChanged;
            Future.Config.EffectiveConfigChanged                -= OnFutureConfigChanged;
            Option.Series.AtmStrikeChanged                      -= SeriesOnAtmStrikeChanged;
            ConfigProvider.General.EffectiveConfigChanged       -= GeneralOnEffectiveConfigChanged;
            ConfigProvider.ValuationParams.ListOrItemChanged    -= ValuationParamsOnListOrItemChanged;
            _depth.FilteredMarketDepthChanged                   -= OnOptionFilteredMarketDepthChanged;
            Future.TradingModule.ParamsUpdatedOnPositionChange  -= FutureOnParamsUpdatedOnPositionChange;
            SecProcessor.OrdersDataEnd                          -= OnSecurityOrdersDataEnd;
            ChildStrategies.Changed                             -= ChildStrategiesOnChanged;
            Robot.RecalculateRequired                           -= RobotOnRecalculateRequired;
            TranRateController.StateChanged                     -= TranRateControllerOnStateChanged;
        }

        #endregion

        #region order operations

        public void SendOrder(Strategy s, Order order) {
            if(s.ProcessState != ProcessStates.Started) {
                _log.Dbg.AddWarningLog("Unable to send order for strategy {0}: strategy in state {1}", s.Name, s.ProcessState);
                return;
            }

            s.RegisterOrder(order);
            _depth.AddOrder(order);
        }

        public void MoveOrder(Strategy s, Order oldOrder, Order newOrder) {
            if(s.ProcessState != ProcessStates.Started) {
                _log.Dbg.AddWarningLog("Unable to move order for strategy {0}: strategy in state {1}", s.Name, s.ProcessState);
                return;
            }

            s.ReRegisterOrder(oldOrder, newOrder);
            _depth.AddOrder(newOrder);
        }

        public void MoveOrderPair(Strategy s1, Strategy s2, Order old1, Order new1, Order old2, Order new2) {
            if(s1.ProcessState != ProcessStates.Started || s2.ProcessState != ProcessStates.Started) {
                _log.Dbg.AddWarningLog("Unable to movepair orders for strategies {0}-{1}: states are {2}-{3}", s1.Name, s2.Name, s1.ProcessState, s2.ProcessState);
                return;
            }

            this.AddInfoLog("Перерегистрация пары заявок ({0} {1} {2}({3})@{4} => {5}@{6}), ({7} {8} {9}({10})@{11} => {12}@{13})", 
                            old1.TransactionId, old1.Direction, old1.Volume, old1.Balance, old1.Price, new1.Volume, new1.Price,
                            old2.TransactionId, old2.Direction, old2.Volume, old2.Balance, old2.Price, new2.Volume, new2.Price);

            s1.AttachOrder(new1, new List<MyTrade>());
            s2.AttachOrder(new2, new List<MyTrade>());
            PlazaTrader.ReRegisterOrderPair(old1, new1, old2, new2);

            _depth.AddOrder(new1);
            _depth.AddOrder(new2);
        }

        public void CancelOrder(Strategy s, Order order) {
            if(s.ProcessState != ProcessStates.Started) {
                _log.Dbg.AddWarningLog("Unable to cancel order for strategy {0}: strategy in state {1}", s.Name, s.ProcessState);
                return;
            }

            s.CancelOrder(order);
        }

        #endregion

        public void CheckPeriodicRecalculate() {
            if(!IsStrategyActive)
                return;

            var period = TimeSpan.FromSeconds(CfgGeneral.MMCalcPeriodic);
            var now = SteadyClock.Now;

            if(now - _lastRecalculate > period)
                Recalculate(RecalcReason.Periodic);
        }

        public void HandleNotEnoughMoney(string strategyName) {
            if(!IsInSecurityThread) {
                _log.Dbg.AddWarningLog("HandleNotEnoughMoney({0}) called from wrong thread", strategyName);
                SecProcessor.Post(() => HandleNotEnoughMoney(strategyName), "handle_not_enough_money");
                return;
            }

            if(_notEnoughMoneyDelayAction != null) return;

            var now = DateTime.UtcNow;
            var removeBefore = now - _moneyErrorCalcPeriod;
            _moneyErrorTimes.RemoveAll(d => d < removeBefore);
            _moneyErrorTimes.Add(now);

            if(_moneyErrorTimes.Count > MaxMoneyErrorsInPeriod) {
                // force deactivate all VM strategies which currently point to this option
                var shift = Option.AtmShift;

                if(shift != null) {
                    var strategies = Enum.GetValues(typeof(StrategyType)).Cast<StrategyType>().Select(st => shift.Strategy(st)).Where(s => s != null).ToArray();
                    _log.Dbg.AddWarningLog($"Деактивация {strategies.Length} стратегий опциона {Option.Id}, shift={shift.ShiftString}");

                    strategies.ForEach(s => s.ForceDeactivateStrategy());
                }

                OnStrategyFail("{0}: Слишком много ошибок нехватки средств в заданный период".Put(strategyName), null);
                return;
            }

            _log.AddWarningLog("Ошибка нехватки средств ({0}) {1}/{2}", strategyName, _moneyErrorTimes.Count, MaxMoneyErrorsInPeriod);

            Robot.HandleNotEnoughMoney(Option);

            _notEnoughMoneyError = true;
            _notEnoughMoneyDelayAction = SecProcessor.DelayedPost(() => {
                _notEnoughMoneyError = false;
                _notEnoughMoneyDelayAction = null;
                Recalculate(RecalcReason.MoneyErrorDelay);
            }, _moneyErrorDelay, "money err delay", _recalcMessagesCounter);
        }
    }

    /// <summary>Сохраненное состояние стратегии. Используется в асинхронном обработчике.</summary>
    public class RecalculateState : IOptionModelInputData {
        #region internal classes

        public enum ActionType {New, Move, Cancel}
        public enum ActionCancelReason {NotInRealtimeMode, TransactionsLimit, CrossPrice, SendConditionFalse}

        public class OrderAction {
            public OrderAction(ActionType action, OptionStrategy.IOrderActionInfo actionDraft, RecalculateState recalcState, VMStrategy strategy) {
                if(action != ActionType.Cancel && (actionDraft.Volume == 0 || actionDraft.Price == 0))
                    throw new ArgumentException("size and price must be set for action '{0}'".Put(action));

                RecalcState = recalcState;
                VMStrategy = strategy;
                Wrapper = actionDraft.Wrapper;
                Action = action;
                Size = actionDraft.Volume;
                Price = actionDraft.Price;
                ConsiderPrice = actionDraft.ConsiderPrice;
                TargetIv = actionDraft.TargetIv;
                PriceCorrection = actionDraft.PriceCorrection;
                OrderToMoveOrCancel = actionDraft.Wrapper.OrderToMoveOrCancel;
                CancelThisAction = actionDraft.CancelThisAction;
            }

            public RecalculateState RecalcState {get; private set;}
            public VMStrategy VMStrategy {get; private set;}
            public OrderWrapper Wrapper {get; private set;}
            public RobotOptionOrder OrderToMoveOrCancel {get; private set;}
            public ActionType Action {get; private set;}
            public int Size {get; private set;}
            public decimal ConsiderPrice {get; private set;}
            public decimal Price {get; private set;}

            public double TargetIv {get; private set;}
            public decimal PriceCorrection {get; private set;}
            public bool CancelThisAction {get; private set;}

            public int VegaVolumeLimit => Wrapper.Direction == Sides.Buy ? RecalcState.VegaVolLimitBuy : RecalcState.VegaVolLimitSell;
            public int VegaVolumeTarget => Wrapper.Direction == Sides.Buy ? RecalcState.VegaVolTargetBuy : RecalcState.VegaVolTargetSell;
            public int MMVegaVolumeLimit => Wrapper.Direction == Sides.Buy ? RecalcState.MMVegaVolLimitBuy : RecalcState.MMVegaVolLimitSell;

            public int GammaVolumeLimit => Wrapper.Direction == Sides.Buy ? RecalcState.GammaVolLimitBuy : RecalcState.GammaVolLimitSell;
            public int GammaVolumeTarget => Wrapper.Direction == Sides.Buy ? RecalcState.GammaVolTargetBuy : RecalcState.GammaVolTargetSell;
            public int MMGammaVolumeLimit => Wrapper.Direction == Sides.Buy ? RecalcState.MMGammaVolLimitBuy : RecalcState.MMGammaVolLimitSell;

            // todo not calculated
            public int VannaVolumeLimit {get; set;}
            public int VannaVolumeTarget {get; set;}
            public int VommaVolumeLimit {get; set;}
            public int VommaVolumeTarget {get; set;}

            public int LastSecondTransactions {get; set;}

            public Sides Direction {get {return Wrapper.Direction;}}
        }

        #endregion

        public FuturesInfo.IFutureParams FutParams {get; set;}

        public int Position {get; set;}
        public DateTime LastDealTime {get; set;}

        public PricePair FutQuote {get; set;}
        public PricePair OptQuote {get; set;}
        public PricePair LastCalcOptQuote {get; set;}

        public double OptionCalcBid => (double)OptQuote.Bid;
        public int OptionCalcBidVol {get; set;}
        public double OptionCalcAsk => (double)OptQuote.Ask;
        public int OptionCalcAskVol {get; set;}
        public double FutureCalcBid => (double)FutQuote.Bid;
        public double FutureCalcAsk => (double)FutQuote.Ask;
        public int GlassBidVolume {get; set;}
        public int GlassOfferVolume {get; set;}

        public int VegaVolLimitBuy {get; private set;}
        public int VegaVolLimitSell {get; private set;}
        public int GammaVolLimitBuy {get; private set;}
        public int GammaVolLimitSell {get; private set;}
        public int MMVegaVolLimitBuy {get; private set;}
        public int MMVegaVolLimitSell {get; private set;}
        public int MMGammaVolLimitBuy {get; private set;}
        public int MMGammaVolLimitSell {get; private set;}

        public int VegaVolTargetBuy {get; private set;}
        public int VegaVolTargetSell {get; private set;}
        public int GammaVolTargetBuy {get; private set;}
        public int GammaVolTargetSell {get; private set;}

        public int RegularBuyOpen {get; set;}
        public int RegularBuyClose {get; set;}
        public int RegularSellOpen {get; set;}
        public int RegularSellClose {get; set;}
        public int VegaBuy {get; set;}
        public int VegaSell {get; set;}
        public int GammaBuy {get; set;}
        public int GammaSell {get; set;}

        public CanTradeState CanTrade {get; set;}

        public IOptionModelData ModelData {get; set;}

        public DateTime Time {get; set;}

        public List<OrderAction> OrderActions {get; private set;}

        public string LogStr => 
                        "bid={0}, ask={1}, pos={2}, vega(lb,ls,tb,ts,mmlb,mmls)=({3},{4},{5},{6},{7},{8}), gamma(lb,ls,tb,ts,mmlb,mmls)=({9},{10},{11},{12},{13},{14})"
                        .Put(OptQuote.Bid, OptQuote.Ask, Position,
                             VegaVolLimitBuy, VegaVolLimitSell, VegaVolTargetBuy, VegaVolTargetSell, MMVegaVolLimitBuy, MMVegaVolLimitSell,
                             GammaVolLimitBuy, GammaVolLimitSell, GammaVolTargetBuy, GammaVolTargetSell, MMGammaVolLimitBuy, MMGammaVolLimitSell);
                

        public decimal MinSellPrice {get; private set;}
        public decimal MaxBuyPrice {get; private set;}

        public RecalculateState() {
            OrderActions = new List<OrderAction>();
        }

        public void UpdateFutureParams(OptionModel model, IConfigGeneral cfgGeneral) {
            var vega = model.Vega;
            var gamma = model.Gamma;
            var vanna = model.Vanna;
            var vomma = model.Vomma;

            if(vega.IsZero() || gamma.IsZero()) {
                VegaVolLimitBuy = VegaVolLimitSell = GammaVolLimitBuy = GammaVolLimitSell = 0;
                MMVegaVolLimitBuy = MMVegaVolLimitSell = MMGammaVolLimitBuy = MMGammaVolLimitSell = 0;
                return;
            }

            var p = FutParams;
            var vegaCoeff = (double)cfgGeneral.VegaCoeff;
            var gammaCoeff = (double)cfgGeneral.GammaCoeff;
            var vommaLongLimit = p.VommaLongLimit / Math.Abs(vomma);
            var vommaShortLimit = p.VommaShortLimit / Math.Abs(vomma);

            double vegaVolLimitBuy, vegaVolLimitSell;
            double gammaVolLimitBuy, gammaVolLimitSell;

            if(model.OptionType == OptionTypes.Call) {
                var vegaCallBuyLimit = p.VegaCallBuyLimit / vega;
                var vegaCallSellLimit = p.VegaCallSellLimit / vega;

                vegaVolLimitBuy = Math.Round(Util.Min(p.VegaBuyLimit / vega, vegaCallBuyLimit, vommaLongLimit));
                vegaVolLimitSell = Math.Round(Util.Min(p.VegaSellLimit / vega, vegaCallSellLimit, vommaShortLimit));
                gammaVolLimitBuy = Math.Round(Util.Min(p.GammaBuyLimit / gamma, vegaCallBuyLimit, vommaLongLimit));
                gammaVolLimitSell = Math.Round(Util.Min(p.GammaSellLimit / gamma, vegaCallSellLimit, vommaShortLimit));
            } else {
                var vegaPutBuyLimit = p.VegaPutBuyLimit / vega;
                var vegaPutSellLimit = p.VegaPutSellLimit / vega;

                vegaVolLimitBuy = Math.Round(Util.Min(p.VegaBuyLimit / vega, vegaPutBuyLimit, vommaLongLimit));
                vegaVolLimitSell = Math.Round(Util.Min(p.VegaSellLimit / vega, vegaPutSellLimit, vommaShortLimit));
                gammaVolLimitBuy = Math.Round(Util.Min(p.GammaBuyLimit / gamma, vegaPutBuyLimit, vommaLongLimit));
                gammaVolLimitSell = Math.Round(Util.Min(p.GammaSellLimit / gamma, vegaPutSellLimit, vommaShortLimit));
            }

            var vannaLongLimit = p.VannaLongLimit / Math.Abs(vanna);
            var vannaShortLimit = p.VannaShortLimit / Math.Abs(vanna);

            // ванна-лимиты
            if(model.Moneyness < 0) {
                // покупка колла без денег или пута в деньгах - верхний лимит
                VegaVolLimitBuy = Math.Min(vegaVolLimitBuy, Math.Round(vannaLongLimit)).ToInt32Checked();
                GammaVolLimitBuy = Math.Min(gammaVolLimitBuy, Math.Round(vannaLongLimit)).ToInt32Checked();

                // продажа колла без денег или пута в деньгах - нижний лимит
                VegaVolLimitSell = Math.Min(vegaVolLimitSell, Math.Round(vannaShortLimit)).ToInt32Checked();
                GammaVolLimitSell = Math.Min(gammaVolLimitSell, Math.Round(vannaShortLimit)).ToInt32Checked();
            } else {
                // покупка колла в деньгах или пута без денег - нижний лимит
                VegaVolLimitBuy = Math.Min(vegaVolLimitBuy, Math.Round(vannaShortLimit)).ToInt32Checked();
                GammaVolLimitBuy = Math.Min(gammaVolLimitBuy, Math.Round(vannaShortLimit)).ToInt32Checked();

                // продажа колла в деньгах или пута без денег - верхний лимит
                VegaVolLimitSell = Math.Min(vegaVolLimitSell, Math.Round(vannaLongLimit)).ToInt32Checked();
                GammaVolLimitSell = Math.Min(gammaVolLimitSell, Math.Round(vannaLongLimit)).ToInt32Checked();
            }

            MMVegaVolLimitBuy = model.MMVegaVolLimitBuy = Math.Round(p.MMVegaBuyLimit / vega).ToInt32Checked();
            MMVegaVolLimitSell = model.MMVegaVolLimitSell = Math.Round(p.MMVegaSellLimit / vega).ToInt32Checked();
            MMGammaVolLimitBuy = model.MMGammaVolLimitBuy = Math.Round(p.MMGammaBuyLimit / gamma).ToInt32Checked();
            MMGammaVolLimitSell = model.MMGammaVolLimitSell = Math.Round(p.MMGammaSellLimit / gamma).ToInt32Checked();

            VegaVolTargetBuy = Math.Min(VegaVolLimitBuy, Math.Round(vegaCoeff * p.VegaBuyTarget / vega).ToInt32Checked());
            VegaVolTargetSell = Math.Min(VegaVolLimitSell, Math.Round(vegaCoeff * p.VegaSellTarget / vega).ToInt32Checked());

            GammaVolTargetBuy = Math.Min(GammaVolLimitBuy, Math.Round(gammaCoeff * p.GammaBuyTarget / gamma).ToInt32Checked());
            GammaVolTargetSell = Math.Min(GammaVolLimitSell, Math.Round(gammaCoeff * p.GammaSellTarget / gamma).ToInt32Checked());
        }

        public void RecalcMinMaxPrices(IEnumerable<OrderWrapper> wrappers) {
            MinSellPrice = decimal.MaxValue;
            MaxBuyPrice = decimal.MinValue;

            foreach(var wrapper in wrappers) {
                if(wrapper.Direction == Sides.Buy) {
                    if(wrapper.MaxCurrentOrderPrice > MaxBuyPrice)
                        MaxBuyPrice = wrapper.MaxCurrentOrderPrice;
                } else {
                    if(wrapper.MinCurrentOrderPrice < MinSellPrice)
                        MinSellPrice = wrapper.MinCurrentOrderPrice;
                }
            }
        }

        public void Init() {
            OrderActions = new List<OrderAction>();
            RegularBuyOpen = RegularBuyClose = RegularSellOpen = RegularSellClose = 0;
            VegaBuy = VegaSell = GammaBuy = GammaSell = 0;
            VegaVolLimitBuy = VegaVolLimitSell = VegaVolTargetBuy = VegaVolTargetSell = MMVegaVolLimitBuy = MMVegaVolLimitSell = 0;
            GammaVolLimitBuy = GammaVolLimitSell = GammaVolTargetBuy = GammaVolTargetSell = MMGammaVolLimitBuy = MMGammaVolLimitSell = 0;
        }
    }
}
