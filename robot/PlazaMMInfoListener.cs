using System;
using System.Collections.Generic;
using Ecng.Common;
using StockSharp.Logging;
using StockSharp.Plaza;

namespace OptionBot.robot
{
    /// <summary>
    /// Расширение адаптера плазы для получения информации по обязательствам маркетмейкера.
    /// </summary>
    class PlazaMMInfoListener : PlazaExtension {
        public event Action<MMInfoRecord> MMInfoInserted;

        public PlazaMMInfoListener(PlazaTraderEx trader) : base(trader) {
            var mmParams = _trader.TableRegistry.ColumnRegistry.MarketMakingOptionParams;

            var mmColumns = new[] {
                mmParams.Spread,
                mmParams.PriceEdgeSell,
                mmParams.AmountSells,
                mmParams.PriceEdgeBuy,
                mmParams.AmountBuys,
                mmParams.MarketMakingSpread,
                mmParams.MarketMakingAmount,
                mmParams.SpreadSign,
                mmParams.AmountSign,
                mmParams.PercentTime,
                mmParams.PeriodStart,
                mmParams.PeriodEnd,
                mmParams.ClientCode,
                mmParams.ActiveSign,
                mmParams.FillMin,
                mmParams.FillPartial,
                mmParams.FillTotal,
                mmParams.IsFillMin,
                mmParams.IsFillPartial,
                mmParams.IsFillTotal,
                mmParams.CStrikeOffset,
            };

            RegisterTableHandler(_trader.TableRegistry.MarketMakingOption, mmColumns, OnMMInfoInserted);
        }

        void OnMMInfoInserted(PlazaRecord r) {
            var c = _trader.TableRegistry.ColumnRegistry.MarketMakingOptionParams;

            var record = new MMInfoRecord(r.GetInt(c.IsinId), r.GetInt(c.SessionId)) {
                ReplId = r.GetLong(c.ReplId),
                ReplAct = r.GetLong(c.ReplAct),
                Spread = r.GetDecimal2(c.Spread),
                PriceEdgeSell = r.GetDecimal2(c.PriceEdgeSell),
                AmountSells = r.GetInt(c.AmountSells),
                PriceEdgeBuy = r.GetDecimal2(c.PriceEdgeBuy),
                AmountBuys = r.GetInt(c.AmountBuys),
                MarketMakingSpread = r.GetDecimal2(c.MarketMakingSpread),
                MarketMakingAmount = r.GetInt(c.MarketMakingAmount),
                SpreadSign = r.GetBool(c.SpreadSign),
                AmountSign = r.GetBool(c.AmountSign),
                PercentTime = r.GetDecimal2(c.PercentTime),
                PeriodStart = r.GetDateTime(c.PeriodStart),
                PeriodEnd = r.GetDateTime(c.PeriodEnd),
                ClientCode = r.GetString(c.ClientCode),
                ActiveSign = r.GetInt(c.ActiveSign) == 0,
                FillMin = r.GetDecimal2(c.FillMin),
                FillPartial = r.GetDecimal2(c.FillPartial),
                FillTotal = r.GetDecimal2(c.FillTotal),
                IsFillMin = r.GetBool(c.IsFillMin),
                IsFillPartial = r.GetBool(c.IsFillPartial),
                IsFillTotal = r.GetBool(c.IsFillTotal),
                CStrikeOffset = r.GetDecimal2(c.CStrikeOffset),
            };

            MMInfoInserted.SafeInvoke(record);
        }
    }

    public interface IMMInfoRecord {
        MMInfoRecordKey Key         {get;}
        long ReplId                 {get;}
        long ReplAct                {get;}
        int IsinId                  {get;}
        int SessionId               {get;}
        decimal Spread              {get;}
        decimal PriceEdgeSell       {get;}
        int AmountSells             {get;}
        decimal PriceEdgeBuy        {get;}
        int AmountBuys              {get;}
        decimal MarketMakingSpread  {get;}
        int MarketMakingAmount      {get;}
        bool SpreadSign             {get;}
        bool AmountSign             {get;}
        decimal PercentTime         {get;}
        DateTime PeriodStart        {get;}
        DateTime PeriodEnd          {get;}
        string ClientCode           {get;}
        bool ActiveSign             {get;}
        decimal FillMin             {get;}
        decimal FillPartial         {get;}
        decimal FillTotal           {get;}
        bool IsFillMin              {get;}
        bool IsFillPartial          {get;}
        bool IsFillTotal            {get;}
        decimal CStrikeOffset       {get;}
    }

    public class MMInfoRecord : IMMInfoRecord {
        public MMInfoRecordKey Key {get;}
        public long ReplId {get; set;}
        public long ReplAct {get; set;}
        public int IsinId => Key.IsinId;
        public int SessionId => Key.SessionId;
        public decimal Spread {get; set;}
        public decimal PriceEdgeSell {get; set;}
        public int AmountSells {get; set;}
        public decimal PriceEdgeBuy {get; set;}
        public int AmountBuys {get; set;}
        public decimal MarketMakingSpread {get; set;}
        public int MarketMakingAmount {get; set;}
        public bool SpreadSign {get; set;}
        public bool AmountSign {get; set;}
        public decimal PercentTime {get; set;}
        public DateTime PeriodStart {get; set;}
        public DateTime PeriodEnd {get; set;}
        public string ClientCode {get; set;}
        public bool ActiveSign {get; set;}
        public decimal FillMin {get; set;}
        public decimal FillPartial {get; set;}
        public decimal FillTotal {get; set;}
        public bool IsFillMin {get; set;}
        public bool IsFillPartial {get; set;}
        public bool IsFillTotal {get; set;}
        public decimal CStrikeOffset {get; set;}

        public MMInfoRecord(int isinId, int sessionId) {
            Key = new MMInfoRecordKey(isinId, sessionId);
        }

        public override string ToString() {
            return $"MMInfo(active={ActiveSign}, replId/replAct={ReplId}/{ReplAct}, ssign/asign={SpreadSign}/{AmountSign}, isin/offset={IsinId}/{CStrikeOffset}, price={PriceEdgeBuy}/{PriceEdgeSell}, amount={AmountBuys}/{AmountSells}, mmamount={MarketMakingAmount}, spread/mm_spread={Spread}/{MarketMakingSpread})";
        }

        public string[] PropertyDiff(MMInfoRecord other) {
            if(object.ReferenceEquals(this, other))
                return new string[0];

            var result = new List<string>();

            if(ReplId != other.ReplId)                          result.Add(nameof(ReplId));
            if(ReplAct != other.ReplAct)                        result.Add(nameof(ReplAct));
            if(IsinId != other.IsinId)                          result.Add(nameof(IsinId));
            if(SessionId != other.SessionId)                    result.Add(nameof(SessionId));
            if(Spread != other.Spread)                          result.Add(nameof(Spread));
            if(PriceEdgeSell != other.PriceEdgeSell)            result.Add(nameof(PriceEdgeSell));
            if(AmountSells != other.AmountSells)                result.Add(nameof(AmountSells));
            if(PriceEdgeBuy != other.PriceEdgeBuy)              result.Add(nameof(PriceEdgeBuy));
            if(AmountBuys != other.AmountBuys)                  result.Add(nameof(AmountBuys));
            if(MarketMakingSpread != other.MarketMakingSpread)  result.Add(nameof(MarketMakingSpread));
            if(MarketMakingAmount != other.MarketMakingAmount)  result.Add(nameof(MarketMakingAmount));
            if(SpreadSign != other.SpreadSign)                  result.Add(nameof(SpreadSign));
            if(AmountSign != other.AmountSign)                  result.Add(nameof(AmountSign));
            if(PercentTime != other.PercentTime)                result.Add(nameof(PercentTime));
            if(PeriodStart != other.PeriodStart)                result.Add(nameof(PeriodStart));
            if(PeriodEnd != other.PeriodEnd)                    result.Add(nameof(PeriodEnd));
            if(ClientCode != other.ClientCode)                  result.Add(nameof(ClientCode));
            if(ActiveSign != other.ActiveSign)                  result.Add(nameof(ActiveSign));
            if(FillMin != other.FillMin)                        result.Add(nameof(FillMin));
            if(FillPartial != other.FillPartial)                result.Add(nameof(FillPartial));
            if(FillTotal != other.FillTotal)                    result.Add(nameof(FillTotal));
            if(IsFillMin != other.IsFillMin)                    result.Add(nameof(IsFillMin));
            if(IsFillPartial != other.IsFillPartial)            result.Add(nameof(IsFillPartial));
            if(IsFillTotal != other.IsFillTotal)                result.Add(nameof(IsFillTotal));
            if(CStrikeOffset != other.CStrikeOffset)            result.Add(nameof(CStrikeOffset));

            return result.ToArray();
        }

        public static IEnumerable<string> GetLoggerFields() {
            return new[] {
                nameof(IsinId),
                nameof(SessionId),
                nameof(Spread),
                nameof(PriceEdgeSell),
                nameof(AmountSells),
                nameof(PriceEdgeBuy),
                nameof(AmountBuys),
                nameof(MarketMakingSpread),
                nameof(MarketMakingAmount),
                nameof(SpreadSign),
                nameof(AmountSign),
                nameof(PercentTime),
                nameof(PeriodStart),
                nameof(PeriodEnd),
                nameof(ClientCode),
                nameof(ActiveSign),
                nameof(FillMin),
                nameof(FillPartial),
                nameof(FillTotal),
                nameof(IsFillMin),
                nameof(IsFillPartial),
                nameof(IsFillTotal),
                nameof(CStrikeOffset),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                IsinId,
                SessionId,
                Spread,
                PriceEdgeSell,
                AmountSells,
                PriceEdgeBuy,
                AmountBuys,
                MarketMakingSpread,
                MarketMakingAmount,
                SpreadSign,
                AmountSign,
                PercentTime,
                PeriodStart,
                PeriodEnd,
                ClientCode,
                ActiveSign,
                FillMin,
                FillPartial,
                FillTotal,
                IsFillMin,
                IsFillPartial,
                IsFillTotal,
                CStrikeOffset,
            };
        } 

    }

    public struct MMInfoRecordKey {
        public MMInfoRecordKey(int isinId, int sessionId) {
            IsinId = isinId;
            SessionId = sessionId;
        }

        public int IsinId {get;}
        public int SessionId {get;}
    }
}
