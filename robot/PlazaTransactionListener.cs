using System;
using Ecng.Collections;
using Ecng.Common;
using StockSharp.Logging;
using StockSharp.Messages;
using StockSharp.Plaza;
using StockSharp.Plaza.Metadata;

namespace OptionBot.robot {
    /// <summary>
    /// Расширение адаптера плазы для получения информации о количестве транзакций.
    /// </summary>
    class PlazaTransactionListener : PlazaExtension {
        RobotLogger RobotLogger => _trader.Controller.RobotLogger;

        public PlazaTransactionListener(PlazaTraderEx trader) : base(trader) {
            RegisterTableHandler(_trader.TableRegistry.OrdersLogFuture, null, OnOrderRecordInserted);
            RegisterTableHandler(_trader.TableRegistry.OrdersLogOption, null, OnOrderRecordInserted);
        }

        public event Action<PlazaTransactionInfo> NewTransaction;

        readonly SynchronizedDictionary<int, PlazaTransactionInfo> _prevIsinRecord = new SynchronizedDictionary<int, PlazaTransactionInfo>(); 
        PlazaTransactionInfo _prevRecord;

        void OnOrderRecordInserted(PlazaRecord r) {
            //logger thread = low priority
            RobotLogger.LoggerThread.ExecuteAsync(() => {
                var columns = _trader.TableRegistry.ColumnRegistry.OrdersLogFuture as PlazaOrdersLogDerivativeColumns;

                var transaction = new PlazaTransactionInfo(
                                r.GetLong(columns.ReplId),
                                r.GetLong(columns.ReplRev),
                                r.GetInt(columns.SessionId),
                                r.GetInt(columns.IsinId),
                                r.GetLong(columns.OrderId),
                                r.GetInt(columns.ExtId),
                                r.GetBool(columns.Direction),
                                r.GetInt(columns.AmountOperation),
                                r.GetLong(columns.DealId),
                                r.GetDecimal2(columns.DealPrice),
                                r.GetDateTime(columns.Moment),
                                r.GetLong(columns.XStatus),
                                (OrderLogActions)r.GetSByte(columns.Action));

                var isNewTransaction = false;
                try {
                    if(transaction.Action == OrderLogActions.Matched) {
                        if(transaction.LastRecordInTransaction)
                            _prevIsinRecord.Remove(transaction.IsinId);

                        return; // ignore matches, use only Register and Cancel to calculate transactions
                    }

                    if(!IsTheSameTransaction(transaction)) {
                        isNewTransaction = true;
                        OnNewTransaction(transaction);
                    }

                    _prevIsinRecord[transaction.IsinId] = transaction;
                } finally {
                    _prevRecord = transaction;

                    if(!RobotData.IsRobotStopped)
                        _trader.AddDebugLog("new " + (isNewTransaction ? "tra" : "rec") + ": " + transaction);
                }
            });
        }

        readonly TimeSpan _maxTransactionTimediff = TimeSpan.FromMilliseconds(200);

        bool IsTheSameTransaction(PlazaTransactionInfo newRec) {
            if(_prevRecord != null && !_prevRecord.LastRecordInTransaction && _prevRecord.IsGroupdDelete && newRec.IsGroupdDelete)
                return true;

            var prevSameIsinRec = _prevIsinRecord.TryGetValue(newRec.IsinId);

            if(prevSameIsinRec == null) return false;
            if(prevSameIsinRec.LastRecordInTransaction) return false;

            var diff = newRec.MarketTime - prevSameIsinRec.MarketTime;
            return  diff == TimeSpan.Zero || 
                    (diff < _maxTransactionTimediff && newRec.OrderId == prevSameIsinRec.OrderId);
        }

        void OnNewTransaction(PlazaTransactionInfo transaction) {
            NewTransaction?.Invoke(transaction);
        }
    }

    public enum OrderLogActions { Canceled, Registered, Matched }

    public class PlazaTransactionInfo {
        public PlazaTransactionInfo(long replId, long replRev, int sessId, int isinId, long orderId, int extId, bool dir, int amount, long dealId, decimal dealPrice, DateTime marketTime, long xstatus, OrderLogActions action) {
            Id = ValTuple.Create(replId, replRev);
            SessionId = sessId;
            Action = action;
            IsinId = isinId;
            OrderId = orderId;
            ExtId = extId;
            Direction = dir ? Sides.Buy : Sides.Sell;
            AmountOperation = amount;
            DealId = dealId;
            DealPrice = dealPrice;
            MarketTime = marketTime;
            _xstatus = xstatus;
        }

        public override string ToString() {
            return $"{Id}, {Action}, isin={IsinId}, order={OrderId}, ext_id={ExtId}, dealId={DealId}, dealPrice={DealPrice}, time={MarketTime:HH:mm:ss.fff} last={LastRecordInTransaction} group={IsGroupdDelete} CoD={IsCancelOnDisconnectOperation} xstatus=0x{_xstatus:X}";
        }

        readonly long _xstatus;

        public ValTuple<long, long> Id {get;}
        public int SessionId {get;}
        public OrderLogActions Action {get;}
        public int IsinId {get;}
        public long OrderId {get;}
        public int ExtId {get;}
        public Sides Direction {get;}
        public int AmountOperation {get; private set;}
        public long DealId {get;}
        public decimal DealPrice {get;}
        public DateTime MarketTime {get;}
        public bool LastRecordInTransaction => _xstatus.HasBits(0x1000);
        public bool IsGroupdDelete => _xstatus.HasBits(0x400000);
        public bool IsCancelOnDisconnectOperation => _xstatus.HasBits(0x100000000);
    }
}
