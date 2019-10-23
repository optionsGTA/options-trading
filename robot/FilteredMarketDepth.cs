using System;
using System.Collections.Generic;
using System.Linq;
using Ecng.Common;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Messages;

namespace OptionBot.robot {
    class FilteredMarketDepth : Disposable {
        static readonly Logger _log = new Logger();
        static readonly TimeSpan PurgeInterval = TimeSpan.FromSeconds(1);
        readonly OptionInfo _option;
        readonly Security _security;
        readonly ISecurityProcessor _secProcessor;
        DateTime _lastPurgeTime;
        decimal _optionChangeTrigger;
        int _valuationQuoteMinVolume, _valuationGlassDepth, _valuationGlassMinVolume;
        decimal _lastTrueUpdateBid, _lastTrueUpdateAsk;

        PlazaTraderEx PlazaTrader => (PlazaTraderEx)_security.Connector;

        public decimal BestBidPrice {get; private set;}
        public int BestBidVol {get; private set;}
        public decimal BestAskPrice {get; private set;}
        public int BestAskVol {get; private set;}

        public DateTime BidSeTime {get; private set;}
        public DateTime AskSeTime {get; private set;}

        public int GlassBidVolume {get; private set;}
        public int GlassAskVolume {get; private set;}

        //public bool IsEmpty => BestBidPrice == 0 || BestAskPrice == 0;

        readonly MarketDepth _depth;

        readonly Dictionary<decimal, PriceLevelInfo> _buys = new Dictionary<decimal, PriceLevelInfo>(); 
        readonly Dictionary<decimal, PriceLevelInfo> _sells = new Dictionary<decimal, PriceLevelInfo>();

        bool _notifyAnyChangeOnce;

        public event Action FilteredMarketDepthChanged;

        public FilteredMarketDepth(OptionInfo opt) {
            _option = opt;
            _valuationQuoteMinVolume = _valuationGlassDepth = _valuationGlassMinVolume = 1;
            _security = _option.NativeSecurity;
            _secProcessor = PlazaTrader.GetSecurityProcessor(_security);
            PlazaTrader.MarketDepthChanged += PlazaTraderOnMarketDepthChanged;

            _depth = _security.GetMarketDepth();
            if(_depth == null)
                throw new InvalidOperationException("Unable to get market depth for {0}".Put(_security.Id));

            Update();
        }

        protected override void DisposeManaged() {
            PlazaTrader.MarketDepthChanged -= PlazaTraderOnMarketDepthChanged;
            base.DisposeManaged();
        }

        public void ForceAnyChangeNotificationOnce() {
            _notifyAnyChangeOnce = true;
        }

        public void AddOrder(Order order) {
            _secProcessor.CheckThread();
            if(order.Price == 0) throw new InvalidOperationException("order price is zero");

            Dictionary<decimal, PriceLevelInfo> dict;
            bool intoSpread;

            if(order.Direction == Sides.Buy) {
                dict = _buys;
                intoSpread = BestBidPrice == 0 || order.Price > BestBidPrice;
            } else {
                dict = _sells;
                intoSpread = BestAskPrice == 0 || order.Price < BestBidPrice;
            }

            PriceLevelInfo level;
            if(!dict.TryGetValue(order.Price, out level))
                dict.Add(order.Price, level = new PriceLevelInfo(order.Price));

            level.AddOrder(order, DateTime.UtcNow, intoSpread);
        }

        bool Update() {
            decimal bidPrice, askPrice;
            int bidVol, askVol;
            int sumBids, sumAsks;
            var now = DateTime.UtcNow;

            if(now - _lastPurgeTime > PurgeInterval)
                Purge(now);

            GetQuoteData(_buys, _depth.Bids, now, out bidVol, out bidPrice, out sumBids);
            GetQuoteData(_sells, _depth.Asks, now, out askVol, out askPrice, out sumAsks);

            if(bidVol == 0 || askVol == 0) {
                bidVol = askVol = 0;
                bidPrice = askPrice = 0;
            }

            if(bidVol != 0)
                BidSeTime = _depth.LastChangeTime;

            if(askVol != 0)
                AskSeTime = _depth.LastChangeTime;

            var priceChange = Math.Max(Math.Abs(bidPrice - _lastTrueUpdateBid), Math.Abs(askPrice - _lastTrueUpdateAsk));
            var changed = priceChange >= _optionChangeTrigger;

            BestBidVol = bidVol;
            BestBidPrice = bidPrice;
            GlassBidVolume = sumBids;

            BestAskVol = askVol;
            BestAskPrice = askPrice;
            GlassAskVolume = sumAsks;

            if(changed) {
                _lastTrueUpdateBid = BestBidPrice;
                _lastTrueUpdateAsk = BestAskPrice;
            }

            return changed;
        }

        void GetQuoteData(Dictionary<decimal, PriceLevelInfo> ownOrders, Quote[] quotes, DateTime now, out int bestVolume, out decimal bestPrice, out int sumVolume) {
            var levelsLeft = Math.Min(_valuationGlassDepth, quotes.Length);
            var minVol = _valuationQuoteMinVolume;
            decimal price, sumVol;
            int vol;
            price = sumVol = vol = 0;

            if(ownOrders.Count > 0) { // there are our active buy orders
                foreach(var quote in quotes) {
                    if(levelsLeft <= 0)
                        break;

                    PriceLevelInfo level;
                    if(!ownOrders.TryGetValue(quote.Price, out level)) { // no our orders on this level
                        --levelsLeft;
                        sumVol += quote.Volume;

                        if(vol == 0) {
                            if(quote.Volume >= minVol) {
                                vol = (int)quote.Volume;
                                price = quote.Price;
                            }
                        }
                    } else {
                        // there are our orders on this level
                        var trueVol = level.TrueVolume(_depth, quote, now);
                        if(trueVol <= 0) continue;

                        --levelsLeft;
                        sumVol += quote.Volume;

                        if(vol == 0) {
                            if(trueVol >= minVol) {
                                vol = trueVol;
                                price = quote.Price;
                            }
                        }
                    }
                }
            } else { // no our buy orders
                foreach(var quote in quotes) {
                    if(--levelsLeft < 0)
                        break;

                    sumVol += quote.Volume;

                    if(vol == 0) {
                        if(quote.Volume >= minVol) {
                            vol = (int)quote.Volume;
                            price = quote.Price;
                        }
                    }
                }
            }

            sumVolume = (int)sumVol;

            if(vol != 0 && sumVolume >= _valuationGlassMinVolume) {
                bestVolume = vol;
                bestPrice = price;
            } else {
                bestPrice = bestVolume = 0;
            }
        }

        void Purge(DateTime now) {
            _lastPurgeTime = now;

            foreach(var level in _buys.Values.ToArray()) {
                level.Purge(now);
                if(level.CanRemove)
                    _buys.Remove(level.Price);
            }

            foreach(var level in _sells.Values.ToArray()) {
                level.Purge(now);
                if(level.CanRemove)
                    _sells.Remove(level.Price);
            }
        }

        void PlazaTraderOnMarketDepthChanged(MarketDepth depth) {
            if(!object.ReferenceEquals(depth, _depth)) return;

            _secProcessor.CheckThread();

            var force = _notifyAnyChangeOnce;
            _notifyAnyChangeOnce = false;

            if(Update() || force) FilteredMarketDepthChanged.SafeInvoke();
        }

        public void UpdateValuationParams() {
            var cfg = _option.CfgValuationParams;
            if(cfg == null)
                return;

            _optionChangeTrigger = cfg.OptionChangeTrigger * _option.MinStepSize;
            _valuationQuoteMinVolume = cfg.ValuationQuoteMinVolume;
            _valuationGlassDepth = cfg.ValuationGlassDepth;
            _valuationGlassMinVolume = cfg.ValuationGlassMinVolume;

            _log.Dbg.AddDebugLog($"_optionChangeTrigger={_optionChangeTrigger}");

            _secProcessor.Post(() => PlazaTraderOnMarketDepthChanged(_depth));
        }

        class PriceLevelInfo {
            static readonly TimeSpan WarningTimeout = TimeSpan.FromSeconds(30);
            static readonly TimeSpan DeleteCompleteOrdersTimeout = TimeSpan.FromSeconds(5);
            static readonly TimeSpan OrderLatencyLimit = TimeSpan.FromSeconds(3);
            public readonly decimal Price;
            readonly List<OrderInfo> Orders = new List<OrderInfo>(5);

            public bool CanRemove {get {return Orders.Count == 0;}}

            public PriceLevelInfo(decimal price) {
                Price = price;
            }

            public void AddOrder(Order order, DateTime now, bool submittedIntoSpread) {
                if(order.Price != Price)
                    throw new InvalidOperationException("wrong price");

                Orders.Add(new OrderInfo(order, now, submittedIntoSpread));
            }

            public int TrueVolume(MarketDepth depth, Quote quote, DateTime now) {
                if(quote.Price != Price) throw new InvalidOperationException("wrong price");

                var vol = quote.Volume;

                foreach(var info in Orders) {
                    if(info.Order.Id != 0) {
                        vol -= depth.GetVolumeByOrderIdUnsafe(info.Order.Id);
                    } else if(info.SubmittedIntoSpread && info.Order.State != OrderStates.Failed && (now - info.InitTime < OrderLatencyLimit)) {
                        vol -= info.Order.Volume;
                    }
                }

                return (int)vol;
            }

            public void Purge(DateTime now) {
                if(Orders.Count == 0)
                    return;

                foreach(var info in Orders.ToArray()) {
                    if(info.Order.State == OrderStates.Failed) {
                        Orders.Remove(info);
                        continue;
                    }

                    if(info.Order.State != OrderStates.Done) {
                        if(info.Order.State != OrderStates.Active && now - info.InitTime > WarningTimeout)
                            _log.Dbg.AddErrorLog("Order transId={0} seems stuck. InitTime={1}, State={2}", info.Order.TransactionId, info.InitTime, info.Order.State);

                        continue;
                    }

                    if(info.DoneTime == default(DateTime)) {
                        info.DoneTime = now;
                    } else if(now - info.DoneTime >= DeleteCompleteOrdersTimeout) {
                        Orders.Remove(info);
                    }
                }
            }

            class OrderInfo {
                public readonly Order Order;
                public readonly DateTime InitTime;
                public readonly bool SubmittedIntoSpread;

                public DateTime DoneTime;

                public OrderInfo(Order order, DateTime now, bool submittedIntoSpread) {
                    if(order.TransactionId == 0)
                        throw new InvalidOperationException("order is not initialized");

                    Order = order;
                    SubmittedIntoSpread = submittedIntoSpread;
                    InitTime = now;
                }
            }
        }
    }
}
