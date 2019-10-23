using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Runtime.Serialization;
using Ecng.Common;
using StockSharp.Plaza;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

namespace OptionBot.Config {
    public interface IConfigGeneral : IReadOnlyConfiguration {
        string PlazaAddress {get;}
        string PlazaCGateKey {get;}
        int PlazaTransactionsLimit {get;}
        double PlazaTransactionsRateNewOrderFraction {get;}
        double PlazaTransactionsMMOnlyDelay {get;}
        bool PlazaUseFastRepl {get;}
        string Portfolio {get;}
        decimal RealtimeEnterTimeDiff {get;}
        decimal RealtimeExitTimeDiff {get;}
        decimal RealtimeExitDelay {get;}
        decimal RealtimeMaxCalcDelay {get;}
        decimal IR {get;}
        decimal IvCalculationAccuracy {get;}
        decimal DeltaExpositionLimit {get;}
        decimal CapitalLimit {get;}
        decimal TransactionsRatio {get;}
        decimal DealsRatio {get;}
        decimal TransactionsFeeRatio {get;}
        decimal TransactionsCommissionLimit {get;}
        int TransactionOpenPosDayLimit {get;}
        decimal VegaCoeff {get;}
        decimal GammaCoeff {get;}
        decimal ThetaUnit {get;}
        decimal DrawdownLimit {get;}
        int MarketOrderShift {get;}
        string NotifyEmail {get;}
        bool ExtendedLog {get;}
        decimal RecalcGroupPeriod {get;}
        bool UseZeroPollTimeout {get;}
        bool LogMessageProcessor {get;}
        int MMDelayFirst {get;}
        int MMDelayPeriodic {get;}
        int MMCalcPeriodic {get;}
        int VolumeGroupPeriod {get;}
        // note: need to change _generalCfgCurveNames in CurveManager if added/removed curve params
        int CurveInterval {get;}
        int CurveDiscrete {get;}
        int PreCurveBegin {get;}
        int CurveDelayLimit {get;}
        int PreCurveDelayLimit {get;}

        IEnumerable<object> GetLoggerValues();
    }

    [Serializable]
    [DataContract]
    [CategoryOrder(PlazaCategory, 1)]
    [CategoryOrder(ConstantsCategory, 2)]
    [CategoryOrder(TransactionCategory, 3)]
    [CategoryOrder(RealtimeCategory, 4)]
    public class ConfigGeneral : BaseConfig<ConfigGeneral, IConfigGeneral>, IConfigGeneral {
        const string PlazaCategory = "Plaza";
        const string RealtimeCategory = "Режим реального времени";
        const string TransactionCategory = "Транзакции";
        const string ConstantsCategory = "Константы";
        const string MiscCategory = "Misc";

        #region fields/properties

        [DataMember] string _plazaAddress, _plazaCGateKey;
        [DataMember] int _plazaTransactionsLimit;
        [DataMember] double _plazaTransactionsMMOnlyDelay, _plazaTransactionsRateNewOrderFraction;
        [DataMember] bool _plazaUseFastRepl;
        [DataMember] string _portfolio;
        [DataMember] decimal _realtimeEnterTimeDiff, _realtimeExitTimeDiff, _realtimeExitDelay, _realtimeMaxCalcDelay;
        [DataMember] decimal _ir;
        [DataMember] decimal _ivCalcAccuracy;
        [DataMember] decimal _deltaExpositionLimit;
        [DataMember] decimal _capitalLimit;
        [DataMember] decimal _transactionsRatio, _dealsRatio, _transactionsFeeRatio, _transactionsCommissionLimit;
        [DataMember] int _transactionDayLimit;
        [DataMember] decimal _vegaCoeff;
        [DataMember] decimal _gammaCoeff;
        [DataMember] decimal _thetaUnit;
        [DataMember] decimal _drawdownLimit;
        [DataMember] int _marketOrderShift;
        [DataMember] string _notifyEmail;
        [DataMember] bool _extendedLog;
        [DataMember] decimal _recalcGroupPeriod;
        [DataMember] bool _useZeroPollTimeout;
        [DataMember] bool _logMessageProcessor;
        [DataMember] int _mmDelayFirst, _mmDelayPeriodic;
        [DataMember] int _volumeGroupPeriod;
        [DataMember] int _curveInterval, _curveDiscrete, _preCurveBegin, _curveDelayLimit, _preCurveDelayLimit;
        [DataMember] int _mmCalcPeriodic;

        #region plaza

        [Category(PlazaCategory)]
        [PropertyOrder(0)]
        [DisplayName(@"Адрес Plaza")]
        [Description(@"Адрес шлюза Plaza, который будет использован роботом для подключения.")]
        public string PlazaAddress {get {return _plazaAddress;} set {SetField(ref _plazaAddress, value);}}

        [Category(PlazaCategory)]
        [PropertyOrder(1)]
        [DisplayName(@"Ключ CGate")]
        [Description(@"Ключ для подключения через CGate. (ключ для тестового полигона - " + PlazaSessionHolder.DemoCGateKey + ")")]
        public string PlazaCGateKey {get {return _plazaCGateKey;} set {SetField(ref _plazaCGateKey, value);}}

        [Category(PlazaCategory)]
        [PropertyOrder(2)]
        [DisplayName(@"tran_plaza_limit")]
        [Description(@"Максимальная частота транзакций Plaza, в секунду.")]
        public int PlazaTransactionsLimit {get {return _plazaTransactionsLimit;} set {SetField(ref _plazaTransactionsLimit, value);}}

        [Category(PlazaCategory)]
        [PropertyOrder(3)]
        [DisplayName(@"tran_neworder_fraction")]
        [Description(@"Доля от максимальной частоты транзакций, которую не могут превышать транзакции типа NewOrder. [0.1; 0.9]")]
        public double PlazaTransactionsRateNewOrderFraction {get {return _plazaTransactionsRateNewOrderFraction;} set {SetField(ref _plazaTransactionsRateNewOrderFraction, value);}}

        [Category(PlazaCategory)]
        [PropertyOrder(4)]
        [DisplayName(@"tran_mm_only_delay")]
        [Description(@"Период после превышения лимита по транзакциям, в течение которого отправляются заявки только по ММ стратегиям, сек")]
        public double PlazaTransactionsMMOnlyDelay {get {return _plazaTransactionsMMOnlyDelay;} set {SetField(ref _plazaTransactionsMMOnlyDelay, value);}}

        [Category(PlazaCategory)]
        [PropertyOrder(5)]
        [DisplayName(@"plaza_use_fast_repl")]
        [Description(@"Использовать скоростные потоки FASTREPL.")]
        public bool PlazaUseFastRepl {get {return _plazaUseFastRepl;} set {SetField(ref _plazaUseFastRepl, value);}}

        [Category(PlazaCategory)]
        [PropertyOrder(6)]
        [DisplayName(@"Портфель")]
        [Description(@"Название портфеля, используемого для торговли.")]
        public string Portfolio {get {return _portfolio;} set {SetField(ref _portfolio, value);} }

        #endregion

        #region realtime

        [Category(RealtimeCategory)]
        [PropertyOrder(0)]
        [DisplayName(@"realtime_enter_diff")]
        [Description(@"Задержка данных для входа в режим реального времени, секунд.")]
        public decimal RealtimeEnterTimeDiff { get { return _realtimeEnterTimeDiff; } set { SetField(ref _realtimeEnterTimeDiff, value); } }

        [Category(RealtimeCategory)]
        [PropertyOrder(1)]
        [DisplayName(@"realtime_exit_diff")]
        [Description(@"Задержка данных для выхода из режима реального времени, секунд")]
        public decimal RealtimeExitTimeDiff { get { return _realtimeExitTimeDiff; } set { SetField(ref _realtimeExitTimeDiff, value); } }

        [Category(RealtimeCategory)]
        [PropertyOrder(2)]
        [DisplayName(@"realtime_exit_delay")]
        [Description(@"Чтобы робот вышел из режима реального времени, задержка больше, чем realtime_exit_diff должна продержаться дольше, чем realtime_exit_delay, секунд.")]
        public decimal RealtimeExitDelay { get { return _realtimeExitDelay; } set { SetField(ref _realtimeExitDelay, value); } }

        [Category(RealtimeCategory)]
        [PropertyOrder(3)]
        [DisplayName(@"realtime_max_calc_delay")]
        [Description(@"Максимальная задержка данных, при которой робот может принимать торговые решения (при условии нахождения в режиме реального времени), секунд.")]
        public decimal RealtimeMaxCalcDelay { get { return _realtimeMaxCalcDelay; } set { SetField(ref _realtimeMaxCalcDelay, value); } }

        #endregion

        #region transactions

        [Category(TransactionCategory)]
        [PropertyOrder(0)]
        [DisplayName(@"transactions_ratio")]
        [Description(@"Балл за транзакции.")]
        public decimal TransactionsRatio {get {return _transactionsRatio;} set {SetField(ref _transactionsRatio, value);}}
        
        [Category(TransactionCategory)]
        [PropertyOrder(1)]
        [DisplayName(@"deals_ratio")]
        [Description(@"Балл за сделки.")]
        public decimal DealsRatio {get {return _dealsRatio;} set {SetField(ref _dealsRatio, value);}}
        
        [Category(TransactionCategory)]
        [PropertyOrder(3)]
        [DisplayName(@"transactions_fee_ratio")]
        [Description(@"Коэффициент расчета комиссии биржи за неэффективные транзакции.")]
        public decimal TransactionsFeeRatio {get {return _transactionsFeeRatio;} set {SetField(ref _transactionsFeeRatio, value);}}
        
        [Category(TransactionCategory)]
        [PropertyOrder(4)]
        [DisplayName(@"transactions_comission_limit")]
        [Description(@"Лимит комиссии за неэффективные транзакции.")]
        public decimal TransactionsCommissionLimit {get {return _transactionsCommissionLimit;} set {SetField(ref _transactionsCommissionLimit, value);}}
        
        [Category(TransactionCategory)]
        [PropertyOrder(5)]
        [DisplayName(@"transactions_daily_open_limit")]
        [Description(@"Лимит количества транзакций, после которого нельзя отправлять заявки на открытие новых позиций.")]
        public int TransactionOpenPosDayLimit { get { return _transactionDayLimit; } set { SetField(ref _transactionDayLimit, value); } }

        #endregion

        #region constants

        [Category(ConstantsCategory)]
        [PropertyOrder(0)]
        [DisplayName(@"ir")]
        [Description(@"Процентная ставка.")]
        public decimal IR { get { return _ir; } set { SetField(ref _ir, value); } }

        [Category(ConstantsCategory)]
        [PropertyOrder(1)]
        [DisplayName(@"iv_calculation_accuracy")]
        [Description(@"Точность расчета волатильности.")]
        public decimal IvCalculationAccuracy { get { return _ivCalcAccuracy; } set { SetField(ref _ivCalcAccuracy, value); } }

        [Category(ConstantsCategory)]
        [PropertyOrder(2)]
        [DisplayName(@"delta_exposition_limit")]
        [Description(@"Экспозиция по фьючерсу, при превышении которой происходит хеджирование.")]
        public decimal DeltaExpositionLimit { get { return _deltaExpositionLimit; } set { SetField(ref _deltaExpositionLimit, value); } }

        [Category(ConstantsCategory)]
        [PropertyOrder(3)]
        [DisplayName(@"capital_limit")]
        [Description(@"Лимит капитала (ГО) для торговли.")]
        public decimal CapitalLimit {get {return _capitalLimit;} set {SetField(ref _capitalLimit, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(4)]
        [DisplayName(@"vega_coeff")]
        [Description(@"Коэффициент снижения объема в заявке на хеджирование веги.")]
        public decimal VegaCoeff {get {return _vegaCoeff;} set {SetField(ref _vegaCoeff, value);}}
        
        [Category(ConstantsCategory)]
        [PropertyOrder(5)]
        [DisplayName(@"gamma_coeff")]
        [Description(@"Коэффициент снижения объема в заявке на хеджирование гаммы.")]
        public decimal GammaCoeff {get {return _gammaCoeff;} set {SetField(ref _gammaCoeff, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(6)]
        [DisplayName(@"theta_unit")]
        [Description(@"Тета юнит в днях.")]
        public decimal ThetaUnit {get {return _thetaUnit;} set {SetField(ref _thetaUnit, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(7)]
        [DisplayName(@"drawdown_limit")]
        [Description(@"Лимит снижения лимита капитала.")]
        public decimal DrawdownLimit {get {return _drawdownLimit;} set {SetField(ref _drawdownLimit, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(8)]
        [DisplayName(@"market_order_shift")]
        [Description(@"Сдвиг цены для рыночной заявки (в шагах цены инструмента).")]
        public int MarketOrderShift {get {return _marketOrderShift;} set {SetField(ref _marketOrderShift, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(10)]
        [DisplayName(@"mm_delay_first")]
        [Description(@"Первый отчет о невыполнении обязательств ММ будет послан через mm_delay_first секунд.")]
        public int MMDelayFirst {get {return _mmDelayFirst;} set {SetField(ref _mmDelayFirst, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(11)]
        [DisplayName(@"mm_delay_periodic")]
        [Description(@"Последующие отчеты посылаются периодически каждые mm_delay_periodic секунд.")]
        public int MMDelayPeriodic {get {return _mmDelayPeriodic;} set {SetField(ref _mmDelayPeriodic, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(12)]
        [DisplayName(@"mm_calc_periodic")]
        [Description(@"Для опционов с запущенной ММ стратегией пересчитывать модель не реже чем каждые mm_calc_periodic секунд.")]
        public int MMCalcPeriodic {get {return _mmCalcPeriodic;} set {SetField(ref _mmCalcPeriodic, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(13)]
        [DisplayName(@"curve_interval")]
        [Description(@"Период времени, за который производится расчет параметров кв, в секундах.")]
        public int CurveInterval {get {return _curveInterval;} set {SetField(ref _curveInterval, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(14)]
        [DisplayName(@"curve_discrete")]
        [Description(@"Дискретность пересчетов параметров кв, в секундах.")]
        public int CurveDiscrete {get {return _curveDiscrete;} set {SetField(ref _curveDiscrete, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(15)]
        [DisplayName(@"pre_curve_begin")]
        [Description(@"Период времени до начала режима хеджирования, когда начинается запись в данных в таблицу pre_curve_array, в секундах.")]
        public int PreCurveBegin {get {return _preCurveBegin;} set {SetField(ref _preCurveBegin, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(16)]
        [DisplayName(@"curve_delay_limit")]
        [Description(@"Количество периодов curve_descrete, в течение которых допускается пропуск записи в таблицу curve_array() (по причине отсутствия достаточного количества ликвидных данных), после чего таблица curve_array очищается (ресет).")]
        public int CurveDelayLimit {get {return _curveDelayLimit;} set {SetField(ref _curveDelayLimit, value);}}

        [Category(ConstantsCategory)]
        [PropertyOrder(17)]
        [DisplayName(@"pre_curve_delay_limit")]
        [Description(@"Количество периодов pre_curve_descrete, в течение которых допускается пропуск записи в таблицу pre_curve_array() (по причине отсутствия достаточного количества ликвидных данных), после чего таблица pre_curve_array очищается.")]
        public int PreCurveDelayLimit {get {return _preCurveDelayLimit;} set {SetField(ref _preCurveDelayLimit, value);}}


        #endregion

        #region misc

        [Category(MiscCategory)]
        [PropertyOrder(0)]
        [DisplayName(@"notify_email")]
        [Description(@"Адрес для посылки сообщений об ошибках.")]
        public string NotifyEmail {get {return _notifyEmail;} set {SetField(ref _notifyEmail, value);}}

        [Category(MiscCategory)]
        [PropertyOrder(1)]
        [DisplayName(@"extended_log")]
        [Description(@"Расширенное логирование.")]
        public bool ExtendedLog {get {return _extendedLog;} set {SetField(ref _extendedLog, value);}}

        [Category(MiscCategory)]
        [PropertyOrder(2)]
        [DisplayName(@"recalc_group_period")]
        [Description(@"Период группировки для суммирования количества пересчетов модели (секунды).")]
        public decimal RecalcGroupPeriod {get {return _recalcGroupPeriod;} set {SetField(ref _recalcGroupPeriod, value);}}

        [Category(MiscCategory)]
        [PropertyOrder(3)]
        [DisplayName(@"zero_poll_timeout")]
        [Description(@"Использовать нулевую задержку PollTimeout. Нулевой PollTimeout значительно повышает загрузку процессора, но не гарантирует повышения общей производительности. Экспериментальная функция.")]
        public bool UseZeroPollTimeout {get {return _useZeroPollTimeout;} set {SetField(ref _useZeroPollTimeout, value);}}

        [Category(MiscCategory)]
        [PropertyOrder(4)]
        [DisplayName(@"log_messages")]
        [Description(@"Логирование состояние обработчика сообщений и всех сообщений (значительный объем).")]
        public bool LogMessageProcessor {get {return _logMessageProcessor;} set {SetField(ref _logMessageProcessor, value);}}

        [Category(MiscCategory)]
        [PropertyOrder(5)]
        [DisplayName(@"vol_group_period")]
        [Description(@"Период группировки в таблице объемов (минуты).")]
        public int VolumeGroupPeriod {get {return _volumeGroupPeriod;} set {SetField(ref _volumeGroupPeriod, value);}}

        #endregion

        #endregion

        protected override void SetDefaultValues() {
            base.SetDefaultValues();

            PlazaAddress = "127.0.0.1:4001";
            PlazaCGateKey = PlazaSessionHolder.DemoCGateKey;
            PlazaTransactionsLimit = 30;
            PlazaTransactionsRateNewOrderFraction = 0.5;
            PlazaTransactionsMMOnlyDelay = 5;
            PlazaUseFastRepl = false;
            Portfolio =  "";
            RealtimeEnterTimeDiff = 0.3m;
            RealtimeExitTimeDiff = 1;
            RealtimeExitDelay = 5;
            RealtimeMaxCalcDelay = 1m;
            IR = 0;
            DeltaExpositionLimit = 0.65m;
            IvCalculationAccuracy = 0.0001m;
            TransactionOpenPosDayLimit = 2000;
            CapitalLimit = 100000;
            TransactionsRatio = 0.5m;
            DealsRatio = 40;
            TransactionsFeeRatio = 0.1m;
            TransactionsCommissionLimit = 200;
            VegaCoeff = 0.5m;
            GammaCoeff = 0.5m;
            DrawdownLimit = 0.03m;
            MarketOrderShift = 100;
            ExtendedLog = false;
            RecalcGroupPeriod = 1;
            UseZeroPollTimeout = false;
            LogMessageProcessor = false;
            ThetaUnit = 1;
            MMDelayFirst = 10;
            MMDelayPeriodic = 3600;
            MMCalcPeriodic = 3;
            VolumeGroupPeriod = 30;
            CurveInterval = 1200;
            CurveDiscrete = 120;
            PreCurveBegin = 60;
            CurveDelayLimit = 3;
            PreCurveDelayLimit = 20;
        }

        public override void VerifyConfig(List<string> errors) {
            base.VerifyConfig(errors);

            try { PlazaAddress.To<IPEndPoint>(); } catch(Exception) { errors.Add("адрес шлюза Plaza"); }

            if(PlazaCGateKey.IsEmpty() || PlazaCGateKey.Length < 5)
                errors.Add("Ключ CGate не указан");

            if(PlazaTransactionsLimit < 8)
                errors.Add("лимит частоты транзакций plaza");

            if(PlazaTransactionsRateNewOrderFraction < 0.1 || PlazaTransactionsRateNewOrderFraction > 0.9)
                errors.Add("tran_neworder_fraction");

            if(PlazaTransactionsMMOnlyDelay < 1)
                errors.Add("tran_mm_only_delay");

            if(RealtimeEnterTimeDiff < 0.03m)
                errors.Add("задержка для входа в режим реального времени");
            if(RealtimeExitTimeDiff < 0.3m)
                errors.Add("задержка для выхода из режима реального времени");
            if(RealtimeEnterTimeDiff >= RealtimeExitTimeDiff)
                errors.Add("realtime_enter_diff >= realtime_exit_diff");
            if(RealtimeExitDelay < 0)
                errors.Add("realtime_exit_delay");
            if(RealtimeMaxCalcDelay < RealtimeEnterTimeDiff)
                errors.Add("realtime_max_calc_delay < realtime_enter_diff");

            if(IR < 0 || IR > 0.5m)
                errors.Add("процентная ставка");
            if(IvCalculationAccuracy < BaseConfig.EpsilonDecimal || IvCalculationAccuracy > 1)
                errors.Add("точность расчета волатильности");
            if(TransactionOpenPosDayLimit < 10)
                errors.Add("лимит количества транзакций");
            if(DeltaExpositionLimit < 0)
                errors.Add("лимит экспозиции");

            if(MarketOrderShift < 5)
                errors.Add("сдвиг цены рыночных заявок");

            if(ThetaUnit < 0)
                errors.Add("theta_unit");

            var email = NotifyEmail.With(em => em.Trim());
            if(!email.IsEmpty() && !EmailSender.VerifyEmailAddress(email))
                errors.Add("notify_email");

            if(MMDelayFirst < 1) errors.Add("mm_delay_first can't be less than 1 sec");

            if(MMDelayPeriodic < 60) errors.Add("mm_delay_periodic can't be less than 60 sec");

            if(MMCalcPeriodic < 1) errors.Add(nameof(MMCalcPeriodic));

            if(RecalcGroupPeriod < 0) errors.Add(nameof(RecalcGroupPeriod));

            if(VolumeGroupPeriod < 5 || VolumeGroupPeriod > 60) errors.Add(nameof(VolumeGroupPeriod));

            if(CurveInterval < 10) errors.Add(nameof(CurveInterval));
            if(CurveDiscrete < 1) errors.Add(nameof(CurveDiscrete));
            if(CurveDelayLimit < 0) errors.Add(nameof(CurveDelayLimit));
            if(PreCurveDelayLimit < 0) errors.Add(nameof(PreCurveDelayLimit));

            if(CurveInterval < CurveDiscrete) errors.Add($"{nameof(CurveInterval)} < {nameof(CurveDiscrete)}");
        }

        protected override void CopyFromImpl(ConfigGeneral other) {
            base.CopyFromImpl(other);

            PlazaAddress = other.PlazaAddress;
            PlazaCGateKey = other.PlazaCGateKey;
            PlazaTransactionsLimit = other.PlazaTransactionsLimit;
            PlazaTransactionsRateNewOrderFraction = other.PlazaTransactionsRateNewOrderFraction;
            PlazaTransactionsMMOnlyDelay = other.PlazaTransactionsMMOnlyDelay;
            PlazaUseFastRepl = other.PlazaUseFastRepl;
            Portfolio = other.Portfolio;
            RealtimeEnterTimeDiff = other.RealtimeEnterTimeDiff;
            RealtimeExitTimeDiff = other.RealtimeExitTimeDiff;
            RealtimeExitDelay = other.RealtimeExitDelay;
            RealtimeMaxCalcDelay = other.RealtimeMaxCalcDelay;
            IR = other.IR;
            DeltaExpositionLimit = other.DeltaExpositionLimit;
            IvCalculationAccuracy = other.IvCalculationAccuracy;
            TransactionOpenPosDayLimit = other.TransactionOpenPosDayLimit;
            CapitalLimit = other.CapitalLimit;
            TransactionsRatio = other.TransactionsRatio;
            DealsRatio = other.DealsRatio;
            TransactionsFeeRatio = other.TransactionsFeeRatio;
            TransactionsCommissionLimit = other.TransactionsCommissionLimit;
            VegaCoeff = other.VegaCoeff;
            GammaCoeff = other.GammaCoeff;
            ThetaUnit = other.ThetaUnit;
            DrawdownLimit = other.DrawdownLimit;
            MarketOrderShift = other.MarketOrderShift;
            NotifyEmail = other.NotifyEmail;
            ExtendedLog = other.ExtendedLog;
            RecalcGroupPeriod = other.RecalcGroupPeriod;
            UseZeroPollTimeout = other.UseZeroPollTimeout;
            LogMessageProcessor = other.LogMessageProcessor;
            MMDelayFirst = other.MMDelayFirst;
            MMDelayPeriodic = other.MMDelayPeriodic;
            MMCalcPeriodic = other.MMCalcPeriodic;
            VolumeGroupPeriod = other.VolumeGroupPeriod;
            CurveInterval = other.CurveInterval;
            CurveDiscrete = other.CurveDiscrete;
            PreCurveBegin = other.PreCurveBegin;
            CurveDelayLimit = other.CurveDelayLimit;
            PreCurveDelayLimit = other.PreCurveDelayLimit;
        }

        protected override bool OnEquals(ConfigGeneral other) {
            return
                base.OnEquals(other) &&
                PlazaAddress == other.PlazaAddress &&
                PlazaCGateKey == other.PlazaCGateKey &&
                PlazaTransactionsLimit == other.PlazaTransactionsLimit &&
                PlazaTransactionsRateNewOrderFraction.IsEqual(other.PlazaTransactionsRateNewOrderFraction) &&
                PlazaTransactionsMMOnlyDelay.IsEqual(other.PlazaTransactionsMMOnlyDelay) &&
                PlazaUseFastRepl == other.PlazaUseFastRepl &&
                Portfolio == other.Portfolio &&
                RealtimeEnterTimeDiff == other.RealtimeEnterTimeDiff &&
                RealtimeExitTimeDiff == other.RealtimeExitTimeDiff &&
                RealtimeExitDelay == other.RealtimeExitDelay &&
                RealtimeMaxCalcDelay == other.RealtimeMaxCalcDelay &&
                IR == other.IR &&
                DeltaExpositionLimit == other.DeltaExpositionLimit &&
                IvCalculationAccuracy == other.IvCalculationAccuracy &&
                TransactionOpenPosDayLimit == other.TransactionOpenPosDayLimit &&
                CapitalLimit == other.CapitalLimit &&
                TransactionsRatio == other.TransactionsRatio &&
                DealsRatio == other.DealsRatio &&
                TransactionsFeeRatio == other.TransactionsFeeRatio &&
                TransactionsCommissionLimit == other.TransactionsCommissionLimit &&
                VegaCoeff == other.VegaCoeff &&
                GammaCoeff == other.GammaCoeff &&
                ThetaUnit == other.ThetaUnit &&
                DrawdownLimit == other.DrawdownLimit &&
                MarketOrderShift == other.MarketOrderShift &&
                NotifyEmail == other.NotifyEmail &&
                ExtendedLog == other.ExtendedLog &&
                RecalcGroupPeriod == other.RecalcGroupPeriod &&
                UseZeroPollTimeout == other.UseZeroPollTimeout &&
                LogMessageProcessor == other.LogMessageProcessor &&
                MMDelayFirst == other.MMDelayFirst &&
                MMDelayPeriodic == other.MMDelayPeriodic &&
                MMCalcPeriodic == other.MMCalcPeriodic &&
                VolumeGroupPeriod == other.VolumeGroupPeriod &&
                CurveInterval == other.CurveInterval &&
                CurveDiscrete == other.CurveDiscrete &&
                PreCurveBegin == other.PreCurveBegin &&
                CurveDelayLimit == other.CurveDelayLimit &&
                PreCurveDelayLimit == other.PreCurveDelayLimit;
        }

        #region logger methods

        public static string[] GetLoggerFields() {
            return new [] {
                nameof(PlazaAddress),                   // 0
                nameof(PlazaCGateKey),                  // 1
                nameof(PlazaTransactionsLimit),         // 2
                nameof(PlazaTransactionsRateNewOrderFraction), // 3
                nameof(PlazaTransactionsMMOnlyDelay),   // 4
                nameof(PlazaUseFastRepl),               // 5
                nameof(Portfolio),                      // 6
                nameof(RealtimeEnterTimeDiff),          // 7
                nameof(RealtimeExitTimeDiff),           // 8
                nameof(RealtimeExitDelay),              // 9
                nameof(RealtimeMaxCalcDelay),           // 10
                nameof(IR),                             // 11
                nameof(IvCalculationAccuracy),          // 12
                nameof(DeltaExpositionLimit),           // 13
                nameof(CapitalLimit),                   // 14
                nameof(TransactionsRatio),              // 15
                nameof(DealsRatio),                     // 16
                nameof(TransactionsFeeRatio),           // 17
                nameof(TransactionsCommissionLimit),    // 18
                nameof(TransactionOpenPosDayLimit),     // 19
                nameof(VegaCoeff),                      // 20
                nameof(GammaCoeff),                     // 21
                nameof(ThetaUnit),                      // 22
                nameof(DrawdownLimit),                  // 23
                nameof(MarketOrderShift),               // 24
                nameof(NotifyEmail),                    // 25
                nameof(ExtendedLog),                    // 26
                nameof(RecalcGroupPeriod),              // 27
                nameof(UseZeroPollTimeout),             // 28
                nameof(LogMessageProcessor),            // 29
                nameof(MMDelayFirst),                   // 30
                nameof(MMDelayPeriodic),                // 31
                nameof(MMCalcPeriodic),                 // 32
                nameof(VolumeGroupPeriod),              // 33
                nameof(CurveInterval),                  // 34
                nameof(CurveDiscrete),                  // 35
                nameof(PreCurveBegin),                  // 36
                nameof(CurveDelayLimit),                // 37
                nameof(PreCurveDelayLimit),             // 38
            };

        }

        public IEnumerable<object> GetLoggerValues() {
               return new object[] {
                    PlazaAddress,               // 0
                    PlazaCGateKey,              // 1
                    PlazaTransactionsLimit,     // 2
                    PlazaTransactionsRateNewOrderFraction, // 3
                    PlazaTransactionsMMOnlyDelay,// 4
                    PlazaUseFastRepl,           // 5
                    Portfolio,                  // 6
                    RealtimeEnterTimeDiff,      // 7
                    RealtimeExitTimeDiff,       // 8
                    RealtimeExitDelay,          // 9
                    RealtimeMaxCalcDelay,       // 10
                    IR,                         // 11
                    IvCalculationAccuracy,      // 12
                    DeltaExpositionLimit,       // 13
                    CapitalLimit,               // 14
                    TransactionsRatio,          // 15
                    DealsRatio,                 // 16
                    TransactionsFeeRatio,       // 17
                    TransactionsCommissionLimit,// 18
                    TransactionOpenPosDayLimit, // 19
                    VegaCoeff,                  // 20
                    GammaCoeff,                 // 21
                    ThetaUnit,                  // 22
                    DrawdownLimit,              // 23
                    MarketOrderShift,           // 24
                    NotifyEmail,                // 25
                    ExtendedLog,                // 26
                    RecalcGroupPeriod,          // 27
                    UseZeroPollTimeout,         // 28
                    LogMessageProcessor,        // 29
                    MMDelayFirst,               // 30
                    MMDelayPeriodic,            // 31
                    MMCalcPeriodic,             // 32
                    VolumeGroupPeriod,          // 33
                    CurveInterval,              // 34
                    CurveDiscrete,              // 35
                    PreCurveBegin,              // 36
                    CurveDelayLimit,            // 37
                    PreCurveDelayLimit,         // 38
            };
        }

        #endregion
    }
}
