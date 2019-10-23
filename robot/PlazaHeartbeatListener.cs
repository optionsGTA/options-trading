using System;
using Ecng.Common;
using StockSharp.Plaza;

namespace OptionBot.robot {
    /// <summary>
    /// Расширение адаптера плазы для получения информации о времени сервера.
    /// </summary>
    class PlazaHeartbeatListener : PlazaExtension {
        public event Action<DateTime> Heartbeat;

        public PlazaHeartbeatListener(PlazaTraderEx trader) : base(trader) {
            RegisterTableHandler(_trader.TableRegistry.HeartBeatFuture, null, OnHeartbeatInserted);
        }

        void OnHeartbeatInserted(PlazaRecord r) {
            Heartbeat.SafeInvoke(r.GetDateTime(_trader.TableRegistry.ColumnRegistry.HeartBeatFuture.ServerTime));
        }
    }
}
