using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using alglib;
using Ecng.Common;
using MoreLinq;
using OptionBot.Config;
using StockSharp.BusinessEntities;
using StockSharp.Messages;

namespace OptionBot.robot {
    public interface IOptionModelInputData {
        double OptionCalcBid {get;}
        double OptionCalcAsk {get;}
        int OptionCalcBidVol {get;}
        int OptionCalcAskVol {get;}
        double FutureCalcBid {get;}
        double FutureCalcAsk {get;}
        int GlassBidVolume {get;}
        int GlassOfferVolume {get;}
        int Position {get;}
        DateTime LastDealTime {get;}
        DateTime Time {get;}
        FuturesInfo.IFutureParams FutParams {get;}
    }

    public interface IOptionModelData {
        IOptionModelInputData Input {get;}
        double DeltaBid {get;}
        double IvBid {get;}
        double IvOffer {get;}
        double IvAverage {get;}
        double MarketBid {get;}
        double MarketOffer {get;}
        double MarketAverage {get;}
        double MarketSpread {get;}
        double MarketIvBid {get;}
        double MarketIvOffer {get;}
        double MarketIvSpread {get;}
        double MarketIvAverage {get;}
        double IvSpread {get;}
        double Vega {get;}
        double Gamma {get;}
        double Theta {get;}
        double Vanna {get;}
        double Vomma {get;}
        double CurrentSpread {get;}
        double ValuationTargetSpread {get;}
        double ValuationSpread {get;}
        double WideningMktIv {get;}
        double NarrowingMktIv {get;}
        StrikeMoneyness StrikeMoneyness {get;}
        GreeksRegime GreeksRegime {get;}
        double Moneyness {get;}
        double InitialDelta {get;}
        double BestDeltaExpectation {get;}
        double IlliquidDelta {get;}
        double IlliquidIv {get;}
        double IlliquidVega {get;}
        double IlliquidGamma {get;}
        double IlliquidTheta {get;}
        double IlliquidVanna {get;}
        double IlliquidVomma {get;}
        double CalculationDelta {get;}
        double CalculationVega {get;}
        double CalculationGamma {get;}
        double CalculationTheta {get;}
        double CalculationVanna {get;}
        double CalculationVomma {get;}
        CurveModelStatus CurveModelStatus {get;}
        double CurveBid {get;}
        double CurveOffer {get;}
        double CurveIvBid {get;}
        double CurveIvOffer {get;}
        double CurveSpread {get;}
        double CurveAverage {get;}
        double CurveIvSpread {get;}
        double CurveIvAverage {get;}
        double CurveDelta {get;}
        double CurveVega {get;}
        double CurveGamma {get;}
        double CurveTheta {get;}
        double CurveVanna {get;}
        double CurveVomma {get;}
        double MarketSpreadResetLimit {get;}
        ValuationStatus ValuationStatus {get;}
        DateTime TimeValuationStatus {get;}
        DateTime? AggressiveResetStartTime {get;}
        DateTime? ConservativeResetStartTime {get;}
        DateTime? QuoteVolBidResetStartTime {get;}
        DateTime? QuoteVolOfferResetStartTime {get;}
        int MMVegaVolLimitBuy {get;}
        int MMVegaVolLimitSell {get;}
        int MMGammaVolLimitBuy {get;}
        int MMGammaVolLimitSell {get;}
        bool CalcSuccessful {get;}
        bool CalcDone {get;}
        string Error {get;}
    }

    /// <summary>
    /// Модель расчета параметров основного робота.
    /// </summary>
    public class OptionModel {
        public class Data : IOptionModelData {
            public Data() { Input = new RecalculateState(); }
            public Data(IOptionModelData previous, IOptionModelInputData input) {
                Input = input;
                MarketIvBid = previous.MarketIvBid;
                MarketIvOffer = previous.MarketIvOffer;
                Vega = previous.Vega;
                MarketSpread = previous.MarketSpread;
                CalculationDelta = previous.CalculationDelta;
                CalculationVega = previous.CalculationVega;
                CalculationGamma = previous.CalculationGamma;
                CalculationTheta = previous.CalculationTheta;
                ValuationSpread = previous.ValuationSpread;
                CalculationVanna = previous.CalculationVanna;
                CalculationVomma = previous.CalculationVomma;
                ValuationStatus = previous.ValuationStatus;
                TimeValuationStatus = previous.TimeValuationStatus;
                AggressiveResetStartTime = previous.AggressiveResetStartTime;
                ConservativeResetStartTime = previous.ConservativeResetStartTime;
                QuoteVolBidResetStartTime = previous.QuoteVolBidResetStartTime;
                QuoteVolOfferResetStartTime = previous.QuoteVolOfferResetStartTime;
            }

            public IOptionModelInputData Input {get; private set;}

            public double DeltaBid {get; set;}
            public double IvBid {get; set;}
            public double IvOffer {get; set;}
            public double IvSpread {get; set;}
            public double IvAverage => 0.5d * (IvBid + IvOffer);
            public double MarketBid {get; set;}
            public double MarketOffer {get; set;}
            public double MarketAverage {get; set;}
            public double MarketIvBid {get; set;}
            public double MarketIvOffer {get; set;}
            public double MarketIvSpread {get; set;}
            public double MarketIvAverage {get; set;}
            public double Vega {get; set;}
            public double Gamma {get; set;}
            public double Theta {get; set;}
            public double Vanna {get; set;}
            public double Vomma {get; set;}
            public double MarketSpread {get; set;}
            public double CurrentSpread {get; set;}
            public double ValuationTargetSpread {get; set;}
            public double ValuationSpread {get; set;}
            public double WideningMktIv {get; set;}
            public double NarrowingMktIv {get; set;}
            public StrikeMoneyness StrikeMoneyness {get; set;}
            public GreeksRegime GreeksRegime {get; set;}
            public double Moneyness {get; set;}
            public double InitialDelta {get; set;}
            public double BestDeltaExpectation {get; set;}
            public double IlliquidDelta {get; set;}
            public double IlliquidIv {get; set;}
            public double IlliquidVega {get; set;}
            public double IlliquidGamma {get; set;}
            public double IlliquidTheta {get; set;}
            public double IlliquidVanna {get; set;}
            public double IlliquidVomma {get; set;}
            public double CalculationDelta {get; set;}
            public double CalculationVega {get; set;}
            public double CalculationGamma {get; set;}
            public double CalculationTheta {get; set;}
            public double CalculationVanna {get; set;}
            public double CalculationVomma {get; set;}
            public CurveModelStatus CurveModelStatus {get; set;}
            public double CurveBid {get; set;}
            public double CurveOffer {get; set;}
            public double CurveIvBid {get; set;}
            public double CurveIvOffer {get; set;}
            public double CurveSpread {get; set;}
            public double CurveAverage {get; set;}
            public double CurveIvSpread {get; set;}
            public double CurveIvAverage {get; set;}
            public double CurveDelta {get; set;}
            public double CurveVega {get; set;}
            public double CurveGamma {get; set;}
            public double CurveTheta {get; set;}
            public double CurveVanna {get; set;}
            public double CurveVomma {get; set;}
            public double MarketSpreadResetLimit {get; set;}
            public ValuationStatus ValuationStatus {get; set;}
            public DateTime TimeValuationStatus {get; set;}
            public DateTime? AggressiveResetStartTime {get; set;}
            public DateTime? ConservativeResetStartTime {get; set;}
            public DateTime? QuoteVolBidResetStartTime {get; set;}
            public DateTime? QuoteVolOfferResetStartTime {get; set;}
            public int MMVegaVolLimitBuy {get; set;}
            public int MMVegaVolLimitSell {get; set;}
            public int MMGammaVolLimitBuy {get; set;}
            public int MMGammaVolLimitSell {get; set;}
            public bool CalcSuccessful {get; set;}
            public bool CalcDone {get; set;}
            public string Error {get; set;}
        }

        class WorkBuffer {
            readonly OptionModel _parent;
            public WorkBuffer(OptionModel parent) {
                _parent = parent;
            }

            public WorkBuffer(OptionModel parent, DateTime time) : this(parent) {
                SetTime(time);
            }

            public void SetTime(DateTime time) {
                TimeDays = (_parent._expirationTime - time).TotalDays;
                TimeTillExpiration = TimeDays / 365;
                TimeToExpSqrt = Math.Sqrt(TimeTillExpiration);
                TimeToExpSqrtInv = 1 / Math.Sqrt(TimeTillExpiration);
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                ExpRate = _parent._ir == 0 ? 1 : Math.Exp(-_parent._ir * TimeTillExpiration);
            }

            public double TimeDays {get; private set;}
            public double TimeTillExpiration {get; private set;}
            public double TimeToExpSqrt {get; private set;}
            public double TimeToExpSqrtInv {get; private set;}
            public double ExpRate {get; private set;}

            public double AssetPrice {get; set;}
            public double Arg0 {get; set;}
            public double Arg1 {get; set;}
            public double Nd1 {get; set;}
        }

        readonly OptionInfo _option;
        readonly RobotLogger.ModelExtLogger _extLogger;
        readonly double _strike;
        readonly DateTime _expirationTime;
        readonly Func<WorkBuffer, double, double> _getPremium;
        static readonly double TwoPiSqrt = Math.Sqrt(2 * Math.PI);
        static readonly double TwoSqrt = Math.Sqrt(2d);

        readonly double _minPriceStep;

        double _ivCalculationAccuracy, _ir;
        double _vegaUnit, _gammaUnit, _thetaUnit, _vannaUnit, _vommaUnit;
        double _valuationHighSpreadLimit, _valuationLowSpreadLimit, _valuationWide, _valuationNarrow, _valuationMaxSpread;
        double _aWideningMktIv, _aNarrowingMktIv;
        double _highLiquidSpreadLimit;
        double _amsrl, _bmsrl;
        bool _permanentlyIlliquid;
        bool _resetAggr, _resetCons, _resetQuoteVol, _resetDealVol, _resetTime;
        int _quoteVolResetLimit, _timeResetLimit;

        TimeSpan _aggrResetTimeLimit, _consResetTimeLimit, _quoteResetTimeLimit;
        double _consResetLimit;

        double _deepInTheMoneyLimit, _deepOutOfMoneyLimit;
        double _deepInTheMoneyDeltaCall, _deepInTheMoneyDeltaPut;
        double _deepOutOfMoneyDeltaCall, _deepOutOfMoneyDeltaPut;
        double _daysToExpirationForInitialDelta;
        double _deltaCorrection;
        double _bestIvExpectation;
        double _lowX, _highX;
        double _vegaSLimit, _vegaLLimit;
        double _vannaSLimit, _vannaLLimit;

        TimeSpan _recalcGroupPeriod;
        readonly List<DateTime> _recalcsByFuture = new List<DateTime>(64);
        readonly List<DateTime> _recalcsByOption = new List<DateTime>(64);
        readonly List<DateTime> _recalcsByOther = new List<DateTime>(64);

        readonly WorkBuffer _buffer;

        double _calculationIvBid, _calculationIvOffer;
        double _futBid, _futAsk;
        double _futLogPriceToStrikeBuy, _futLogPriceToStrikeSell;

        bool _vpAvailable;
        bool _needToReset;
        bool _needDealReset;
        string _resetComment;

        Data _actualData = new Data();
        Data _lastData;

        public OptionTypes OptionType {get;}

        public IOptionModelData LastData => _lastData;

        public double DeltaBid {get {return _actualData.DeltaBid; } private set {_actualData.DeltaBid = value;}}
        public double IvBid {get {return _actualData.IvBid; } private set {_actualData.IvBid = value;}}
        public double IvOffer {get {return _actualData.IvOffer; } private set {_actualData.IvOffer = value;}}
        public double IvSpread {get {return _actualData.IvSpread; } private set {_actualData.IvSpread = value;}}
        public double MarketBid {get {return _actualData.MarketBid; } private set {_actualData.MarketBid = value;}}
        public double MarketOffer {get {return _actualData.MarketOffer; } private set {_actualData.MarketOffer = value;}}
        public double MarketAverage {get {return _actualData.MarketAverage; } private set {_actualData.MarketAverage = value;}}
        public double MarketIvBid {get {return _actualData.MarketIvBid; } private set {_actualData.MarketIvBid = value;}}
        public double MarketIvOffer {get {return _actualData.MarketIvOffer; } private set {_actualData.MarketIvOffer = value;}}
        public double MarketIvSpread {get {return _actualData.MarketIvSpread; } private set {_actualData.MarketIvSpread = value;}}
        public double MarketIvAverage {get {return _actualData.MarketIvAverage; } private set {_actualData.MarketIvAverage = value;}}
        public double Vega {get {return _actualData.Vega; } private set {_actualData.Vega = value;}}
        public double Gamma {get {return _actualData.Gamma; } private set {_actualData.Gamma = value;}}
        public double Theta {get {return _actualData.Theta; } private set {_actualData.Theta = value;}}
        public double Vanna {get {return _actualData.Vanna;} set {_actualData.Vanna = value;}}
        public double Vomma {get {return _actualData.Vomma;} set {_actualData.Vomma = value;}}
        public double MarketSpread {get {return _actualData.MarketSpread; } private set {_actualData.MarketSpread = value;}}
        public double CurrentSpread {get {return _actualData.CurrentSpread; } private set {_actualData.CurrentSpread = value;}}
        public double ValuationTargetSpread {get {return _actualData.ValuationTargetSpread; } private set {_actualData.ValuationTargetSpread = value;}}
        public double ValuationSpread {get {return _actualData.ValuationSpread; } private set {_actualData.ValuationSpread = value;}}
        public double WideningMktIv {get {return _actualData.WideningMktIv; } private set {_actualData.WideningMktIv = value;}}
        public double NarrowingMktIv {get {return _actualData.NarrowingMktIv; } private set {_actualData.NarrowingMktIv = value;}}
        public StrikeMoneyness StrikeMoneyness {get {return _actualData.StrikeMoneyness;} set{_actualData.StrikeMoneyness = value;}}
        public GreeksRegime GreeksRegime {get {return _actualData.GreeksRegime;} set{_actualData.GreeksRegime = value;}}
        public double Moneyness {get {return _actualData.Moneyness;} set{_actualData.Moneyness = value;}}
        public double InitialDelta {get {return _actualData.InitialDelta;} set{_actualData.InitialDelta = value;}}
        public double BestDeltaExpectation {get {return _actualData.BestDeltaExpectation;} set{_actualData.BestDeltaExpectation = value;}}
        public double IlliquidDelta {get {return _actualData.IlliquidDelta;} set{_actualData.IlliquidDelta = value;}}
        public double IlliquidIv {get {return _actualData.IlliquidIv;} set{_actualData.IlliquidIv = value;}}
        public double IlliquidVega {get {return _actualData.IlliquidVega;} set{_actualData.IlliquidVega = value;}}
        public double IlliquidGamma {get {return _actualData.IlliquidGamma;} set{_actualData.IlliquidGamma = value;}}
        public double IlliquidTheta {get {return _actualData.IlliquidTheta;} set{_actualData.IlliquidTheta = value;}}
        public double IlliquidVanna {get {return _actualData.IlliquidVanna;} set {_actualData.IlliquidVanna = value;}}
        public double IlliquidVomma {get {return _actualData.IlliquidVomma;} set {_actualData.IlliquidVomma = value;}}
        public double CalculationDelta {get {return _actualData.CalculationDelta;} set{_actualData.CalculationDelta = value;}}
        public double CalculationVega {get {return _actualData.CalculationVega;} set{_actualData.CalculationVega = value;}}
        public double CalculationGamma {get {return _actualData.CalculationGamma;} set{_actualData.CalculationGamma = value;}}
        public double CalculationTheta {get {return _actualData.CalculationTheta;} set{_actualData.CalculationTheta = value;}}
        public double CalculationVanna {get {return _actualData.CalculationVanna;} set {_actualData.CalculationVanna = value;}}
        public double CalculationVomma {get {return _actualData.CalculationVomma;} set {_actualData.CalculationVomma = value;}}
        public CurveModelStatus CurveModelStatus {get {return _actualData.CurveModelStatus;} set {_actualData.CurveModelStatus = value;}}
        public double CurveBid {get {return _actualData.CurveBid;} set {_actualData.CurveBid = value;}}
        public double CurveOffer {get {return _actualData.CurveOffer;} set {_actualData.CurveOffer = value;}}
        public double CurveIvBid {get {return _actualData.CurveIvBid;} set {_actualData.CurveIvBid = value;}}
        public double CurveIvOffer {get {return _actualData.CurveIvOffer;} set {_actualData.CurveIvOffer = value;}}
        public double CurveSpread {get {return _actualData.CurveSpread;} set {_actualData.CurveSpread = value;}}
        public double CurveAverage {get {return _actualData.CurveAverage;} set {_actualData.CurveAverage = value;}}
        public double CurveIvSpread {get {return _actualData.CurveIvSpread;} set {_actualData.CurveIvSpread = value;}}
        public double CurveIvAverage {get {return _actualData.CurveIvAverage;} set {_actualData.CurveIvAverage = value;}}
        public double CurveDelta {get {return _actualData.CurveDelta;} set {_actualData.CurveDelta = value;}}
        public double CurveVega {get {return _actualData.CurveVega;} set {_actualData.CurveVega = value;}}
        public double CurveGamma {get {return _actualData.CurveGamma;} set {_actualData.CurveGamma = value;}}
        public double CurveTheta {get {return _actualData.CurveTheta;} set {_actualData.CurveTheta = value;}}
        public double CurveVanna {get {return _actualData.CurveVanna;} set {_actualData.CurveVanna = value;}}
        public double CurveVomma {get {return _actualData.CurveVomma;} set {_actualData.CurveVomma = value;}}
        public int MMVegaVolLimitBuy {get {return _actualData.MMVegaVolLimitBuy;} set{_actualData.MMVegaVolLimitBuy = value;}}
        public int MMVegaVolLimitSell {get {return _actualData.MMVegaVolLimitSell;} set{_actualData.MMVegaVolLimitSell = value;}}
        public int MMGammaVolLimitBuy {get {return _actualData.MMGammaVolLimitBuy;} set{_actualData.MMGammaVolLimitBuy = value;}}
        public int MMGammaVolLimitSell {get {return _actualData.MMGammaVolLimitSell;} set{_actualData.MMGammaVolLimitSell = value;}}
        public double MarketSpreadResetLimit {get {return _actualData.MarketSpreadResetLimit;} set {_actualData.MarketSpreadResetLimit = value;}}
        public ValuationStatus ValuationStatus {get {return _actualData.ValuationStatus;} set {_actualData.ValuationStatus = value;}}
        public DateTime TimeValuationStatus {get {return _actualData.TimeValuationStatus;} set {_actualData.TimeValuationStatus = value;}}
        public DateTime? AggressiveResetStartTime {get {return _actualData.AggressiveResetStartTime;} set {_actualData.AggressiveResetStartTime = value;}}
        public DateTime? ConservativeResetStartTime {get {return _actualData.ConservativeResetStartTime;} set {_actualData.ConservativeResetStartTime = value;}}
        public DateTime? QuoteVolBidResetStartTime {get {return _actualData.QuoteVolBidResetStartTime;} set {_actualData.QuoteVolBidResetStartTime = value;}}
        public DateTime? QuoteVolOfferResetStartTime {get {return _actualData.QuoteVolOfferResetStartTime;} set {_actualData.QuoteVolOfferResetStartTime = value;}}
        public bool LastCalcSuccessful {get {return _actualData.CalcSuccessful; } set {_actualData.CalcSuccessful = value;}}
        public bool CalcDone {get {return _actualData.CalcDone; } set {_actualData.CalcDone = value;}}
        public string LastError {get {return _actualData.Error; } set {_actualData.Error = value;}}

        ConfigProvider              ConfigProvider      => _option.Controller.ConfigProvider;
        IConfigValuationParameters  CfgValuationParams  => _option.CfgValuationParams;
        IConfigGeneral              CfgGeneral          => ConfigProvider.General.Effective;
        IConfigFuture               CfgFuture           => _option.Future.CfgFuture;

        bool _settingsUpdated;
        readonly Logger _log;

        public OptionModel(OptionInfo option) {
            _log = new Logger($"model_{option.Code}");
            _buffer = new WorkBuffer(this);
            _option = option;
            _extLogger = _option.Controller.RobotLogger.ModelExt(option);
            _lastData = _actualData;
            _strike = (double)option.Strike.Strike;
            _expirationTime = GetExpirationTime(option.NativeSecurity);
            // ReSharper disable once PossibleInvalidOperationException
            OptionType = option.NativeSecurity.OptionType.Value;
            _getPremium = OptionType == OptionTypes.Call ? (Func<WorkBuffer,double,double>)GetPremiumCall : GetPremiumPut;
            if(option.NativeSecurity.PriceStep <= 0) throw new InvalidOperationException("PriceStep is incorrect for {0}".Put(option.Id));

            _minPriceStep = (double)option.NativeSecurity.PriceStep;

            _option.Series.AtmStrikeChanged += serInfo => _settingsUpdated = true;
            option.Future.Config.EffectiveConfigChanged += (pair, names) => _settingsUpdated = true;
            ConfigProvider.General.EffectiveConfigChanged += (pair, strings) => _settingsUpdated = true;
            _option.ValuationParamsUpdated += () => _settingsUpdated = true;
            UpdateSettings();
        }

        void UpdateSettings() {
            _settingsUpdated = false;

            var cfgGen = CfgGeneral;
            var cfgFut = CfgFuture;
            _ivCalculationAccuracy  = (double)cfgGen.IvCalculationAccuracy;
            _ir                     = (double)cfgGen.IR;

            var cfg = CfgValuationParams;
            if(cfg != null) {
                _vpAvailable = true;
                _vegaUnit = (double)cfg.VegaUnit;
                _gammaUnit = (double)cfgFut.GammaUnit;
                _thetaUnit = (double)cfgGen.ThetaUnit;
                _vannaUnit = (double)cfgFut.VannaUnit;
                _vommaUnit = (double)cfgFut.VommaUnit;
                _recalcGroupPeriod = TimeSpan.FromSeconds((double)cfgGen.RecalcGroupPeriod);
                _valuationHighSpreadLimit = (double)cfg.ValuationHighSpreadLimit;
                _valuationLowSpreadLimit = (double)cfg.ValuationLowSpreadLimit;
                _valuationWide = (double)cfg.ValuationWide;
                _valuationNarrow = (double)cfg.ValuationNarrow;
                _valuationMaxSpread = (double)cfg.ValuationMaxSpread;
                _deepInTheMoneyLimit = (double)cfgFut.DeepInTheMoneyLimit;
                _deepOutOfMoneyLimit = (double)cfgFut.DeepOutOfMoneyLimit;
                _deepInTheMoneyDeltaCall = (double)cfgFut.DeepInTheMoneyDeltaCall;
                _deepInTheMoneyDeltaPut = (double)cfgFut.DeepInTheMoneyDeltaPut;
                _deepOutOfMoneyDeltaCall = (double)cfgFut.DeepOutOfMoneyDeltaCall;
                _deepOutOfMoneyDeltaPut = (double)cfgFut.DeepOutOfMoneyDeltaPut;
                _daysToExpirationForInitialDelta = (double)cfgFut.DaysToExpirationForInitialDelta;
                _deltaCorrection = (double)cfg.DeltaCorrection;
                _bestIvExpectation = (double)cfg.BestIvExpectation;
                _lowX = (double)cfg.LowX;
                _highX = (double)cfg.HighX;
                _highLiquidSpreadLimit = (double)cfg.HighLiquidSpreadLimit;
                _aWideningMktIv = (double)cfg.AWideningMktIv;
                _aNarrowingMktIv = (double)cfg.ANarrowingMktIv;
                _permanentlyIlliquid = cfg.PermanentlyIlliquid;
                _amsrl = (double)cfg.AMSRL;
                _bmsrl = (double)cfg.BMSRL;
                _resetAggr = cfg.AggressiveReset;
                _resetCons = cfg.ConservativeReset;
                _resetQuoteVol = cfg.QuoteVolumeReset;
                _resetDealVol = cfg.DealVolumeReset;
                _resetTime = cfg.TimeReset;
                _quoteVolResetLimit = cfg.QuoteVolumeResetLimit;
                _timeResetLimit = cfg.TimeResetLimit;
                _vegaSLimit = (double)cfgFut.VegaSLimit;
                _vegaLLimit = (double)cfgFut.VegaLLimit;
                _vannaSLimit = (double)cfgFut.VannaSLimit;
                _vannaLLimit = (double)cfgFut.VannaLLimit;
                _aggrResetTimeLimit = TimeSpan.FromSeconds((double)cfg.AggressiveResetTimeLimit);
                _consResetTimeLimit = TimeSpan.FromSeconds((double)cfg.ConservativeResetTimeLimit);
                _quoteResetTimeLimit = TimeSpan.FromSeconds((double)cfg.QuoteVolumeResetTimeLimit);
                _consResetLimit = (double)cfg.ConservativeResetLimit;
            } else {
                _vpAvailable = false;
            }
        }

        public void Reset(string comment) {
            _resetComment = comment;
            _needToReset = true;
        }

        public void DealReset() {
            _needDealReset = true;
        }

        bool _mktReset;
        DateTime _lastReset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ResetValuationStatus(ValuationStatus status, DateTime now, bool resetBid, bool resetOffer) {
            if(!_mktReset) {
                _mktReset = true;
                _lastReset = now;
                ValuationStatus = status;
                TimeValuationStatus = now;
                if(resetBid)
                    MarketIvBid = IvBid;
                if(resetOffer)
                    MarketIvOffer = IvOffer;
                _calculationIvBid = _calculationIvOffer = 0;

                if(status == ValuationStatus.Manual)
                    ValuationSpread = 0;

                _extLogger.Comment($"Model reset ({status}){(status==ValuationStatus.Manual ? ": "+_resetComment : null)}");
            }
        }

        /// <summary>
        /// Пересчитать параметры основного робота.
        /// </summary>
        /// <param name="input">Входные параметры.</param>
        /// <param name="reason">Причина обновления модели (для логирования).</param>
        /// <param name="strategies">Стратегии, для которых производится расчет. Некоторые параметры стратегий могут быть автоматически раcсчитаны.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public IOptionModelData Update(IOptionModelInputData input, RecalcReason reason, IOptionStrategy[] strategies = null) {
            if(input == null) throw new ArgumentNullException(nameof(input));

            _mktReset = false;
            _actualData = new Data(_actualData, input);

            _extLogger.Reset();

            if(_needToReset) {
                _needToReset = false;
                // manual reset must be here (before ivbid/ivoffer calculated), otherwise condition below MarketIvBid==0 won't work.
                ResetValuationStatus(ValuationStatus.Manual, input.Time, true, true);
            }

            try {
                #region init
                LastCalcSuccessful = false;
                LastError = null;

                if(_settingsUpdated) UpdateSettings();

                var optBid = input.OptionCalcBid;
                var optOffer = input.OptionCalcAsk;
                _futBid = input.FutureCalcBid;
                _futAsk = input.FutureCalcAsk;

                _buffer.SetTime(input.Time);

                if(_futBid <= 0 || _futAsk <= 0 || _futBid >= _futAsk || _buffer.TimeTillExpiration <= 0 || (optBid > 0 && optOffer > 0 && optBid >= optOffer)) {
                    LastError = "invalid input parameters";
                    return _actualData;
                }

                double futPriceBuy, futPriceSell;

                if(OptionType == OptionTypes.Call) {
                    futPriceBuy = _futBid;
                    futPriceSell = _futAsk;
                    _futLogPriceToStrikeBuy = Math.Log(futPriceBuy / _strike);
                    _futLogPriceToStrikeSell = Math.Log(futPriceSell / _strike);
                } else {
                    futPriceBuy = _futAsk;
                    futPriceSell = _futBid;
                    _futLogPriceToStrikeBuy = Math.Log(futPriceBuy / _strike);
                    _futLogPriceToStrikeSell = Math.Log(futPriceSell / _strike);
                }

                CurrentSpread = optOffer - optBid;

                #endregion

                #region Определение денежности опциона

                if(OptionType == OptionTypes.Call) {
                    Moneyness = Math.Log(_futBid / _strike);
                    if(Moneyness > _deepInTheMoneyLimit)
                        StrikeMoneyness = StrikeMoneyness.DeepInTheMoney;
                    else if(Moneyness < -_deepOutOfMoneyLimit)
                        StrikeMoneyness = StrikeMoneyness.DeepOutOfMoney;
                    else
                        StrikeMoneyness = StrikeMoneyness.AtTheMoney;
                } else {
                    Moneyness = Math.Log(_futAsk / _strike);
                    if(Moneyness < -_deepInTheMoneyLimit)
                        StrikeMoneyness = StrikeMoneyness.DeepInTheMoney;
                    else if(Moneyness > _deepOutOfMoneyLimit)
                        StrikeMoneyness = StrikeMoneyness.DeepOutOfMoney;
                    else
                        StrikeMoneyness = StrikeMoneyness.AtTheMoney;
                }

                #endregion

                if(!_vpAvailable) {
                    LastError = "valuation_params not available";
                    return _actualData;
                }

                #region current iv bid/offer calculation

                if(!optOffer.IsZero()) {
                    // перед вызовом CalcIV() или _getPremium() необходимо выставить корректные значения для _buffer.AssetPrice и _buffer.Arg0
                    _buffer.AssetPrice = futPriceSell;
                    _buffer.Arg0 = _futLogPriceToStrikeSell;
                    IvOffer = CalcIV(_buffer, optOffer);
                }

                if(!optBid.IsZero()) {
                    _buffer.AssetPrice = futPriceBuy;
                    _buffer.Arg0 = _futLogPriceToStrikeBuy;
                    IvBid = CalcIV(_buffer, optBid);
                }

                /////////////////////////
                // ReSharper disable CompareOfFloatsByEqualityOperator
                if(IvBid != 0 && IvOffer != 0)
                    IvSpread = IvOffer - IvBid;
                // ReSharper restore CompareOfFloatsByEqualityOperator

                #endregion

                #region Расчет неликвидных параметров для опциона

                const double smallDeltaCorr = 0.00001d;

                if(StrikeMoneyness == StrikeMoneyness.AtTheMoney) {
                    var bestArg1Expectation = CalcArg1(Moneyness, _bestIvExpectation);
                    IlliquidDelta = BestDeltaExpectation = normaldistr.normaldistribution(bestArg1Expectation);
                    IlliquidIv = _bestIvExpectation;
                } else {
                    if(StrikeMoneyness == StrikeMoneyness.DeepInTheMoney) {
                        if(OptionType == OptionTypes.Call) {
                            InitialDelta = _deepInTheMoneyDeltaCall;
                            IlliquidDelta = _buffer.TimeDays < _daysToExpirationForInitialDelta ? 
                                InitialDelta - smallDeltaCorr :
                                InitialDelta + _deltaCorrection;
                        } else {
                            InitialDelta = _deepInTheMoneyDeltaPut;
                            IlliquidDelta = _buffer.TimeDays < _daysToExpirationForInitialDelta ? 
                                InitialDelta + smallDeltaCorr :
                                InitialDelta - _deltaCorrection;
                        }
                    } else {
                        if(OptionType == OptionTypes.Call) {
                            InitialDelta = _deepOutOfMoneyDeltaCall;
                            IlliquidDelta = _buffer.TimeDays < _daysToExpirationForInitialDelta ? 
                                InitialDelta + smallDeltaCorr :
                                InitialDelta - _deltaCorrection;
                        } else {
                            InitialDelta = _deepOutOfMoneyDeltaPut;
                            IlliquidDelta = _buffer.TimeDays < _daysToExpirationForInitialDelta ? 
                                InitialDelta - smallDeltaCorr :
                                InitialDelta + _deltaCorrection;
                        }
                    }

                    var illiquid_arg1 = normaldistr.invnormaldistribution(IlliquidDelta);
                    var d0 = -illiquid_arg1 * _buffer.TimeToExpSqrtInv;
                    var d = d0 * d0 - 2 * (Moneyness / _buffer.TimeTillExpiration + _ir);
                    var dsqrt = Math.Sqrt(d);
                    var iv1 = illiquid_arg1 * _buffer.TimeToExpSqrtInv + dsqrt;
                    var iv2 = illiquid_arg1 * _buffer.TimeToExpSqrtInv - dsqrt;

                    if(iv1 >= _lowX && iv1 <= _highX)
                        IlliquidIv = iv1;
                    else if(iv2 >= _lowX && iv2 <= _highX)
                        IlliquidIv = iv2;
                    else
                        IlliquidIv = _bestIvExpectation;
                }

                var finalIlliquidArg1 = CalcArg1(Moneyness, IlliquidIv);
                var illiquidNd11 = CalcNd11(finalIlliquidArg1);
                IlliquidVega = CalcVega(_buffer, _futBid, illiquidNd11);
                IlliquidGamma = CalcGamma(_buffer, _futBid, illiquidNd11, IlliquidIv);
                IlliquidTheta = CalcTheta(_buffer, _futBid, finalIlliquidArg1, illiquidNd11, IlliquidIv);
                IlliquidVanna = CalcVanna(_buffer, finalIlliquidArg1, illiquidNd11, IlliquidIv);
                IlliquidVomma = CalcVomma(_buffer, _futBid, finalIlliquidArg1, illiquidNd11, IlliquidIv);

                #endregion

                #region Расчет греков и волатильностей по кв, не зависит от "ликвидности" режима

                var serCalcParams = _option.Series.CalcConfig;
                var ctBid = serCalcParams.CurveType(CurveTypeParam.Bid, CurveConfigType.Curve);
                var ctOffer = serCalcParams.CurveType(CurveTypeParam.Offer, CurveConfigType.Curve);
                CurveModelStatus = serCalcParams.CurveModelStatus;
                if(CurveModelStatus == CurveModelStatus.Valuation) {
                    var cubeBid = ctBid.Model == CurveTypeModel.Cube ? 1 : 0;
                    var parabolaBid = ctBid.Model != CurveTypeModel.Linear ? 1 : 0;
                    var cubeOffer = ctOffer.Model == CurveTypeModel.Cube ? 1 : 0;
                    var parabolaOffer = ctOffer.Model != CurveTypeModel.Linear ? 1 : 0;
                    var m2 = Moneyness * Moneyness;
                    var m3 = m2 * Moneyness;

                    CurveIvBid = ctBid.A3 * cubeBid * m3 +
                                 ctBid.A2 * parabolaBid * m2 +
                                 ctBid.A1 * Moneyness +
                                 ctBid.A0;

                    CurveIvOffer = ctOffer.A3 * cubeOffer * m3 +
                                   ctOffer.A2 * parabolaOffer * m2 +
                                   ctOffer.A1 * Moneyness +
                                   ctOffer.A0;

                    _buffer.Arg0 = _futLogPriceToStrikeBuy;
                    _buffer.AssetPrice = futPriceBuy;
                    CurveBid = _getPremium(_buffer, CurveIvBid);

                    _buffer.Arg0 = _futLogPriceToStrikeSell;
                    _buffer.AssetPrice = futPriceSell;
                    CurveOffer = _getPremium(_buffer, CurveIvOffer);

                    CurveAverage = 0.5d * (CurveBid + CurveOffer);
                    CurveIvAverage = 0.5d * (CurveIvBid + CurveIvOffer);

                    CurveSpread = CurveOffer - CurveBid;
                    CurveIvSpread = CurveIvOffer - CurveIvBid;

                    var curveArg1 = CalcArg1(Moneyness, CurveIvAverage);
                    var curveNd11 = CalcNd11(curveArg1);

                    CurveDelta = normaldistr.normaldistribution(curveArg1);
                    CurveVega = CalcVega(_buffer, _futBid, curveNd11);
                    CurveGamma = CalcGamma(_buffer, _futBid, curveNd11, CurveIvAverage);
                    CurveTheta = CalcTheta(_buffer, _futBid, curveArg1, curveNd11, CurveIvAverage);
                    CurveVanna = CalcVanna(_buffer, curveArg1, curveNd11, CurveIvAverage);
                    CurveVomma = CalcVomma(_buffer, _futBid, curveArg1, curveNd11, CurveIvAverage);
                }

                #endregion

                #region Условия перехода в неликвидный режим

                if(_permanentlyIlliquid || CurrentSpread >= _highLiquidSpreadLimit || IvBid.IsZero() || IvOffer.IsZero()) {
                    GreeksRegime = GreeksRegime.Illiquid;

                    if(CurveModelStatus == CurveModelStatus.Valuation) {
                        DeltaBid = CurveDelta;
                        Vega = CurveVega;
                        Gamma = CurveGamma;
                        Theta = CurveTheta;
                        Vanna = CurveVanna;
                        Vomma = CurveVomma;
                    } if(_permanentlyIlliquid) {
                        DeltaBid = IlliquidDelta;
                        Vega = IlliquidVega;
                        Gamma = IlliquidGamma;
                        Theta = IlliquidTheta;
                        Vanna = IlliquidVanna;
                        Vomma = IlliquidVomma;
                    } else {
                        DeltaBid = CalculationDelta.IsZero() ? IlliquidDelta : CalculationDelta;
                        Vega = CalculationVega.IsZero() ? IlliquidVega : CalculationVega;
                        Gamma = CalculationGamma.IsZero() ? IlliquidGamma : CalculationGamma;
                        Theta = CalculationTheta.IsZero() ? IlliquidTheta : CalculationTheta;
                        Vanna = CalculationVanna.IsZero() ? IlliquidVanna : CalculationVanna;
                        Vomma = CalculationVomma.IsZero() ? IlliquidVomma : CalculationVomma;
                    }
                } else {
                    GreeksRegime = GreeksRegime.Liquid;
                }

                #endregion

                if(MarketIvBid.IsZero() || MarketIvOffer.IsZero())
                    MarketIvBid = MarketIvOffer = 0d;

                #region Пересчет рыночной волатильности

                if(GreeksRegime == GreeksRegime.Liquid) {
                    ValuationTargetSpread = Math.Max(Math.Min(_valuationHighSpreadLimit, CurrentSpread), _valuationLowSpreadLimit);

                    if(ValuationSpread.IsZero())
                        ValuationSpread = ValuationTargetSpread;
                    else if(ValuationSpread < ValuationTargetSpread)
                        ValuationSpread = Math.Max(_valuationLowSpreadLimit, Util.Min(_valuationHighSpreadLimit, ValuationTargetSpread, ValuationSpread + _valuationWide * _minPriceStep));
                    else if(ValuationSpread > ValuationTargetSpread)
                        ValuationSpread = Util.Max(_valuationLowSpreadLimit, ValuationTargetSpread, Math.Min(_valuationHighSpreadLimit, ValuationSpread - _valuationNarrow * _minPriceStep));

                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    var firstCalc = MarketIvBid == 0d;

                    if(firstCalc) {
                        MarketIvBid = IvBid;
                        MarketIvOffer = IvOffer;
                    }

                    var arg1 = CalcArg1(_futLogPriceToStrikeBuy, MarketIvBid);
                    var nd11 = CalcNd11(arg1);
                    Vega = CalcVega(_buffer, futPriceBuy, nd11);

                    if(firstCalc) {
                        Gamma = CalcGamma(_buffer, futPriceBuy, nd11, MarketIvBid);
                        DeltaBid = normaldistr.normaldistribution(arg1);
                        Theta = CalcTheta(_buffer, futPriceBuy, arg1, nd11, MarketIvBid);
                        Vanna = CalcVanna(_buffer, arg1, nd11, MarketIvBid);
                        Vomma = CalcVomma(_buffer, futPriceBuy, arg1, nd11, MarketIvBid);

                        _buffer.Arg0 = _futLogPriceToStrikeBuy;
                        _buffer.AssetPrice = futPriceBuy;
                        MarketBid = _getPremium(_buffer, MarketIvBid);

                        _buffer.Arg0 = _futLogPriceToStrikeSell;
                        _buffer.AssetPrice = futPriceSell;
                        MarketOffer = _getPremium(_buffer, MarketIvOffer);

                        MarketAverage = 0.5d * (MarketBid + MarketOffer);
                        MarketSpread = MarketOffer - MarketBid;
                    } else {
                        #region стандартный сценарий

                        var v = _vegaUnit / Vega;
                        WideningMktIv = _aWideningMktIv * _minPriceStep * v;
                        NarrowingMktIv = _aNarrowingMktIv * _minPriceStep * v;

                        if(CurrentSpread < ValuationSpread) { // только расширяем рыночные котировки
                            MarketIvBid = Math.Max(Math.Min(IvBid, _calculationIvBid), _calculationIvBid - WideningMktIv);
                            MarketIvOffer = Math.Min(Math.Max(IvOffer, _calculationIvOffer), _calculationIvOffer + WideningMktIv);
                        } else if(CurrentSpread < _valuationMaxSpread) {
                            MarketIvBid = IvBid - _calculationIvBid >= 0 ? 
                                Math.Min(IvBid, _calculationIvBid + NarrowingMktIv) /*сужаем*/ :
                                Math.Max(IvBid, _calculationIvBid - WideningMktIv);

                            MarketIvOffer = _calculationIvOffer - IvOffer >= 0 ?
                                Math.Max(IvOffer, _calculationIvOffer - NarrowingMktIv) /*сужаем*/ :
                                Math.Min(IvOffer, _calculationIvOffer + WideningMktIv);
                        } else {
                            // спред очень широкий. ничего не делаем.
                        }

                        // Расчет оценочных рыночных котировок для проверки на ресет - только в ликвидном режиме.
                        _buffer.Arg0 = _futLogPriceToStrikeBuy;
                        _buffer.AssetPrice = futPriceBuy;
                        MarketBid = _getPremium(_buffer, MarketIvBid);

                        _buffer.Arg0 = _futLogPriceToStrikeSell;
                        _buffer.AssetPrice = futPriceSell;
                        MarketOffer = _getPremium(_buffer, MarketIvOffer);

                        MarketAverage = 0.5d * (MarketBid + MarketOffer);
                        MarketSpread = MarketOffer - MarketBid;

                        MarketSpreadResetLimit = _amsrl * ValuationSpread + _bmsrl;

                        CheckResetValuationStatus(input);

                        // Окончательный расчет в ликвидном режиме
                        arg1 = CalcArg1(_futLogPriceToStrikeBuy, MarketIvBid);
                        nd11 = CalcNd11(arg1);

                        Vega = CalcVega(_buffer, futPriceBuy, nd11);
                        Gamma = CalcGamma(_buffer, futPriceBuy, nd11, MarketIvBid);
                        DeltaBid = normaldistr.normaldistribution(arg1);
                        Theta = CalcTheta(_buffer, futPriceBuy, arg1, nd11, MarketIvBid);
                        Vanna = CalcVanna(_buffer, arg1, nd11, MarketIvBid);
                        Vomma = CalcVomma(_buffer, futPriceBuy, arg1, nd11, MarketIvBid);

                        CalculationVega = Vega;
                        CalculationGamma = Gamma;
                        CalculationDelta = DeltaBid;
                        CalculationTheta = Theta;
                        CalculationVanna = Vanna;
                        CalculationVomma = Vomma;

                        // Перерасчет рыночных котировок после ресета
                        if(_mktReset) {
                            _buffer.Arg0 = _futLogPriceToStrikeBuy;
                            _buffer.AssetPrice = futPriceBuy;
                            MarketBid = _getPremium(_buffer, MarketIvBid);

                            _buffer.Arg0 = _futLogPriceToStrikeSell;
                            _buffer.AssetPrice = futPriceSell;
                            MarketOffer = _getPremium(_buffer, MarketIvOffer);

                            MarketAverage = 0.5d * (MarketBid + MarketOffer);
                            MarketSpread = MarketOffer - MarketBid;
                        }

                        #endregion
                    }
                }

                #endregion

                MarketIvAverage = 0.5d * (MarketIvBid + MarketIvOffer);
                MarketIvSpread = MarketIvOffer - MarketIvBid;

                _calculationIvBid = MarketIvBid;
                _calculationIvOffer = MarketIvOffer;

                // при необходимости пересчитать параметры стратегий
                strategies?.ForEach(s => UpdateCalculatedStrategyParams(s, input));

                CalcDone = LastCalcSuccessful = true;

                return _actualData;
            } finally {
                _needDealReset = false;
                _lastData = _actualData;

                var removeBefore = input.Time - _recalcGroupPeriod;
                _recalcsByFuture.RemoveAll(dt => dt < removeBefore);
                _recalcsByOption.RemoveAll(dt => dt < removeBefore);
                _recalcsByOther.RemoveAll(dt => dt < removeBefore);

                if(reason == RecalcReason.FutureChanged)
                    _recalcsByFuture.Add(input.Time);
                else if(reason == RecalcReason.OptionChanged)
                    _recalcsByOption.Add(input.Time);
                else
                    _recalcsByOther.Add(input.Time);

                _extLogger.Log(strategies, reason, _recalcsByFuture.Count, _recalcsByOption.Count, _recalcsByOther.Count);
            }
        }

        void UpdateCalculatedStrategyParams(IOptionStrategy strategy, IOptionModelInputData input) {
            var cfg = strategy.ActiveConfig;
            if(cfg == null) return;

            var vms = strategy.ActiveStrategy;
            var p = vms.CalcParams;
            var stype = cfg.StrategyType;
            var tradingAllowedByLiquidity = false;

            try {
                if(GreeksRegime == GreeksRegime.Liquid) {
                    if(stype != StrategyType.MM) {
                        p.ChangeWide = Math.Min(cfg.HighChangeWide, Math.Max(cfg.LowChangeWide, cfg.AChangeWide * ValuationSpread / _minPriceStep));
                        p.OrderSpread = Math.Max(cfg.LowOrderSpread, Math.Min(cfg.HighOrderSpread, cfg.A1OrderSpread * ValuationSpread + cfg.A2OrderSpread));
                        p.ChangeNarrow = Math.Min(cfg.HighChangeNarrow, Math.Max(cfg.LowChangeNarrow, cfg.AChangeNarrow * p.OrderSpread / _minPriceStep));
                        p.SpreadsDifference = p.OrderSpread - MarketSpread;

                        p.OrderShift = 0.5d * p.SpreadsDifference;

                        if(p.OrderShift < 0) {
                            p.ShiftOL = Math.Max(cfg.ShiftOLLimit, cfg.AShiftOL * p.OrderShift);
                            p.ShiftOS = Math.Max(cfg.ShiftOSLimit, cfg.AShiftOS * p.OrderShift);

                            if(stype == StrategyType.Regular && cfg.CloseRegime) {
                                p.ShiftCL = Math.Max(cfg.ShiftCLLimit, cfg.AShiftCL * p.OrderShift);
                                p.ShiftCS = Math.Max(cfg.ShiftCSLimit, cfg.AShiftCS * p.OrderShift);
                            }
                        } else {
                            p.ShiftOL = cfg.AShiftOS * p.OrderShift;
                            p.ShiftOS = cfg.AShiftOL * p.OrderShift;

                            if(stype == StrategyType.Regular && cfg.CloseRegime) {
                                p.ShiftCL = cfg.AShiftCS * p.OrderShift;
                                p.ShiftCS = cfg.AShiftCL * p.OrderShift;
                            }
                        }
                    } else { // MM
                        var mmRecord = _option.MMRecord;
                        if(mmRecord != null) {
                            p.ObligationsVolume = mmRecord.MarketMakingAmount;
                            p.ObligationsSpread = (double)mmRecord.MarketMakingSpread;
                        } else {
                            p.ObligationsSpread = p.ObligationsVolume = 0;
                        }

                        p.CalcMMVolume = cfg.AutoObligationsVolume && mmRecord != null ? p.ObligationsVolume + cfg.ObligationsVolumeCorrection : cfg.MMVolume;
                        p.CalcMMMaxSpread = cfg.AutoObligationsSpread && mmRecord != null ? p.ObligationsSpread + cfg.ObligationsSpreadCorrection : cfg.MMMaxSpread;

                        p.ChangeWide = Math.Min(cfg.HighChangeWide, Math.Max(cfg.LowChangeWide, cfg.AChangeWide * p.CalcMMMaxSpread / _minPriceStep));
                        p.SpreadsDifference = p.CalcMMMaxSpread - MarketSpread;

                        p.OrderShift = 0.5d * p.SpreadsDifference;
                        p.OrderSpread = MarketSpread + 2 * p.OrderShift;
                        p.ChangeNarrow = Math.Min(cfg.HighChangeNarrow, Math.Max(cfg.LowChangeNarrow, cfg.AChangeNarrow * p.OrderSpread / _minPriceStep));

                        // определение коэффициентов при шифте
                        var vegaPort = input.FutParams.VegaPortfolio;
                        var vannaPort = input.FutParams.VannaPortfolio;

                        if(vegaPort < _vegaSLimit) { // Вега-экспозиция меньше нормального лимита (короткая)
                            if(vannaPort >= _vannaSLimit && vannaPort <= _vannaLLimit) { // Ванна-экспозиция в интервале нормальных лимитов
                                p.MMAShiftOS = cfg.AShiftMM;
                            } else if(vannaPort < _vannaSLimit) {
                                // Ванна-экспозиция меньше нижнего лимита, дифференцируем коэффициенты при шифтах по типу инструмента.
                                // Для веги предпочтительна покупка, для ванны путы лучше не покупать. Смещаем только коллы.
                                p.MMAShiftOS = OptionType == OptionTypes.Call ? cfg.AShiftMM : cfg.AShiftNorm;
                            } else {
                                // Ванна-экспозиция больше верхнего лимита, дифференцируем коэффициенты при шифтах по типу инструмента.
                                // Для веги предпочтительна покупка, для ванны коллы лучше не покупать. Смещаем только путы.
                                p.MMAShiftOS = OptionType == OptionTypes.Call ? cfg.AShiftNorm : cfg.AShiftMM;
                            }

                            p.MMAShiftOL = 2 - p.MMAShiftOS;
                        } else if(vegaPort > _vegaLLimit) { // Вега-экспозиция больше нормального лимита
                            if(vannaPort >= _vannaSLimit && vannaPort <= _vannaLLimit) { // Ванна-экспозиция в интервале нормальных лимитов
                                p.MMAShiftOL = cfg.AShiftMM;
                            } else if(vannaPort < _vannaSLimit) {
                                // Ванна-экспозиция меньше нижнего лимита, дифференцируем коэффициенты при шифтах по типу инструмента.
                                // Для веги предпочтительна продажа, для ванны коллы лучше не продавать. Смещаем только путы.
                                p.MMAShiftOL = OptionType == OptionTypes.Call ? cfg.AShiftNorm : cfg.AShiftMM;
                            } else {
                                // Ванна-экспозиция больше верхнего лимита, дифференцируем коэффициенты при шифтах по типу инструмента.
                                // Для веги предпочтительна продажа, для ванны путы лучше не продавать. Смещаем только коллы.
                                p.MMAShiftOL = OptionType == OptionTypes.Call ? cfg.AShiftMM : cfg.AShiftNorm;
                            }

                            p.MMAShiftOS = 2 - p.MMAShiftOL;
                        } else {
                            // Вега-экспозиция в интервале нормальных лимитов
                            p.MMAShiftOS = cfg.AShiftNorm;
                            p.MMAShiftOL = 2 - p.MMAShiftOS;
                        }

                        if(p.OrderShift >= 0) {
                            p.ShiftOL = p.MMAShiftOL * p.OrderShift;
                            p.ShiftOS = p.MMAShiftOS * p.OrderShift;
                        } else {
                            p.ShiftOL = Math.Max(cfg.ShiftOLLimit, p.MMAShiftOS * p.OrderShift);
                            p.ShiftOS = Math.Max(cfg.ShiftOSLimit, p.MMAShiftOL * p.OrderShift);
                        }
                    }

                    tradingAllowedByLiquidity = MarketSpread <= cfg.OrderSpreadLimit;
                } else {
                    p.ChangeWide = cfg.HighChangeWide;
                    p.ChangeNarrow = cfg.HighChangeNarrow;
                    p.IlliquidIvBid = Math.Min(cfg.HighIlliquidIvBid, cfg.AIlliquidIvBid * IlliquidIv + cfg.BIlliquidIvBid);
                    p.IlliquidIvOffer = Math.Max(cfg.LowIlliquidIvOffer, cfg.AIlliquidIvOffer * IlliquidIv + cfg.BIlliquidIvOffer);
                    p.IlliquidIvSpread = p.IlliquidIvOffer - p.IlliquidIvBid;
                    p.IlliquidIvSpread = 0.5d * (p.IlliquidIvOffer + p.IlliquidIvBid);

                    tradingAllowedByLiquidity = cfg.IlliquidTrading || (cfg.IlliquidCurveTrading && CurveModelStatus == CurveModelStatus.Valuation);
                }
            } finally {
                if(p.TradingAllowedByLiquidity != tradingAllowedByLiquidity) {
                    p.TradingAllowedByLiquidity = tradingAllowedByLiquidity;

                    _log.Dbg.AddDebugLog($"TradingAllowedByLiquidity({vms.Id}): {tradingAllowedByLiquidity} (regime={GreeksRegime}, CurveModelStatus={CurveModelStatus}, illiquidTrading={cfg.IlliquidTrading}, illiquidCurveTrading={cfg.IlliquidCurveTrading}, market_iv_spread={MarketIvSpread}, illiquid_iv_spread={p.IlliquidIvSpread})");
                }

                p.Recalculated();
            }
        }

        void CheckResetValuationStatus(IOptionModelInputData input) {
            #region aggressive reset

            if(_resetAggr && MarketSpread > MarketSpreadResetLimit && CurrentSpread >= ValuationSpread && CurrentSpread <= MarketSpreadResetLimit) {
                if(AggressiveResetStartTime == null)
                    AggressiveResetStartTime = input.Time;

                var diff = input.Time - AggressiveResetStartTime.Value;
                if(!_mktReset && diff >= _aggrResetTimeLimit) {
                    ResetValuationStatus(ValuationStatus.Aggressive, input.Time, true, true);
                    AggressiveResetStartTime = null;
                }
            } else {
                AggressiveResetStartTime = null;
            }

            #endregion

            #region conservative reset

            if(_resetCons && CurrentSpread <= _valuationMaxSpread && CurrentSpread - MarketSpread >= _consResetLimit) {
                if(ConservativeResetStartTime == null)
                    ConservativeResetStartTime = input.Time;

                var diff = input.Time - ConservativeResetStartTime.Value;
                if(!_mktReset && diff >= _consResetTimeLimit) {
                    ResetValuationStatus(ValuationStatus.Conservative, input.Time, true, true);
                    ConservativeResetStartTime = null;
                }
            } else {
                ConservativeResetStartTime = null;
            }

            #endregion

            #region quote reset

            if(_resetQuoteVol) {
                var bidReset = input.OptionCalcBidVol >= _quoteVolResetLimit;
                var offerReset = input.OptionCalcAskVol >= _quoteVolResetLimit;

                if(bidReset) {
                    if(QuoteVolBidResetStartTime == null)
                        QuoteVolBidResetStartTime = input.Time;

                    bidReset &= input.Time - QuoteVolBidResetStartTime.Value >= _quoteResetTimeLimit;
                } else {
                    QuoteVolBidResetStartTime = null;
                }

                if(offerReset) {
                    if(QuoteVolOfferResetStartTime == null)
                        QuoteVolOfferResetStartTime = input.Time;

                    offerReset &= input.Time - QuoteVolOfferResetStartTime.Value >= _quoteResetTimeLimit;
                } else {
                    QuoteVolOfferResetStartTime = null;
                }

                if(!_mktReset && (bidReset || offerReset)) {
                    var newStatus = !bidReset ? ValuationStatus.QuoteVolumeOffer :
                                (!offerReset ? ValuationStatus.QuoteVolumeBid : ValuationStatus.QuoteVolume);

                    if(newStatus != ValuationStatus) {
                        ResetValuationStatus(newStatus, input.Time, bidReset, offerReset);

                        if(bidReset)   QuoteVolBidResetStartTime = null;
                        if(offerReset) QuoteVolOfferResetStartTime = null;
                    }
                }
            } else {
                QuoteVolBidResetStartTime = null;
                QuoteVolOfferResetStartTime = null;
            }

            #endregion

            #region deal reset

            if(!_mktReset && _resetDealVol && _needDealReset && CurrentSpread >= ValuationSpread && CurrentSpread <= _valuationMaxSpread)
                ResetValuationStatus(ValuationStatus.DealVolume, input.Time, true, true);

            #endregion

            #region time reset

            if(!_mktReset && _resetTime) {
                var sinceLastDeal = (input.Time - input.LastDealTime).TotalMinutes;
                var sinceLastReset = (input.Time - _lastReset).TotalMinutes;
                var minutes = Math.Min(sinceLastDeal, sinceLastReset);
                if(minutes >= _timeResetLimit && CurrentSpread >= ValuationSpread && CurrentSpread <= _valuationMaxSpread)
                    ResetValuationStatus(ValuationStatus.Time, input.Time, true, true);
            }

            #endregion
        }

        public double GetPremium(Sides side, double iv) {
            if(side == Sides.Buy) {
                _buffer.Arg0 = _futLogPriceToStrikeBuy;
                _buffer.AssetPrice = OptionType == OptionTypes.Call ? _futBid : _futAsk;
            } else {
                _buffer.Arg0 = _futLogPriceToStrikeSell;
                _buffer.AssetPrice = OptionType == OptionTypes.Call ? _futAsk : _futBid;
            }

            return _getPremium(_buffer, iv);
        }

        double GetPremiumCall(WorkBuffer buf, double iv) {
            buf.Arg1 = CalcArg1(buf, iv);
            buf.Nd1 = normaldistr.normaldistribution(buf.Arg1);
            var nd2 = normaldistr.normaldistribution(buf.Arg1 - iv * buf.TimeToExpSqrt);

            return buf.AssetPrice * buf.Nd1 - _strike * buf.ExpRate * nd2;
        }

        double GetPremiumPut(WorkBuffer buf, double iv) {
            buf.Arg1 = CalcArg1(buf, iv);
            buf.Nd1 = normaldistr.normaldistribution(buf.Arg1);
            var nd2 = normaldistr.normaldistribution(buf.Arg1 - iv * buf.TimeToExpSqrt);

            return (buf.Nd1 - 1) * buf.AssetPrice - (nd2 - 1) * _strike * buf.ExpRate;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalcArg1(WorkBuffer buf, double iv) {
            return (buf.Arg0 + (_ir + iv*iv/2d) * buf.TimeTillExpiration) / (iv * buf.TimeToExpSqrt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalcArg1(double arg, double iv) {
            return (arg + (_ir + iv*iv/2d) * _buffer.TimeTillExpiration) / (iv * _buffer.TimeToExpSqrt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalcArg2(WorkBuffer buf, double arg1, double iv) {
            return arg1 - iv * buf.TimeToExpSqrt;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalcNd11(double arg) {
            return Math.Exp(-0.5d * arg * arg) / TwoPiSqrt;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalcVega(WorkBuffer buf, double price, double nd11) {
            return price * buf.TimeToExpSqrt * nd11 * _vegaUnit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalcGamma(WorkBuffer buf, double price, double nd11, double iv) {
            return _gammaUnit * nd11 / (price * iv * buf.TimeToExpSqrt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalcTheta(WorkBuffer buf, double price, double arg1, double nd11, double iv) {
            var arg2 = CalcArg2(buf, arg1, iv);
            var nd2 = normaldistr.normaldistribution(arg2);

            return -_thetaUnit / 365 * 
                      (price * iv * nd11 / (TwoSqrt * buf.TimeToExpSqrt) + 
                       _strike * _ir * buf.ExpRate * nd2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalcVanna(WorkBuffer buf, double arg1, double nd11, double iv) {
            var arg2 = CalcArg2(buf, arg1, iv);
            return -_vannaUnit * nd11 * arg2 / iv;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalcVomma(WorkBuffer buf, double price, double arg1, double nd11, double iv) {
            var arg2 = CalcArg2(buf, arg1, iv);
            return _vommaUnit * price * buf.TimeToExpSqrt * nd11 * arg1 * arg2 / iv;
        }

        double CalcIV(WorkBuffer buf, double premium) {
            var accuracy = _ivCalculationAccuracy;
            if(accuracy < BaseConfig.EpsilonDouble) return 0;

            var high = 10d;
            var low = 0d;

            if(premium < _getPremium(buf, _ivCalculationAccuracy))
                return 0;

            while((high - low) > _ivCalculationAccuracy) {
                var iv = 0.5d * (high + low);

                if(_getPremium(buf, iv) > premium)
                    high = iv;
                else
                    low = iv;
            }

            return 0.5d * (high + low);
        }

        public double CalculateIv(DateTime time, double premium, double futurePrice) {
            var buf = new WorkBuffer(this, time) {
                AssetPrice = futurePrice,
                Arg0 = Math.Log(futurePrice / _strike),
            };

            return CalcIV(buf, premium);
        }

        public static DateTime GetExpirationTime(Security security) {
            // ReSharper disable once PossibleInvalidOperationException
            var expDate = (DateTime)security.ExpiryDate;

            if(expDate.TimeOfDay == TimeSpan.Zero)
                expDate += security.Board.ExpiryTime;

            return expDate;
        }

        public static double GetDaysTillExpiration(DateTime expTime, DateTime curTime) {
            var diff = expTime - curTime;
            return diff > TimeSpan.Zero ? diff.TotalDays : 0;
        }
    }
}
