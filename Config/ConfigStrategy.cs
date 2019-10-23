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
    [KnownType(typeof(ConfigRegularStrategy))]
    [KnownType(typeof(ConfigMMStrategy))]
    [KnownType(typeof(ConfigVegaGammaHedgeStrategy))]
    [CategoryOrder(CategoryGeneral, 1)]
    [CategoryOrder(CategoryStrategy, 2)]
    public abstract class ConfigStrategy : BaseConfig<ConfigStrategy, IConfigStrategy>, IConfigStrategy {
        const string CategoryGeneral = "Общие";
        const string CategoryStrategy = "Стратегия";

        #region fields/properties

        public override Type SerializerType => typeof(ConfigStrategy);

        // common
        [DataMember] OptionSeriesId _seriesId;
        [DataMember] int _atmStrikeShift;
        [DataMember] OptionTypes _optionType;
        [DataMember] int _balanceLimit;
        [DataMember] int _incremental;
        [DataMember] StrategyRegime _strategyRegime;
        [DataMember] int _minIncreaseOrderVolume, _minDecreaseOrderVolume;
        [DataMember] double _orderSpreadLimit;
        [DataMember] double _orderLowestSellIvLimit, _orderHighestBuyIvLimit;
        [DataMember] bool _checkOrderIv;
        [DataMember] double _AShiftOL, _AShiftOS; // except MM
        [DataMember] double _aChangeNarrow, _aChangeWide;
        [DataMember] double _lowChangeNarrow, _highChangeNarrow;
        [DataMember] double _lowChangeWide, _highChangeWide;
        [DataMember] bool _illiquidTrading, _illiquidCurveTrading;
        [DataMember] double _AIlliquidIvBid, _AIlliquidIvOffer;
        [DataMember] double _BIlliquidIvBid, _BIlliquidIvOffer;
        [DataMember] double _highIlliquidIvBid, _lowIlliquidIvOffer;
        [DataMember] bool _autoStartStop, _curveOrdering, _curveControl, _marketControl;
        [DataMember] double _activeCurveShiftOL, _activeCurveShiftOS, _passiveCurveShiftOL, _passiveCurveShiftOS;
        // regular
        [DataMember] bool _closeRegime;
        [DataMember] int _minOrderVolume; // +vega/gamma
        [DataMember] StrategyOrderDirection? _strategyDirection;
        [DataMember] double _AShiftCL, _AShiftCS;
        [DataMember] double _a1OrderSpread, _a2OrderSpread; // +vega/gamma
        [DataMember] int _lowOrderSpread, _highOrderSpread; // +vega/gamma
        [DataMember] double _aShiftNorm, _aShiftMM; // +MM
        [DataMember] int _shiftCLLimit, _shiftCSLimit;
        [DataMember] int _shiftOLLimit, _shiftOSLimit; // +vega/gamma
        [DataMember] double _activeCurveShiftCL, _activeCurveShiftCS, _passiveCurveShiftCL, _passiveCurveShiftCS;
        // mm
        [DataMember] int _mmVolume;
        [DataMember] double _MMMaxSpread;
        [DataMember] bool _autoObligationsVolume, _autoObligationsSpread;
        [DataMember] int _obligationsVolumeCorrection, _obligationsSpreadCorrection;


        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"strategy_type")]
        [Description(@"Тип стратегии.")]
        public abstract StrategyType StrategyType {get;}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"series")]
        [Description(@"Серия опционов.")]
        public OptionSeriesId SeriesId {get {return _seriesId;} set{ SetField(ref _seriesId, value);}}
        
        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"atm_strike_shift")]
        [Description(@"Сдвиг центрального страйка, шагов (для fRTS 1 шаг = 5000 пунктов).")]
        public int AtmStrikeShift {get {return _atmStrikeShift;} set{ SetField(ref _atmStrikeShift, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"call_put")]
        [Description(@"Тип опциона.")]
        public OptionTypes OptionType {get {return _optionType;} set{ SetField(ref _optionType, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"balance_limit")]
        [Description(@"Лимит открытой позиции по опционам.")]
        public int BalanceLimit {get {return _balanceLimit;} set{ SetField(ref _balanceLimit, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"incremental")]
        [Description(@"Лимит лотов в заявке.")]
        public int Incremental {get {return _incremental;} set{ SetField(ref _incremental, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"strategy_regime")]
        [Description(@"Режим стратегии.")]
        public StrategyRegime StrategyRegime {get {return _strategyRegime;} set{ SetField(ref _strategyRegime, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"min_increase_order_volume")]
        [Description(@"Минимальное увеличение объема заявки, если цена не изменилась значительно.")]
        public int MinIncreaseOrderVolume {get {return _minIncreaseOrderVolume;} set{ SetField(ref _minIncreaseOrderVolume, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"min_decrease_order_volume")]
        [Description(@"Минимальное уменьшение объема заявки, если цена не изменилась значительно.")]
        public int MinDecreaseOrderVolume {get {return _minDecreaseOrderVolume;} set{ SetField(ref _minDecreaseOrderVolume, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"order_spread_limit")]
        [Description(@"Верхний лимит рыночного спреда market_spread, выше которого заявки не отправляются. Лимит также участвует в расчете объемов для заявки.")]
        public double OrderSpreadLimit {get {return _orderSpreadLimit;} set{ SetField(ref _orderSpreadLimit, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"order_lowest_sell_iv_limit")]
        [Description(@"Волатильность, ниже которой заявки на продажу не отправляются.")]
        public double OrderLowestSellIvLimit {get {return _orderLowestSellIvLimit;} set{ SetField(ref _orderLowestSellIvLimit, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"order_highest_buy_iv_limit")]
        [Description(@"Волатильность, выше которой заявки на покупку не отправляются.")]
        public double OrderHighestBuyIvLimit {get {return _orderHighestBuyIvLimit;} set{ SetField(ref _orderHighestBuyIvLimit, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"check_order_iv")]
        [Description(@"Пересчет цены заявки в волатильность и проверка перед отправкой.")]
        public bool CheckOrderIv {get {return _checkOrderIv;} set{ SetField(ref _checkOrderIv, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"a_shift_ol")]
        [Description(@"Линейный коэффициент A для расчета iv_target_shift_open_long.")]
        public virtual double AShiftOL {get {return _AShiftOL;} set{ SetField(ref _AShiftOL, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"a_shift_os")]
        [Description(@"Линейный коэффициент A для расчета iv_target_shift_open_short.")]
        public virtual double AShiftOS {get {return _AShiftOS;} set{ SetField(ref _AShiftOS, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"a_change_narrow")]
        [Description(@"Линейный коэффициент A для расчета change_narrow в зависимости от market_spread / min_price_step.")]
        public double AChangeNarrow {get {return _aChangeNarrow;} set{ SetField(ref _aChangeNarrow, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"a_change_wide")]
        [Description(@"Линейный коэффициент A для расчета change_wide в зависимости от market_spread / min_price_step.")]
        public double AChangeWide {get {return _aChangeWide;} set{ SetField(ref _aChangeWide, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"low_change_narrow")]
        [Description(@"Нижняя граница change_narrow.")]
        public double LowChangeNarrow {get {return _lowChangeNarrow;} set{ SetField(ref _lowChangeNarrow, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"high_change_narrow")]
        [Description(@"Верхняя граница change_narrow.")]
        public double HighChangeNarrow {get {return _highChangeNarrow;} set{ SetField(ref _highChangeNarrow, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"low_change_wide")]
        [Description(@"Нижняя граница change_wide.")]
        public double LowChangeWide {get {return _lowChangeWide;} set{ SetField(ref _lowChangeWide, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"high_change_wide")]
        [Description(@"Верхняя граница change_wide.")]
        public double HighChangeWide {get {return _highChangeWide;} set{ SetField(ref _highChangeWide, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"illiquid_trading")]
        [Description(@"Возможность совершать сделки по неликвидным инструментам, даже если нет кв.")]
        public bool IlliquidTrading {get {return _illiquidTrading;} set{ SetField(ref _illiquidTrading, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"illiquid_curve_trading")]
        [Description(@"Признак, определяющий возможность отправлять заявки по неликвидным инструментам при наличии параметров кв.")]
        public bool IlliquidCurveTrading {get {return _illiquidCurveTrading;} set{ SetField(ref _illiquidCurveTrading, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"a_illiquid_iv_bid")]
        [Description(@"Линейный коэффициент А для расчета illiquid_iv_bid.")]
        public double AIlliquidIvBid {get {return _AIlliquidIvBid;} set{ SetField(ref _AIlliquidIvBid, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"a_illiquid_iv_offer")]
        [Description(@"Линейный коэффициент А для расчета illiquid_iv_offer.")]
        public double AIlliquidIvOffer {get {return _AIlliquidIvOffer;} set{ SetField(ref _AIlliquidIvOffer, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"b_illiquid_iv_bid")]
        [Description(@"Линейный коэффициент B для расчета illiquid_iv_bid, в терминах волатильности.")]
        public double BIlliquidIvBid {get {return _BIlliquidIvBid;} set{ SetField(ref _BIlliquidIvBid, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"b_illiquid_iv_offer")]
        [Description(@"Линейный коэффициент B для расчета illiquid_iv_offer, в терминах волатильности.")]
        public double BIlliquidIvOffer {get {return _BIlliquidIvOffer;} set{ SetField(ref _BIlliquidIvOffer, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"high_illiquid_iv_bid")]
        [Description(@"Верхняя граница illiquid_iv_bid, база для максимально возможной цены в заявке на покупку неликвидного опциона.")]
        public double HighIlliquidIvBid {get {return _highIlliquidIvBid;} set{ SetField(ref _highIlliquidIvBid, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"low_illiquid_iv_offer")]
        [Description(@"Нижняя граница illiquid_iv_offer, база для минимально возможной цены в заявке на продажу неликвидного опциона.")]
        public double LowIlliquidIvOffer {get {return _lowIlliquidIvOffer;} set{ SetField(ref _lowIlliquidIvOffer, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"auto_start_stop")]
        [Description(@"Автоматический запуск/остановка (по таблице MM-obligations для MM, по позиции - для остальных).")]
        public bool AutoStartStop {get {return _autoStartStop;} set{ SetField(ref _autoStartStop, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"curve_ordering")]
        [Description(@"Признак расчета цен в заявках по кв.")]
        public bool CurveOrdering {get {return _curveOrdering;} set{ SetField(ref _curveOrdering, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"curve_control")]
        [Description(@"Признак контроля цен в заявках по рынку на цены по кв.")]
        public bool CurveControl {get {return _curveControl;} set{ SetField(ref _curveControl, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"market_control")]
        [Description(@"Признак контроля цен в заявках по кв на цены по рынку.")]
        public bool MarketControl {get {return _marketControl;} set{ SetField(ref _marketControl, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"active_curve_shift_ol")]
        [Description(@"Сдвиг цены в заявке по кв на открытие длинной позиции относительно curve_bid, положительное значение снижает цену заявки, отрицательное - повышает.")]
        public double ActiveCurveShiftOL {get {return _activeCurveShiftOL;} set{ SetField(ref _activeCurveShiftOL, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"active_curve_shift_os")]
        [Description(@"Сдвиг цены в заявке по кв на открытие короткой позиции относительно curve_offer, положительное значение повышает цену заявки, отрицательное - снижает.")]
        public double ActiveCurveShiftOS {get {return _activeCurveShiftOS;} set{ SetField(ref _activeCurveShiftOS, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"passive_curve_shift_ol")]
        [Description(@"Сдвиг цены по кв относительно curve_bid для контроля заявки по рынку на открытие длинной позиции, положительное значение снижает цену по кв, отрицательное - повышает.")]
        public double PassiveCurveShiftOL {get {return _passiveCurveShiftOL;} set{ SetField(ref _passiveCurveShiftOL, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"passive_curve_shift_os")]
        [Description(@"Сдвиг цены по кв относительно curve_offer для контроля заявки по рынку на открытие короткой позиции, положительное значение повышает цену по кв, отрицательное - снижает.")]
        public double PassiveCurveShiftOS {get {return _passiveCurveShiftOS;} set{ SetField(ref _passiveCurveShiftOS, value);}}

        // #################### regular ####################

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"close_regime")]
        [Description(@"Режим закрытия позиций.")]
        public virtual bool CloseRegime {get {return _closeRegime;} set{ SetField(ref _closeRegime, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"min_order_volume")]
        [Description(@"Минимальный объем заявки.")]
        public virtual int MinOrderVolume {get {return _minOrderVolume;} set{ SetField(ref _minOrderVolume, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"strategy_direction")]
        [Description(@"Направление заявок по инструменту.")]
        public virtual StrategyOrderDirection? StrategyDirection {get {return _strategyDirection;} set {SetField(ref _strategyDirection, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"a_shift_cl")]
        [Description(@"Линейный коэффициент A для расчета iv_target_shift_close_long.")]
        public virtual double AShiftCL {get {return _AShiftCL;} set{ SetField(ref _AShiftCL, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"a_shift_cs")]
        [Description(@"Линейный коэффициент A для расчета iv_target_shift_close_short.")]
        public virtual double AShiftCS {get {return _AShiftCS;} set{ SetField(ref _AShiftCS, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"a1_order_spread")]
        [Description(@"Линейный коэффициент A для расчета order_spread в зависимости от valuation_spread.")]
        public virtual double A1OrderSpread {get {return _a1OrderSpread;} set{ SetField(ref _a1OrderSpread, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"a2_order_spread")]
        [Description(@"Линейный коэффициент A для расчета order_spread в зависимости от change_wide * min_price_step.")]
        public virtual double A2OrderSpread {get {return _a2OrderSpread;} set{ SetField(ref _a2OrderSpread, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"low_order_spread")]
        [Description(@"Нижняя граница order_spread, целое положительное число.")]
        public virtual int LowOrderSpread {get {return _lowOrderSpread;} set{ SetField(ref _lowOrderSpread, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"high_order_spread")]
        [Description(@"Верхняя граница order_spread, целое положительное число.")]
        public virtual int HighOrderSpread {get {return _highOrderSpread;} set{ SetField(ref _highOrderSpread, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"a_shift_norm")]
        [Description(@"Линейный коэффициент для расчета a_shift_ol и a_shift_os, если вега-экспозиция портфеля находится в интервале нормальных вега-лимитов (vega_s_limit, vega_l_limit). Если коэффициент > 1, то агрессивнее цена заявки в сторону, противоположную вега-экспозиции, с учетом ванна-экспозиции (агрессивно закрывается открытая вега-экспозиция).")]
        public virtual double AShiftNorm {get {return _aShiftNorm;} set{ SetField(ref _aShiftNorm, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"a_shift_mm")]
        [Description(@"Линейный коэффициент для расчета a_shift_ol и a_shift_os, если вега-экспозиция портфеля не выходит за вега-лимит для стратегий mm. Если коэффициент > 1, то агрессивнее цена заявки в сторону, противоположную вега-экспозиции, с учетом ванна-экспозиции (агрессивно закрывается открытая вега-экспозиция).")]
        public virtual double AShiftMM {get {return _aShiftMM;} set{ SetField(ref _aShiftMM, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"shift_ol_limit")]
        [Description(@"Нижний лимит смещения цены заявки на покупку-открытие позиции относительно рыночной цены опциона, в пунктах.")]
        public int ShiftOLLimit {get {return _shiftOLLimit;} set{ SetField(ref _shiftOLLimit, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"shift_os_limit")]
        [Description(@"Нижний лимит смещения цены заявки на продажу-открытие позиции относительно рыночной цены опциона, в пунктах.")]
        public int ShiftOSLimit {get {return _shiftOSLimit;} set{ SetField(ref _shiftOSLimit, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"shift_cl_limit")]
        [Description(@"Нижний лимит смещения цены заявки на покупку-закрытие позиции относительно рыночной цены опциона, в пунктах.")]
        public virtual int ShiftCLLimit {get {return _shiftCLLimit;} set{ SetField(ref _shiftCLLimit, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"shift_cs_limit")]
        [Description(@"Нижний лимит смещения цены заявки на продажу-закрытие позиции относительно рыночной цены опциона, в пунктах.")]
        public virtual int ShiftCSLimit {get {return _shiftCSLimit;} set{ SetField(ref _shiftCSLimit, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"active_curve_shift_cl")]
        [Description(@"Сдвиг цены в заявке по кв на закрытие длинной позиции относительно curve_offer, положительное значение повышает цену заявки, отрицательное - понижает.")]
        public virtual double ActiveCurveShiftCL {get {return _activeCurveShiftCL;} set{ SetField(ref _activeCurveShiftCL, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"active_curve_shift_cs")]
        [Description(@"Сдвиг цены в заявке по кв на закрытие короткой позиции относительно curve_bid, положительное значение снижает цену заявки, отрицательное - повышает.")]
        public virtual double ActiveCurveShiftCS {get {return _activeCurveShiftCS;} set{ SetField(ref _activeCurveShiftCS, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"passive_curve_shift_cl")]
        [Description(@"Сдвиг цены по кв относительно curve_offer для контроля заявки по рынку на закрытие длинной позиции, положительное значение повышает цену по кв, отрицательное - снижает.")]
        public virtual double PassiveCurveShiftCL {get {return _passiveCurveShiftCL;} set{ SetField(ref _passiveCurveShiftCL, value);}}

        [Category(CategoryGeneral)]
        [AutoPropertyOrder]
        [DisplayName(@"passive_curve_shift_cs")]
        [Description(@"Сдвиг цены по кв относительно curve_bid для контроля заявки по рынку на закрытие короткой позиции, положительное значение снижает цену по кв, отрицательное - повышает.")]
        public virtual double PassiveCurveShiftCS {get {return _passiveCurveShiftCS;} set{ SetField(ref _passiveCurveShiftCS, value);}}

        // #################### mm ####################

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"mm_volume")]
        [Description(@"Минимальный объем в заявке по обязательствам маркет­мейкера.")]
        public virtual int MMVolume {get {return _mmVolume;} set {SetField(ref _mmVolume, value); }}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"mm_max_spread")]
        [Description(@"Максимальный спред мм, в пунктах (устанавливается биржей).")]
        public virtual double MMMaxSpread {get {return _MMMaxSpread;} set{ SetField(ref _MMMaxSpread, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"auto_obligations_volume")]
        [Description(@"Признак автоматического расчета mm_volume.")]
        public virtual bool AutoObligationsVolume {get {return _autoObligationsVolume;} set{ SetField(ref _autoObligationsVolume, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"obligations_volume_correction")]
        [Description(@"Ручная корректировка obligations_volume.")]
        public virtual int ObligationsVolumeCorrection {get {return _obligationsVolumeCorrection;} set{ SetField(ref _obligationsVolumeCorrection, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"auto_obligations_spread")]
        [Description(@"Признак автоматического расчета mm_max_spread.")]
        public virtual bool AutoObligationsSpread {get {return _autoObligationsSpread;} set{ SetField(ref _autoObligationsSpread, value);}}

        [Category(CategoryStrategy)]
        [AutoPropertyOrder]
        [DisplayName(@"obligations_spread_correction")]
        [Description(@"Ручная корректировка obligations_spread.")]
        public virtual int ObligationsSpreadCorrection {get {return _obligationsSpreadCorrection;} set{ SetField(ref _obligationsSpreadCorrection, value);}}

        #endregion

        public override void VerifyConfig(List<string> errors) {
            base.VerifyConfig(errors);

            if(BalanceLimit < 0 || BalanceLimit > 100000)
                errors.Add("лимит открытой позиции по опционам");
            if(Incremental < 0)
                errors.Add("лимит лотов в заявке");
            if(OrderHighestBuyIvLimit < EpsilonDouble || OrderHighestBuyIvLimit > 10)
                errors.Add("OrderHighestBuyIvLimit");
            if(OrderLowestSellIvLimit < EpsilonDouble || OrderLowestSellIvLimit > 10)
                errors.Add("OrderHighestBuyIvLimit");

            if(MinIncreaseOrderVolume < 1) errors.Add("min_increase_order_volume");
            if(MinDecreaseOrderVolume < 1) errors.Add("min_decrease_order_volume");

            if(AIlliquidIvBid <= 0 || AIlliquidIvBid > 2) errors.Add("AIlliquidIvBid");
            if(AIlliquidIvOffer <= 0 || AIlliquidIvOffer > 10) errors.Add("AIlliquidIvOffer");

            if(BIlliquidIvBid < -1 || BIlliquidIvBid > 2) errors.Add("BIlliquidIvBid");
            if(BIlliquidIvOffer < -1 || BIlliquidIvOffer > 2) errors.Add("BIlliquidIvOffer");

            if(HighChangeNarrow <= 0) errors.Add(nameof(HighChangeNarrow));
            if(LowChangeNarrow <= 0) errors.Add(nameof(LowChangeNarrow));
            if(HighChangeWide <= 0) errors.Add(nameof(HighChangeWide));
            if(LowChangeWide <= 0) errors.Add(nameof(LowChangeWide));

            if(HighIlliquidIvBid <= 0 || HighIlliquidIvBid > 10) errors.Add(nameof(HighIlliquidIvBid));
            if(LowIlliquidIvOffer < -10 || LowIlliquidIvOffer > 10) errors.Add(nameof(LowIlliquidIvOffer));

            if(OrderSpreadLimit <= 0) errors.Add(nameof(OrderSpreadLimit));
        }

        protected override void SetDefaultValues() {
            base.SetDefaultValues();

            AtmStrikeShift = 0;
            OptionType = OptionTypes.Call;
            BalanceLimit = 10;
            Incremental = 10;
            StrategyRegime = StrategyRegime.OpenAndClose;
            MinIncreaseOrderVolume = MinDecreaseOrderVolume = 5;
            OrderSpreadLimit = 100;
            OrderLowestSellIvLimit = 0.3d;
            OrderHighestBuyIvLimit = 0.6d;
            CheckOrderIv = false;
            AShiftOL = AShiftOS = 1;
            AChangeNarrow = 0.4;
            AChangeWide = 0.4;
            LowChangeNarrow = 1;
            HighChangeNarrow = 2;
            LowChangeWide = 1;
            HighChangeWide = 2;
            IlliquidTrading = IlliquidCurveTrading = false;
            AIlliquidIvBid = 1;
            AIlliquidIvOffer = 1.05;
            BIlliquidIvBid = 0;
            BIlliquidIvOffer = 0;
            HighIlliquidIvBid = 0.3;
            LowIlliquidIvOffer = 0.6;
            AutoStartStop = false;
            ShiftOLLimit = -10;
            ShiftOSLimit = -10;
            ShiftCLLimit = -20;
            ShiftCSLimit = -20;

            CloseRegime = false;
            MinOrderVolume = 5;
            StrategyDirection = StrategyOrderDirection.BuyAndSell;
            AShiftCL = AShiftCS = 1;
            A1OrderSpread = 1;
            A2OrderSpread = 1.5;
            LowOrderSpread = 30;
            HighOrderSpread = 50;

            MMVolume = 100;
            MMMaxSpread = 150;

            CurveOrdering = CurveControl = MarketControl = false;
            ActiveCurveShiftOL = ActiveCurveShiftOS = 20;
            ActiveCurveShiftCL = ActiveCurveShiftCS = 10;
            PassiveCurveShiftOL = PassiveCurveShiftOS = 10;
            PassiveCurveShiftCL = PassiveCurveShiftCS = 0;
        }

        protected override void CopyFromImpl(ConfigStrategy other) {
            base.CopyFromImpl(other);

            SeriesId = other.SeriesId;
            AtmStrikeShift = other.AtmStrikeShift;
            OptionType = other.OptionType;
            BalanceLimit = other.BalanceLimit;
            Incremental = other.Incremental;
            StrategyRegime = other.StrategyRegime;
            MinIncreaseOrderVolume = other.MinIncreaseOrderVolume;
            MinDecreaseOrderVolume = other.MinDecreaseOrderVolume;
            OrderSpreadLimit = other.OrderSpreadLimit;
            OrderLowestSellIvLimit = other.OrderLowestSellIvLimit;
            OrderHighestBuyIvLimit = other.OrderHighestBuyIvLimit;
            CheckOrderIv = other.CheckOrderIv;
            AShiftOL = other.AShiftOL;
            AShiftOS = other.AShiftOS;
            AChangeNarrow = other.AChangeNarrow;
            AChangeWide = other.AChangeWide;
            LowChangeNarrow = other.LowChangeNarrow;
            HighChangeNarrow = other.HighChangeNarrow;
            LowChangeWide = other.LowChangeWide;
            HighChangeWide = other.HighChangeWide;
            IlliquidTrading = other.IlliquidTrading;
            IlliquidCurveTrading = other.IlliquidCurveTrading;
            AIlliquidIvBid = other.AIlliquidIvBid;
            AIlliquidIvOffer = other.AIlliquidIvOffer;
            BIlliquidIvBid = other.BIlliquidIvBid;
            BIlliquidIvOffer = other.BIlliquidIvOffer;
            HighIlliquidIvBid = other.HighIlliquidIvBid;
            LowIlliquidIvOffer = other.LowIlliquidIvOffer;
            AutoStartStop = other.AutoStartStop;
            CurveOrdering = other.CurveOrdering;
            CurveControl = other.CurveControl;
            MarketControl = other.MarketControl;
            ActiveCurveShiftOL = other.ActiveCurveShiftOL;
            ActiveCurveShiftOS = other.ActiveCurveShiftOS;
            PassiveCurveShiftOL = other.PassiveCurveShiftOL;
            PassiveCurveShiftOS = other.PassiveCurveShiftOS;
            // regular
            CloseRegime = other.CloseRegime;
            MinOrderVolume = other.MinOrderVolume;
            StrategyDirection = other.StrategyDirection;
            AShiftCL = other.AShiftCL;
            AShiftCS = other.AShiftCS;
            A1OrderSpread = other.A1OrderSpread;
            A2OrderSpread = other.A2OrderSpread;
            LowOrderSpread = other.LowOrderSpread;
            HighOrderSpread = other.HighOrderSpread;
            AShiftNorm = other.AShiftNorm;
            AShiftMM = other.AShiftMM;
            ShiftOLLimit = other.ShiftOLLimit;
            ShiftOSLimit = other.ShiftOSLimit;
            ShiftCLLimit = other.ShiftCLLimit;
            ShiftCSLimit = other.ShiftCSLimit;
            ActiveCurveShiftCL = other.ActiveCurveShiftCL;
            ActiveCurveShiftCS = other.ActiveCurveShiftCS;
            PassiveCurveShiftCL = other.PassiveCurveShiftCL;
            PassiveCurveShiftCS = other.PassiveCurveShiftCS;
            // mm
            MMVolume = other.MMVolume;
            MMMaxSpread = other.MMMaxSpread;
            AutoObligationsVolume = other.AutoObligationsVolume;
            ObligationsVolumeCorrection = other.ObligationsVolumeCorrection;
            AutoObligationsSpread = other.AutoObligationsSpread;
            ObligationsSpreadCorrection = other.ObligationsSpreadCorrection;
        }

        protected override bool OnEquals(ConfigStrategy other) {
            return base.OnEquals(other) &&
                StrategyType == other.StrategyType &&
                SeriesId == other.SeriesId &&
                AtmStrikeShift == other.AtmStrikeShift &&
                OptionType == other.OptionType &&
                BalanceLimit == other.BalanceLimit &&
                Incremental == other.Incremental &&
                StrategyRegime == other.StrategyRegime &&
                MinIncreaseOrderVolume == other.MinIncreaseOrderVolume &&
                MinDecreaseOrderVolume == other.MinDecreaseOrderVolume &&
                OrderSpreadLimit.IsEqual(other.OrderSpreadLimit) &&
                OrderLowestSellIvLimit.IsEqual(other.OrderLowestSellIvLimit) &&
                OrderHighestBuyIvLimit.IsEqual(other.OrderHighestBuyIvLimit) &&
                CheckOrderIv == other.CheckOrderIv &&
                AShiftOL.IsEqual(other.AShiftOL) &&
                AShiftOS.IsEqual(other.AShiftOS) &&
                AChangeNarrow.IsEqual(other.AChangeNarrow) &&
                AChangeWide.IsEqual(other.AChangeWide) &&
                LowChangeNarrow.IsEqual(other.LowChangeNarrow) &&
                HighChangeNarrow.IsEqual(other.HighChangeNarrow)  &&
                LowChangeWide.IsEqual(other.LowChangeWide) &&
                HighChangeWide.IsEqual(other.HighChangeWide) &&
                IlliquidTrading == other.IlliquidTrading &&
                IlliquidCurveTrading == other.IlliquidCurveTrading &&
                AIlliquidIvBid.IsEqual(other.AIlliquidIvBid) &&
                AIlliquidIvOffer.IsEqual(other.AIlliquidIvOffer) &&
                BIlliquidIvBid.IsEqual(other.BIlliquidIvBid) &&
                BIlliquidIvOffer.IsEqual(other.BIlliquidIvOffer) &&
                HighIlliquidIvBid.IsEqual(other.HighIlliquidIvBid) &&
                LowIlliquidIvOffer.IsEqual(other.LowIlliquidIvOffer) &&
                AutoStartStop == other.AutoStartStop &&
                CurveOrdering == other.CurveOrdering &&
                CurveControl == other.CurveControl &&
                MarketControl == other.MarketControl &&
                ActiveCurveShiftOL.IsEqual(other.ActiveCurveShiftOL) &&
                ActiveCurveShiftOS.IsEqual(other.ActiveCurveShiftOS) &&
                PassiveCurveShiftOL.IsEqual(other.PassiveCurveShiftOL) &&
                PassiveCurveShiftOS.IsEqual(other.PassiveCurveShiftOS) &&
                // regular
                CloseRegime == other.CloseRegime &&
                MinOrderVolume == other.MinOrderVolume &&
                StrategyDirection == other.StrategyDirection &&
                AShiftCL.IsEqual(other.AShiftCL) &&
                AShiftCS.IsEqual(other.AShiftCS) &&
                A1OrderSpread.IsEqual(other.A1OrderSpread) &&
                A2OrderSpread.IsEqual(other.A2OrderSpread) &&
                LowOrderSpread == other.LowOrderSpread &&
                HighOrderSpread == other.HighOrderSpread &&
                AShiftNorm.IsEqual(other.AShiftNorm) &&
                AShiftMM.IsEqual(other.AShiftMM) &&
                ShiftOLLimit == other.ShiftOLLimit &&
                ShiftOSLimit == other.ShiftOSLimit &&
                ShiftCLLimit == other.ShiftCLLimit &&
                ShiftCSLimit == other.ShiftCSLimit &&
                ActiveCurveShiftCL.IsEqual(other.ActiveCurveShiftCL) &&
                ActiveCurveShiftCS.IsEqual(other.ActiveCurveShiftCS) &&
                PassiveCurveShiftCL.IsEqual(other.PassiveCurveShiftCL) &&
                PassiveCurveShiftCS.IsEqual(other.PassiveCurveShiftCS) &&
                // mm
                MMVolume == other.MMVolume &&
                MMMaxSpread.IsEqual(other.MMMaxSpread) &&
                AutoObligationsVolume == other.AutoObligationsVolume &&
                ObligationsVolumeCorrection == other.ObligationsVolumeCorrection &&
                AutoObligationsSpread == other.AutoObligationsSpread &&
                ObligationsSpreadCorrection == other.ObligationsSpreadCorrection;
        }

        public static IEnumerable<string> GetLoggerFields() {
            return new[] {
                nameof(SeriesId),
                nameof(AtmStrikeShift),
                nameof(OptionType),
                nameof(BalanceLimit),
                nameof(Incremental),
                nameof(StrategyRegime),
                nameof(MinIncreaseOrderVolume),
                nameof(MinDecreaseOrderVolume),
                nameof(OrderSpreadLimit),
                nameof(OrderLowestSellIvLimit),
                nameof(OrderHighestBuyIvLimit),
                nameof(CheckOrderIv),
                nameof(AShiftOL),
                nameof(AShiftOS),
                nameof(AChangeNarrow),
                nameof(AChangeWide),
                nameof(LowChangeNarrow),
                nameof(HighChangeNarrow),
                nameof(LowChangeWide),
                nameof(HighChangeWide),
                nameof(IlliquidTrading),
                nameof(IlliquidCurveTrading),
                nameof(AIlliquidIvBid),
                nameof(AIlliquidIvOffer),
                nameof(BIlliquidIvBid),
                nameof(BIlliquidIvOffer),
                nameof(HighIlliquidIvBid),
                nameof(LowIlliquidIvOffer),
                nameof(AutoStartStop),
                nameof(CurveOrdering),
                nameof(CurveControl),
                nameof(MarketControl),
                nameof(ActiveCurveShiftOL),
                nameof(ActiveCurveShiftOS),
                nameof(PassiveCurveShiftOL),
                nameof(PassiveCurveShiftOS),
                // regular
                nameof(CloseRegime),
                nameof(MinOrderVolume),
                nameof(StrategyDirection),
                nameof(AShiftCL),
                nameof(AShiftCS),
                nameof(A1OrderSpread),
                nameof(A2OrderSpread),
                nameof(LowOrderSpread),
                nameof(HighOrderSpread),
                nameof(AShiftNorm),
                nameof(AShiftMM),
                nameof(ShiftOLLimit),
                nameof(ShiftOSLimit),
                nameof(ShiftCLLimit),
                nameof(ShiftCSLimit),
                nameof(ActiveCurveShiftCL),
                nameof(ActiveCurveShiftCS),
                nameof(PassiveCurveShiftCL),
                nameof(PassiveCurveShiftCS),
                // mm
                nameof(MMVolume),
                nameof(MMMaxSpread),
                nameof(AutoObligationsVolume),
                nameof(ObligationsVolumeCorrection),
                nameof(AutoObligationsSpread),
                nameof(ObligationsSpreadCorrection),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                SeriesId,
                AtmStrikeShift,
                OptionType,
                BalanceLimit,
                Incremental,
                StrategyRegime,
                MinIncreaseOrderVolume,
                MinDecreaseOrderVolume,
                OrderSpreadLimit,
                OrderLowestSellIvLimit,
                OrderHighestBuyIvLimit,
                CheckOrderIv,
                AShiftOL,
                AShiftOS,
                AChangeNarrow,
                AChangeWide,
                LowChangeNarrow,
                HighChangeNarrow,
                LowChangeWide,
                HighChangeWide,
                IlliquidTrading,
                IlliquidCurveTrading,
                AIlliquidIvBid,
                AIlliquidIvOffer,
                BIlliquidIvBid,
                BIlliquidIvOffer,
                HighIlliquidIvBid,
                LowIlliquidIvOffer,
                AutoStartStop,
                CurveOrdering,
                CurveControl,
                MarketControl,
                ActiveCurveShiftOL,
                ActiveCurveShiftOS,
                PassiveCurveShiftOL,
                PassiveCurveShiftOS,
                // regular
                CloseRegime,
                MinOrderVolume,
                StrategyDirection,
                AShiftCL,
                AShiftCS,
                A1OrderSpread,
                A2OrderSpread,
                LowOrderSpread,
                HighOrderSpread,
                AShiftNorm,
                AShiftMM,
                ShiftOLLimit,
                ShiftOSLimit,
                ShiftCLLimit,
                ShiftCSLimit,
                ActiveCurveShiftCL,
                ActiveCurveShiftCS,
                PassiveCurveShiftCL,
                PassiveCurveShiftCS,
                // mm
                MMVolume,
                MMMaxSpread,
                AutoObligationsVolume,
                ObligationsVolumeCorrection,
                AutoObligationsSpread,
                ObligationsSpreadCorrection,
            };
        }
    }

    [Serializable]
    [DataContract]
    public class ConfigRegularStrategy : ConfigStrategy {
        #region fields/properties

        public override StrategyType StrategyType => StrategyType.Regular;

        // remove mm params from ui
        [Browsable(false)] public override int MMVolume {get {return 0;} set {}}
        [Browsable(false)] public override double MMMaxSpread {get {return 0;} set {}}
        [Browsable(false)] public override bool AutoObligationsVolume {get {return false;} set {}}
        [Browsable(false)] public override bool AutoObligationsSpread {get {return false;} set {}}
        [Browsable(false)] public override int ObligationsVolumeCorrection {get {return 0;} set {}}
        [Browsable(false)] public override int ObligationsSpreadCorrection {get {return 0;} set {}}

        #endregion

        public override void VerifyConfig(List<string> errors) {
            base.VerifyConfig(errors);

            if(MinOrderVolume < 1) errors.Add("min_order_volume");

            if(StrategyDirection == null)
                errors.Add("strategy_direction is null");

            if(HighOrderSpread <= 0) errors.Add(nameof(HighOrderSpread));
            if(LowOrderSpread <= 0) errors.Add(nameof(LowOrderSpread));
        }
    }

    [Serializable]
    [DataContract]
    public class ConfigMMStrategy : ConfigStrategy {
        #region fields/properties

        public override StrategyType StrategyType => StrategyType.MM;

        // remove non-mm params from ui
        [Browsable(false)] public override StrategyOrderDirection? StrategyDirection {get {return null;} set {}}
        [Browsable(false)] public override double AShiftOL {get {return 0;} set {}}
        [Browsable(false)] public override double AShiftOS {get {return 0;} set {}}
        [Browsable(false)] public override double AShiftCL {get {return 0;} set {}}
        [Browsable(false)] public override double AShiftCS {get {return 0;} set {}}
        [Browsable(false)] public override int MinOrderVolume {get {return 0;} set {}}
        [Browsable(false)] public override bool CloseRegime {get {return false;} set {}}
        [Browsable(false)] public override double A1OrderSpread {get {return 0;} set {}}
        [Browsable(false)] public override double A2OrderSpread {get {return 0;} set {}}
        [Browsable(false)] public override int HighOrderSpread {get {return 0;} set {}}
        [Browsable(false)] public override int LowOrderSpread {get {return 0;} set {}}
        [Browsable(false)] public override int ShiftCLLimit {get {return 0;} set {}}
        [Browsable(false)] public override int ShiftCSLimit {get {return 0;} set {}}
        [Browsable(false)] public override double ActiveCurveShiftCL {get {return 0;} set {}}
        [Browsable(false)] public override double ActiveCurveShiftCS {get {return 0;} set {}}
        [Browsable(false)] public override double PassiveCurveShiftCL {get {return 0;} set {}}
        [Browsable(false)] public override double PassiveCurveShiftCS {get {return 0;} set {}}

        #endregion

        public override void VerifyConfig(List<string> errors) {
            base.VerifyConfig(errors);

            if(MMVolume < 1)
                errors.Add(nameof(MMVolume));

//            if(ObligationsVolumeCorrection < 0)
//                errors.Add(nameof(ObligationsVolumeCorrection));
        }

        protected override void SetDefaultValues() {
            base.SetDefaultValues();

            MinIncreaseOrderVolume = 1;
            AChangeNarrow = 0.2;
            AChangeWide = 0.1;
            LowChangeNarrow = 2;
            HighChangeNarrow = 3;
            LowChangeWide = 2;
            HighChangeWide = 3;
            AShiftNorm = 1;
            AShiftMM = 1.5;
            ObligationsVolumeCorrection = 10;
            ObligationsSpreadCorrection = 0;
            AutoObligationsSpread = AutoObligationsVolume = false;
        }
    }

    [Serializable]
    [DataContract]
    public class ConfigVegaGammaHedgeStrategy : ConfigStrategy {
        #region fields/properties

        public override StrategyType StrategyType => _type;

        [DataMember] StrategyType _type;

        // remove mm params from ui
        [Browsable(false)] public override int MMVolume {get {return 0;} set {}}
        [Browsable(false)] public override double MMMaxSpread {get {return 0;} set {}}
        [Browsable(false)] public override bool AutoObligationsVolume {get {return false;} set {}}
        [Browsable(false)] public override bool AutoObligationsSpread {get {return false;} set {}}
        [Browsable(false)] public override int ObligationsVolumeCorrection {get {return 0;} set {}}
        [Browsable(false)] public override int ObligationsSpreadCorrection {get {return 0;} set {}}

        // remove regular params from ui
        [Browsable(false)] public override StrategyOrderDirection? StrategyDirection {get {return null;} set {}}
        [Browsable(false)] public override double AShiftCL {get {return 0;} set {}}
        [Browsable(false)] public override double AShiftCS {get {return 0;} set {}}
        [Browsable(false)] public override bool CloseRegime {get {return false;} set {}}
        [Browsable(false)] public override int ShiftCLLimit {get {return 0;} set {}}
        [Browsable(false)] public override int ShiftCSLimit {get {return 0;} set {}}

        [Browsable(false)] public override double AShiftNorm {get {return 0;} set {}}
        [Browsable(false)] public override double AShiftMM {get {return 0;} set {}}

        [Browsable(false)] public override double ActiveCurveShiftCL {get {return 0;} set {}}
        [Browsable(false)] public override double ActiveCurveShiftCS {get {return 0;} set {}}
        [Browsable(false)] public override double PassiveCurveShiftCL {get {return 0;} set {}}
        [Browsable(false)] public override double PassiveCurveShiftCS {get {return 0;} set {}}

        #endregion

        public ConfigVegaGammaHedgeStrategy(StrategyType stype) {
            if(stype != StrategyType.GammaHedge && stype != StrategyType.VegaHedge)
                throw new ArgumentOutOfRangeException(nameof(stype));

            _type = stype;
        }

        public override void VerifyConfig(List<string> errors) {
            base.VerifyConfig(errors);

            if(MinOrderVolume < 1) errors.Add(nameof(MinOrderVolume));
            if(HighOrderSpread <= 0) errors.Add(nameof(HighOrderSpread));
            if(LowOrderSpread <= 0) errors.Add(nameof(LowOrderSpread));
        }
    }

    #region strategy config interfaces

    public interface IConfigStrategy : IReadOnlyConfiguration {
        StrategyType StrategyType {get;}
        OptionSeriesId SeriesId {get;}
        int AtmStrikeShift {get;}
        OptionTypes OptionType {get;}
        int BalanceLimit {get;}
        int Incremental {get;}
        StrategyRegime StrategyRegime {get;}
        int MinIncreaseOrderVolume {get;}
        int MinDecreaseOrderVolume {get;}
        double OrderSpreadLimit {get;}
        double OrderLowestSellIvLimit {get;}
        double OrderHighestBuyIvLimit {get;}
        bool CheckOrderIv {get;}
        double AShiftOL {get;}
        double AShiftOS {get;}
        double AChangeNarrow {get;}
        double AChangeWide {get;}
        double LowChangeNarrow {get;}
        double HighChangeNarrow {get;}
        double LowChangeWide {get;}
        double HighChangeWide {get;}
        bool IlliquidTrading {get;}
        bool IlliquidCurveTrading {get;}
        double AIlliquidIvBid {get;}
        double AIlliquidIvOffer {get;}
        double BIlliquidIvBid {get;}
        double BIlliquidIvOffer {get;}
        double HighIlliquidIvBid {get;}
        double LowIlliquidIvOffer {get;}
        bool AutoStartStop {get;}
        bool CurveOrdering {get;}
        bool CurveControl {get;}
        bool MarketControl {get;}
        double ActiveCurveShiftOL {get;}
        double ActiveCurveShiftOS {get;}
        double PassiveCurveShiftOL {get;}
        double PassiveCurveShiftOS {get;}
        // regular
        bool CloseRegime {get;}
        int MinOrderVolume {get;} // +vega,gamma
        StrategyOrderDirection? StrategyDirection {get;}
        double AShiftCL {get;}
        double AShiftCS {get;}
        double A1OrderSpread {get;}
        double A2OrderSpread {get;}
        int LowOrderSpread {get;}
        int HighOrderSpread {get;}
        double AShiftNorm {get;}
        double AShiftMM {get;}
        int ShiftOLLimit {get;}
        int ShiftOSLimit {get;}
        int ShiftCLLimit {get;}
        int ShiftCSLimit {get;}
        double ActiveCurveShiftCL {get;}
        double ActiveCurveShiftCS {get;}
        double PassiveCurveShiftCL {get;}
        double PassiveCurveShiftCS {get;}
        // mm
        int MMVolume {get;}
        double MMMaxSpread {get;}
        bool AutoObligationsVolume {get;}
        int ObligationsVolumeCorrection {get;}
        bool AutoObligationsSpread {get;}
        int ObligationsSpreadCorrection {get;}

        IEnumerable<object> GetLoggerValues();
    }

    public interface ICalculatedConfigStrategy {
        [MyPropertyOrder] int CalcMMVolume {get;}
        [MyPropertyOrder] double CalcMMMaxSpread {get;}
        [MyPropertyOrder] double SpreadsDifference {get;}
        [MyPropertyOrder] double OrderShift {get;}
        [MyPropertyOrder] bool TradingAllowedByLiquidity {get;}
        [MyPropertyOrder] double IlliquidIvBid {get;}
        [MyPropertyOrder] double IlliquidIvOffer {get;}
        [MyPropertyOrder] double IlliquidIvSpread {get;}
        [MyPropertyOrder] double IlliquidIvAverage {get;}
        [MyPropertyOrder] double OrderSpread {get;}
        [MyPropertyOrder] double MMAShiftOL {get;}
        [MyPropertyOrder] double MMAShiftOS {get;}
        [MyPropertyOrder] double ShiftOL {get;}
        [MyPropertyOrder] double ShiftOS {get;}
        [MyPropertyOrder] double ShiftCL {get;}
        [MyPropertyOrder] double ShiftCS {get;}
        [MyPropertyOrder] double ChangeNarrow {get;}
        [MyPropertyOrder] double ChangeWide {get;}
        [MyPropertyOrder] double ObligationsSpread {get;}
        [MyPropertyOrder] int ObligationsVolume {get;}
    }

    #endregion
}
