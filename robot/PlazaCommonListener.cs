using System;
using System.Diagnostics;
using StockSharp.Logging;
using StockSharp.Plaza;

namespace OptionBot.robot {
    class PlazaCommonListener : PlazaExtension {
        static readonly TimeSpan _monStart = new TimeSpan(18, 43, 0);
        static readonly TimeSpan _monStop = new TimeSpan(19, 2, 0);

        readonly DateTime _startTime;
        readonly Stopwatch _watch;

        DateTime Now => _startTime + _watch.Elapsed;

        public PlazaCommonListener(PlazaTraderEx trader) : base(trader) {
            var cols = _trader.TableRegistry.ColumnRegistry.CommonOption;

            _startTime = _trader.Controller.Connector.GetMarketTime();
            _watch = Stopwatch.StartNew();

            var list = new[] {
                cols.ReplId,
                cols.ReplRev,
                cols.ReplAct,
                cols.IsinId,
                cols.SessionId,
                cols.DealTime,
                cols.DealCount,
                cols.ContractCount,
                cols.Position,
                cols.OpeningPrice,
                cols.ClosingPrice,
            };

            RegisterTableHandler(_trader.TableRegistry.CommonOption, list, OnCommonRecordInserted);
        }

        void OnCommonRecordInserted(PlazaRecord r) {
            var now = Now.TimeOfDay;
            if(now < _monStart || now > _monStop)
                return;

            var c = _trader.TableRegistry.ColumnRegistry.CommonOption;
            var id = $"{r.GetLong(c.ReplId)},{r.GetLong(c.ReplRev)},{r.GetLong(c.ReplAct)}";
            var deals = $"deal_time={r.GetDateTime(c.DealTime)}, deal_count={r.GetInt(c.DealCount)}";
            var contr = $"contr_count={r.GetInt(c.ContractCount)}, pos={r.GetInt(c.Position)}";
            var price = $"o={r.GetDecimal2(c.OpeningPrice)}, c={r.GetDecimal2(c.ClosingPrice)}";

            _trader.AddDebugLog($"common: ({id}) sess={r.GetInt(c.SessionId)}, isin={r.GetInt(c.IsinId)}, {deals}, {contr}, {price}");
        }
    }
}
