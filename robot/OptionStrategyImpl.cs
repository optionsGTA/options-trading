using System;
using System.Collections.Generic;
using System.Linq;
using Ecng.Common;
using OptionBot.Config;
using StockSharp.Algo;
using StockSharp.Messages;

namespace OptionBot.robot {
    public interface IOptionStrategy {
        IEnumerable<OrderWrapper> MyOrders {get;}
        StrategyType StrategyType {get;}
        void BeginCalculation();
        VMStrategy ActiveStrategy {get;}
        IConfigStrategy ActiveConfig {get;}
        void Recalculate(RecalculateState state, RecalcReason reason);
    }

    public abstract class OptionStrategy : BaseStrategy, IOptionStrategy {
        protected readonly OrderActionDraft _buyOpen, _buyClose, _sellOpen, _sellClose;
        readonly OrderActionDraft[] _myOrders;

        int _minIncreaseOrderVolume, _minDecreaseOrderVolume;
        decimal _changeWide, _changeNarrow;

        OptionMainStrategy _parent;
        protected OptionMainStrategy MainStrategy   => _parent ?? (_parent = (OptionMainStrategy)Parent);
        public OptionInfo Option                    => (OptionInfo)SecurityInfo;
        protected FuturesInfo Future                => Option.Future;
        protected OptionModel Model                 => Option.Model;
        public IEnumerable<OrderWrapper> MyOrders {get {return _myOrders.Select(o => o.Wrapper);}}

        protected VMStrategy _vmStrategy;
        protected IConfigStrategy _currentConfig;

        public StrategyType StrategyType {get;}

        protected override bool CanStop => _buyOpen.Wrapper.IsInactive && _buyClose.Wrapper.IsInactive && _sellOpen.Wrapper.IsInactive && _sellClose.Wrapper.IsInactive;

        protected CanTradeState CanTrade {get; private set;}

        ISecurityPriorityBooster _secPriorityBooster;

        public VMStrategy ActiveStrategy => _vmStrategy;
        public IConfigStrategy ActiveConfig => _currentConfig;

        protected ICalculatedConfigStrategy CalcParams => _vmStrategy.CalcParams;

        void UpdateCanTrade(RecalculateState state, IConfigStrategy cfg) {
            CanTradeState ct;

            if(IsStopping || !IsStrategyActive || _vmStrategy == null || cfg == null)
                ct = CanTradeState.TradingDisabled;
            else
                ct = TranRateController.CanTrade(StrategyType) ? state.CanTrade : CanTradeState.TradingDisabled;

            ct = ct.Normalize();

            if(CanTrade == ct)
                return;

            var wasAllowed = CanTrade.CanTrade();
            var newAllowed = ct.CanTrade();

            if(wasAllowed == newAllowed) {
                CanTrade = ct;
                return;
            }

            _log.Dbg.AddDebugLog($"CanTrade: {CanTrade} => {ct}, stopping={IsStopping}, active={IsStrategyActive}, vmNull={_vmStrategy==null}, cfgNull={cfg==null}");
            CanTrade = ct;
        }

        #region init/deinit

        protected OptionStrategy(StrategyWrapper wrapper, StrategyType sType) : base(wrapper.Robot, wrapper.SecurityInfo, sType.ToString()) {
            StrategyType = sType;
            _buyOpen = new OrderActionDraft(new OrderWrapper(this, () => MainStrategy, Sides.Buy, true, "bo"));
            _buyClose = new OrderActionDraft(new OrderWrapper(this, () => MainStrategy, Sides.Buy, false, "bc"));
            _sellOpen = new OrderActionDraft(new OrderWrapper(this, () => MainStrategy, Sides.Sell, true, "so"));
            _sellClose = new OrderActionDraft(new OrderWrapper(this, () => MainStrategy, Sides.Sell, false, "sc"));

            _myOrders = new[] {_buyOpen, _buyClose, _sellOpen, _sellClose};
        }

        protected override void OnStopping() {
            TranRateController.DeregisterStrategy(this);
            Array.ForEach(_myOrders, o => o.Dispose());

            base.OnStopping();
        }

        protected override void OnStarting() {
            if(_buyOpen.IsDisposed) throw new InvalidOperationException("can't start second time");
            if(MainStrategy == null) throw new InvalidOperationException("Main option strategy not found");

            _secPriorityBooster = SecProcessor.GetPriorityBooster();
            TranRateController.RegisterStrategy(this);
        }

        protected override void OnStarted2() {
            _log.AddInfoLog("Стратегия {0} запущена.", StrategyType);

            MainStrategy.Do(s => {
                s.ResetMarketIv(RecalcReason.OnStart.GetRecalcReasonDescription());
                s.Recalculate(RecalcReason.OnStart);
            });
        }

        protected override void OnStop(bool force) {
            var statuses = new List<string>();

            foreach(var order in _myOrders.Where(o => !o.Wrapper.IsInactive)) {
                statuses.Add(order.Wrapper.ToString());

                if(!force) {
                    if(order.Wrapper.CanCancel)
                        order.Wrapper.Cancel();
                } else {
                    order.Wrapper.ForceReset();
                }
            }

            if(statuses.Any())
                _log.Dbg.AddDebugLog("OnStop(force={0}): {1}", force, string.Join(", ", statuses.Select(s => "(" + s + ")")));
        }

        protected override void OnStopped2() {
            base.OnStopped2();

            if(StrategyFailed)
                _vmStrategy.Do(vms => vms.ForceDeactivateStrategy());

            _secPriorityBooster.Do(spb => spb.Dispose());

            _log.AddInfoLog("Стратегия {0} остановлена.", StrategyType);
        }

        #endregion

        #region subscription

        void SubscribeWrapper(OrderWrapper w) {
            w.CurrentOrderChanged += WrapperOnCurrentOrderChanged;
            w.OrderStateChanged += WrapperOnOrderStateChanged;
            w.NotEnoughMoney += WrapperOnNotEnoughMoney;
            w.FatalError += WrapperOnFatalError;
        }

        void UnsubscribeWrapper(OrderWrapper w) {
            w.CurrentOrderChanged -= WrapperOnCurrentOrderChanged;
            w.OrderStateChanged -= WrapperOnOrderStateChanged;
            w.NotEnoughMoney -= WrapperOnNotEnoughMoney;
            w.FatalError -= WrapperOnFatalError;
        }

        protected override void OnSubscribe() {
            base.OnSubscribe();
            Array.ForEach(_myOrders, o => SubscribeWrapper(o.Wrapper));
        }

        protected override void OnUnsubscribe() {
            base.OnUnsubscribe();
            Array.ForEach(_myOrders, o => UnsubscribeWrapper(o.Wrapper));
        }

        #endregion

        public void BeginCalculation() {
            if(MainStrategy == null || !IsStrategyActive)
                return;

            _vmStrategy = Option.AtmShift.With(shift => shift.Strategy(StrategyType));
            _currentConfig = _vmStrategy?.CfgStrategy;
        }

        public void Recalculate(RecalculateState state, RecalcReason reason) {
            CheckStop("recalc " + StrategyType);

            if(MainStrategy == null) { _log.Dbg.AddWarningLog("recalculate({0}): parent is null", reason); return; }
            if(!IsStrategyActive) { _log.Dbg.AddWarningLog("recalculate({0}): strategy is not active", reason); return; }

            if(_vmStrategy == null)
                _log.Dbg.AddWarningLog("recalculate({0}): VMStrategy is null", reason);

            Array.ForEach(_myOrders, o => o.Reset());

            var oldCanTrade = CanTrade;

            UpdateCanTrade(state, _currentConfig);

            var cfg = _currentConfig;

            if(CanTrade.CanTrade() && CalcParams.TradingAllowedByLiquidity) {
                RecalculateVolumes(state);

                if(cfg.StrategyType != StrategyType.Regular && (_buyClose.Volume != 0 || _sellClose.Volume != 0)) {
                    _log.Dbg.AddErrorLog($"unexpected close volume for {cfg.StrategyType}: buyClose={_buyClose.Volume}, sellClose={_sellClose.Volume}");
                    _buyClose.Volume = 0;
                    _sellClose.Volume = 0;
                }

                RecalculatePrices(state);
            }

            if(_currentConfig != null) {
                _changeWide = CalcParams.ChangeWide.ToDecimalChecked() * Option.MinStepSize;
                _changeNarrow = CalcParams.ChangeNarrow.ToDecimalChecked() * Option.MinStepSize;
                _minIncreaseOrderVolume = _currentConfig.MinIncreaseOrderVolume;
                _minDecreaseOrderVolume = _currentConfig.MinDecreaseOrderVolume;
            }

            RecalculateState.OrderAction action;

            if((action = ProcessOrder(_buyOpen, state)) != null)
                state.OrderActions.Add(action);

            if((action = ProcessOrder(_buyClose, state)) != null)
                state.OrderActions.Add(action);

            if((action = ProcessOrder(_sellOpen, state)) != null)
                state.OrderActions.Add(action);

            if((action = ProcessOrder(_sellClose, state)) != null)
                state.OrderActions.Add(action);

            if(oldCanTrade != CanTrade) {
                const string sep = "-";
                var actionsStr = state.OrderActions.Select(a => a.Action + sep + a.Direction + sep + a.Size + sep + a.Price + sep + a.OrderToMoveOrCancel?.TransactionId).Join(",");
                _log.Dbg.AddDebugLog($"CanTrade: actions=({actionsStr})");
            }
        }

        RecalculateState.OrderAction ProcessOrder(OrderActionDraft actionDraft, RecalculateState recalcState) {
            RecalculateState.ActionType? action = null;
            // при наличии частично сведенной заявки newSize считается исходя из баланса заявки (и позиции с учетом сведенной части)
            // поэтому при операции Move необходимо прибавлять сведенную часть к объему заявки
            // например: есть заявка vol/bal = 6/5, newSize=5 ==> в операции move должен быть объем 5+1=6, тогда при regime=3 заявка активируется с объемом 5
            var newSize = actionDraft.Volume;
            var newPrice = actionDraft.Price;

            //if(newSize != 0 && newPrice <= 0) _log.Dbg.AddWarningLog("ProcessOrder: newSize={0}, newPrice={1}", newSize, newPrice);
            var wrapper = actionDraft.Wrapper;
            int curVolume, curBalance;

            if(wrapper.CurrentOrder != null) {
                curVolume = (int)wrapper.CurrentOrder.Volume;
                curBalance = (int)wrapper.CurrentOrder.Balance;
            } else {
                curVolume = curBalance = 0;
            }

            if(wrapper.IsInactive) {
                if(newSize > 0 && newPrice > 0)
                    action = RecalculateState.ActionType.New;
            } else if(!wrapper.IsCancelRequested) {
                var oldPrice = wrapper.Price;
                var needChangeByPrice = newPrice != oldPrice;

                needChangeByPrice &= actionDraft.Wrapper.Direction == Sides.Buy ?
                                            actionDraft.ConsiderPrice - oldPrice >= _changeNarrow || oldPrice - actionDraft.ConsiderPrice >= _changeWide :
                                            actionDraft.ConsiderPrice - oldPrice >= _changeWide   || oldPrice - actionDraft.ConsiderPrice >= _changeNarrow;

                var needChangeByVolume = curBalance != 0 && 
                                         (newSize - curBalance >= _minIncreaseOrderVolume || curBalance - newSize >= _minDecreaseOrderVolume);

                if(!wrapper.IsProcessing) {
                    if(newSize == 0 || !(newPrice > 0)) {
                        action = RecalculateState.ActionType.Cancel;
                    } else if(needChangeByPrice || needChangeByVolume) {
                        action = RecalculateState.ActionType.Move;
                        actionDraft.Volume = newSize + (curVolume - curBalance);
                    }
                } else if(needChangeByPrice || newSize == 0 || !(newPrice > 0)) {
                    action = RecalculateState.ActionType.Cancel;
                }
            }

            var cfg = _currentConfig; // can be null, but in that case action==Cancel
            if(Model.GreeksRegime == GreeksRegime.Liquid && (action == RecalculateState.ActionType.New || action == RecalculateState.ActionType.Move) && cfg.CheckOrderIv) {
                var dir = actionDraft.Wrapper.Direction;
                var input = Model.LastData.Input;
                var futPrice = (Option.OptionType == OptionTypes.Call) == (dir == Sides.Buy) ? input.FutureCalcBid : input.FutureCalcAsk;

                actionDraft.TargetIv = Model.CalculateIv(input.Time, (double)newPrice, futPrice);

                if((dir == Sides.Buy  && actionDraft.TargetIv > cfg.OrderHighestBuyIvLimit) ||
                   (dir == Sides.Sell && actionDraft.TargetIv < cfg.OrderLowestSellIvLimit)) {

                    if(action == RecalculateState.ActionType.New)
                        actionDraft.CancelThisAction = true;
                    else
                        action = RecalculateState.ActionType.Cancel;
                }
            }

            return action == null ? null : new RecalculateState.OrderAction(action.Value, actionDraft, recalcState, _vmStrategy);
        }

        #region order handlers

        void WrapperOnCurrentOrderChanged(OrderWrapper w) {
        }

        void WrapperOnOrderStateChanged(OrderWrapper w, string comment) {
            MainStrategy.Recalculate(RecalcReason.OrderOrPositionUpdate);
        }

        void WrapperOnNotEnoughMoney(OrderWrapper w) {
            MainStrategy.HandleNotEnoughMoney(StrategyType.ToString());
        }

        void WrapperOnFatalError(OrderWrapper w, string comment) {
            OnStrategyFail("{0} strategy failed: {1}".Put(StrategyType, comment), null);
        }

        #endregion

        protected abstract void RecalculateVolumes(RecalculateState state);

        void RecalculatePrices(RecalculateState state) {
            if(Model.GreeksRegime == GreeksRegime.Liquid)
                RecalculatePricesLiquid(state);
            else
                RecalculatePricesIlliquid(state);
        }

        void RecalculatePricesLiquid(RecalculateState state) {
            var cfg = _currentConfig;
            var action = _buyOpen;
            if(action.Volume != 0) {
                var fromMkt = (decimal)(Model.MarketBid - CalcParams.ShiftOL);
                if(!cfg.CurveOrdering) {
                    var fromCurve = (decimal)(Model.CurveBid - cfg.PassiveCurveShiftOL);
                    action.ConsiderPrice = !cfg.CurveControl ? fromMkt : Math.Min(fromMkt, fromCurve);
                } else {
                    var fromCurve = (decimal)(Model.CurveBid - cfg.ActiveCurveShiftOL);
                    action.ConsiderPrice = !cfg.MarketControl ? fromCurve : Math.Min(fromCurve, fromMkt);
                }

                action.Price = Math.Max(0, action.ConsiderPrice).FloorStep(Option.MinStepSize);
                action.PriceCorrection = action.Price - state.OptQuote.Bid;
            }

            action = _buyClose;
            if(action.Volume != 0) {
                var fromMkt = (decimal)(Model.MarketBid - CalcParams.ShiftCS);
                if(!cfg.CurveOrdering) {
                    var fromCurve = (decimal)(Model.CurveBid - cfg.PassiveCurveShiftCS);
                    action.ConsiderPrice = !cfg.CurveControl ? fromMkt : Math.Min(fromMkt, fromCurve);
                } else {
                    var fromCurve = (decimal)(Model.CurveBid - cfg.ActiveCurveShiftCS);
                    action.ConsiderPrice = !cfg.MarketControl ? fromCurve : Math.Min(fromCurve, fromMkt);
                }

                action.Price = Math.Max(0, action.ConsiderPrice).FloorStep(Option.MinStepSize);
                action.PriceCorrection = action.Price - state.OptQuote.Bid;
            }

            action = _sellOpen;
            if(action.Volume != 0) {
                var fromMkt = (decimal)(Model.MarketOffer + CalcParams.ShiftOS);
                if(!cfg.CurveOrdering) {
                    var fromCurve = (decimal)(Model.CurveOffer + cfg.PassiveCurveShiftOS);
                    action.ConsiderPrice = !cfg.CurveControl ? fromMkt : Math.Max(fromMkt, fromCurve);
                } else {
                    var fromCurve = (decimal)(Model.CurveOffer + cfg.ActiveCurveShiftOS);
                    action.ConsiderPrice = !cfg.MarketControl ? fromCurve : Math.Max(fromCurve, fromMkt);
                }

                action.Price = Math.Max(0, action.ConsiderPrice).CeilingStep(Option.MinStepSize);
                action.PriceCorrection = state.OptQuote.Ask - action.Price;
            }

            action = _sellClose;
            if(action.Volume != 0) {
                var fromMkt = (decimal)(Model.MarketOffer + CalcParams.ShiftCL);
                if(!cfg.CurveOrdering) {
                    var fromCurve = (decimal)(Model.CurveOffer + cfg.PassiveCurveShiftCL);
                    action.ConsiderPrice = !cfg.CurveControl ? fromMkt : Math.Max(fromMkt, fromCurve);
                } else {
                    var fromCurve = (decimal)(Model.CurveOffer + cfg.ActiveCurveShiftCL);
                    action.ConsiderPrice = !cfg.MarketControl ? fromCurve : Math.Max(fromCurve, fromMkt);
                }

                action.Price = Math.Max(0, action.ConsiderPrice).CeilingStep(Option.MinStepSize);
                action.PriceCorrection = state.OptQuote.Ask - action.Price;
            }
        }

        void RecalculatePricesIlliquid(RecalculateState state) {
            var cfg = _currentConfig;
            var buy = _buyOpen.Volume != 0 || _buyClose.Volume != 0;
            var sell = _sellOpen.Volume != 0 || _sellClose.Volume != 0;

            if(!buy && !sell) return;

            double ivBid, ivOffer;
            var useCurve = cfg.IlliquidCurveTrading && Model.CurveModelStatus == CurveModelStatus.Valuation;
            if(useCurve) {
                ivBid = Model.CurveIvBid;
                ivOffer = Model.CurveIvOffer;
            } else {
                ivBid = CalcParams.IlliquidIvBid;
                ivOffer = CalcParams.IlliquidIvOffer;
            }

            _sellOpen.ConsiderPrice = (decimal)Model.GetPremium(Sides.Sell, ivOffer);
            if(useCurve)
                _sellOpen.ConsiderPrice = Math.Max(0, _sellOpen.ConsiderPrice + (decimal)cfg.ActiveCurveShiftOS);

            _sellOpen.Price = _sellOpen.ConsiderPrice.CeilingStep(Option.MinStepSize);
            _sellOpen.PriceCorrection = state.OptQuote.Ask - _sellOpen.Price;

            if(buy) {
                _buyOpen.ConsiderPrice = (decimal)Model.GetPremium(Sides.Buy, ivBid);
                if(useCurve)
                    _buyOpen.ConsiderPrice = Math.Max(0, _buyOpen.ConsiderPrice - (decimal)cfg.ActiveCurveShiftOL);

                _buyOpen.Price = _buyOpen.ConsiderPrice.FloorStep(Option.MinStepSize);

                var orderSpread = CalcParams.OrderSpread.ToDecimalChecked();
                if(_sellOpen.Price - _buyOpen.Price < orderSpread)
                    _buyOpen.Price = Math.Max(Option.MinStepSize, _sellOpen.Price - orderSpread);

                _buyOpen.PriceCorrection = _buyOpen.Price - state.OptQuote.Bid;

                if(_buyClose.Volume != 0) {
                    _buyClose.ConsiderPrice = _buyOpen.ConsiderPrice;
                    _buyClose.Price = _buyOpen.Price;
                    _buyClose.PriceCorrection = _buyOpen.PriceCorrection;
                }
            }

            if(_sellClose.Volume != 0) {
                _sellClose.ConsiderPrice = _sellOpen.ConsiderPrice;
                _sellClose.Price = _sellOpen.Price;
                _sellClose.PriceCorrection = _sellOpen.PriceCorrection;
            }
        }

        public interface IOrderActionInfo {
            OrderWrapper Wrapper {get;}
            int Volume {get;}
            decimal ConsiderPrice {get;}
            decimal Price {get;}
            double TargetIv {get;}
            decimal PriceCorrection {get;}
            bool CancelThisAction {get;}
        }

        protected class OrderActionDraft : Disposable, IOrderActionInfo {
            readonly OrderWrapper _wrapper;

            public OrderWrapper Wrapper => _wrapper;
            public int Volume {get; set;}
            public decimal ConsiderPrice {get; set;}
            public decimal Price {get; set;}
            public double TargetIv {get; set;}
            public decimal PriceCorrection {get; set;}
            public bool CancelThisAction {get; set;}

            public OrderActionDraft(OrderWrapper wrapper) {
                _wrapper = wrapper;
            }

            public void Reset() {
                Volume = 0;
                CancelThisAction = false;
                Price = PriceCorrection = 0;
                TargetIv = 0;
            }

            protected override void DisposeManaged() {
                _wrapper.Dispose();
                base.DisposeManaged();
            }
        }
    }

    public class RegularStrategy : OptionStrategy {
        public RegularStrategy(StrategyWrapper<RegularStrategy> wrapper) : base(wrapper, StrategyType.Regular) {}

        protected override void RecalculateVolumes(RecalculateState state) {
            try {
                var cfg = _currentConfig;
                var canOpen = CanTrade.CanOpenPositions() && cfg.StrategyRegime != StrategyRegime.CloseOnly;

                if(canOpen) {
                    if(state.Position <= 0) {
                        if(cfg.StrategyDirection != StrategyOrderDirection.SellOnly)
                            _buyClose.Volume = Math.Max(0, Util.Min(-state.Position - state.VegaBuy - state.GammaBuy, state.VegaVolLimitBuy, state.GammaVolLimitBuy, cfg.Incremental));

                        if(_buyClose.Volume + state.VegaBuy + state.GammaBuy >= -state.Position && cfg.StrategyDirection != StrategyOrderDirection.SellOnly)
                            _buyOpen.Volume = Math.Min(cfg.BalanceLimit, Math.Max(0, Util.Min(state.VegaVolLimitBuy, state.GammaVolLimitBuy, cfg.Incremental) - _buyClose.Volume));

                        if(cfg.StrategyDirection != StrategyOrderDirection.BuyOnly)
                            _sellOpen.Volume = Math.Max(0, Util.Min(cfg.BalanceLimit + state.Position, state.VegaVolLimitSell, state.GammaVolLimitSell, cfg.Incremental) - state.VegaSell - state.GammaSell);
                    }

                    if(state.Position >= 0) {
                        if(cfg.StrategyDirection != StrategyOrderDirection.BuyOnly)
                            _sellClose.Volume = Math.Max(0, Util.Min(state.Position - state.VegaSell - state.GammaSell, state.VegaVolLimitSell, state.GammaVolLimitSell, cfg.Incremental));

                        if(_sellClose.Volume + state.VegaSell + state.GammaSell >= state.Position && cfg.StrategyDirection != StrategyOrderDirection.BuyOnly)
                            _sellOpen.Volume = Math.Min(cfg.BalanceLimit, Math.Max(0, Util.Min(state.VegaVolLimitSell, state.GammaVolLimitSell, cfg.Incremental) - _sellClose.Volume));

                        if(cfg.StrategyDirection != StrategyOrderDirection.SellOnly)
                            _buyOpen.Volume = Math.Max(0, Util.Min(cfg.BalanceLimit - state.Position, state.VegaVolLimitBuy, state.GammaVolLimitBuy, cfg.Incremental) - state.VegaBuy - state.GammaBuy);
                    }

//                    if(Model.GreeksRegime == GreeksRegime.Illiquid) {
//                        _log.Dbg.AddDebugLog($"recalcvols: pos={state.Position} bo={_buyOpen.Volume} so={_sellOpen.Volume} vb={state.VegaBuy} vs={state.VegaSell} gb={state.GammaBuy} gs={state.GammaSell} vvlb={state.VegaVolLimitBuy} vvls={state.VegaVolLimitSell} gvlb={state.GammaVolLimitBuy} gvls={state.GammaVolLimitSell}");
//                    }

                } else {
                    if(state.Position < 0 && cfg.StrategyDirection != StrategyOrderDirection.SellOnly)
                        _buyClose.Volume = Math.Max(0, Math.Min(-state.Position - state.VegaBuy - state.GammaBuy, cfg.Incremental));
                    else if(state.Position > 0 && cfg.StrategyDirection != StrategyOrderDirection.BuyOnly)
                        _sellClose.Volume = Math.Max(0, Math.Min(state.Position - state.VegaSell - state.GammaSell, cfg.Incremental));
                }

                if(!cfg.CloseRegime) {
                    // объединение объемов на открытие/закрытие в один объем
                    _buyOpen.Volume += _buyClose.Volume;
                    _buyClose.Volume = 0;

                    _sellOpen.Volume += _sellClose.Volume;
                    _sellClose.Volume = 0;
                }

                // учитываем мин. объем заявки
                if(_buyOpen.Volume   < cfg.MinOrderVolume)  _buyOpen.Volume = 0;
                if(_sellOpen.Volume  < cfg.MinOrderVolume)  _sellOpen.Volume = 0;
                if(_buyClose.Volume  < cfg.MinOrderVolume)  _buyClose.Volume = 0;
                if(_sellClose.Volume < cfg.MinOrderVolume)  _sellClose.Volume = 0;

            } finally {
                state.RegularBuyOpen = _buyOpen.Volume;
                state.RegularBuyClose = _buyClose.Volume;
                state.RegularSellOpen = _sellOpen.Volume;
                state.RegularSellClose = _sellClose.Volume;
            }
        }
    }

    public class MMStrategy : OptionStrategy {
        public MMStrategy(StrategyWrapper<MMStrategy> wrapper) : base(wrapper, StrategyType.MM) {}

        protected override void RecalculateVolumes(RecalculateState state) {
            var cfg = _currentConfig;
            var regularBuy = state.RegularBuyOpen + state.RegularBuyClose;
            var regularSell = state.RegularSellOpen + state.RegularSellClose;
            var mmVol = CalcParams.CalcMMVolume;

            if(state.Position <= 0) {
                var otherBuy = regularBuy + state.VegaBuy + state.GammaBuy;

                _buyClose.Volume = Util.Min(state.MMVegaVolLimitBuy, 
                                            state.MMGammaVolLimitBuy, 
                                            Math.Max(0, cfg.Incremental - otherBuy), 
                                            Math.Max(0, mmVol - otherBuy), 
                                            Math.Max(0, -state.Position - otherBuy));

                if(_buyClose.Volume + regularBuy + state.VegaBuy + state.GammaBuy >= -state.Position)
                    _buyOpen.Volume = Math.Min(cfg.BalanceLimit, Math.Max(0, Util.Min(state.MMVegaVolLimitBuy, state.MMGammaVolLimitBuy, mmVol) - _buyClose.Volume - regularBuy - state.VegaBuy - state.GammaBuy));

                _sellOpen.Volume = Math.Max(0, Util.Min(state.MMVegaVolLimitSell, state.MMGammaVolLimitSell, mmVol, cfg.BalanceLimit + state.Position) - regularSell - state.VegaSell - state.GammaSell);
            }

            if(state.Position >= 0) {
                var otherSell = regularSell + state.VegaSell + state.GammaSell;

                _sellClose.Volume = Util.Min(state.MMVegaVolLimitSell, 
                                             state.MMGammaVolLimitSell,
                                             Math.Max(0, cfg.Incremental - otherSell), 
                                             Math.Max(0, mmVol - otherSell), 
                                             Math.Max(0, state.Position - otherSell));

                if(_sellClose.Volume + regularSell + state.VegaSell + state.GammaSell >= state.Position)
                    _sellOpen.Volume = Math.Min(cfg.BalanceLimit, Math.Max(0, Util.Min(state.MMVegaVolLimitSell, state.MMGammaVolLimitSell, mmVol) - _sellClose.Volume - regularSell - state.VegaSell - state.GammaSell));

                _buyOpen.Volume = Math.Max(0, Util.Min(state.MMVegaVolLimitBuy, state.MMGammaVolLimitBuy, mmVol, cfg.BalanceLimit - state.Position) - regularBuy - state.VegaBuy - state.GammaBuy);
            }

            _buyOpen.Volume += _buyClose.Volume;
            _buyClose.Volume = 0;

            _sellOpen.Volume += _sellClose.Volume;
            _sellClose.Volume = 0;

            //_log.Dbg.AddDebugLog($"MM: pos={state.Position}, bo.vol={_buyOpen.Volume}, so.vol={_sellOpen.Volume}, rb={regularBuy}, mmvvlb={state.MMVegaVolLimitBuy}, mmgvlb={state.MMGammaVolLimitBuy}, inc={cfg.Incremental}, mmvol={mmVol}, vb={state.VegaBuy}, gb={state.GammaBuy}");
            //_log.Dbg.AddDebugLog($"MM: rs={regularSell}, mmvvls={state.MMVegaVolLimitSell}, mmgvlb={state.MMGammaVolLimitSell}, vs={state.VegaSell}, gs={state.GammaSell}");

            var totalBuy = _buyOpen.Volume + regularBuy + state.VegaBuy + state.GammaBuy;
            var totalSell = _sellOpen.Volume + regularSell + state.VegaSell + state.GammaSell;

            if(totalBuy < mmVol || totalSell < mmVol)
                _buyOpen.Volume = _sellOpen.Volume = 0;
        }

        protected override void OnSubscribe() {
            base.OnSubscribe();

            Robot.Periodic += OnPeriodicTimer;
        }

        protected override void OnUnsubscribe() {
            base.OnUnsubscribe();

            Robot.Periodic -= OnPeriodicTimer;
        }

        void OnPeriodicTimer() {
            if(IsStrategyActive)
                MainStrategy.CheckPeriodicRecalculate();
        }
    }

    public class VegaHedgeStrategy : OptionStrategy {
        public VegaHedgeStrategy(StrategyWrapper<VegaHedgeStrategy> wrapper) : base(wrapper, StrategyType.VegaHedge) {}

        protected override void RecalculateVolumes(RecalculateState state) {
            var cfg = _currentConfig;
            if(state.Position <= 0) {
                _buyClose.Volume = Util.Min(-state.Position, state.VegaVolTargetBuy, cfg.Incremental, state.GammaVolLimitBuy);
                
                if(_buyClose.Volume == -state.Position)
                    _buyOpen.Volume = Math.Min(cfg.BalanceLimit, Math.Max(0, Util.Min(state.VegaVolTargetBuy, cfg.Incremental, state.GammaVolLimitBuy) - _buyClose.Volume));

                _sellOpen.Volume = Util.Min(Math.Max(0, cfg.BalanceLimit + state.Position), state.VegaVolTargetSell, cfg.Incremental, state.GammaVolLimitSell);
            }

            if(state.Position >= 0) {
                _sellClose.Volume = Util.Min(state.Position, state.VegaVolTargetSell, cfg.Incremental, state.GammaVolLimitSell);

                if(_sellClose.Volume == state.Position)
                    _sellOpen.Volume = Math.Min(cfg.BalanceLimit, Math.Max(0, Util.Min(state.VegaVolTargetSell, cfg.Incremental, state.GammaVolLimitSell) - _sellClose.Volume));

                _buyOpen.Volume = Util.Min(Math.Max(0, cfg.BalanceLimit - state.Position), state.VegaVolTargetBuy, cfg.Incremental, state.GammaVolLimitBuy);
            }

            // объединение объемов на открытие/закрытие в один объем
            _buyOpen.Volume += _buyClose.Volume;
            _buyClose.Volume = 0;

            _sellOpen.Volume += _sellClose.Volume;
            _sellClose.Volume = 0;

            // учитываем мин. объем заявки
            if(_buyOpen.Volume < cfg.MinOrderVolume)
                _buyOpen.Volume = 0;

            if(_sellOpen.Volume < cfg.MinOrderVolume)
                _sellOpen.Volume = 0;

            state.VegaBuy = _buyOpen.Volume;
            state.VegaSell = _sellOpen.Volume;
        }
    }

    public class GammaHedgeStrategy : OptionStrategy {
        public GammaHedgeStrategy(StrategyWrapper<GammaHedgeStrategy> wrapper) : base(wrapper, StrategyType.GammaHedge) {}

        protected override void RecalculateVolumes(RecalculateState state) {
            var cfg = _currentConfig;

            if(state.Position <= 0) {
                _buyClose.Volume = Util.Min(-state.Position, state.GammaVolTargetBuy, cfg.Incremental, state.VegaVolLimitBuy);

                if(_buyClose.Volume == -state.Position)
                    _buyOpen.Volume = Math.Min(cfg.BalanceLimit, Math.Max(0, Util.Min(state.GammaVolTargetBuy, cfg.Incremental, state.VegaVolLimitBuy) - _buyClose.Volume));

                _sellOpen.Volume = Util.Min(Math.Max(0, cfg.BalanceLimit + state.Position), state.GammaVolTargetSell, cfg.Incremental, state.VegaVolLimitSell);
            }

            if(state.Position >= 0) {
                _sellClose.Volume = Util.Min(state.Position, state.GammaVolTargetSell, cfg.Incremental, state.VegaVolLimitSell);

                if(_sellClose.Volume == state.Position)
                    _sellOpen.Volume = Math.Min(cfg.BalanceLimit, Math.Max(0, Util.Min(state.GammaVolTargetSell, cfg.Incremental, state.VegaVolLimitSell) - _sellClose.Volume));

                _buyOpen.Volume = Util.Min(Math.Max(0, cfg.BalanceLimit - state.Position), state.GammaVolTargetBuy, cfg.Incremental, state.VegaVolLimitBuy);
            }

            // объединение объемов на открытие/закрытие в один объем
            _buyOpen.Volume += _buyClose.Volume;
            _buyClose.Volume = 0;

            _sellOpen.Volume += _sellClose.Volume;
            _sellClose.Volume = 0;

            // учитываем мин. объем заявки
            if(_buyOpen.Volume < cfg.MinOrderVolume)
                _buyOpen.Volume = 0;

            if(_sellOpen.Volume < cfg.MinOrderVolume)
                _sellOpen.Volume = 0;

            state.GammaBuy = _buyOpen.Volume;
            state.GammaSell = _sellOpen.Volume;
        }
    }
}