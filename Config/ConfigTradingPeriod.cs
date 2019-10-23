using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using OptionBot.robot;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

namespace OptionBot.Config {
    [Serializable]
    [DataContract]
    [CategoryOrder(PeriodCategory, 1)]
    public class ConfigTradingPeriod : BaseConfig<ConfigTradingPeriod, IConfigTradingPeriod>, IConfigTradingPeriod {
        const string PeriodCategory = "Торговый период";

        #region fields/properties

        [DataMember] TradingPeriodType _periodType;
        [DataMember] int _shiftDeltaHedge, _shiftStart, _shiftEnd;
        [DataMember] bool _stopMMByTimePercent;

        [Category(PeriodCategory)]
        [PropertyOrder(1)]
        [DisplayName(@"period_type")]
        [Description(@"Тип торгового периода.")]
        public TradingPeriodType PeriodType {get {return _periodType;} set {SetField(ref _periodType, value);}}

        [Category(PeriodCategory)]
        [PropertyOrder(2)]
        [DisplayName(@"shift_delta_hedge")]
        [Description(@"Сдвиг начала работы стратегии дельта-хеджирования относительно старта торгов после перерыва, секунд.")]
        public int ShiftDeltaHedge {get {return _shiftDeltaHedge;} set {SetField(ref _shiftDeltaHedge, value);}}

        [Category(PeriodCategory)]
        [PropertyOrder(3)]
        [DisplayName(@"shift_start")]
        [Description(@"Сдвиг начала торговли относительно времени начала торговой сессии, секунд.")]
        public int ShiftStart {get {return _shiftStart;} set {SetField(ref _shiftStart, value);}}

        [Category(PeriodCategory)]
        [PropertyOrder(4)]
        [DisplayName(@"shift_end")]
        [Description(@"Сдвиг окончания торговли относительно времени окончания торговой сессии, секунд.")]
        public int ShiftEnd {get {return _shiftEnd;} set {SetField(ref _shiftEnd, value);}}

        [Category(PeriodCategory)]
        [PropertyOrder(5)]
        [DisplayName(@"stop_mm_by_time_percent")]
        [Description(@"Автоматическая остановка стратегий ММ при выполнении обязательств по времени.")]
        public bool StopMMByTimePercent {get {return _stopMMByTimePercent;} set {SetField(ref _stopMMByTimePercent, value);}}

        #endregion

        protected override void SetDefaultValues() {
            ShiftDeltaHedge = 60; // PeriodType == TradingPeriodType.MainBeforeClearing ? 120 : 60;
            ShiftStart = 60;
            ShiftEnd = 5;
            StopMMByTimePercent = false;
        }

        protected override void CopyFromImpl(ConfigTradingPeriod other) {
            base.CopyFromImpl(other);

            PeriodType = other.PeriodType;
            ShiftDeltaHedge = other.ShiftDeltaHedge;
            ShiftStart = other.ShiftStart;
            ShiftEnd = other.ShiftEnd;
            StopMMByTimePercent = other.StopMMByTimePercent;
        }

        protected override bool OnEquals(ConfigTradingPeriod other) {
            return
                base.OnEquals(other) &&
                PeriodType == other.PeriodType &&
                ShiftDeltaHedge == other.ShiftDeltaHedge &&
                ShiftStart == other.ShiftStart &&
                ShiftEnd == other.ShiftEnd &&
                StopMMByTimePercent == other.StopMMByTimePercent;
        }

        public override void VerifyConfig(List<string> errors) {
            base.VerifyConfig(errors);

            if(ShiftDeltaHedge < 1) errors.Add(nameof(ShiftDeltaHedge));
            if(ShiftStart < 1)      errors.Add(nameof(ShiftStart));
            if(ShiftEnd < 1)        errors.Add(nameof(ShiftEnd));

            if(ShiftDeltaHedge > ShiftStart)
                errors.Add($"{nameof(ShiftDeltaHedge)} > {nameof(ShiftStart)}");
        }

        #region logger methods

        public static string[] GetLoggerFields() {
            return new [] {
                nameof(PeriodType),
                nameof(ShiftDeltaHedge),
                nameof(ShiftStart),
                nameof(ShiftEnd),
                nameof(StopMMByTimePercent),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                PeriodType,
                ShiftDeltaHedge,
                ShiftStart,
                ShiftEnd,
                StopMMByTimePercent,
            };
        }
        #endregion
    }

    public interface IConfigTradingPeriod : IReadOnlyConfiguration {
        TradingPeriodType PeriodType {get;}
        int ShiftDeltaHedge {get;}
        int ShiftStart {get;}
        int ShiftEnd {get;}
        bool StopMMByTimePercent {get;}

        IEnumerable<object> GetLoggerValues();
    }
}
