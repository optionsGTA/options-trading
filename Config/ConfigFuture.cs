using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

namespace OptionBot.Config {
    [Serializable]
    [DataContract]
    [CategoryOrder(FutureCategory, 1)]
    public class ConfigFuture : BaseConfig<ConfigFuture, IConfigFuture>, IConfigFuture {
        const string FutureCategory = "Фьючерс";

        #region fields/properties

        [DataMember] string _securityId;
        [DataMember] int _futuresChangeStartTrigger;
        [DataMember] int _atmStrikeDelay;
        [DataMember] decimal _vegaCallLongLimit, _vegaCallShortLimit, _vegaPutLongLimit, _vegaPutShortLimit;
        [DataMember] decimal _vegaLLimit, _vegaSLimit, _vegaHedgeLLimit, _vegaHedgeSLimit;
        [DataMember] decimal _mmVegaLongLimit, _mmVegaShortLimit, _deltaTarget, _vegaTarget;
        [DataMember] decimal _gammaUnit, _gammaTarget, _mmGammaLongLimit, _mmGammaShortLimit;
        [DataMember] decimal _gammaLLimit, _gammaSLimit, _gammaHedgeLLimit, _gammaHedgeSLimit;
        [DataMember] decimal _deepInTheMoneyLimit, _deepOutOfMoneyLimit;
        [DataMember] decimal _deepInTheMoneyDeltaCall, _deepOutOfMoneyDeltaCall;
        [DataMember] decimal _deepInTheMoneyDeltaPut, _deepOutOfMoneyDeltaPut;
        [DataMember] decimal _atTheMoneyDelta;
        [DataMember] decimal _liquidStrikeStep, _liquidSwitchFraction;
        [DataMember] decimal _daysToExpirationForInitialDelta;
        [DataMember] decimal _vannaUnit, _vannaLLimit, _vannaSLimit;
        [DataMember] decimal _vommaUnit, _vommaLLimit, _vommaSLimit;

        [Browsable(false)] public string SecurityId {get {return _securityId;} set {SetField(ref _securityId, value);}}

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"futures_change_limit")]
        [Description(@"Лимит изменения котировки фьючерса для запуска основного робота и модуля хеджирования, шагов цены фьючерса (1 для fRTS означает 10 пунктов).")]
        public int FuturesChangeStartTrigger { get { return _futuresChangeStartTrigger; } set { SetField(ref _futuresChangeStartTrigger, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"atm_strike_delay")]
        [Description(@"Запаздывание смены центрального страйка, шагов цены фьючерса (1 для fRTS означает 10 пунктов).")]
        public int AtmStrikeDelay { get { return _atmStrikeDelay; } set { SetField(ref _atmStrikeDelay, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"delta_target")]
        [Description(@"Целевая дельта.")]
        public decimal DeltaTarget { get { return _deltaTarget; } set { SetField(ref _deltaTarget, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vega_target")]
        [Description(@"Целевая вега.")]
        public decimal VegaTarget { get { return _vegaTarget; } set { SetField(ref _vegaTarget, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vega_l_limit")]
        [Description(@"Лимит длинной вега-экспозиции портфеля, в пунктах, может принимать положительные и отрицательные значения.")]
        public decimal VegaLLimit { get { return _vegaLLimit; } set { SetField(ref _vegaLLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vega_s_limit")]
        [Description(@"Лимит короткой вега-экспозиции портфеля, в пунктах, может принимать положительные и отрицательные значения.")]
        public decimal VegaSLimit { get { return _vegaSLimit; } set { SetField(ref _vegaSLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vega_call_long_limit")]
        [Description(@"Лимит длинной экспозиции портфеля коллов по веге, принимаемый за основу для расчета вега-лимита открытой позиции по каждому инструменту, в пунктах.")]
        public decimal VegaCallLongLimit { get { return _vegaCallLongLimit; } set { SetField(ref _vegaCallLongLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vega_call_short_limit")]
        [Description(@"Лимит короткой экспозиции портфеля коллов по веге, принимаемый за основу для расчета вега-лимита открытой позиции по каждому инструменту, в пунктах.")]
        public decimal VegaCallShortLimit { get { return _vegaCallShortLimit; } set { SetField(ref _vegaCallShortLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vega_put_long_limit")]
        [Description(@"Лимит длинной экспозиции портфеля путов по веге, принимаемый за основу для расчета вега-лимита открытой позиции по каждому инструменту, в пунктах.")]
        public decimal VegaPutLongLimit { get { return _vegaPutLongLimit; } set { SetField(ref _vegaPutLongLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vega_put_short_limit")]
        [Description(@"Лимит короткой экспозиции портфеля путов по веге, принимаемый за основу для расчета вега-лимита открытой позиции по каждому инструменту, в пунктах.")]
        public decimal VegaPutShortLimit { get { return _vegaPutShortLimit; } set { SetField(ref _vegaPutShortLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"mm_vega_long_limit")]
        [Description(@"Лимит положительной экспозиции портфеля по веге в пунктах, принимаемый за основу для расчета вега-лимита открытой позиции по каждому инструменту в стратегиях mm.")]
        public decimal MMVegaLongLimit { get { return _mmVegaLongLimit; } set { SetField(ref _mmVegaLongLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"mm_vega_short_limit")]
        [Description(@"Лимит отрицательной экспозиции портфеля по веге в пунктах, принимаемый за основу для расчета вега-лимита открытой позиции по каждому инструменту в стратегиях mm.")]
        public decimal MMVegaShortLimit { get { return _mmVegaShortLimit; } set { SetField(ref _mmVegaShortLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vega_hedge_l_limit")]
        [Description(@"Лимит длинной вега-экспозиции портфеля, по достижении которого активируется стратегия хеджирования, в пунктах, может принимать положительные и отрицательные значения.")]
        public decimal VegaHedgeLLimit { get { return _vegaHedgeLLimit; } set { SetField(ref _vegaHedgeLLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vega_hedge_s_limit")]
        [Description(@"Лимит короткой вега-экспозиции портфеля, по достижении которого активируется стратегия хеджирования, в пунктах, может принимать положительные и отрицательные значения.")]
        public decimal VegaHedgeSLimit { get { return _vegaHedgeSLimit; } set { SetField(ref _vegaHedgeSLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"gamma_unit")]
        [Description(@"Величина изменения котировки фьючерса, принимаемая за основу для расчета гаммы, в шагах цены фьючерса.")]
        public decimal GammaUnit { get { return _gammaUnit; } set { SetField(ref _gammaUnit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"gamma_target")]
        [Description(@"Целевая гамма.")]
        public decimal GammaTarget { get { return _gammaTarget; } set { SetField(ref _gammaTarget, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"gamma_l_limit")]
        [Description(@"Лимит длинной гамма-экспозиции портфеля, может принимать положительные и отрицательные значения.")]
        public decimal GammaLLimit { get { return _gammaLLimit; } set { SetField(ref _gammaLLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"gamma_s_limit")]
        [Description(@"Лимит короткой гамма-экспозиции портфеля, может принимать положительные и отрицательные значения.")]
        public decimal GammaSLimit { get { return _gammaSLimit; } set { SetField(ref _gammaSLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"mm_gamma_long_limit")]
        [Description(@"Лимит положительной экспозиции портфеля по гамме в пунктах, принимаемый за основу для расчета гамма-лимита открытой позиции по каждому инструменту в стратегиях mm.")]
        public decimal MMGammaLongLimit { get { return _mmGammaLongLimit; } set { SetField(ref _mmGammaLongLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"mm_gamma_short_limit")]
        [Description(@"Лимит отрицательной экспозиции портфеля по гамме в пунктах, принимаемый за основу для расчета гамма-лимита открытой позиции по каждому инструменту в стратегиях mm.")]
        public decimal MMGammaShortLimit { get { return _mmGammaShortLimit; } set { SetField(ref _mmGammaShortLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"gamma_hedge_l_limit")]
        [Description(@"Лимит длинной гамма-экспозиции портфеля, по достижении которого активируется стратегия хеджирования, может принимать положительные и отрицательные значения.")]
        public decimal GammaHedgeLLimit { get { return _gammaHedgeLLimit; } set { SetField(ref _gammaHedgeLLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"gamma_hedge_s_limit")]
        [Description(@"Лимит короткой гамма-экспозиции портфеля, по достижении которого активируется стратегия хеджирования, может принимать положительные и отрицательные значения.")]
        public decimal GammaHedgeSLimit { get { return _gammaHedgeSLimit; } set { SetField(ref _gammaHedgeSLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vanna_unit")]
        [Description(@"Ванна-юнит.")]
        public decimal VannaUnit { get { return _vannaUnit; } set { SetField(ref _vannaUnit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vanna_l_limit")]
        [Description(@"Лимит длинной ванна-экспозиции портфеля, может принимать положительные и отрицательные значения.")]
        public decimal VannaLLimit { get { return _vannaLLimit; } set { SetField(ref _vannaLLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vanna_s_limit")]
        [Description(@"Лимит короткой ванна-экспозиции портфеля, может принимать положительные и отрицательные значения.")]
        public decimal VannaSLimit { get { return _vannaSLimit; } set { SetField(ref _vannaSLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vomma_unit")]
        [Description(@"Вомма-юнит.")]
        public decimal VommaUnit { get { return _vommaUnit; } set { SetField(ref _vommaUnit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vomma_l_limit")]
        [Description(@"Лимит длинной вомма-экспозиции портфеля, может принимать положительные и отрицательные значения.")]
        public decimal VommaLLimit { get { return _vommaLLimit; } set { SetField(ref _vommaLLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"vomma_s_limit")]
        [Description(@"Лимит короткой вомма-экспозиции портфеля, может принимать положительные и отрицательные значения.")]
        public decimal VommaSLimit { get { return _vommaSLimit; } set { SetField(ref _vommaSLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"deep_in_the_money_limit")]
        [Description(@"Абсолютное значение лимита, после которого опцион получает признак «deep_in_the_money» или «at_the_money», может принимать значения [0,1].")]
        public decimal DeepInTheMoneyLimit { get { return _deepInTheMoneyLimit; } set { SetField(ref _deepInTheMoneyLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"deep_out_of_money_limit")]
        [Description(@"Абсолютное значение лимита, после которого опцион получает признак «deep_out_of_money» или «at_the_money», может принимать значения [0,1].")]
        public decimal DeepOutOfMoneyLimit { get { return _deepOutOfMoneyLimit; } set { SetField(ref _deepOutOfMoneyLimit, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"deep_in_the_money_delta_call")]
        [Description(@"Начальное значение дельты для неликвидного опциона кол «глубоко в деньгах», может принимать значения [0,1].")]
        public decimal DeepInTheMoneyDeltaCall { get { return _deepInTheMoneyDeltaCall; } set { SetField(ref _deepInTheMoneyDeltaCall, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"deep_out_of_money_delta_call")]
        [Description(@"Начальное значение дельты для неликвидного опциона кол «глубоко вне денег», может принимать значения [0,1].")]
        public decimal DeepOutOfMoneyDeltaCall { get { return _deepOutOfMoneyDeltaCall; } set { SetField(ref _deepOutOfMoneyDeltaCall, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"deep_in_the_money_delta_put")]
        [Description(@"Начальное значение дельты для неликвидного опциона пут «глубоко в деньгах», может принимать значения [0,1].")]
        public decimal DeepInTheMoneyDeltaPut { get { return _deepInTheMoneyDeltaPut; } set { SetField(ref _deepInTheMoneyDeltaPut, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"deep_out_of_money_delta_call")]
        [Description(@"Начальное значение дельты для неликвидного опциона пут «глубоко вне денег», может принимать значения [0,1].")]
        public decimal DeepOutOfMoneyDeltaPut { get { return _deepOutOfMoneyDeltaPut; } set { SetField(ref _deepOutOfMoneyDeltaPut, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"at_the_money_delta")]
        [Description(@"Начальное значение дельты для неликвидных опционов «около денег».")]
        public decimal AtTheMoneyDelta { get { return _atTheMoneyDelta; } set { SetField(ref _atTheMoneyDelta, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"liquid_strike_step")]
        [Description(@"Шаг ликвидного страйка в пунктах базового актива (если у RTS шаг=5000, то ликвидными будут страйки, кратные 5000).")]
        public decimal LiquidStrikeStep { get { return _liquidStrikeStep; } set { SetField(ref _liquidStrikeStep, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"liquid_switch_fraction")]
        [Description(@"Доля расстояния между ликвидными страйками, при которой будет произведено переключение ATM. Для ликвидных страйков (70000, 75000) и доли=0.25 переключение колла происходит на цене 71250, пута - 73750")]
        public decimal LiquidSwitchFraction { get { return _liquidSwitchFraction; } set { SetField(ref _liquidSwitchFraction, value); } }

        [Category(FutureCategory)]
        [AutoPropertyOrder]
        [DisplayName(@"days_to_expiration_for_initial_delta")]
        [Description(@"Количество дней до экспирации, начиная с которого неликвидные дельты принимают начальные значения и не корректируются, может принимать дробные значения.")]
        public decimal DaysToExpirationForInitialDelta { get { return _daysToExpirationForInitialDelta; } set { SetField(ref _daysToExpirationForInitialDelta, value); } }

        #endregion

        protected override void SetDefaultValues() {
            SecurityId = "";
            FuturesChangeStartTrigger = 2;
            AtmStrikeDelay = 30;
            VegaLLimit = 2000;
            VegaSLimit = -2000;
            VegaCallLongLimit = VegaCallShortLimit = VegaPutLongLimit = VegaPutShortLimit = 1000;
            DeltaTarget = 0;
            VegaTarget = 0;
            VegaHedgeLLimit = 500;
            VegaHedgeSLimit = -500;
            GammaTarget = 0;
            GammaUnit = 500;
            GammaLLimit = 10;
            GammaSLimit = -10;
            GammaHedgeLLimit = 5;
            GammaHedgeSLimit = -5;
            VannaUnit = 0.01m;
            VannaLLimit = 10;
            VannaSLimit = -10;
            VommaUnit = 0.0001m;
            VommaLLimit = 1000;
            VommaSLimit = -1000;
            LiquidStrikeStep = 1;
            LiquidSwitchFraction = 0.25m;
            MMVegaLongLimit = 10000;
            MMVegaShortLimit = -10000;
            MMGammaLongLimit = 30;
            MMGammaShortLimit = -30;
            DeepInTheMoneyLimit = 0.05m;
            DeepOutOfMoneyLimit = 0.15m;
            DeepInTheMoneyDeltaCall = 1;
            DeepOutOfMoneyDeltaCall = 0;
            DeepInTheMoneyDeltaCall = 0;
            DeepOutOfMoneyDeltaCall = 1;
            AtTheMoneyDelta = 0.5m;
            DaysToExpirationForInitialDelta = 2;
        }

        protected override void CopyFromImpl(ConfigFuture other) {
            base.CopyFromImpl(other);

            SecurityId = other.SecurityId;
            FuturesChangeStartTrigger = other.FuturesChangeStartTrigger;
            AtmStrikeDelay = other.AtmStrikeDelay;
            DeltaTarget = other.DeltaTarget;
            VegaTarget = other.VegaTarget;
            VegaLLimit = other.VegaLLimit;
            VegaSLimit = other.VegaSLimit;
            VegaHedgeLLimit = other.VegaHedgeLLimit;
            VegaHedgeSLimit = other.VegaHedgeSLimit;
            VegaCallLongLimit = other.VegaCallLongLimit;
            VegaCallShortLimit = other.VegaCallShortLimit;
            VegaPutLongLimit = other.VegaPutLongLimit;
            VegaPutShortLimit = other.VegaPutShortLimit;
            MMVegaLongLimit = other.MMVegaLongLimit;
            MMVegaShortLimit = other.MMVegaShortLimit;
            GammaUnit = other.GammaUnit;
            GammaTarget = other.GammaTarget;
            GammaLLimit = other.GammaLLimit;
            GammaSLimit = other.GammaSLimit;
            GammaHedgeLLimit = other.GammaHedgeLLimit;
            GammaHedgeSLimit = other.GammaHedgeSLimit;
            VannaUnit = other.VannaUnit;
            VannaLLimit = other.VannaLLimit;
            VannaSLimit = other.VannaSLimit;
            VommaUnit = other.VommaUnit;
            VommaLLimit = other.VommaLLimit;
            VommaSLimit = other.VommaSLimit;
            MMGammaLongLimit = other.MMGammaLongLimit;
            MMGammaShortLimit = other.MMGammaShortLimit;
            DeepInTheMoneyLimit = other.DeepInTheMoneyLimit;
            DeepOutOfMoneyLimit = other.DeepOutOfMoneyLimit;
            DeepInTheMoneyDeltaCall = other.DeepInTheMoneyDeltaCall;
            DeepOutOfMoneyDeltaCall = other.DeepOutOfMoneyDeltaCall;
            DeepInTheMoneyDeltaPut = other.DeepInTheMoneyDeltaPut;
            DeepOutOfMoneyDeltaPut = other.DeepOutOfMoneyDeltaPut;
            AtTheMoneyDelta = other.AtTheMoneyDelta;
            LiquidStrikeStep = other.LiquidStrikeStep;
            LiquidSwitchFraction = other.LiquidSwitchFraction;
            DaysToExpirationForInitialDelta = other.DaysToExpirationForInitialDelta;
        }

        protected override bool OnEquals(ConfigFuture other) {
            return
                base.OnEquals(other) &&
                SecurityId == other.SecurityId &&
                FuturesChangeStartTrigger == other.FuturesChangeStartTrigger &&
                AtmStrikeDelay == other.AtmStrikeDelay &&
                DeltaTarget == other.DeltaTarget &&
                VegaTarget == other.VegaTarget &&
                VegaLLimit == other.VegaLLimit &&
                VegaSLimit == other.VegaSLimit &&
                VegaHedgeLLimit == other.VegaHedgeLLimit &&
                VegaHedgeSLimit == other.VegaHedgeSLimit &&
                VegaCallLongLimit == other.VegaCallLongLimit &&
                VegaCallShortLimit == other.VegaCallShortLimit &&
                VegaPutLongLimit == other.VegaPutLongLimit &&
                VegaPutShortLimit == other.VegaPutShortLimit &&
                MMVegaLongLimit == other.MMVegaLongLimit &&
                MMVegaShortLimit == other.MMVegaShortLimit &&
                GammaUnit == other.GammaUnit &&
                GammaTarget == other.GammaTarget &&
                GammaLLimit == other.GammaLLimit &&
                GammaSLimit == other.GammaSLimit &&
                GammaHedgeLLimit == other.GammaHedgeLLimit &&
                GammaHedgeSLimit == other.GammaHedgeSLimit &&
                VannaUnit == other.VannaUnit &&
                VannaLLimit == other.VannaLLimit &&
                VannaSLimit == other.VannaSLimit &&
                VommaUnit == other.VommaUnit &&
                VommaLLimit == other.VommaLLimit &&
                VommaSLimit == other.VommaSLimit &&
                MMGammaLongLimit == other.MMGammaLongLimit &&
                MMGammaShortLimit == other.MMGammaShortLimit &&
                DeepInTheMoneyLimit == other.DeepInTheMoneyLimit &&
                DeepOutOfMoneyLimit == other.DeepOutOfMoneyLimit &&
                DeepInTheMoneyDeltaCall == other.DeepInTheMoneyDeltaCall &&
                DeepOutOfMoneyDeltaCall == other.DeepOutOfMoneyDeltaCall &&
                DeepInTheMoneyDeltaPut == other.DeepInTheMoneyDeltaPut &&
                DeepOutOfMoneyDeltaPut == other.DeepOutOfMoneyDeltaPut &&
                AtTheMoneyDelta == other.AtTheMoneyDelta &&
                LiquidStrikeStep == other.LiquidStrikeStep &&
                LiquidSwitchFraction == other.LiquidSwitchFraction &&
                DaysToExpirationForInitialDelta == other.DaysToExpirationForInitialDelta;
        }

        public override void VerifyConfig(List<string> errors) {
            base.VerifyConfig(errors);

            if(FuturesChangeStartTrigger < 1 || FuturesChangeStartTrigger > 1000)
                errors.Add("лимит изменения котировки фьючерса");

            if(LiquidStrikeStep <= 0)
                errors.Add("liquid_strike_step");

            if(LiquidSwitchFraction < 0 || LiquidSwitchFraction > 1)
                errors.Add("liquid_switch_fraction");

            if(DeepInTheMoneyLimit < 0 || DeepInTheMoneyLimit > 1) errors.Add(nameof(DeepInTheMoneyLimit));
            if(DeepOutOfMoneyLimit < 0 || DeepOutOfMoneyLimit > 1) errors.Add(nameof(DeepOutOfMoneyLimit));

            if(DeepInTheMoneyDeltaCall < 0 || DeepInTheMoneyDeltaCall > 1) errors.Add(nameof(DeepInTheMoneyDeltaCall));
            if(DeepOutOfMoneyDeltaCall < 0 || DeepOutOfMoneyDeltaCall > 1) errors.Add(nameof(DeepOutOfMoneyDeltaCall));

            if(DeepInTheMoneyDeltaPut < 0 || DeepInTheMoneyDeltaPut > 1) errors.Add(nameof(DeepInTheMoneyDeltaPut));
            if(DeepOutOfMoneyDeltaPut < 0 || DeepOutOfMoneyDeltaPut > 1) errors.Add(nameof(DeepOutOfMoneyDeltaPut));

            if(AtTheMoneyDelta < 0 || AtTheMoneyDelta > 1) errors.Add(nameof(AtTheMoneyDelta));

            if(DaysToExpirationForInitialDelta < 0) errors.Add(nameof(DaysToExpirationForInitialDelta));

            if(VegaCallLongLimit < 0) errors.Add(nameof(VegaCallLongLimit));
            if(VegaCallShortLimit < 0) errors.Add(nameof(VegaCallShortLimit));
            if(VegaPutLongLimit < 0) errors.Add(nameof(VegaPutLongLimit));
            if(VegaPutShortLimit < 0) errors.Add(nameof(VegaPutShortLimit));

            if(VegaSLimit > VegaLLimit) errors.Add($"{nameof(VegaSLimit)} > {nameof(VegaLLimit)}");
        }

        #region logger methods

        public static string[] GetLoggerFields() {
            return new [] {
                nameof(SecurityId),
                nameof(FuturesChangeStartTrigger),
                nameof(AtmStrikeDelay),
                nameof(DeltaTarget),
                nameof(VegaTarget),
                nameof(VegaLLimit),
                nameof(VegaSLimit),
                nameof(VegaHedgeLLimit),
                nameof(VegaHedgeSLimit),
                nameof(VegaCallLongLimit),
                nameof(VegaCallShortLimit),
                nameof(VegaPutLongLimit),
                nameof(VegaPutShortLimit),
                nameof(MMVegaLongLimit),
                nameof(MMVegaShortLimit),
                nameof(GammaUnit),
                nameof(GammaTarget),
                nameof(GammaLLimit),
                nameof(GammaSLimit),
                nameof(GammaHedgeLLimit),
                nameof(GammaHedgeSLimit),
                nameof(VannaUnit),
                nameof(VannaLLimit),
                nameof(VannaSLimit),
                nameof(VommaUnit),
                nameof(VommaLLimit),
                nameof(VommaSLimit),
                nameof(MMGammaLongLimit),
                nameof(MMGammaShortLimit),
                nameof(DeepInTheMoneyLimit),
                nameof(DeepOutOfMoneyLimit),
                nameof(DeepInTheMoneyDeltaCall),
                nameof(DeepOutOfMoneyDeltaCall),
                nameof(DeepInTheMoneyDeltaPut),
                nameof(DeepOutOfMoneyDeltaPut),
                nameof(AtTheMoneyDelta),
                nameof(LiquidStrikeStep),
                nameof(LiquidSwitchFraction),
                nameof(DaysToExpirationForInitialDelta),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                SecurityId,
                FuturesChangeStartTrigger,
                AtmStrikeDelay,
                DeltaTarget,
                VegaTarget,
                VegaLLimit,
                VegaSLimit,
                VegaHedgeLLimit,
                VegaHedgeSLimit,
                VegaCallLongLimit,
                VegaCallShortLimit,
                VegaPutLongLimit,
                VegaPutShortLimit,
                MMVegaLongLimit,
                MMVegaShortLimit,
                GammaUnit,
                GammaTarget,
                GammaLLimit,
                GammaSLimit,
                GammaHedgeLLimit,
                GammaHedgeSLimit,
                VannaUnit,
                VannaLLimit,
                VannaSLimit,
                VommaUnit,
                VommaLLimit,
                VommaSLimit,
                MMGammaLongLimit,
                MMGammaShortLimit,
                DeepInTheMoneyLimit,
                DeepOutOfMoneyLimit,
                DeepInTheMoneyDeltaCall,
                DeepOutOfMoneyDeltaCall,
                DeepInTheMoneyDeltaPut,
                DeepOutOfMoneyDeltaPut,
                AtTheMoneyDelta,
                LiquidStrikeStep,
                LiquidSwitchFraction,
                DaysToExpirationForInitialDelta,
            };
        }
        #endregion
    }

    public interface IConfigFuture : IReadOnlyConfiguration {
        string SecurityId {get;}
        int FuturesChangeStartTrigger {get;}
        int AtmStrikeDelay {get;}
        decimal DeltaTarget {get;}
        decimal VegaTarget {get;}
        decimal VegaLLimit {get;}
        decimal VegaSLimit {get;}
        decimal VegaCallLongLimit {get;}
        decimal VegaCallShortLimit {get;}
        decimal VegaPutLongLimit {get;}
        decimal VegaPutShortLimit {get;}
        decimal VegaHedgeLLimit {get;}
        decimal VegaHedgeSLimit {get;}
        decimal GammaUnit {get;}
        decimal GammaTarget {get;}
        decimal GammaLLimit {get;}
        decimal GammaSLimit {get;}
        decimal GammaHedgeLLimit {get;}
        decimal GammaHedgeSLimit {get;}
        decimal VannaUnit {get;}
        decimal VannaLLimit {get;}
        decimal VannaSLimit {get;}
        decimal VommaUnit {get;}
        decimal VommaLLimit {get;}
        decimal VommaSLimit {get;}
        decimal LiquidStrikeStep {get;}
        decimal LiquidSwitchFraction {get;}
        decimal MMVegaLongLimit {get;}
        decimal MMVegaShortLimit {get;}
        decimal MMGammaLongLimit {get;}
        decimal MMGammaShortLimit {get;}
        decimal DeepInTheMoneyLimit {get;}
        decimal DeepOutOfMoneyLimit {get;}
        decimal DeepInTheMoneyDeltaCall {get;}
        decimal DeepOutOfMoneyDeltaCall {get;}
        decimal DeepInTheMoneyDeltaPut {get;}
        decimal DeepOutOfMoneyDeltaPut {get;}
        decimal AtTheMoneyDelta {get;}
        decimal DaysToExpirationForInitialDelta {get;}

        IEnumerable<object> GetLoggerValues();
    }
}
