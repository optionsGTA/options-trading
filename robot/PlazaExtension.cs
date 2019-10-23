using System;
using System.Collections.Generic;
using System.Linq;
using StockSharp.Logging;
using StockSharp.Plaza;
using StockSharp.Plaza.Metadata;

namespace OptionBot.robot
{
    abstract class PlazaExtension {
        protected readonly PlazaTraderEx _trader;

        protected RobotData RobotData => _trader.RobotData;

        protected PlazaExtension(PlazaTraderEx trader) {
            _trader = trader;
        }

        protected void RegisterTableHandler(PlazaTable table, IEnumerable<PlazaColumn> columns, Action<PlazaRecord> handler) {
            if(columns != null)
                foreach(var c in columns.Where(c => !table.Columns.Contains(c)))
                    table.Columns.Add(c);

            table.Inserted += rec => OnInserted(rec.Table.ToString(), () => handler(rec));
        }

        protected void OnInserted(string name, Action action) {
            try {
                action();
            } catch(Exception e) {
                _trader.AddErrorLog("OnInserted({0}): {1}", name, e);
            }
        }
    }
}
