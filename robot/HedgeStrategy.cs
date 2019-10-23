using System;
using System.Collections.Generic;
using System.Linq;
using MoreLinq;
using OptionBot.Config;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Messages;

namespace OptionBot.robot {
    /// <summary>
    /// Стратегия модуля хеджирования.
    /// </summary>
    public class HedgeStrategy : BaseRobotStrategy {
        #region fields

        static readonly TimeSpan _allTradesHandlerDelay = TimeSpan.FromMilliseconds(200);

        readonly RobotLogger.LatencyLogger _latencyLogger;
        readonly RobotLogger.TradeLogger _myTradeLogger;

        FuturesInfo Future {get {return (FuturesInfo)SecurityInfo; }}
        FutureTradingModule TradingModule {get {return Future.TradingModule;}}

        PricePair _futPrevQuote;
        decimal _futureChangeTrigger;

        HedgeCalculator Calculator {get {return Future.Calculator;}}

        int _numOptionStrategiesWhenOrderSent;
        bool _notEnoughMoneyWithRunningStrategies;
        Order _order;

        readonly IntCounter _recalcMessagesCounter = new IntCounter();
        public IntCounter RecalcMessagesCounter {get {return _recalcMessagesCounter;}}
        bool ThereAreRecalculationRequests {get {return _recalcMessagesCounter.Value > 0;}}

        DateTime _lastErrorDisplayTime;

        bool _settingsUpdated;

        bool _needRecalc;
        bool _waitGlobalDataEnd, _notifyParamsUpdatedOnPosChange;

        bool _processorInitialized;

        IConfigFuture CfgFuture {get {return Future.Config.Effective;}}

        ISecurityPriorityBooster _secPriorityBooster;

        #endregion

        #region init/deinit

        public HedgeStrategy(StrategyWrapper<HedgeStrategy> wrapper) : base(wrapper) {
            _latencyLogger = Robot.RobotLogger.LatencyHedge(Future);
            //_latencyHedgeCalc = Robot.RobotLogger.LatencyHedgeCalc(Future);
            _myTradeLogger = Robot.RobotLogger.Trades;
        }

        protected override void OnStarting() {
            Position = Robot.GetRealPosition(Security.Id);

            Future.Params = new FuturesInfo.FutureParams();

            Calculator.UpdateOptionsInfo();

            _secPriorityBooster = SecProcessor.GetPriorityBooster();

            UpdateFutureInfo();
        }

        protected override void OnStarted2() {
            _log.AddInfoLog("Хеджирующая стратегия на фьючерсе запущена. Позиция = {0}", Position);

            SecProcessor.Post(() => {
                _processorInitialized = true;
                Recalculate("start");
            });
        }

        protected override void OnStopped() {
            //Future.Params = new FuturesInfo.FutureParams();
            TradingModule.SetDeltaHedgeStatus(false);
            base.OnStopped();
        }

        protected override void OnStopped2() {
            base.OnStopped2();

            _secPriorityBooster.Do(spb => spb.Dispose());

            _log.AddInfoLog("Хеджирующая стратегия на фьючерсе остановлена. Позиция = {0}({1})", Robot.GetRealPosition(Security.Id), Position);
        }

        protected override void DisposeManaged() {
            if(!WasStopped) {
                _log.Dbg.AddWarningLog("Disposing not-stopped hedge strategy. Resetting delta hedge status...");
                TradingModule.SetDeltaHedgeStatus(false);
            }

            base.DisposeManaged();
        }

        protected override void OnStop(bool force) { }

        #endregion

        void Recalculate(string reason) {
            if(!_processorInitialized)
                return;

            Action action = () => {
                var recalculated = false;

                try {
                    if(!WasStarted || !IsStrategyActive || !PlazaTrader.RealtimeMonitor.InRealtimeMode || !TranRateController.CanTrade())
                        return;

                    if(ThereAreUnprocessedOrders || ThereAreRecalculationRequests || ThereAreUnprocessedDepthMessages || _waitGlobalDataEnd) {
                        NeedRecalc();
                        return;
                    }

                    var notifyParams = _notifyParamsUpdatedOnPosChange;
                    _notifyParamsUpdatedOnPosChange = false;

                    NeedRecalc(false);
                    try {
                        recalculated = true;
                        _latencyLogger.StartTimer();
                        RecalculateImpl(reason);
                    } finally {
                        _latencyLogger.StopAndCommit();
                    }

                    if(notifyParams)
                        Future.TradingModule.RaiseParamsUpdatedOnPositionChange();

                } catch(Exception e) {
                    OnStrategyFail("ошибка стратегии дельта хеджирования", e);
                } finally {
                    CheckStop("hedge recalc finally");

                    if(recalculated)
                        Future.HedgeLogger.Log((int)Position);

//                    else // todo REMOVE
//                        _log.Dbg.AddDebugLog($"Not calculated: started={WasStarted}, active={IsStrategyActive}, rt={PlazaTrader.RealtimeMonitor.InRealtimeMode}, trLimitExceeded={_transactionLimitExceeded}, orders={ThereAreUnprocessedOrders}, recReq={ThereAreRecalculationRequests}, depth={ThereAreUnprocessedDepthMessages}, wait={_waitGlobalDataEnd}");
                }
            };

            if(!IsInSecurityThread) {
                SecProcessor.Post(action, "recalc_hedge("+ reason +")", _recalcMessagesCounter);
            } else {
                action();
            }
        }

        /// <summary>
        /// Пересчитать параметры хеджирования и отправить заявки при необходимости.
        /// </summary>
        /// <param name="reason">Причина запуска модуля хеджирования.</param>
        void RecalculateImpl(string reason) {
            if(_order != null && !_order.IsInFinalState())
                return;

            //_log.Dbg.AddDebugLog("hedge recalc");
            var cfgFut = CfgFuture;

            if(_settingsUpdated) {
                _settingsUpdated = false;
                _futureChangeTrigger = cfgFut.FuturesChangeStartTrigger * Security.PriceStep;
            }

            var pair = Security.BestPair;
            if(pair == null)
                return;

            var state = new HedgeCalculatorInputData {
                FutureBid = pair.Bid.Return(q => q.Price, 0), 
                FutureAsk = pair.Ask.Return(q => q.Price, 0), 
                FuturePosition = (int)Position, 
                InputOptions = Calculator.CreateInputOptionsList(PlazaTrader.Positions),
                Now = MyConnector.GetMarketTime()
            };

            var dict = new Dictionary<string, Tuple<object, object>>();
            foreach(var tuple in state.InputOptions)
                dict[tuple.Item1.Series.SeriesId.Id] = Tuple.Create(tuple.Item1.Series.GetActiveStrikesState(OptionTypes.Call), tuple.Item1.Series.GetActiveStrikesState(OptionTypes.Put));

            state.StrikeStates = dict;

            _futPrevQuote = new PricePair(state.FutureBid, state.FutureAsk);

            if(ThereAreUnprocessedOrders || ThereAreRecalculationRequests) return;

            Calculator.Update(state);

            if(ThereAreUnprocessedOrders || ThereAreRecalculationRequests) return;

            var lastCalcDetails = Calculator.LastCalcDetails;

            Future.ExpositionCalculated = Calculator.LastUpdateSuccessful;

            if(!Calculator.LastUpdateSuccessful) {
                if(DateTime.UtcNow - _lastErrorDisplayTime > TimeSpan.FromSeconds(5)) {
                    _lastErrorDisplayTime = DateTime.UtcNow;
                    _log.AddWarningLog(Calculator.Messages);
                } else {
                    _log.Dbg.AddWarningLog(Calculator.Messages);
                }
                return;
            }

            var newParams = new FuturesInfo.FutureParams {
                VegaCallBuyLimit = (double)Math.Max(cfgFut.VegaCallLongLimit + cfgFut.VegaTarget - Calculator.VegaCallPortfolio, 0),
                VegaPutBuyLimit = (double)Math.Max(cfgFut.VegaPutLongLimit + cfgFut.VegaTarget - Calculator.VegaPutPortfolio, 0),
                VegaCallSellLimit = (double)Math.Max(cfgFut.VegaCallShortLimit - cfgFut.VegaTarget + Calculator.VegaCallPortfolio, 0),
                VegaPutSellLimit = (double)Math.Max(cfgFut.VegaPutShortLimit - cfgFut.VegaTarget + Calculator.VegaPutPortfolio, 0),
                VegaBuyLimit = (double)Math.Max(cfgFut.VegaLLimit + cfgFut.VegaTarget - Calculator.VegaPortfolio, 0),
                VegaSellLimit = (double)Math.Max(-cfgFut.VegaSLimit - cfgFut.VegaTarget + Calculator.VegaPortfolio, 0),
                MMVegaBuyLimit = (double)Math.Max(cfgFut.MMVegaLongLimit - Calculator.VegaPortfolio, 0),
                MMVegaSellLimit = (double)Math.Max(-cfgFut.MMVegaShortLimit + Calculator.VegaPortfolio, 0),
                GammaBuyLimit = (double)Math.Max(cfgFut.GammaLLimit + cfgFut.GammaTarget - Calculator.GammaPortfolio, 0),
                GammaSellLimit = (double)Math.Max(-cfgFut.GammaSLimit - cfgFut.GammaTarget + Calculator.GammaPortfolio, 0),
                MMGammaBuyLimit = (double)Math.Max(cfgFut.MMGammaLongLimit - Calculator.GammaPortfolio, 0),
                MMGammaSellLimit = (double)Math.Max(-cfgFut.MMGammaShortLimit + Calculator.GammaPortfolio, 0),
                VegaPortfolio = (double)Calculator.VegaPortfolio,
                GammaPortfolio = (double)Calculator.GammaPortfolio,
                VegaBuyTarget = (double)(Calculator.VegaPortfolio <= cfgFut.VegaHedgeSLimit + cfgFut.VegaTarget ? Math.Max(-Calculator.VegaPortfolio + cfgFut.VegaTarget, 0) : 0),
                VegaSellTarget = (double)(Calculator.VegaPortfolio >= cfgFut.VegaHedgeLLimit + cfgFut.VegaTarget ? Math.Max(Calculator.VegaPortfolio - cfgFut.VegaTarget, 0) : 0),
                GammaBuyTarget = (double)(Calculator.GammaPortfolio <= cfgFut.GammaHedgeSLimit + cfgFut.GammaTarget ? Math.Max(-Calculator.GammaPortfolio + cfgFut.GammaTarget, 0) : 0),
                GammaSellTarget = (double)(Calculator.GammaPortfolio >= cfgFut.GammaHedgeLLimit + cfgFut.GammaTarget ? Math.Max(Calculator.GammaPortfolio - cfgFut.GammaTarget, 0) : 0),
                VannaPortfolio = (double)Calculator.VannaPortfolio,
                VannaLongLimit = (double)Math.Max(cfgFut.VannaLLimit + Calculator.VannaPortfolio, 0),
                VannaShortLimit = (double)Math.Max(-cfgFut.VannaSLimit - Calculator.VannaPortfolio, 0),
                VommaPortfolio = (double)Calculator.VommaPortfolio,
                VommaLongLimit = (double)Math.Max(cfgFut.VommaLLimit + Calculator.VommaPortfolio, 0),
                VommaShortLimit = (double)Math.Max(-cfgFut.VommaSLimit - Calculator.VommaPortfolio, 0),
                ThetaPortfolio = (double)Calculator.ThetaPortfolio,
            };

            Future.Params = newParams;

            var deltaExpLimit = CfgGeneral.DeltaExpositionLimit;
            var calculatedHedgedFuturePosition = Calculator.CalculatedHedgedFuturePosition;
            var exposition = state.FuturePosition - calculatedHedgedFuturePosition;

            if(ThereAreUnprocessedOrders || ThereAreRecalculationRequests) return;

            decimal orderVol = 0;

            if(exposition < -deltaExpLimit + cfgFut.DeltaTarget)
                orderVol = Math.Abs(Math.Round(exposition - cfgFut.DeltaTarget));
            else if(exposition > deltaExpLimit + cfgFut.DeltaTarget)
                orderVol = -Math.Abs(Math.Round(exposition - cfgFut.DeltaTarget));

            if(orderVol == 0) {
                TradingModule.SetDeltaHedgeStatus(true);
            } else if(TradingModule.DeltaHedgeTradingAllowed) {
                var numOptStrategies = TradingModule.NumActiveOptionStrategies;
                if(_notEnoughMoneyWithRunningStrategies && numOptStrategies > 0) {
                    _log.Dbg.AddWarningLog($"Not sending hedge order ({orderVol}) because of error (not enough money) and there are still {numOptStrategies} option strategies running. Waiting for them to stop...");
                    TradingModule.SetDeltaHedgeStatus(false);
                } else {
                    var notEnoughError = _notEnoughMoneyWithRunningStrategies;
                    if(SendOrder(orderVol)) {
                        _log.Dbg.AddInfoLog($"hedge order {orderVol} (trId={_order.TransactionId}) notEnoughError={notEnoughError}. {lastCalcDetails}");
                        _log.Dbg.AddInfoLog("exposition={0}={1}-{2}; deltaExpLimit={3}; DeltaTarget={4}", exposition, state.FuturePosition, calculatedHedgedFuturePosition, deltaExpLimit, cfgFut.DeltaTarget);
                    } else {
                        TradingModule.SetDeltaHedgeStatus(false);
                    }
                }
            } else {
                TradingModule.SetDeltaHedgeStatus(false);
            }

            Future.Exposition = (double)exposition;
        }

        /// <summary>
        /// Послать рыночную заявку с заданным объемом.
        /// </summary>
        /// <param name="orderVol">Объем заявки. Покупка - если положительный. Продажа - если отрицательный.</param>
        bool SendOrder(decimal orderVol) {
            try {
                _order = CreateMarketOrder(orderVol);
            } catch(Exception e) {
                OnStrategyFail("создание рыночной заявки", e);
                return false;
            }

            ApplyOrderRules(_order);

            try {
                RegisterOrder(_order);
            } catch(Exception e) {
                OnStrategyFail("failed to register order", e);
                return false;
            }

            _notEnoughMoneyWithRunningStrategies = false;
            _numOptionStrategiesWhenOrderSent = TradingModule.NumActiveOptionStrategies;

            return true;
        }

        void TradingModuleOnModuleStateChanged(FutureTradingModule m) {
            Recalculate("future trading module state changed");
        }

        void ValuationParamsOnListOrItemChanged(bool isListChange) {
            Recalculate("valuation_params changed");
        }

        void ConfigOnEffectiveConfigChanged(ICfgPairFuture cfgPairFuture, string[] strings) {
            _settingsUpdated = true;
            Recalculate("future config changed");
        }

        void NeedRecalc(bool need = true) {
            _needRecalc = need;
        }

        /// <summary>
        /// Применить правила обработки событий заявки.
        /// </summary>
        void ApplyOrderRules(Order order) {
            (new IMarketRule[] {order.WhenMatched(), order.WhenRegisterFailed(), order.WhenCanceled()})
                .Or()
                .Do(arg => {
                    try {
                        if(order.State == OrderStates.Failed) {
                            if(!PlazaTrader.IsTransactionLimitFail((OrderFail)arg)) {
                                var isMoneyFail = PlazaTrader.IsNotEnoughMoneyFail((OrderFail)arg);
                                if(isMoneyFail)
                                    Robot.HandleNotEnoughMoney(Future);

                                if(!isMoneyFail || _numOptionStrategiesWhenOrderSent == 0)
                                    OnStrategyFail("hedge order failed", ((OrderFail)arg).Error);
                                else
                                    _notEnoughMoneyWithRunningStrategies = true;

                                TradingModule.SetDeltaHedgeStatus(false);
                            }
                        }

                        _log.Dbg.AddDebugLog($"hedge order done (trId={_order.TransactionId})");

                    } catch(Exception e) {
                        OnStrategyFail("order done handler error", e);
                    }
                })
                .Once()
                .Apply(this);

            order.WhenAllTrades().Do(t => RobotThread.DelayedAction(() => {
                var trades = t.ToArray();
                if(trades.Length == 0) {
                    _log.Dbg.AddErrorLog("WhenAllTrades({0}): received empty trades array", order.TransactionId);
                    return;
                }

                var robotStartTime = RobotData.RobotStartTime;
                var firstTrade = trades[0];
                var futureTradeTime = firstTrade.Trade.Time;
                var price = (double)trades.GetAveragePrice();
                var allMyTrades = PlazaTrader.MyTrades as MyTrade[];

                _log.Dbg.AddInfoLog("Received AllTrades({0}) for order {1}", trades.Length, firstTrade.Order.Id);

                if(allMyTrades == null) {
                    _log.Dbg.AddErrorLog("Unable to cast PlazaTrader.MyTrades (type={0})", PlazaTrader.MyTrades.GetType().Name);
                    return;
                }

                var optTradesToLog = new List<MyTradeEx>();

                for(var i = allMyTrades.Length - 1; i >= 0; --i) {
                    var mt = (MyTradeEx)allMyTrades[i];

                    if(mt.Trade.Time > futureTradeTime)
                        continue; // skip trades which happened after future trade

                    var sec = mt.Order.Security;
                    if(sec.UnderlyingSecurityId != Security.Id || sec.Type != SecurityTypes.Option)
                        continue;

                    if(mt.TradeIv != null || mt.Trade.Time < robotStartTime)
                        break;

                    var option = (OptionInfo)RobotData.GetSecurityById(sec.Id);
                    if(option == null) {
                        _log.Dbg.AddErrorLog("future trades handler: unable to get SecurityInfo for '{0}'", sec.Id);
                        return;
                    }

                    if(mt.Trade.OrderDirection == null)
                        _log.Dbg.AddWarningLog($"MyTrade direction not initialized. tradeId={mt.Trade.Id}, orderId={mt.Order.Id}");

                    mt.TradeIv = option.Model.CalculateIv(mt.Trade.Time, (double)mt.Trade.Price, price);
                    optTradesToLog.Add(mt);
                }

                Enumerable.Reverse(optTradesToLog).ForEach(mt => _myTradeLogger.LogMyTrade(mt));
                trades.Cast<MyTradeEx>().ForEach(mt => _myTradeLogger.LogMyTrade(mt));

                _log.Dbg.AddInfoLog("TradeIv calculated for trades: {0}", string.Join(",", optTradesToLog.Select(myt => myt.Trade.Id)));
            }, _allTradesHandlerDelay))
            .Apply(this);
        }

        void PlazaTraderOnMarketDepthChanged(MarketDepth depth) {
            if(depth.Security.Id != Security.Id) return;

            if(UpdateFutureInfo() || _needRecalc)
                Recalculate("future changed");
        }

        protected override void RobotOnCanTradeStateChanged() {
            // стратегия будет при необходимости остановлена из Robot.cs
        }

        bool UpdateFutureInfo() {
            var futPair = Security.BestPair;
            decimal bid, ask;

            if(futPair != null) {
                bid = futPair.Bid.Return(b => b.Price, 0);
                ask = futPair.Ask.Return(b => b.Price, 0);
            } else {
                bid = ask = 0;
            }

            return Math.Max(Math.Abs(bid - _futPrevQuote.Bid), Math.Abs(ask - _futPrevQuote.Ask)) >= _futureChangeTrigger;
        }

        void OnStrategyPositionChange() {
            HandleFutureOrOptionPosChange(Future);
        }

        void OnChildOptionStrategyPositionChange(OptionInfo option) {
            HandleFutureOrOptionPosChange(option);
        }

        void HandleFutureOrOptionPosChange(SecurityInfo sec) {
            if(!sec.SecProcessor.IsOrdersDataProcessing)
                return;

            _log.Dbg.AddDebugLog($"Position changed for {sec.Code}. Waiting for global stream end to recalculate.");

            _waitGlobalDataEnd = _notifyParamsUpdatedOnPosChange = true;
            NeedRecalc();
        }

        // событие вызывается после обработки всех сообщений с обновлениями ордеров в параллельных потоках.
        // событие вызывается на потоке инструмента, по которому пришло последнее обновление в пакете.
        void OnGlobalOrdersDataEnd() {
            _waitGlobalDataEnd = false;
            if(_needRecalc) {
                _log.Dbg.AddDebugLog($"global stream end: pos={Position}");
                Recalculate("global orders data end");
            }
        }

        void TranRateControllerOnStateChanged(TransactionControllerState newState) {
            if(newState != TransactionControllerState.LimitExceeded)
                Recalculate($"TransactionControllerState: {newState}");
        }

        void OptionSeriesInfoOnSeriesConfigChanged(OptionSeriesInfo series) {
            if(series.Future.Id == Future.Id)
                Recalculate($"Series cfg changed ({series.SeriesId.StrFutDate})");
        }

        #region subscription

        protected override void OnSubscribe() {
            base.OnSubscribe();
            PlazaTrader.OrdersDataEnd                                   += OnGlobalOrdersDataEnd;
            PositionChanged                                             += OnStrategyPositionChange;
            Future.TradingModule.ChildOptionStrategyPositionChange      += OnChildOptionStrategyPositionChange;
            PlazaTrader.MarketDepthChanged                              += PlazaTraderOnMarketDepthChanged;
            TradingModule.ModuleStateChanged                            += TradingModuleOnModuleStateChanged;
            Future.Config.EffectiveConfigChanged                        += ConfigOnEffectiveConfigChanged;
            Controller.ConfigProvider.ValuationParams.ListOrItemChanged += ValuationParamsOnListOrItemChanged;
            TranRateController.StateChanged                             += TranRateControllerOnStateChanged;
            OptionSeriesInfo.SeriesConfigChanged                        += OptionSeriesInfoOnSeriesConfigChanged;
        }

        protected override void OnUnsubscribe() {
            base.OnUnsubscribe();
            PlazaTrader.OrdersDataEnd                                   -= OnGlobalOrdersDataEnd;
            PositionChanged                                             -= OnStrategyPositionChange;
            Future.TradingModule.ChildOptionStrategyPositionChange      -= OnChildOptionStrategyPositionChange;
            PlazaTrader.MarketDepthChanged                              -= PlazaTraderOnMarketDepthChanged;
            TradingModule.ModuleStateChanged                            -= TradingModuleOnModuleStateChanged;
            Future.Config.EffectiveConfigChanged                        -= ConfigOnEffectiveConfigChanged;
            Controller.ConfigProvider.ValuationParams.ListOrItemChanged -= ValuationParamsOnListOrItemChanged;
            TranRateController.StateChanged                             -= TranRateControllerOnStateChanged;
            OptionSeriesInfo.SeriesConfigChanged                        -= OptionSeriesInfoOnSeriesConfigChanged;
        }

        #endregion
    }
}
