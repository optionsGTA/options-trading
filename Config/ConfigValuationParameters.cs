using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using OptionBot.robot;
using StockSharp.Messages;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

namespace OptionBot.Config {
    [Serializable]
    [DataContract]
    public class ConfigValuationParameters : BaseConfig<ConfigValuationParameters, IConfigValuationParameters>, IConfigValuationParameters {
        #region fields/properties

        [DataMember] OptionSeriesId _seriesId;
        [DataMember] OptionTypes _optionType;
        [DataMember] int _atmStrikeShift;
        [DataMember] bool _isActive;
        [DataMember] decimal _vegaUnit;
        [DataMember] decimal _empiricDeltaShift;
        [DataMember] decimal _valuationLowSpreadLimit, _valuationHighSpreadLimit;
        [DataMember] decimal _valuationWide, _valuationNarrow;
        [DataMember] decimal _highLiquidSpreadLimit;
        [DataMember] decimal _deltaCorrection;
        [DataMember] decimal _lowX, _highX;
        [DataMember] decimal _bestIvExpectation;
        [DataMember] decimal _aWideningMktIv, _aNarrowingMktIv;
        [DataMember] decimal _valuationMaxSpread;
        [DataMember] bool _permanentlyIlliquid;
        [DataMember] int _optionChangeTrigger;
        [DataMember] bool _disableCurveModel;
        [DataMember] int _valuationQuoteMinVolume, _valuationGlassDepth, _valuationGlassMinVolume;
        [DataMember] bool _aggressiveReset;
        [DataMember] decimal _aggressiveResetTimeLimit;
        [DataMember] decimal _amsrl, _bmsrl;
        [DataMember] bool _conservativeReset, _quoteVolumeReset;
        [DataMember] decimal _conservativeResetLimit, _conservativeResetTimeLimit;
        [DataMember] int _quoteVolumeResetLimit;
        [DataMember] decimal _quoteVolumeResetTimeLimit;
        [DataMember] bool _dealVolumeReset;
        [DataMember] int _dealVolumeResetLimit;
        [DataMember] bool _timeReset;
        [DataMember] int _timeResetLimit;

        [Browsable(false)] public bool IsActive {get {return _isActive;} set{ SetField(ref _isActive, value);}}
        [Browsable(false)] public string Id => $"VP {OptionType} ({OptionStrikeShift.ShiftToString(AtmStrikeShift)}) {SeriesId.StrFutSerCodeShortDate}";

        [AutoPropertyOrder]
        [DisplayName(@"series")]
        [Description(@"Серия опционов.")]
        public OptionSeriesId SeriesId {get {return _seriesId;} set{ SetField(ref _seriesId, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"call_put")]
        [Description(@"Тип опциона.")]
        public OptionTypes OptionType {get {return _optionType;} set{ SetField(ref _optionType, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"atm_strike_shift")]
        [Description(@"Сдвиг центрального страйка, шагов (для fRTS 1 шаг = 5000 пунктов).")]
        public int AtmStrikeShift {get {return _atmStrikeShift;} set{ SetField(ref _atmStrikeShift, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"a_widening_mkt_iv")]
        [Description(@"Линейный коэффициент разового расширения рыночной волатильности.")]
        public decimal AWideningMktIv { get { return _aWideningMktIv; } set { SetField(ref _aWideningMktIv, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"a_narrowing_mkt_iv")]
        [Description(@"Линейный коэффициент сужения рыночной волатильности.")]
        public decimal ANarrowingMktIv { get { return _aNarrowingMktIv; } set { SetField(ref _aNarrowingMktIv, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"valuation_max_spread")]
        [Description(@"Максимальный лимит текущего спреда, при котором может измениться market_iv_bid и market_iv_offer, пункты.")]
        public decimal ValuationMaxSpread { get { return _valuationMaxSpread; } set { SetField(ref _valuationMaxSpread, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"vega_unit")]
        [Description(@"Изменение волатильности, которое берется за основу для определения вега-лимита открытой позиции по каждому инструменту.")]
        public decimal VegaUnit { get { return _vegaUnit; } set { SetField(ref _vegaUnit, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"empiric_delta_shift")]
        [Description(@"Смещение эмпирической дельты относительно модели БШ.")]
        public decimal EmpiricDeltaShift { get { return _empiricDeltaShift; } set { SetField(ref _empiricDeltaShift, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"valuation_low_spread_limit")]
        [Description(@"Нижний лимит спреда оценки, пункты")]
        public decimal ValuationLowSpreadLimit { get { return _valuationLowSpreadLimit; } set { SetField(ref _valuationLowSpreadLimit, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"valuation_high_spread_limit")]
        [Description(@"Верхний лимит спреда оценки, пункты")]
        public decimal ValuationHighSpreadLimit { get { return _valuationHighSpreadLimit; } set { SetField(ref _valuationHighSpreadLimit, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"valuation_wide")]
        [Description(@"Разовое расширение минимального спреда оценки, в минимальных шагах цены.")]
        public decimal ValuationWide { get { return _valuationWide; } set { SetField(ref _valuationWide, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"valuation_narrow")]
        [Description(@"Разовое сужение минимального спреда оценки, в минимальных шагах цены.")]
        public decimal ValuationNarrow { get { return _valuationNarrow; } set { SetField(ref _valuationNarrow, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"high_liquid_spread_limit")]
        [Description(@"Верхняя граница текущего спреда для ликвидного инструмента, если current_spread >= high_liquid_spread_limit, то режим инструмента переключается на неликвидный. (0;10000).")]
        public decimal HighLiquidSpreadLimit { get { return _highLiquidSpreadLimit; } set { SetField(ref _highLiquidSpreadLimit, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"delta_correction")]
        [Description(@"Корректировка начального значения дельты для неликвидных опционов «глубоко в деньгах» или «глубоко вне денег», может принимать значения (-1, -0,0001].")]
        public decimal DeltaCorrection { get { return _deltaCorrection; } set { SetField(ref _deltaCorrection, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"low_x")]
        [Description(@"Нижний лимит для решения квадратного уравнения.")]
        public decimal LowX { get { return _lowX; } set { SetField(ref _lowX, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"high_x")]
        [Description(@"Верхний лимит для решения квадратного уравнения.")]
        public decimal HighX { get { return _highX; } set { SetField(ref _highX, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"best_iv_expectation")]
        [Description(@"Наиболее вероятное значение волатильности для опциона центрального страйка.")]
        public decimal BestIvExpectation { get { return _bestIvExpectation; } set { SetField(ref _bestIvExpectation, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"permanently_illiquid")]
        [Description(@"Обязательное использование неликвидных параметров для оценки опциона. При выборе этой галки оценка по кв также не используется.")]
        public bool PermanentlyIlliquid { get { return _permanentlyIlliquid; } set { SetField(ref _permanentlyIlliquid, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"option_change_limit")]
        [Description(@"Лимит изменения котировки опциона для пересчета модели опциона, шагов цены опциона (1 для опционов на fRTS означает 10 пунктов).")]
        public int OptionChangeTrigger { get { return _optionChangeTrigger; } set { SetField(ref _optionChangeTrigger, value); } }

        [AutoPropertyOrder]
        [DisplayName(@"disable_curve_model")]
        [Description(@"Запрет на использование параметров кв для оценки опциона.")]
        public bool DisableCurveModel {get {return _disableCurveModel;} set{ SetField(ref _disableCurveModel, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"valuation_quote_min_volume")]
        [Description(@"Минимальный объем лучшей котировки в стакане, при котором котировка считается ликвидной.")]
        public int ValuationQuoteMinVolume {get {return _valuationQuoteMinVolume;} set{ SetField(ref _valuationQuoteMinVolume, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"valuation_glass_depth")]
        [Description(@"Глубина стакана, используемая для расчета glass_bid_volume и glass_offer_volume.")]
        public int ValuationGlassDepth {get {return _valuationGlassDepth;} set{ SetField(ref _valuationGlassDepth, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"valuation_glass_min_volume")]
        [Description(@"Минимальный лимит суммы объемов лучших котировок в стакане в количестве valuation_glass_depth котировок, при котором лучшая котировка считается ликвидной.")]
        public int ValuationGlassMinVolume {get {return _valuationGlassMinVolume;} set{ SetField(ref _valuationGlassMinVolume, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"aggressive_reset")]
        [Description(@"Проверка на агрессивный ресет, когда рыночный спред больше верхнего лимита рыночного спреда.")]
        public bool AggressiveReset {get {return _aggressiveReset;} set{ SetField(ref _aggressiveReset, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"aggressive_reset_time_limit")]
        [Description(@"Лимит времени, по истечении которого выполняется агрессивный ресет оценки опциона, секунд.")]
        public decimal AggressiveResetTimeLimit {get {return _aggressiveResetTimeLimit;} set{ SetField(ref _aggressiveResetTimeLimit, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"a_msrl")]
        [Description(@"Линейный коэффициент А для расчета верхнего лимита рыночного спреда волатильностей market_iv_spread_limit в зависимости от спреда оценки valuation_spread, по достижении которого спредом market_iv_spread производится ресет оценки опциона.")]
        public decimal AMSRL {get {return _amsrl;} set{ SetField(ref _amsrl, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"b_msrl")]
        [Description(@"Линейный параметр В для расчета верхнего лимита рыночного спреда волатильностей market_iv_spread_limit в зависимости от спреда оценки valuation_spread, по достижении которого спредом market_iv_spread производится ресет оценки опциона, в минимальных шагах цены.")]
        public decimal BMSRL {get {return _bmsrl;} set{ SetField(ref _bmsrl, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"conservative_reset")]
        [Description(@"Проверка на консервативный ресет, когда рыночный спред меньше текущего спреда.")]
        public bool ConservativeReset {get {return _conservativeReset;} set{ SetField(ref _conservativeReset, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"conservative_reset_limit")]
        [Description(@"Лимит превышения текущего спреда над рыночным спредом, по достижении которого начинается отсчет времени для выполнения консервативного ресета оценки опциона, пунктов.")]
        public decimal ConservativeResetLimit {get {return _conservativeResetLimit;} set{ SetField(ref _conservativeResetLimit, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"conservative_reset_time_limit")]
        [Description(@"Лимит времени, по истечении которого выполняется консервативный ресет оценки опциона, секунд.")]
        public decimal ConservativeResetTimeLimit {get {return _conservativeResetTimeLimit;} set{ SetField(ref _conservativeResetTimeLimit, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"quote_volume_reset")]
        [Description(@"Проверка на ресет, когда market_bid/market_offer хуже лучшей котировки с объемом не менее quote_volume_reset_limit.")]
        public bool QuoteVolumeReset {get {return _quoteVolumeReset;} set{ SetField(ref _quoteVolumeReset, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"quote_volume_reset_limit")]
        [Description(@"Объем лучшей котировки bid или offer в стакане, при котором производится ресет оценки опциона в соответствующей категории bid или offer.")]
        public int QuoteVolumeResetLimit {get {return _quoteVolumeResetLimit;} set{ SetField(ref _quoteVolumeResetLimit, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"quote_volume_reset_time_limit")]
        [Description(@"Лимит времени, по истечении которого выполняется ресет оценки опциона по объему лучшей котировки, секунд.")]
        public decimal QuoteVolumeResetTimeLimit {get {return _quoteVolumeResetTimeLimit;} set{ SetField(ref _quoteVolumeResetTimeLimit, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"deal_volume_reset")]
        [Description(@"Проверка на ресет, когда совершается наша сделка объемом не менее deal_volume_reset_limit.")]
        public bool DealVolumeReset {get {return _dealVolumeReset;} set{ SetField(ref _dealVolumeReset, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"deal_volume_reset_limit")]
        [Description(@"Лимит разового объема сделки по инструменту, по достижении которого производится ресет оценки опциона.")]
        public int DealVolumeResetLimit {get {return _dealVolumeResetLimit;} set{ SetField(ref _dealVolumeResetLimit, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"time_reset")]
        [Description(@"Проверка на ресет по времени.")]
        public bool TimeReset {get {return _timeReset;} set{ SetField(ref _timeReset, value);}}

        [AutoPropertyOrder]
        [DisplayName(@"time_reset_limit")]
        [Description(@"Лимит времени, прошедшего с последней нашей сделки по инструменту, по достижении которого производится ресет оценки опциона, в минутах.")]
        public int TimeResetLimit {get {return _timeResetLimit;} set{ SetField(ref _timeResetLimit, value);}}

        #endregion

        protected override void SetDefaultValues() {
            base.SetDefaultValues();

            VegaUnit = 0.01m;
            ValuationLowSpreadLimit = 20;
            ValuationHighSpreadLimit = 200;
            ValuationWide = 0.1m;
            ValuationNarrow = 0.1m;
            DeltaCorrection = -0.5m;
            LowX = 0.1m;
            HighX = 1;
            BestIvExpectation = 0.35m;
            HighLiquidSpreadLimit = 400;
            PermanentlyIlliquid = false;
            AWideningMktIv = 4;
            ANarrowingMktIv = 0.1m;
            ValuationWide = ValuationNarrow = 0.2m;
            ValuationMaxSpread = 200;
            OptionChangeTrigger = 1;
            DisableCurveModel = false;
            ValuationQuoteMinVolume = 10;
            ValuationGlassDepth = 5;
            ValuationGlassMinVolume = 50;
            AggressiveReset = true;
            AggressiveResetTimeLimit = 20;
            AMSRL = 1.1m;
            BMSRL = 1;
            ConservativeReset = true;
            ConservativeResetLimit = 10;
            ConservativeResetTimeLimit = 1;
            QuoteVolumeReset = true;
            QuoteVolumeResetLimit = 100;
            QuoteVolumeResetTimeLimit = 30;
            DealVolumeReset = true;
            DealVolumeResetLimit = 50;
            TimeReset = true;
            TimeResetLimit = 30;
        }

        public override void VerifyConfig(List<string> errors) {
            base.VerifyConfig(errors);

            if(VegaUnit < 0)
                errors.Add("vega_unit");

            if(EmpiricDeltaShift < -100 || EmpiricDeltaShift > 100)
                errors.Add("empiric_delta_shift");

            if(HighLiquidSpreadLimit <= 0 || HighLiquidSpreadLimit > 10000) errors.Add(nameof(HighLiquidSpreadLimit));

            if(DeltaCorrection <= -1 || DeltaCorrection > -0.0001m) errors.Add("DeltaCorrection");
            if(LowX <= 0 || LowX > 3) errors.Add("LowX");
            if(HighX <= 0 || HighX > 3) errors.Add("HighX");
            if(LowX > HighX) errors.Add("LowX > HighX");
            if(BestIvExpectation < 0 || BestIvExpectation > 10) errors.Add(nameof(BestIvExpectation));

            if(AWideningMktIv < 0 || AWideningMktIv > 100) errors.Add(nameof(AWideningMktIv));
            if(ANarrowingMktIv <= 0 || ANarrowingMktIv > 1) errors.Add(nameof(ANarrowingMktIv));

            if(OptionChangeTrigger < 1) errors.Add(nameof(OptionChangeTrigger));

            if(ValuationQuoteMinVolume < 1) errors.Add(nameof(ValuationQuoteMinVolume));
            if(ValuationGlassDepth < 1) errors.Add(nameof(ValuationGlassDepth));
            if(ValuationGlassMinVolume < 1) errors.Add(nameof(ValuationGlassMinVolume));
            if(QuoteVolumeResetLimit < 1) errors.Add(nameof(QuoteVolumeResetLimit));
            if(DealVolumeResetLimit < 1) errors.Add(nameof(DealVolumeResetLimit));
            if(TimeResetLimit < 1) errors.Add(nameof(TimeResetLimit));

            if(AggressiveResetTimeLimit <= 0) errors.Add(nameof(AggressiveResetTimeLimit));
            if(ConservativeResetTimeLimit <= 0) errors.Add(nameof(ConservativeResetTimeLimit));
            if(ConservativeResetLimit < 0) errors.Add(nameof(ConservativeResetLimit));
            if(QuoteVolumeResetTimeLimit <= 0) errors.Add(nameof(QuoteVolumeResetTimeLimit));
        }

        protected override void CopyFromImpl(ConfigValuationParameters other) {
            base.CopyFromImpl(other);

            IsActive = other.IsActive;
            SeriesId = other.SeriesId;
            OptionType = other.OptionType;
            AtmStrikeShift = other.AtmStrikeShift;
            AWideningMktIv = other.AWideningMktIv;
            ANarrowingMktIv = other.ANarrowingMktIv;
            ValuationMaxSpread = other.ValuationMaxSpread;
            VegaUnit = other.VegaUnit;
            EmpiricDeltaShift = other.EmpiricDeltaShift;
            ValuationLowSpreadLimit = other.ValuationLowSpreadLimit;
            ValuationHighSpreadLimit = other.ValuationHighSpreadLimit;
            ValuationWide = other.ValuationWide;
            ValuationNarrow = other.ValuationNarrow;
            HighLiquidSpreadLimit = other.HighLiquidSpreadLimit;
            DeltaCorrection = other.DeltaCorrection;
            LowX = other.LowX;
            HighX = other.HighX;
            BestIvExpectation = other.BestIvExpectation;
            PermanentlyIlliquid = other.PermanentlyIlliquid;
            OptionChangeTrigger = other.OptionChangeTrigger;
            DisableCurveModel = other.DisableCurveModel;
            ValuationQuoteMinVolume = other.ValuationQuoteMinVolume;
            ValuationGlassDepth = other.ValuationGlassDepth;
            ValuationGlassMinVolume = other.ValuationGlassMinVolume;
            AggressiveReset = other.AggressiveReset;
            AggressiveResetTimeLimit = other.AggressiveResetTimeLimit;
            AMSRL = other.AMSRL;
            BMSRL = other.BMSRL;
            ConservativeReset = other.ConservativeReset;
            ConservativeResetLimit = other.ConservativeResetLimit;
            ConservativeResetTimeLimit = other.ConservativeResetTimeLimit;
            QuoteVolumeReset = other.QuoteVolumeReset;
            QuoteVolumeResetLimit = other.QuoteVolumeResetLimit;
            QuoteVolumeResetTimeLimit = other.QuoteVolumeResetTimeLimit;
            DealVolumeReset = other.DealVolumeReset;
            DealVolumeResetLimit = other.DealVolumeResetLimit;
            TimeReset = other.TimeReset;
            TimeResetLimit = other.TimeResetLimit;
        }

        protected override bool OnEquals(ConfigValuationParameters other) {
            return
                base.OnEquals(other) &&
                IsActive == other.IsActive &&
                SeriesId == other.SeriesId &&
                OptionType == other.OptionType &&
                AtmStrikeShift == other.AtmStrikeShift &&
                AWideningMktIv == other.AWideningMktIv &&
                ANarrowingMktIv == other.ANarrowingMktIv &&
                ValuationMaxSpread == other.ValuationMaxSpread &&
                VegaUnit == other.VegaUnit &&
                EmpiricDeltaShift == other.EmpiricDeltaShift &&
                ValuationLowSpreadLimit == other.ValuationLowSpreadLimit &&
                ValuationHighSpreadLimit == other.ValuationHighSpreadLimit &&
                ValuationWide == other.ValuationWide &&
                ValuationNarrow == other.ValuationNarrow &&
                HighLiquidSpreadLimit == other.HighLiquidSpreadLimit &&
                DeltaCorrection == other.DeltaCorrection &&
                LowX == other.LowX &&
                HighX == other.HighX &&
                BestIvExpectation == other.BestIvExpectation &&
                PermanentlyIlliquid == other.PermanentlyIlliquid &&
                OptionChangeTrigger == other.OptionChangeTrigger &&
                DisableCurveModel == other.DisableCurveModel &&
                ValuationQuoteMinVolume == other.ValuationQuoteMinVolume &&
                ValuationGlassDepth == other.ValuationGlassDepth &&
                ValuationGlassMinVolume == other.ValuationGlassMinVolume &&
                AggressiveReset == other.AggressiveReset &&
                AggressiveResetTimeLimit == other.AggressiveResetTimeLimit &&
                AMSRL == other.AMSRL &&
                BMSRL == other.BMSRL &&
                ConservativeReset == other.ConservativeReset &&
                ConservativeResetLimit == other.ConservativeResetLimit &&
                ConservativeResetTimeLimit == other.ConservativeResetTimeLimit &&
                QuoteVolumeReset == other.QuoteVolumeReset &&
                QuoteVolumeResetLimit == other.QuoteVolumeResetLimit &&
                QuoteVolumeResetTimeLimit == other.QuoteVolumeResetTimeLimit &&
                DealVolumeReset == other.DealVolumeReset &&
                DealVolumeResetLimit == other.DealVolumeResetLimit &&
                TimeReset == other.TimeReset &&
                TimeResetLimit == other.TimeResetLimit;
        }

        public static IEnumerable<string> GetLoggerFields() {
            return new[] {
                nameof(IsActive),
                nameof(SeriesId),
                nameof(OptionType),
                nameof(AtmStrikeShift),
                nameof(AWideningMktIv),
                nameof(ANarrowingMktIv),
                nameof(ValuationMaxSpread),
                nameof(VegaUnit),
                nameof(EmpiricDeltaShift),
                nameof(ValuationLowSpreadLimit),
                nameof(ValuationHighSpreadLimit),
                nameof(ValuationWide),
                nameof(ValuationNarrow),
                nameof(HighLiquidSpreadLimit),
                nameof(DeltaCorrection),
                nameof(LowX),
                nameof(HighX),
                nameof(BestIvExpectation),
                nameof(PermanentlyIlliquid),
                nameof(OptionChangeTrigger),
                nameof(DisableCurveModel),
                nameof(ValuationQuoteMinVolume),
                nameof(ValuationGlassDepth),
                nameof(ValuationGlassMinVolume),
                nameof(AggressiveReset),
                nameof(AggressiveResetTimeLimit),
                nameof(AMSRL),
                nameof(BMSRL),
                nameof(ConservativeReset),
                nameof(ConservativeResetLimit),
                nameof(ConservativeResetTimeLimit),
                nameof(QuoteVolumeReset),
                nameof(QuoteVolumeResetLimit),
                nameof(QuoteVolumeResetTimeLimit),
                nameof(DealVolumeReset),
                nameof(DealVolumeResetLimit),
                nameof(TimeReset),
                nameof(TimeResetLimit),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                IsActive,
                SeriesId,
                OptionType,
                AtmStrikeShift,
                AWideningMktIv,
                ANarrowingMktIv,
                ValuationMaxSpread,
                VegaUnit,
                EmpiricDeltaShift,
                ValuationLowSpreadLimit,
                ValuationHighSpreadLimit,
                ValuationWide,
                ValuationNarrow,
                HighLiquidSpreadLimit,
                DeltaCorrection,
                LowX,
                HighX,
                BestIvExpectation,
                PermanentlyIlliquid,
                OptionChangeTrigger,
                DisableCurveModel,
                ValuationQuoteMinVolume,
                ValuationGlassDepth,
                ValuationGlassMinVolume,
                AggressiveReset,
                AggressiveResetTimeLimit,
                AMSRL,
                BMSRL,
                ConservativeReset,
                ConservativeResetLimit,
                ConservativeResetTimeLimit,
                QuoteVolumeReset,
                QuoteVolumeResetLimit,
                QuoteVolumeResetTimeLimit,
                DealVolumeReset,
                DealVolumeResetLimit,
                TimeReset,
                TimeResetLimit,
            };
        } 
    }

    public interface IConfigValuationParameters : IReadOnlyConfiguration {
        bool IsActive {get;}
        string Id {get;}
        OptionSeriesId SeriesId {get;}
        OptionTypes OptionType {get;}
        int AtmStrikeShift {get;}
        decimal AWideningMktIv {get;}
        decimal ANarrowingMktIv {get;}
        decimal ValuationMaxSpread {get;}
        decimal VegaUnit {get;}
        decimal EmpiricDeltaShift {get;}
        decimal ValuationLowSpreadLimit {get;}
        decimal ValuationHighSpreadLimit {get;}
        decimal ValuationWide {get;}
        decimal ValuationNarrow {get;}
        decimal HighLiquidSpreadLimit {get;}
        decimal DeltaCorrection {get;}
        decimal LowX {get;}
        decimal HighX {get;}
        decimal BestIvExpectation {get;}
        bool PermanentlyIlliquid {get;}
        int OptionChangeTrigger {get;}
        bool DisableCurveModel {get;}
        int ValuationQuoteMinVolume {get;}
        int ValuationGlassDepth {get;}
        int ValuationGlassMinVolume {get;}
        bool AggressiveReset {get;}
        decimal AggressiveResetTimeLimit {get;}
        decimal AMSRL {get;}
        decimal BMSRL {get;}
        bool ConservativeReset {get;}
        decimal ConservativeResetLimit {get;}
        decimal ConservativeResetTimeLimit {get;}
        bool QuoteVolumeReset {get;}
        int QuoteVolumeResetLimit {get;}
        decimal QuoteVolumeResetTimeLimit {get;}
        bool DealVolumeReset {get;}
        int DealVolumeResetLimit {get;}
        bool TimeReset {get;}
        int TimeResetLimit {get;}

        IEnumerable<object> GetLoggerValues();
    }
}
