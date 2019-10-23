using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Ecng.Collections;
using Ecng.Common;
using Ecng.ComponentModel;
using MoreLinq;
using OptionBot.Config;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Messages;
using StockSharp.Plaza;

namespace OptionBot.robot {
    /// <summary>Класс-посредник для отображения информации порфеля в пользовательском интерфейсе.</summary>
    public class PortfolioInfo : ConnectorNotifiableObject {
        PortfolioEx _nativePortfolio;
        //public PortfolioEx NativePortfolio {get {return _nativePortfolio;}}

        public string Name {get {return _nativePortfolio.Name; }}
        public decimal VariationMargin {get { return _nativePortfolio.VariationMargin; }}
        public decimal CurrentValue { get { return _nativePortfolio.CurrentValue; }}
        public decimal BlockedMoney {get { return _nativePortfolio.BlockedMoney; }}
        public decimal FreeMoney {get { return _nativePortfolio.FreeMoney; }}
        public decimal Commission {get { return _nativePortfolio.Commission; }}

        public PortfolioInfo(Controller controller, Portfolio p) : base(controller) {
            _nativePortfolio = (PortfolioEx)p;

            // добавление автоматической нотификации при изменении свойств потфеля
            AddNotifier(_nativePortfolio);
            Notifier(_nativePortfolio).AddExternalProperty(() => Name);
            Notifier(_nativePortfolio).AddExternalProperty(() => VariationMargin);
            Notifier(_nativePortfolio).AddExternalProperty(() => CurrentValue);
            Notifier(_nativePortfolio).AddExternalProperty(() => BlockedMoney);
            Notifier(_nativePortfolio).AddExternalProperty(() => FreeMoney);
            Notifier(_nativePortfolio).AddExternalProperty(() => Commission);
        }

        public void ReplacePortfolio(Portfolio newPort) {
            ReplaceConnectorObject(ref _nativePortfolio, (PortfolioEx)newPort);
        }
    }

    /// <summary>Класс-посредник для отображения информации инструмента в пользовательском интерфейсе.</summary>
    public abstract class SecurityInfo : ConnectorNotifiableObject, IRobotDataUpdater {
        protected new static readonly Logger _log = new Logger();
        Security _nativeSecurity;
        ISecurityProcessor _securityProcessor;
        SecurityId _nativeSecurityId;
        public Security NativeSecurity {get {return _nativeSecurity;}}
        public ISecurityProcessor SecProcessor {get {return _securityProcessor;}}
        /// <summary>can only be used in trading if security was synchronized</summary>
        public virtual bool IsOnline => SecProcessor != null;
        PositionInfo _position;
        MyTradeInfo _myLastTrade;
        readonly MarketDepthInfo _marketDepth;
        string _logName;

        public string Id {get { return _nativeSecurity.Id; }}
        public int PlazaIsinId => ((IPlazaSecurityId)_nativeSecurityId).Return(psid => psid.IsinId, 0);
        public string Code {get { return _nativeSecurity.Code; }}
        public string LogName {get { return _logName ?? (_logName = Code.ToLowerInvariant()); }}
        public string ShortName {get { return _nativeSecurity.ShortName; }}
        public string Name {get { return _nativeSecurity.Name; }}
        public decimal MinStepSize { get { return _nativeSecurity.PriceStep; }}
        public Quote BestBid {get {return _nativeSecurity.BestBid; }}
        public Quote BestAsk {get {return _nativeSecurity.BestAsk; }}
        public decimal BestBidPrice {get {return _nativeSecurity.BestBid.Return(q => q.Price, 0); }}
        public decimal BestAskPrice {get {return _nativeSecurity.BestAsk.Return(q => q.Price, 0); }}
        public Trade LastTrade {get {return _nativeSecurity.LastTrade; }}
        public SecurityTypes Type {get {return _nativeSecurity.Type.Value; }}
        public decimal Volume => _nativeSecurity.Volume;
        public decimal OpenInterest => _nativeSecurity.OpenInterest.Return2(d => d, 0);
        public PositionInfo Position {get { return _position; } set { SetField(ref _position, value); }}
        public int PositionValue {get {return (int)Position.Return(pi => pi.CurrentValue, 0);}}
        public MarketDepthInfo MarketDepth {get {return _marketDepth; }}
        public MyTradeInfo MyLastTrade {get {return _myLastTrade;} set {SetField(ref _myLastTrade, value); OnPropertyChanged(() => MyLastTradeStr);}}
        public DateTime? BidAskTime => _marketDepth.LastUpdateTime;
        public int OwnVolume => RobotData.GetOwnVolumeBySecurity(this);

        public decimal? BidAskAverage { get {
            var b = BestBid;
            var a = BestAsk;
            return b == null || a == null ? (decimal?)null : (b.Price + a.Price) / 2;
        }}

        public string MyLastTradeStr {get {return _myLastTrade.With(mti => "{0} {1}@{2}  {3:HH:mm:ss}".Put(mti.OrderDirection, mti.Volume, mti.Price, mti.Time));}}

        public event Action<SecurityInfo> PositionChanged;
        public event Action<SecurityInfo> BestQuotesChanged;
        public static event Action<SecurityInfo> NewSecurityInfo;
        public static event Action<SecurityInfo> NativeSecurityReplaced;

        protected SecurityInfo(Controller controller, Security sec) : base(controller) {
            _nativeSecurity = sec;
            _securityProcessor = sec.Connector.With(c => ((PlazaTraderEx)c).GetSecurityProcessor(sec));

            AddNotifier(_nativeSecurity);
            Notifier(_nativeSecurity).AddExternalProperty(() => Id);
            Notifier(_nativeSecurity).AddExternalProperty(() => ShortName);
            Notifier(_nativeSecurity).AddExternalProperty(() => Name);
            Notifier(_nativeSecurity).AddExternalProperty(() => MinStepSize);
            Notifier(_nativeSecurity).AddExternalProperty(() => Type);
            Notifier(_nativeSecurity).AddExternalProperty(() => BestBid);
            Notifier(_nativeSecurity).AddExternalProperty(() => BestAsk);
            Notifier(_nativeSecurity).AddExternalProperty(() => BestBid, () => BidAskTime);
            Notifier(_nativeSecurity).AddExternalProperty(() => BestAsk, () => BidAskTime);
            Notifier(_nativeSecurity).AddExternalProperty(() => BestBid, () => BidAskAverage);
            Notifier(_nativeSecurity).AddExternalProperty(() => BestAsk, () => BidAskAverage);
            Notifier(_nativeSecurity).AddExternalProperty(() => Volume);
            Notifier(_nativeSecurity).AddExternalProperty(() => OpenInterest);
            //Notifier(_nativeSecurity).AddExternalProperty(() => LastTrade);

            Notifier(_nativeSecurity).AddPropertyChangeHandler(() => BestBid, OnBestQuotesChanged);
            Notifier(_nativeSecurity).AddPropertyChangeHandler(() => BestAsk, OnBestQuotesChanged);

            _marketDepth = new MarketDepthInfo(this);

            RobotData.PositionChanged += info => { if(info == Position) PositionChanged.SafeInvoke(this); };

            RobotData.PropertyChanged += (sender, args) => {
                if(args.PropertyName == nameof(RobotData.ConnectionState) && RobotData.IsDisconnected)
                    _securityProcessor = null; // нужно для того чтобы при переподключении не использовался старый процессор
            };
        }

        public override void Init() {
            base.Init();

            UpdateNativeSecurityId();

            NewSecurityInfo?.Invoke(this);
            NativeSecurityReplaced?.Invoke(this);
        }

        void OnBestQuotesChanged(string propName) {
            BestQuotesChanged?.Invoke(this);
        }

        public void ReplaceSecurity(Security newSecurity) {
            if(newSecurity == null) throw new ArgumentNullException(nameof(newSecurity));

            try {
                var oldIsinId = PlazaIsinId;
                VerifyNewSecurity(newSecurity);

                ReplaceConnectorObject(ref _nativeSecurity, newSecurity);
                _securityProcessor = _nativeSecurity.Connector.With(c => ((PlazaTraderEx)c).GetSecurityProcessor(_nativeSecurity));

                UpdateNativeSecurityId();

                if(oldIsinId != 0 && oldIsinId != PlazaIsinId)
                    _log.Dbg.AddErrorLog($"{Id}: received NEW isinId for security: {oldIsinId} ==> {PlazaIsinId}");

                NativeSecurityReplaced?.Invoke(this);
            } catch(Exception e) {
                _log.Dbg.AddErrorLog("Unable to replace security: {0}", e);
            }
        }

        protected virtual void VerifyNewSecurity(Security newSecurity) { }

        void UpdateNativeSecurityId() {
            _nativeSecurity.Connector.Do(conn => {
                _nativeSecurityId = ((StockSharp.Algo.Connector)conn).GetSecurityId(_nativeSecurity);

                _log.Dbg.AddInfoLog("{0}: isin_id={1}", Code, PlazaIsinId);
            });

            _marketDepth.Init();
        }

        public void UpdatePosition() {
            var p = RobotData.ActivePortfolio;
            if(p == null) { Position = null; return; }

            Position = RobotData.AllPositions.FirstOrDefault(pi => pi.Security.Id == Id && pi.Portfolio.Name == p.Name);
        }

        public override void Activate() {
            base.Activate();
            RobotData.RegisterUpdater(this);
        }

        /// <summary>Обновление данных по таймеру.</summary>
        public void UpdateData() {
            if(IsActive) {
                OnUpdateData();
            } else {
                RobotData.DeregisterUpdater(this);
                OnResetData();
            }
        }

        static readonly TimeSpan _volumesUpdatePeriod = TimeSpan.FromSeconds(1);
        DateTime _lastVolumeUpdatePeriod;

        protected virtual void OnUpdateData() {
            var now = SteadyClock.Now;

            if(now - _lastVolumeUpdatePeriod > _volumesUpdatePeriod) {
                _lastVolumeUpdatePeriod = now;
                RaisePropertyChanged(() => OwnVolume);
            }
        }

        protected virtual void OnResetData() { }
    }

    /// <summary>Информация о фьючерсе.</summary>
    public sealed class FuturesInfo : SecurityInfo {
        #region fields/properties

        readonly RobotLogger.FutureQuotesLogger _quotesLogger;
        bool _disablingFuture;

        double _exposition;
        decimal _vegaCallBuyLimit, _vegaPutBuyLimit, _vegaCallSellLimit, _vegaPutSellLimit;
        decimal _vegaBuyLimit, _vegaSellLimit, _mmVegaBuyLimit, _mmVegaSellLimit, _vegaBuyTarget, _vegaSellTarget, _vegaPortfolio;
        decimal _gammaBuyLimit, _gammaSellLimit, _mmGammaBuyLimit, _mmGammaSellLimit, _gammaBuyTarget, _gammaSellTarget, _gammaPortfolio;
        decimal _vannaPortfolio, _vannaLongLimit, _vannaShortLimit;
        decimal _vommaPortfolio, _vommaLongLimit, _vommaShortLimit;

        decimal _thetaPortfolio;
        decimal _calculationBid, _calculationAsk;
        DateTime? _calculationTime;
        DateTime? _timeValuationRun;
        bool _expositionCalculated;
        FutureTradingModule _tradingModule;

        readonly HedgeCalculator _calculator;
        readonly ICfgPairFuture _config;

        readonly ObservableCollection<OptionInfo> _derivedOptions = new ObservableCollection<OptionInfo>();
        readonly ObservableCollection<OptionSeriesInfo> _optionsSeriesList = new ObservableCollection<OptionSeriesInfo>();

        public RobotLogger.HedgeLogger HedgeLogger {get;}

        public ICfgPairFuture Config {get {return _config;}}
        public IConfigFuture CfgFuture {get {return _config.Effective;}}

        /// <summary>Опционы, базовым активом для которых является данный фьючерс.</summary>
        public IEnumerable<OptionInfo> DerivedOptions { get { return _derivedOptions; } }
        /// <summary>Список серий опционов.</summary>
        public IEnumerable<OptionSeriesInfo> OptionsSeriesList { get { return _optionsSeriesList; }}

        public double Exposition { get { return _exposition; } set {SetField(ref _exposition, value); }}
        /// <summary>Признак успешного расчета экспозиции. Нужен для отображения ошибки в пользовательском интерфейсе.</summary>
        public bool ExpositionCalculated { get { return _expositionCalculated; } set {SetField(ref _expositionCalculated, value); }}

        public decimal VegaCallBuyLimit {get{return _vegaCallBuyLimit;} private set{SetField(ref _vegaCallBuyLimit, value); }}
        public decimal VegaPutBuyLimit {get{return _vegaPutBuyLimit;} private set{SetField(ref _vegaPutBuyLimit, value); }}
        public decimal VegaCallSellLimit {get{return _vegaCallSellLimit;} private set{SetField(ref _vegaCallSellLimit, value); }}
        public decimal VegaPutSellLimit {get{return _vegaPutSellLimit;} private set{SetField(ref _vegaPutSellLimit, value); }}

        public decimal VegaBuyLimit {get{return _vegaBuyLimit;} private set{SetField(ref _vegaBuyLimit, value); }}
        public decimal VegaSellLimit {get{return _vegaSellLimit;} private set{SetField(ref _vegaSellLimit, value); }}
        public decimal MMVegaBuyLimit {get{return _mmVegaBuyLimit;} private set{SetField(ref _mmVegaBuyLimit, value); }}
        public decimal MMVegaSellLimit {get{return _mmVegaSellLimit;} private set{SetField(ref _mmVegaSellLimit, value); }}
        public decimal VegaBuyTarget {get{return _vegaBuyTarget;} private set{SetField(ref _vegaBuyTarget, value); }}
        public decimal VegaSellTarget {get{return _vegaSellTarget;} private set{SetField(ref _vegaSellTarget, value); }}
        public decimal VegaPortfolio {get {return _vegaPortfolio;} private set {SetField(ref _vegaPortfolio, value); }}

        public decimal GammaBuyLimit {get{return _gammaBuyLimit;} private set{SetField(ref _gammaBuyLimit, value); }}
        public decimal GammaSellLimit {get{return _gammaSellLimit;} private set{SetField(ref _gammaSellLimit, value); }}
        public decimal MMGammaBuyLimit {get{return _mmGammaBuyLimit;} private set{SetField(ref _mmGammaBuyLimit, value); }}
        public decimal MMGammaSellLimit {get{return _mmGammaSellLimit;} private set{SetField(ref _mmGammaSellLimit, value); }}
        public decimal GammaBuyTarget {get{return _gammaBuyTarget;} private set{SetField(ref _gammaBuyTarget, value); }}
        public decimal GammaSellTarget {get{return _gammaSellTarget;} private set{SetField(ref _gammaSellTarget, value); }}
        public decimal GammaPortfolio {get {return _gammaPortfolio;} private set {SetField(ref _gammaPortfolio, value); }}

        public decimal VannaPortfolio {get {return _vannaPortfolio;} private set {SetField(ref _vannaPortfolio, value); }}
        public decimal VannaLongLimit {get {return _vannaLongLimit;} private set {SetField(ref _vannaLongLimit, value); }}
        public decimal VannaShortLimit {get {return _vannaShortLimit;} private set {SetField(ref _vannaShortLimit, value); }}

        public decimal VommaPortfolio {get {return _vommaPortfolio;} private set {SetField(ref _vommaPortfolio, value); }}
        public decimal VommaLongLimit {get {return _vommaLongLimit;} private set {SetField(ref _vommaLongLimit, value); }}
        public decimal VommaShortLimit {get {return _vommaShortLimit;} private set {SetField(ref _vommaShortLimit, value); }}

        public decimal ThetaPortfolio {get {return _thetaPortfolio;} private set {SetField(ref _thetaPortfolio, value); }}

        public decimal CalculationBid {get {return _calculationBid; } set {SetField(ref _calculationBid, value);}}
        public decimal CalculationAsk {get {return _calculationAsk; } set {SetField(ref _calculationAsk, value);}}
        public DateTime? CalculationTime {get {return _calculationTime; } set {SetField(ref _calculationTime, value);}}

        public decimal? BidChange { get {
            var b = BestBid;
            var cb = CalculationBid;
            return b == null || cb == 0 ? (decimal?)null : b.Price - cb;
        }}
        public decimal? AskChange { get {
            var a = BestAsk;
            var co = CalculationAsk;
            return a == null || co == 0 ? (decimal?)null : a.Price - co;
        }}
        public decimal? BidAskChange { get {
            var a = AskChange; var b = BidChange;
            return a == null ? b : b == null ? a : Math.Max(a.Value, b.Value);
        }}

        public DateTime? TimeValuationRun {get {return _timeValuationRun; } set {SetField(ref _timeValuationRun, value);}}

        public FutureTradingModule TradingModule => EnsureTradingModule();
        public HedgeCalculator Calculator => _calculator;

        #region trading availability

        IConfigSecuritySelection CfgSelection {get {return ConfigProvider.ConfigSecuritySelection.Effective;}}
        ConfigSecuritySelection CfgSelectionUI {get {return ConfigProvider.ConfigSecuritySelection.UI;}}

        public bool IsSelectedForTrading {get {return !_disablingFuture && CfgSelection.IsSelectedForTrading(this); }}
        public bool IsFutureActive {get {return !_disablingFuture && IsFutureActive2;} set { CfgSelectionUI.SetActive(this, value);}}
        bool IsFutureActive2 => CfgSelection.IsActive(this);

        #endregion

        #endregion

        public FuturesInfo(Controller controller, Security sec) : base(controller, sec) {
            if(sec.Type != SecurityTypes.Future) throw new ArgumentException("sec.Type");
            _calculator = new HedgeCalculator(Controller.Robot, this);
            _config = ConfigProvider.GetFutureConfig(this);
            ConfigProvider.ConfigSecuritySelection.EffectiveConfigChanged += (cfg, names) => {
                RaisePropertyChanged(() => IsFutureActive);
                RaisePropertyChanged(() => IsSelectedForTrading);
            };

            AddNotifier(this);
            Notifier(this).AddExternalProperty(() => BestBid, () => BidChange);
            Notifier(this).AddExternalProperty(() => BestBid, () => BidAskChange);
            Notifier(this).AddExternalProperty(() => CalculationBid, () => BidChange);
            Notifier(this).AddExternalProperty(() => CalculationBid, () => BidAskChange);
            Notifier(this).AddExternalProperty(() => BestAsk, () => AskChange);
            Notifier(this).AddExternalProperty(() => BestAsk, () => BidAskChange);
            Notifier(this).AddExternalProperty(() => CalculationAsk, () => AskChange);
            Notifier(this).AddExternalProperty(() => CalculationAsk, () => BidAskChange);

            HedgeLogger = Controller.RobotLogger.HedgeModule(this);
            _quotesLogger = Controller.RobotLogger.Future(this);

            Controller.Connector.DefaultSubscriber.MarketDepthChanged += ConnectorOnMarketDepthChanged;

            Activate();

            Config.CanUpdateConfig += (cfg, args) => {
                var minStep = _optionsSeriesList.Select(s => s.MinStrikeStep).Where(step => step > 0).DefaultIfEmpty().Min();
                if(!(minStep > 0)) return;

                if(cfg.LiquidStrikeStep > 0 && cfg.LiquidStrikeStep % minStep != 0)
                    args.Errors.Add("Шаг ликвидного страйка для фьючерса {0} должен быть кратным минимальному шагу страйка {1}".Put(Id, minStep));
            };

            Params = new FutureParams();

            _config.EffectiveConfigChanged += (cfg, names) => {
                Controller.RobotLogger.CfgFuture.Log(CfgFuture);
            };
        }

        public override void Init() {
            base.Init();

            Controller.RobotLogger.CfgFuture.Log(CfgFuture);
            _log.Dbg.AddDebugLog("new future: {0}", Code);
        }

        protected override void DisposeManaged() {
            _tradingModule.Do(tm => tm.Dispose());
            base.DisposeManaged();
        }

        /// <summary>Обновление данных по таймеру.</summary>
        protected override void OnUpdateData() {
            var p = Params;

            if(p != null) {
                VegaCallBuyLimit = p.VegaCallBuyLimit.ToDecimalChecked();
                VegaPutBuyLimit = p.VegaPutBuyLimit.ToDecimalChecked();
                VegaCallSellLimit = p.VegaCallSellLimit.ToDecimalChecked();
                VegaPutSellLimit = p.VegaPutSellLimit.ToDecimalChecked();

                VegaBuyLimit = p.VegaBuyLimit.ToDecimalChecked();
                VegaSellLimit = p.VegaSellLimit.ToDecimalChecked();
                MMVegaBuyLimit = p.MMVegaBuyLimit.ToDecimalChecked();
                MMVegaSellLimit = p.MMVegaSellLimit.ToDecimalChecked();
                VegaBuyTarget = p.VegaBuyTarget.ToDecimalChecked();
                VegaSellTarget = p.VegaSellTarget.ToDecimalChecked();
                VegaPortfolio = p.VegaPortfolio.ToDecimalChecked();

                GammaBuyLimit = p.GammaBuyLimit.ToDecimalChecked();
                GammaSellLimit = p.GammaSellLimit.ToDecimalChecked();
                MMGammaBuyLimit = p.MMGammaBuyLimit.ToDecimalChecked();
                MMGammaSellLimit = p.MMGammaSellLimit.ToDecimalChecked();
                GammaBuyTarget = p.GammaBuyTarget.ToDecimalChecked();
                GammaSellTarget = p.GammaSellTarget.ToDecimalChecked();
                GammaPortfolio = p.GammaPortfolio.ToDecimalChecked();

                VannaPortfolio = p.VannaPortfolio.ToDecimalChecked();
                VannaLongLimit = p.VannaLongLimit.ToDecimalChecked();
                VannaShortLimit = p.VannaShortLimit.ToDecimalChecked();

                VommaPortfolio = p.VommaPortfolio.ToDecimalChecked();
                VommaLongLimit = p.VommaLongLimit.ToDecimalChecked();
                VommaShortLimit = p.VommaShortLimit.ToDecimalChecked();

                ThetaPortfolio = p.ThetaPortfolio.ToDecimalChecked();
            }

            base.OnUpdateData();
        }

        FutureTradingModule EnsureTradingModule() {
            if(_tradingModule != null)
                return _tradingModule;

            lock(this) {
                return _tradingModule ?? (_tradingModule = new FutureTradingModule(this));
            }
        }

        protected override void OnResetData() {
            base.OnResetData();

            VegaBuyLimit = VegaSellLimit = VegaBuyTarget = VegaSellTarget = 0m;
            GammaBuyLimit = GammaSellLimit = GammaBuyTarget = GammaSellTarget = 0m;
            CalculationBid = CalculationAsk = 0m;
            CalculationTime = null;
        }

        // данный метод выполняется в потоке коннектора. необходимо возвращать максимально быстро
        void ConnectorOnMarketDepthChanged(Connector connector, MarketDepth depth) {
            if(depth.Security.Id != Id)
                return;

            _quotesLogger.LogQuotes(depth);
        }

        public void ForceDeactivateFuture() {
            _disablingFuture = true;
            RobotData.Dispatcher.MyGuiAsync(() => {
                try {
                    if(!IsFutureActive2) {
                        _log.Dbg.AddWarningLog($"ForceDeactivateFuture({Id}): already inactive");
                        return;
                    }

                    _log.AddWarningLog($"ForceDeactivateFuture({Id}): фьючерс будет деактивирован");

                    ConfigProvider.ConfigSecuritySelection.UndoUIChanges();
                    IsFutureActive = false;
                } finally {
                    _disablingFuture = false;
                }
            });
        }

        public void ForceRecalcATM() {
            _optionsSeriesList.ForEach(s => s.ForceRecalcATM());
        }

        public OptionSeriesInfo GetOptionSeries(OptionInfo option) {
            if(option.Future != this) throw new InvalidOperationException("unexpected option in GetOptionSeries()");

            var seriesId = OptionSeriesId.Create(option.NativeSecurity);

            var series = RobotData.AllOptionSeries.FirstOrDefault(os => os.SeriesId == seriesId);

            if(series == null) {
                series = Util.CreateInitializable(() => new OptionSeriesInfo(Controller, this, seriesId));
                RobotData.TryAddOptionSeries(series);

                if(_optionsSeriesList.FirstOrDefault(os => os.SeriesId == seriesId) != null)
                    _log.Dbg.AddErrorLog("Option series not found in RobotData, but found in future: {0}", seriesId);
                else
                    _optionsSeriesList.Add(series);
            } else {
                if(_optionsSeriesList.FirstOrDefault(os => os.SeriesId == seriesId) == null) {
                    _log.Dbg.AddErrorLog("Option series found in RobotData, but not found in future: {0}", seriesId);
                    _optionsSeriesList.Add(series);
                }
            }

            if(!_derivedOptions.Contains(option))
                _derivedOptions.Add(option);

            _calculator.UpdateOptionsInfo();

            return series;
        }

        #region params

        public FutureParams Params {get; set;}

        public interface IFutureParams {
            double VegaCallBuyLimit {get;}
            double VegaPutBuyLimit {get;}
            double VegaCallSellLimit {get;}
            double VegaPutSellLimit {get;}

            double VegaBuyLimit {get;}
            double VegaSellLimit {get;}
            double MMVegaBuyLimit {get;}
            double MMVegaSellLimit {get;}
            double VegaBuyTarget {get;}
            double VegaSellTarget {get;}
            double VegaPortfolio {get;}

            double GammaBuyLimit {get;}
            double GammaSellLimit {get;}
            double MMGammaBuyLimit {get;}
            double MMGammaSellLimit {get;}
            double GammaBuyTarget {get;}
            double GammaSellTarget {get;}
            double GammaPortfolio {get;}

            double VannaPortfolio {get;}
            double VannaLongLimit {get;}
            double VannaShortLimit {get;}

            double VommaPortfolio {get;}
            double VommaLongLimit {get;}
            double VommaShortLimit {get;}

            double ThetaPortfolio {get;}
        }

        public class FutureParams : IFutureParams {
            public double VegaCallBuyLimit {get; set;}
            public double VegaPutBuyLimit {get; set;}
            public double VegaCallSellLimit {get; set;}
            public double VegaPutSellLimit {get; set;}

            public double VegaBuyLimit {get; set;}
            public double VegaSellLimit {get; set;}
            public double MMVegaBuyLimit {get; set;}
            public double MMVegaSellLimit {get; set;}
            public double VegaBuyTarget {get; set;}
            public double VegaSellTarget {get; set;}
            public double VegaPortfolio {get; set;}

            public double GammaBuyLimit {get; set;}
            public double GammaSellLimit {get; set;}
            public double MMGammaBuyLimit {get; set;}
            public double MMGammaSellLimit {get; set;}
            public double GammaBuyTarget {get; set;}
            public double GammaSellTarget {get; set;}
            public double GammaPortfolio {get; set;}

            public double VannaPortfolio {get; set;}
            public double VannaLongLimit {get; set;}
            public double VannaShortLimit {get; set;}

            public double VommaPortfolio {get; set;}
            public double VommaLongLimit {get; set;}
            public double VommaShortLimit {get; set;}

            public double ThetaPortfolio {get; set;}
        }

        #endregion
    }

    public sealed class OptionSeriesInfo : ConnectorNotifiableObject, IRobotDataUpdater {
        readonly FuturesInfo _future;
        readonly OptionSeriesId _seriesId;
        readonly RobotLogger.AtmStrikesLogger _atmStrikesLogger;
        readonly ICfgPairSeries _config;
        readonly CalculatedSeriesConfig _calcConfigUI;
        CalculatedSeriesConfig _calcConfig, _displayedCalcConfig;
        decimal _minStrikeStep;
        int _seriesVolume, _seriesMmVolDiffSum, _seriesOwnVolume, _seriesOwnMmVolume, _seriesOwnMMVolumeActive;

        readonly ObservableCollection<OptionInfo> _options = new ObservableCollection<OptionInfo>();
        readonly ObservableCollection<OptionStrikeInfo> _allStrikes = new ObservableCollection<OptionStrikeInfo>();
        ActiveStrikesState _activeCalls;
        ActiveStrikesState _activePuts;

        public OptionSeriesId SeriesId => _seriesId;
        public FuturesInfo Future => _future;
        public DateTime ExpirationDate => _seriesId.ExpirationDate;
        public decimal MinStrikeStep {get {return _minStrikeStep;} set {SetField(ref _minStrikeStep, value);}}
        public int SeriesVolume {get {return _seriesVolume;} set {SetField(ref _seriesVolume, value);}}
        public int SeriesMMVolDiffSum {get {return _seriesMmVolDiffSum;} set {SetField(ref _seriesMmVolDiffSum, value);}}
        public int SeriesOwnVolume {get {return _seriesOwnVolume;} set {SetField(ref _seriesOwnVolume, value);}}
        public int SeriesOwnMMVolume {get {return _seriesOwnMmVolume;} set {SetField(ref _seriesOwnMmVolume, value);}}
        public int SeriesOwnMMVolumeActive {get {return _seriesOwnMMVolumeActive;} set {SetField(ref _seriesOwnMMVolumeActive, value);}}
        public double DaysTillOptionsExpiration => OptionModel.GetDaysTillExpiration(ExpirationDate + ExchangeBoard.Forts.ExpiryTime, Controller.Connector.GetMarketTime());
        public IEnumerable<OptionInfo> Options => _options;
        public IEnumerable<OptionStrikeInfo> AllStrikes => _allStrikes;

        public OptionInfo[] OptionsSafe {get {
            lock(_options)
                return _options.OrderBy(o => o.Strike.Strike).ToArray();
        }}

        public ICfgPairSeries Config    => _config;
        public IConfigSeries CfgSeries  => _config.Effective;
        public ICalculatedSeriesConfig CalcConfig => _calcConfig;
        public ICalculatedSeriesConfig CalcConfigUI => _calcConfigUI;

        public DateTime LastOptionAddTime {get; private set;}

        #region trading availability

        ConfigSecuritySelection CfgSelectionUI {get {return ConfigProvider.ConfigSecuritySelection.UI;}}
        IConfigSecuritySelection CfgSelection {get {return ConfigProvider.ConfigSecuritySelection.Effective;}}

        public bool IsSelectedForTrading {get {return CfgSelection.IsSelectedForTrading(this); }}
        public bool IsSeriesActive {get { return CfgSelection.IsActive(this); } set { CfgSelectionUI.SetActive(this, value);}}
        public bool IsMMReportEnabled {get { return CfgSelection.IsMMReportEnabled(this); } set { CfgSelectionUI.SetMMReports(this, value);}}

        #endregion

        public OptionStrikeInfo AtmCall {get {return _activeCalls[0];}}
        public OptionStrikeInfo AtmPut {get {return _activePuts[0];}}

        public Range<decimal> AtmCallRange {get {return _currentAtmRangeCall;}}
        public Range<decimal> AtmPutRange {get {return _currentAtmRangePut;}}

        public static event Action<OptionSeriesInfo> NewSeriesInfo;
        public static event Action<OptionSeriesInfo, OptionStrikeInfo, OptionStrikeInfo> AnySeriesAtmStrikeChanged;
        public static event Action<OptionSeriesInfo> SeriesConfigChanged;
        public event Action<OptionSeriesInfo> AtmStrikeChanged;

        public OptionSeriesInfo(Controller ctl, FuturesInfo future, OptionSeriesId serId) : base(ctl) {
            _future = future;
            _seriesId = serId;
            _config = ConfigProvider.GetSeriesConfig(serId);
            _calcConfig = new CalculatedSeriesConfig(this, false);
            _calcConfigUI = new CalculatedSeriesConfig(this, true);

            _activeCalls = new ActiveStrikesState(this, null, OptionTypes.Call, -1);
            _activePuts = new ActiveStrikesState(this, null, OptionTypes.Put, -1);
            _atmStrikesLogger = RobotLogger.AtmStrikes;

            Activate();
            _log.Dbg.AddDebugLog("new series: {0}", SeriesId);

            ConfigProvider.ConfigSecuritySelection.EffectiveConfigChanged += (cfg, names) => {
                _needToCheckActiveStrikes = true;
                RaisePropertyChanged(() => IsSeriesActive);
                RaisePropertyChanged(() => IsSelectedForTrading);
                RaisePropertyChanged(() => IsMMReportEnabled);
            };

            Future.Config.EffectiveConfigChanged += (pair, names) => {
                if(names.Contains(Util.PropertyName(() => Future.CfgFuture.AtmStrikeDelay)) || 
                   names.Contains(Util.PropertyName(() => Future.CfgFuture.LiquidStrikeStep)) ||
                   names.Contains(Util.PropertyName(() => Future.CfgFuture.LiquidSwitchFraction)))

                    ForceRecalcATM();
            };

            Config.EffectiveConfigChanged += (series, strings) => {
                SeriesConfigChanged?.Invoke(this);
            };

            RobotData.RegisterUpdater(this);

            Controller.Scheduler.PeriodChanged += (scheduler, oldMarketPeriod, oldRobotPeriod) => {
                if(!oldMarketPeriod.IsMarketOpen() && scheduler.MarketPeriod.IsMarketOpen())
                    ForceRecalcATM();
            };
        }

        public override void Init() {
            base.Init();

            NewSeriesInfo?.Invoke(this);
        }

        static readonly TimeSpan _volumesUpdatePeriod = TimeSpan.FromSeconds(1);
        static readonly TimeSpan _curveUpdatePeriod = TimeSpan.FromSeconds(0.5d);
        DateTime _lastVolumeUpdateTime, _lastCurveUpdateTime;

        public void UpdateData() {
            var now = SteadyClock.Now;

            if(now - _lastVolumeUpdateTime > _volumesUpdatePeriod) {
                _lastVolumeUpdateTime = now;
                SeriesVolume = Options.Sum(o => o.Volume.ToInt32Checked());
                SeriesMMVolDiffSum = Options.Where(o => o.IsMMOption).Sum(o => o.VolumeDiff);
                SeriesOwnVolume = RobotData.GetOwnSessionVolumeByOptionSeries(this);
                SeriesOwnMMVolume = Options.Sum(o => o.OwnMMVolume);
                SeriesOwnMMVolumeActive = Options.Sum(o => o.OwnMMVolumeActive);
            }

            if(now - _lastAtmCheckTime < _atmCheckPeriod && !_needToCheckActiveStrikes && !_needToUpdateRange)
                return;

            var oldAtmCall = AtmCall;
            var oldAtmPut = AtmPut;

            var callAtmUpdated = TryUpdateAtmStrike(ref _activeCalls, ref _currentAtmRangeCall);
            var putAtmUpdated = TryUpdateAtmStrike(ref _activePuts, ref _currentAtmRangePut);

            _lastAtmCheckTime = now;
            _needToCheckActiveStrikes = _needToUpdateRange = false;

            if(callAtmUpdated || putAtmUpdated) {
                RaiseAtmStrikeChanged(oldAtmCall, oldAtmPut);
                _atmStrikesLogger.LogSeries(this);
            }

            if(!ReferenceEquals(_displayedCalcConfig, _calcConfig) && (now - _lastCurveUpdateTime > _curveUpdatePeriod)) {
                _lastCurveUpdateTime = now;
                _displayedCalcConfig = _calcConfig;
                _calcConfigUI.CopyFrom(_displayedCalcConfig);
            }

            OnPropertyChanged(() => DaysTillOptionsExpiration);
        }

        public void UpdateCalcConfig(CalculatedSeriesConfig cfg) {
            _calcConfig = cfg;
        }

        #region ATM/active strikes

        bool _needToUpdateRange, _needToCheckActiveStrikes;
        DateTime _lastAtmCheckTime;
        static readonly TimeSpan _atmCheckPeriod = TimeSpan.FromSeconds(2);
        Range<decimal> _currentAtmRangeCall, _currentAtmRangePut;

        public OptionStrikeInfo StrikeByShift(OptionTypes otype, int strikeShift) {
            return otype == OptionTypes.Call ? _activeCalls[strikeShift] : _activePuts[strikeShift];
        }

        public object GetActiveStrikesState(OptionTypes otype) {
            return otype == OptionTypes.Call ? _activeCalls : _activePuts;
        }

        public OptionStrikeShift GetStrikeShift(OptionStrikeInfo strike, OptionTypes otype, object fixedState = null) {
            if(strike.Series != this) throw new InvalidOperationException("wrong series");

            return fixedState == null ?
                (otype == OptionTypes.Call ? _activeCalls[strike] : _activePuts[strike]) :
                ((ActiveStrikesState)fixedState)[strike];
        }

        bool TryUpdateAtmStrike(ref ActiveStrikesState curState, ref Range<decimal> curRange) {
            var createNewState = false;
            var activeStrikesArr = curState.OrderedActiveStrikes;
            var cfgFuture = Future.CfgFuture;

            if(_needToCheckActiveStrikes) {
                var newStrikes = _allStrikes.Where(s => s.IsStrikeCalculated).OrderBy(s => s.Strike).ToArray();
                if(!curState.SameStrikes(newStrikes)) {
                    createNewState = true;
                    activeStrikesArr = newStrikes;
                }
            }

            var oldAtmIndex = curState.AtmStrikeIndex;
            var newAtmIndex = oldAtmIndex;
            var bid = Future.BestBid;
            var ask = Future.BestAsk;
            var switchFraction = cfgFuture.LiquidSwitchFraction;
            var liquidStep = cfgFuture.LiquidStrikeStep;

            if(activeStrikesArr.Length == 0 || liquidStep < 1 || switchFraction < 0 || switchFraction > 1) {
                newAtmIndex = -1;
                curRange = null;
            } else if(bid == null || ask == null) {
                if(oldAtmIndex >= 0) {
                    newAtmIndex = -1;
                    curRange = null;
                    createNewState = true;
                }
            } else {
                var updated = false;
                var price = (bid.Price + ask.Price) / 2;

                if(createNewState || curRange == null || _needToUpdateRange) {
                    createNewState = true;
                    curRange = GetCurAtmRange(activeStrikesArr, curState.OptionType, liquidStep, switchFraction, price, out newAtmIndex);
                    updated = true;
                    _log.Dbg.AddInfoLog("{0}-{1}: new ATM range: {2} - {3}", SeriesId.Id, curState.OptionType, curRange.Min, curRange.Max);
                }

                if(price < curRange.Min || price > curRange.Max) {
                    if(!updated) {
                        curRange = GetCurAtmRange(activeStrikesArr, curState.OptionType, liquidStep, switchFraction, price, out newAtmIndex);
                        _log.Dbg.AddInfoLog("{0}-{1}: new ATM range2: {2} - {3}", SeriesId.Id, curState.OptionType, curRange.Min, curRange.Max);
                    }

                    if(price < curRange.Min || price > curRange.Max) {
                        _log.Dbg.AddErrorLog("{0}-{1}: Ошибка расчета ATM диапазона. После расчета цена не попала в диапазон.", SeriesId.Id, curState.OptionType);
                        createNewState = true;
                        newAtmIndex = -1;
                    }
                }
            }

            if(!createNewState && oldAtmIndex == newAtmIndex)
                return false;

            curState = new ActiveStrikesState(this, activeStrikesArr, curState.OptionType, newAtmIndex);
            return true;
        }

        void RaiseAtmStrikeChanged(OptionStrikeInfo oldAtmCall, OptionStrikeInfo oldAtmPut) {
            OnPropertyChanged(() => AtmCall);
            OnPropertyChanged(() => AtmPut);
            AtmStrikeChanged?.Invoke(this);
            AnySeriesAtmStrikeChanged?.Invoke(this, oldAtmCall, oldAtmPut);
        }

        class ActiveStrikesState {
            public readonly int AtmStrikeIndex;
            public readonly OptionTypes OptionType;
            public readonly OptionStrikeInfo[] OrderedActiveStrikes;
            readonly Dictionary<decimal, OptionStrikeShift> _shifts = new Dictionary<decimal, OptionStrikeShift>();

            public ActiveStrikesState(OptionSeriesInfo series, OptionStrikeInfo[] orderedStrikes, OptionTypes otype, int atmIndex) {
                OptionType = otype;

                if(orderedStrikes == null || orderedStrikes.Length == 0) {
                    AtmStrikeIndex = -1;
                    OrderedActiveStrikes = new OptionStrikeInfo[0];
                } else {
                    if(atmIndex != -1 && (atmIndex < 0 || atmIndex >= orderedStrikes.Length))
                        throw new InvalidOperationException("wrong atm index {0}, strikes={1}".Put(atmIndex, string.Join(",", orderedStrikes.Select(s => s.Strike))));

                    OrderedActiveStrikes = orderedStrikes;
                    AtmStrikeIndex = atmIndex;
                }

                if(AtmStrikeIndex >= 0) {
                    for(var i=0; i < OrderedActiveStrikes.Length; ++i)
                        _shifts[OrderedActiveStrikes[i].Strike] = OptionStrikeShift.GetStrikeShift(series.Controller, series.SeriesId, OptionType, i - AtmStrikeIndex);
                }
            }

            public OptionStrikeInfo this[int shift] {get {
                if(AtmStrikeIndex < 0)
                    return null;

                var index = AtmStrikeIndex + shift;
                if(index < 0 || index >= OrderedActiveStrikes.Length)
                    return null;

                return OrderedActiveStrikes[index];
            }}

            public OptionStrikeShift this[OptionStrikeInfo strike] { get {
                if(AtmStrikeIndex < 0 || _shifts.Count == 0)
                    return null;

                OptionStrikeShift shift;
                return _shifts.TryGetValue(strike.Strike, out shift) ? shift : null;
            }}

            public bool SameStrikes(IEnumerable<OptionStrikeInfo> newStrikes) {
                var currentHash = OrderedActiveStrikes.Select(s => s.Strike).ToHashSet();
                var newHash = newStrikes.Select(s => s.Strike).ToHashSet();

                return currentHash.SetEquals(newHash);
            }
        }

        Range<decimal> GetCurAtmRange(OptionStrikeInfo[] strikes, OptionTypes otype, decimal liquidStep, decimal switchFraction, decimal price, out int atmIndex) {
            if(strikes.Length == 0) {
                _log.Dbg.AddErrorLog("GetCurAtmRange called with empty strikes");
                atmIndex = -1;
                return new Range<decimal>(0, 0);
            }

            var liquidStrikes = strikes.Where(s => s.IsLiquid(liquidStep)).ToArray();
            if(liquidStrikes.Length == 0) {
                _log.Dbg.AddErrorLog("no liquid strikes (step={0})", liquidStep);
                atmIndex = -1;
                return new Range<decimal>(0, 0);
            }

            var lastLiquidIndex = liquidStrikes.Length - 1;
            var stepSize = Future.MinStepSize;
            var cfgDelay = Future.CfgFuture.AtmStrikeDelay;
            if(cfgDelay < 3) cfgDelay = 3;
            var delay = Math.Abs(cfgDelay) * stepSize;
            var fraction = otype == OptionTypes.Call ? switchFraction : (1 - switchFraction);

            if(price <= liquidStrikes[0].Strike) {
                atmIndex = Array.IndexOf(strikes, liquidStrikes[0]);
                if(liquidStrikes.Length == 1) {
                    return new Range<decimal>(0, decimal.MaxValue);
                }

                var diff = liquidStrikes[1].Strike - liquidStrikes[0].Strike;
                var endPoint = (liquidStrikes[0].Strike + diff * fraction).RoundStep(stepSize);

                return new Range<decimal>(0, endPoint + delay);
            }

            if(price >= liquidStrikes[lastLiquidIndex].Strike) {
                atmIndex = Array.IndexOf(strikes, liquidStrikes[lastLiquidIndex]);
                if(liquidStrikes.Length == 1)
                    return new Range<decimal>(0, decimal.MaxValue);

                var diff = liquidStrikes[lastLiquidIndex].Strike - liquidStrikes[lastLiquidIndex - 1].Strike;
                var startPoint = (liquidStrikes[lastLiquidIndex].Strike - diff * (1 - fraction)).RoundStep(stepSize);

                return new Range<decimal>(startPoint - delay, decimal.MaxValue);
            }

            for(var i = 1; i < liquidStrikes.Length; ++i) {
                var strike = liquidStrikes[i];
                if(price > strike.Strike)
                    continue;

                var prevStrike = liquidStrikes[i - 1];

                var diff = strike.Strike - prevStrike.Strike;
                var point = (prevStrike.Strike + diff * fraction).RoundStep(stepSize);

                var liquidIndex = price <= point ? i - 1 : i;
                var atmStrike = liquidStrikes[liquidIndex];
                atmIndex = Array.IndexOf(strikes, atmStrike);

                if(liquidIndex == 0)
                    return new Range<decimal>(0, (prevStrike.Strike + diff * fraction + delay).RoundStep(stepSize));

                if(liquidIndex == lastLiquidIndex)
                    return new Range<decimal>((strike.Strike - diff * (1 - fraction) - delay).RoundStep(stepSize), decimal.MaxValue);

                var from = (atmStrike.Strike - diff * (1 - fraction) - delay).RoundStep(stepSize);
                var to = (atmStrike.Strike + diff * fraction + delay).RoundStep(stepSize);

                return new Range<decimal>(from, to);
            }

            _log.AddErrorLog("Ошибка определения ATM страйка. Цена={0}, liqStep={1}, switchFraction={2}, страйки=({3})", price, liquidStep, switchFraction, string.Join(",", liquidStrikes.Select(s => s.Strike)));
            atmIndex = -1;
            return new Range<decimal>(0, decimal.MaxValue);
        }

        #endregion

        public OptionStrikeInfo GetOptionStrike(OptionInfo option) {
            if(option.Series != this) throw new InvalidOperationException("Unexpected option series in GetOptionStrike(). expected {0}, got {1}".Put(this, option.Series));

            // ReSharper disable once InconsistentlySynchronizedField
            if(_options.Contains(option))
                return _allStrikes.First(s => s.Strike == option.NativeSecurity.Strike);

            _needToCheckActiveStrikes = true;
            lock(_options)
                _options.Add(option);
            LastOptionAddTime = SteadyClock.Now;

            // calculate min strike step
            // ReSharper disable once InconsistentlySynchronizedField
            var calls = _options.Where(c => c.OptionType == OptionTypes.Call).ToArray();
            if(calls.Length > 1) {
                var minDiff = MinStrikeStep;
                foreach(var call in calls) {
                    var diff = Math.Abs(call.NativeSecurity.Strike - option.NativeSecurity.Strike);
                    if(diff > 0 && (diff < minDiff || minDiff <= 0))
                        minDiff = diff;
                }

                if(minDiff > 0 && (MinStrikeStep <= 0 || minDiff < MinStrikeStep)) {
                    MinStrikeStep = minDiff;
                    _log.Dbg.AddDebugLog("{0}: updating min strike step to {1}", SeriesId, MinStrikeStep);
                }
            }

            var strike = _allStrikes.FirstOrDefault(s => s.Strike == option.NativeSecurity.Strike);
            if(strike == null)
                _allStrikes.Add(strike = new OptionStrikeInfo(Controller, option));
            else
                strike.TryAddOption(option);

            return strike;
        }

        public void ForceDeactivateSeries() {
            if(!IsSeriesActive) {
                _log.Dbg.AddWarningLog("ForceDeactivateSeries: already inactive");
                return;
            }

            _log.AddWarningLog("{0}: серия будет деактивирована", SeriesId.Id);

            RobotData.Dispatcher.MyGuiAsync(() => {
                ConfigProvider.ConfigSecuritySelection.UndoUIChanges();
                IsSeriesActive = false;
            });
        }

        public void ForceRecalcATM() {
            RobotData.Dispatcher.MyGuiAsync(() => {
                _needToUpdateRange = true;
            });
        }
    }

    public sealed class OptionStrikeInfo : ConnectorNotifiableObject {
        readonly decimal _strike;
        readonly OptionSeriesInfo _series;
        readonly FuturesInfo _future;
        string _strikeId;
        OptionInfo _call, _put;

        public FuturesInfo Future {get {return _future;}}
        public decimal Strike {get {return _strike;}}

        public OptionInfo Call {get {return _call;} set {SetField(ref _call, value);}}
        public OptionInfo Put {get {return _put;} set {SetField(ref _put, value);}}

        public string StrikeId {get {return _strikeId ?? (_strikeId = SeriesId.Id + "-{0}".Put(Strike));}}
        public OptionSeriesId SeriesId {get {return _series.SeriesId;}}
        public OptionSeriesInfo Series {get {return _series;}}

        public OptionStrikeShift AtmShiftByState(OptionTypes otype, object state) {return Series.GetStrikeShift(this, otype, state);}

        #region trading availability

        ConfigSecuritySelection CfgSelectionUI {get {return ConfigProvider.ConfigSecuritySelection.UI;}}
        IConfigSecuritySelection CfgSelection {get {return ConfigProvider.ConfigSecuritySelection.Effective;}}

        public bool IsStrikeCalculated {get { return CfgSelection.IsStrikeCalculated(this); } set { CfgSelectionUI.SetStrikeCalculation(this, value); }}

        #endregion

        public OptionStrikeInfo(Controller ctl, OptionInfo option) : base(ctl) {
            if(option.NativeSecurity.Strike == 0) throw new InvalidOperationException("zero option strike");

            _strike = option.NativeSecurity.Strike;
            if(option.OptionType == OptionTypes.Call)
                Call = option;
            else
                Put = option;

            _series = option.Series;
            _future = option.Future;

            ConfigProvider.ConfigSecuritySelection.EffectiveConfigChanged += 
                (cfg, names) => RaisePropertyChanged(() => IsStrikeCalculated);

            Activate();

            _log.Dbg.AddDebugLog("new strike: {0} ({1})", SeriesId, Strike);
        }

        public void ForceTurnOffCalculation() {
            if(!IsStrikeCalculated) {
                _log.Dbg.AddWarningLog("ForceTurnOffCalculation: already off");
                return;
            }

            _log.AddWarningLog("{0}: расчет страйка будет отключен", StrikeId);

            RobotData.Dispatcher.MyGuiAsync(() => {
                ConfigProvider.ConfigSecuritySelection.UndoUIChanges();
                CfgSelectionUI.SetStrikeCalculation(this, false);
            });
        }

        public void TryAddOption(OptionInfo option) {
            if(option.NativeSecurity.Strike != _strike) throw new InvalidOperationException("wrong option strike {0} != {1}".Put(option.NativeSecurity.Strike, _strike));

            if(option.OptionType == OptionTypes.Call) {
                if(Call != null && !object.ReferenceEquals(option, Call))
                    _log.Dbg.AddErrorLog("Strike.TryAddOption(call): replacing option with another instance: {0}:{1} => {2}:{3}", Call.Code, Call.NativeSecurity.Strike, option.Code, option.NativeSecurity.Strike);

                Call = option;
            } else {
                if(Put != null && !object.ReferenceEquals(option, Put))
                    _log.Dbg.AddErrorLog("Strike.TryAddOption(put): replacing option with another instance: {0}:{1} => {2}:{3}", Put.Code, Put.NativeSecurity.Strike, option.Code, option.NativeSecurity.Strike);

                Put = option;
            }
        }

        public bool IsLiquid(decimal liquidStrikeStep) {
            if(liquidStrikeStep < 1) return true;

            return Strike % liquidStrikeStep == 0;
        }
    }

    /// <summary>Информация об опционе.</summary>
    public sealed class OptionInfo : SecurityInfo {
        #region fields/properties

        readonly Dictionary<int, SecurityMMInfo> _mmInfos = new Dictionary<int, SecurityMMInfo>();
        readonly string[] _dependentIds;
        readonly OptionSeriesInfo _series;
        readonly OptionStrikeInfo _strike;
        readonly FuturesInfo _future;
        double _ivBid,_marketIvBid;
        double _ivOffer,_marketIvOffer;
        double _ivSpread, _marketIvAverage;
        double _deltaBid;
        double _vega, _gamma, _theta, _vanna, _vomma;
        double _marketSpread, _currentSpread, _targetSpread;
        StrikeMoneyness? _strikeMoneyness;
        GreeksRegime? _greeksRegime;
        double _moneyness, _initialDelta, _bestDeltaExpectation;
        double _illiquidDelta, _illiquidIv, _illiquidVega, _illiquidGamma, _illiquidTheta, _illiquidVanna, _illiquidVomma;
        double _marketBid, _marketOffer, _marketAverage;
        double _valuationTargetSpread, _valuationSpread;
        double _wideningMktIv, _narrowingMktIv;
        double _calculationDelta, _calculationVega, _calculationGamma, _calculationTheta, _calculationVanna, _calculationVomma;
        double _curveIvBid, _curveIvOffer, _curveIvSpread, _curveIvAverage, _curveDelta, _curveVega, _curveGamma, _curveTheta, _curveVanna, _curveVomma;
        double _curveBid, _curveOffer, _curveSpread, _curveAverage;
        double _marketSpreadResetLimit;
        int _glassBidVolume, _glassOfferVolume;
        int _ownMMVolume, _ownMMVolumeActive, _volumeDiff;
        ValuationStatus _valuationStatus;
        DateTime? _timeValuationStatus;
        string _aggressiveResetTime, _conservativeResetTime, _quoteVolBidResetTime, _quoteVolOfferResetTime;

        int _mmVegaVolLimitBuy, _mmVegaVolLimitSell, _mmGammaVolLimitBuy, _mmGammaVolLimitSell;

        OptionTradingModule _tradingModule;
        RobotLogger.OptionLogger _logger;

        bool _needToLog;

        ConfigSecuritySelection CfgSelectionUI => ConfigProvider.ConfigSecuritySelection.UI;
        IConfigSecuritySelection CfgSelection => ConfigProvider.ConfigSecuritySelection.Effective;
        public ICfgPairVP CfgValuationParamsPair { get {return AtmShift.With(s => s.VP);}}
        public IConfigValuationParameters CfgValuationParams { get {return AtmShift.With(s => s.VP.With(pair => pair.Effective));}}
        public IConfigValuationParameters CfgValuationParamsByState(object state) {return Strike.AtmShiftByState(OptionType, state).With(s => s.VP).With(pair => pair.Effective);}

        public override bool IsOnline => base.IsOnline && Future.IsOnline;

        // Был ли выбран данный опцион для возможной торговли (включает в себя IsOptionActive для инструмента/серии/фьючерса и включении страйка в расчет ATM)
        // Модель расчитывается для опциона, если этот флаг==true
        public bool IsSelectedForTrading => CfgSelection.IsSelectedForTrading(this);

        /// <summary>Индивидуальное включение/выключение инструмента юзером.</summary>
        public bool IsOptionActive {get { return CfgSelection.IsActive(this); } set { CfgSelectionUI.SetActive(this, value);}}

        // доступность valuation_parameters для данного инструмента в данный момент времени
        public bool IsVpAvailable {get {return CfgValuationParams.Return(cfg => cfg.IsActive, false);}}
        // можно ли с учетом вышеперечисленных параметров стартовать стратегии по инструменту
        public bool CanStartStrategies => IsSelectedForTrading && IsVpAvailable && Future.TradingModule.IsDeltaHedged;

        public OptionStrikeInfo Strike => _strike;
        public OptionSeriesInfo Series => _series;
        public FuturesInfo Future => _future;

        public OptionStrikeShift AtmShift => Strike.Series.GetStrikeShift(Strike, OptionType);

        public OptionModel Model {get;}

        public OptionTypes OptionType => NativeSecurity.OptionType.Value;

        public OptionInfo Opposite => OptionType == OptionTypes.Call ? Strike.Put : Strike.Call;
        public OptionInfo Call => OptionType == OptionTypes.Call ? this : Strike.Put;
        public OptionInfo Put => OptionType == OptionTypes.Put ? this : Strike.Call;

        public IMMInfoRecord MMRecord {get; private set;}
        public bool IsMMOption => MMRecord != null;

        // Свойства опциона ниже используются для отображения в пользовательском интерфейсе
        // Робот использует информацию непосредственно из OptionModel
        public double IvBid {get {return _ivBid; } set {SetField(ref _ivBid, value); }}
        public double IvOffer {get {return _ivOffer; } set {SetField(ref _ivOffer, value); }}
        public double IvSpread {get {return _ivSpread; } set {SetField(ref _ivSpread, value); }}
        public double IvAverage => (_ivBid + _ivOffer) / 2;
        public double MarketIvBid {get {return _marketIvBid; } set {SetField(ref _marketIvBid, value); }}
        public double MarketIvOffer {get {return _marketIvOffer; } set {SetField(ref _marketIvOffer, value); }}
        public double MarketIvSpread => _marketIvOffer - _marketIvBid;
        public double MarketIvAverage {get {return _marketIvAverage; } set {SetField(ref _marketIvAverage, value); }}
        public double EmpiricDelta {get; set;} // set directly from calculator from another thread
        public double DeltaBid {get {return _deltaBid; } set {SetField(ref _deltaBid, value); }}
        public double Vega {get {return _vega; } set {SetField(ref _vega, value); }}
        public double Gamma {get {return _gamma; } set {SetField(ref _gamma, value); }}
        public double Theta {get {return _theta; } set {SetField(ref _theta, value); }}
        public double Vanna {get {return _vanna; } set {SetField(ref _vanna, value); }}
        public double Vomma {get {return _vomma; } set {SetField(ref _vomma, value); }}
        public double MarketSpread {get {return _marketSpread; } set {SetField(ref _marketSpread, value); }}
        public double CurrentSpread {get {return _currentSpread; } set {SetField(ref _currentSpread, value); }}
        public double TargetSpread {get {return _targetSpread; } set {SetField(ref _targetSpread, value); }}
        public double MarketBid {get {return _marketBid;} set{SetField(ref _marketBid, value);}}
        public double MarketOffer {get {return _marketOffer;} set{SetField(ref _marketOffer, value);}}
        public double MarketAverage {get {return _marketAverage;} set{SetField(ref _marketAverage, value);}}
        public double ValuationTargetSpread {get {return _valuationTargetSpread;} set{SetField(ref _valuationTargetSpread, value);}}
        public double ValuationSpread {get {return _valuationSpread;} set{SetField(ref _valuationSpread, value);}}
        public double WideningMktIv {get {return _wideningMktIv;} set{SetField(ref _wideningMktIv, value);}}
        public double NarrowingMktIv {get {return _narrowingMktIv;} set{SetField(ref _narrowingMktIv, value);}}
        public double CalculationDelta {get {return _calculationDelta;} set{SetField(ref _calculationDelta, value);}}
        public double CalculationVega {get {return _calculationVega;} set{SetField(ref _calculationVega, value);}}
        public double CalculationGamma {get {return _calculationGamma;} set{SetField(ref _calculationGamma, value);}}
        public double CalculationTheta {get {return _calculationTheta;} set{SetField(ref _calculationTheta, value);}}
        public double CalculationVanna {get {return _calculationVanna;} set{SetField(ref _calculationVanna, value); }}
        public double CalculationVomma {get {return _calculationVomma;} set{SetField(ref _calculationVomma, value); }}

        public StrikeMoneyness? StrikeMoneyness {get {return _strikeMoneyness;} set{SetField(ref _strikeMoneyness, value);}}
        public GreeksRegime? GreeksRegime {get {return _greeksRegime;} set{SetField(ref _greeksRegime, value);}}
        public double Moneyness {get {return _moneyness;} set{SetField(ref _moneyness, value);}}
        public double InitialDelta {get {return _initialDelta;} set{SetField(ref _initialDelta, value);}}
        public double BestDeltaExpectation {get {return _bestDeltaExpectation;} set{SetField(ref _bestDeltaExpectation, value);}}
        public double IlliquidDelta {get {return _illiquidDelta;} set{SetField(ref _illiquidDelta, value);}}
        public double IlliquidIv {get {return _illiquidIv;} set{SetField(ref _illiquidIv, value);}}
        public double IlliquidVega {get {return _illiquidVega;} set{SetField(ref _illiquidVega, value);}}
        public double IlliquidGamma {get {return _illiquidGamma;} set{SetField(ref _illiquidGamma, value);}}
        public double IlliquidTheta {get {return _illiquidTheta;} set{SetField(ref _illiquidTheta, value);}}
        public double IlliquidVanna {get {return _illiquidVanna;} set{SetField(ref _illiquidVanna, value); }}
        public double IlliquidVomma {get {return _illiquidVomma;} set{SetField(ref _illiquidVomma, value); }}

        public double CurveBid {get {return _curveBid;} set{SetField(ref _curveBid, value); }}
        public double CurveOffer {get {return _curveOffer;} set{SetField(ref _curveOffer, value); }}
        public double CurveSpread {get {return _curveSpread;} set{SetField(ref _curveSpread, value); }}
        public double CurveAverage {get {return _curveAverage;} set{SetField(ref _curveAverage, value); }}
        public double CurveIvBid {get {return _curveIvBid;} set{SetField(ref _curveIvBid, value); }}
        public double CurveIvOffer {get {return _curveIvOffer;} set{SetField(ref _curveIvOffer, value); }}
        public double CurveIvSpread {get {return _curveIvSpread;} set{SetField(ref _curveIvSpread, value); }}
        public double CurveIvAverage {get {return _curveIvAverage;} set{SetField(ref _curveIvAverage, value); }}
        public double CurveDelta {get {return _curveDelta;} set{SetField(ref _curveDelta, value); }}
        public double CurveVega {get {return _curveVega;} set{SetField(ref _curveVega, value); }}
        public double CurveGamma {get {return _curveGamma;} set{SetField(ref _curveGamma, value); }}
        public double CurveTheta {get {return _curveTheta;} set{SetField(ref _curveTheta, value); }}
        public double CurveVanna {get {return _curveVanna;} set{SetField(ref _curveVanna, value); }}
        public double CurveVomma {get {return _curveVomma;} set{SetField(ref _curveVomma, value); }}
        public double MarketSpreadResetLimit {get {return _marketSpreadResetLimit;} set{SetField(ref _marketSpreadResetLimit, value); }}
        public int GlassBidVolume {get {return _glassBidVolume;} set{SetField(ref _glassBidVolume, value); }}
        public int GlassOfferVolume {get {return _glassOfferVolume;} set{SetField(ref _glassOfferVolume, value); }}
        public ValuationStatus ValuationStatus {get {return _valuationStatus;} set{SetField(ref _valuationStatus, value); }}
        public DateTime? TimeValuationStatus {get {return _timeValuationStatus;} set{SetField(ref _timeValuationStatus, value); }}
        public string AggressiveResetTime {get {return _aggressiveResetTime;} set{SetField(ref _aggressiveResetTime, value); }}
        public string ConservativeResetTime {get {return _conservativeResetTime;} set{SetField(ref _conservativeResetTime, value); }}
        public string QuoteVolBidResetTime {get {return _quoteVolBidResetTime;} set{SetField(ref _quoteVolBidResetTime, value); }}
        public string QuoteVolOfferResetTime {get {return _quoteVolOfferResetTime;} set{SetField(ref _quoteVolOfferResetTime, value); }}

        public int MMVegaVolLimitBuy {get {return _mmVegaVolLimitBuy;} set{SetField(ref _mmVegaVolLimitBuy, value);}}
        public int MMVegaVolLimitSell {get {return _mmVegaVolLimitSell;} set{SetField(ref _mmVegaVolLimitSell, value);}}
        public int MMGammaVolLimitBuy {get {return _mmGammaVolLimitBuy;} set{SetField(ref _mmGammaVolLimitBuy, value);}}
        public int MMGammaVolLimitSell {get {return _mmGammaVolLimitSell;} set{SetField(ref _mmGammaVolLimitSell, value);}}

        public int OwnMMVolume {get {return _ownMMVolume;} set{SetField(ref _ownMMVolume, value);}}
        public int OwnMMVolumeActive {get {return _ownMMVolumeActive;} set{SetField(ref _ownMMVolumeActive, value);}}
        public int VolumeDiff {get {return _volumeDiff;} set{SetField(ref _volumeDiff, value);}}

        public OptionTradingModule TradingModule => EnsureTradingModule();

        public RobotLogger.OptionLogger Logger {get {return _logger ?? (_logger = Controller.RobotLogger.Option(this)); }}

        public event Action ValuationParamsUpdated;
        static public event Action<OptionInfo> IsActiveChangedByUser;

        #endregion

        public OptionInfo(Controller controller, Security sec, FuturesInfo future) : base(controller, sec) {
            EnsureCorrectOption(sec);
            if(sec.UnderlyingSecurityId != future.Id) throw new InvalidOperationException("wrong future: expected '{0}', got '{1}'".Put(sec.UnderlyingSecurityId, future.Id));

            _future = future;

            Notifier(NativeSecurity).AddExternalProperty(() => Strike);
            Notifier(NativeSecurity).AddExternalProperty(() => OptionType);

            AddNotifier(this);
            Notifier(this).AddExternalProperty(() => IvBid, () => IvAverage);
            Notifier(this).AddExternalProperty(() => IvOffer, () => IvAverage);

            Notifier(this).AddExternalProperty(() => MarketIvBid, () => MarketIvSpread);
            Notifier(this).AddExternalProperty(() => MarketIvOffer, () => MarketIvSpread);

            _series = _future.GetOptionSeries(this);
            _strike = _series.GetOptionStrike(this);

            VerifyStructure();

            _dependentIds = new[] {Id, Strike.StrikeId, Series.SeriesId.Id, future.Id};

            Model = new OptionModel(this);
            _log.Dbg.AddDebugLog("new option: {0}", Code);

            ConfigProvider.ConfigSecuritySelection.EffectiveConfigChanged += (cfg, names) => {
                if(_dependentIds.Any(names.Contains)) {
                    RaisePropertyChanged(() => IsOptionActive);
                    RaisePropertyChanged(() => IsSelectedForTrading);
                    RaisePropertyChanged(() => IsVpAvailable);
                    RaisePropertyChanged(() => CanStartStrategies);
                }

                if(Strike.IsStrikeCalculated) {
                    Activate();
                    EnsureTradingModule();
                } else {
                    Deactivate();
                }

                if(names.Contains(Id)) // IsOptionActive changed
                    IsActiveChangedByUser.SafeInvoke(this);
            };

            ConfigProvider.ValuationParams.ListOrItemChanged += isListChange => {
                OnPropertyChanged(() => CfgValuationParams);
                OnPropertyChanged(() => IsVpAvailable);
                OnPropertyChanged(() => CanStartStrategies);

                ValuationParamsUpdated.SafeInvoke();
            };
            Series.AtmStrikeChanged += ser => {
                OnPropertyChanged(() => CfgValuationParams);
                OnPropertyChanged(() => IsVpAvailable);
                OnPropertyChanged(() => CanStartStrategies);
                OnPropertyChanged(() => AtmShift);

                Log();
            };

            PositionChanged += si => Log();
            BestQuotesChanged += si => Log();

            if(Strike.IsStrikeCalculated) {
                Activate();
                EnsureTradingModule();
            }

            Future.TradingModule.DeltaHedgedStateChanged += () => {
                OnPropertyChanged(() => CanStartStrategies);
            };

            RobotData.CurrentSessionIdChanged += UpdateIsMMOption;
        }

        protected override void DisposeManaged() {
            _tradingModule.Do(tm => tm.Dispose());
            base.DisposeManaged();
        }

        void Log() {
            _needToLog = Controller.Scheduler.MarketPeriod.IsMarketOpen();
        }

        void CheckLog() {
            if(!(_needToLog && CanStartStrategies)) return;
            _needToLog = false;

            Logger.FreezeData().Commit();
        }

        public static void EnsureCorrectOption(Security security) {
            if(security.Type != SecurityTypes.Option) throw new ArgumentException("sec.Type");
            if(security.OptionType == null) throw new InvalidOperationException("OptionType is null");
            if(security.Strike == 0) throw new InvalidOperationException("strike is zero");
            if(security.ExpiryDate == null || security.ExpiryDate.Value == default(DateTime)) throw new InvalidOperationException("option with invalid expiration date");
            if(security.UnderlyingSecurityId.IsEmpty()) throw new InvalidOperationException("no underlying security id for '{0}'".Put(security.Id));
        }

        protected override void VerifyNewSecurity(Security newSecurity) {
            base.VerifyNewSecurity(newSecurity);

            EnsureCorrectOption(newSecurity);
        }

        OptionTradingModule EnsureTradingModule() {
            if(_tradingModule != null)
                return _tradingModule;

            _tradingModule = new OptionTradingModule(this);
            _tradingModule.ModuleStateChanged += module => Log();

            return _tradingModule;
        }

        void VerifyStructure() {
            if(_future == null) throw new InvalidOperationException("future is null");
            if(_series == null) throw new InvalidOperationException("series is null");
            if(_strike == null) throw new InvalidOperationException("strike is null");
            if(!object.ReferenceEquals(_series, _strike.Series)) throw new InvalidOperationException("invalid series");
            if(!object.ReferenceEquals(_series.Future, _future)) throw new InvalidOperationException("invalid series future");
            if(!object.ReferenceEquals(_strike.Future, _future)) throw new InvalidOperationException("invalid strike future");
        }

        // Обновление данных на экране по таймеру
        protected override void OnUpdateData() {
            base.OnUpdateData();

            var modelData = Model.LastData;

            var calcDone = modelData.CalcDone;

            if(!calcDone)
                modelData = new OptionModel.Data();

            MarketIvBid = modelData.MarketIvBid;
            MarketIvOffer = modelData.MarketIvOffer;
            MarketIvAverage = modelData.MarketIvAverage;
            IvBid = modelData.IvBid;
            IvOffer = modelData.IvOffer;
            IvSpread = modelData.IvSpread;
            DeltaBid = modelData.DeltaBid;
            Vega = modelData.Vega;
            Gamma = modelData.Gamma;
            Theta = modelData.Theta;
            Vanna = modelData.Vanna;
            Vomma = modelData.Vomma;
            MarketSpread = modelData.MarketSpread;
            CurrentSpread = modelData.CurrentSpread;
            TargetSpread = modelData.ValuationTargetSpread;
            StrikeMoneyness = calcDone ? modelData.StrikeMoneyness : (StrikeMoneyness?)null;
            GreeksRegime = calcDone ? modelData.GreeksRegime : (GreeksRegime?)null;
            Moneyness = modelData.Moneyness;
            InitialDelta = modelData.InitialDelta;
            BestDeltaExpectation = modelData.BestDeltaExpectation;
            IlliquidDelta = modelData.IlliquidDelta;
            IlliquidIv = modelData.IlliquidIv;
            IlliquidVega = modelData.IlliquidVega;
            IlliquidGamma = modelData.IlliquidGamma;
            IlliquidTheta = modelData.IlliquidTheta;
            IlliquidVanna = modelData.IlliquidVanna;
            IlliquidVomma = modelData.IlliquidVomma;
            MarketBid = modelData.MarketBid;
            MarketOffer = modelData.MarketOffer;
            MarketAverage = modelData.MarketAverage;
            ValuationTargetSpread = modelData.ValuationTargetSpread;
            ValuationSpread = modelData.ValuationSpread;
            WideningMktIv = modelData.WideningMktIv;
            NarrowingMktIv = modelData.NarrowingMktIv;
            CalculationDelta = modelData.CalculationDelta;
            CalculationVega = modelData.CalculationVega;
            CalculationGamma = modelData.CalculationGamma;
            CalculationTheta = modelData.CalculationTheta;
            CalculationVanna = modelData.CalculationVanna;
            CalculationVomma = modelData.CalculationVomma;

            CurveBid = modelData.CurveBid;
            CurveOffer = modelData.CurveOffer;
            CurveSpread = modelData.CurveSpread;
            CurveAverage = modelData.CurveAverage;
            CurveIvBid = modelData.CurveIvBid;
            CurveIvOffer = modelData.CurveIvOffer;
            CurveIvSpread = modelData.CurveIvSpread;
            CurveIvAverage = modelData.CurveIvAverage;
            CurveDelta = modelData.CurveDelta;
            CurveVega = modelData.CurveVega;
            CurveGamma = modelData.CurveGamma;
            CurveTheta = modelData.CurveTheta;
            CurveVanna = modelData.CurveVanna;
            CurveVomma = modelData.CurveVomma;
            MarketSpreadResetLimit = modelData.MarketSpreadResetLimit;
            GlassBidVolume = modelData.Input.GlassBidVolume;
            GlassOfferVolume = modelData.Input.GlassOfferVolume;
            ValuationStatus = modelData.ValuationStatus;

            var dt = modelData.TimeValuationStatus;
            TimeValuationStatus = dt.IsDefault() ? (DateTime?)null : dt;

            MMVegaVolLimitBuy = modelData.MMVegaVolLimitBuy;
            MMVegaVolLimitSell = modelData.MMVegaVolLimitSell;
            MMGammaVolLimitBuy = modelData.MMGammaVolLimitBuy;
            MMGammaVolLimitSell = modelData.MMGammaVolLimitSell;

            var now = Controller.Connector.GetMarketTime();
            AggressiveResetTime     = GetResetTimeStr(modelData.AggressiveResetStartTime, now);
            ConservativeResetTime   = GetResetTimeStr(modelData.ConservativeResetStartTime, now);
            QuoteVolBidResetTime    = GetResetTimeStr(modelData.QuoteVolBidResetStartTime, now);
            QuoteVolOfferResetTime  = GetResetTimeStr(modelData.QuoteVolOfferResetStartTime, now);

            RaisePropertyChanged(() => EmpiricDelta);

            CheckLog();
        }

        string GetResetTimeStr(DateTime? startTime, DateTime now) => startTime == null ? null : (now - startTime.Value).FormatInterval();

        public void ResetModel(string comment) {
            _log.Dbg.AddDebugLog($"{Code}: ResetModel({comment})");
            Model.Reset(comment);
        }

        protected override void OnResetData() {
            base.OnResetData();

            MarketIvBid = MarketIvOffer = MarketIvAverage = 0d;
            IvBid = IvOffer = IvSpread = 0d;
            DeltaBid = 0d;
            Vega = Gamma = 0d;
            EmpiricDelta = 0d;
        }

        void UpdateIsMMOption() {
            lock(_mmInfos)
                MMRecord = _mmInfos.TryGetValue(RobotData.CurrentSessionId).With(info => info.Record);
        }

        public void HandleAddMMInfo(SecurityMMInfo smmi) {
            lock(_mmInfos) {
                _mmInfos[smmi.SessionId] = smmi;
                UpdateIsMMOption();
            }
        }

        public void HandleRemoveMMInfo(SecurityMMInfo smmi) {
            lock(_mmInfos) {
                _mmInfos.Remove(smmi.SessionId);
                UpdateIsMMOption();
            }
        }
    }

    /// <summary>Класс-посредник для отображения информации порфеля в пользовательском интерфейсе.</summary>
    public class PositionInfo : ConnectorNotifiableObject {
        Position _position;
        readonly PortfolioInfo _portfolioInfo;
        readonly SecurityInfo _securityInfo;

        //public Position NativePosition {get {return _position;}}
        public PortfolioInfo Portfolio {get{ return _portfolioInfo; }}
        public SecurityInfo Security {get{ return _securityInfo; }}
        public decimal CurrentValue {get {return _position.CurrentValue;}}

        public event Action<PositionInfo> Changed;

        public PositionInfo(Controller controller, PortfolioInfo pInfo, SecurityInfo sInfo, Position pos) : base(controller) {
            _position = pos;
            _portfolioInfo = pInfo;
            _securityInfo = sInfo;

            AddNotifier(_position);
            Notifier(_position).AddExternalProperty(() => CurrentValue);

            base.Activate();

            PropertyChanged += (sender, args) => {
                if(args.PropertyName == Util.PropertyName(() => CurrentValue))
                    Changed.SafeInvoke(this);
            };
        }

        public void ReplacePosition(Position newPos) {
            ReplaceConnectorObject(ref _position, newPos);
        }

        public override void Activate() { _log.Dbg.AddWarningLog("PositionInfo.Activate() called"); }
        public override void Deactivate() { _log.Dbg.AddWarningLog("PositionInfo.Deactivate() called"); }
    }

    /// <summary>Класс-посредник для отображения информации о заявке в пользовательском интерфейсе.</summary>
    public sealed class OrderInfo : ConnectorNotifiableObject {
        Order _order;

        VMStrategy Strategy {get {return (_order as RobotOptionOrder).With(o => o.OrderAction.VMStrategy); }}

        public long Id {get {return _order.Id; }}
        public long TransactionId {get {return _order.TransactionId;}}
        public string SecurityId {get {return _order.Security.Id; }}
        public SecurityTypes? SecurityType {get {return _order.Security.Type; }}
        public string SecurityShortName {get {return _order.Security.ShortName; }}
        public string SecurityCode {get {return _order.Security.Code; }}
//        public string PortfolioName {get {return _order.Portfolio.Name; }}
        public OrderStates State {get {return _order.State; }}
        public Sides Direction {get {return _order.Direction; }}
        public decimal Volume {get { return _order.Volume; }}
        public decimal Price {get { return _order.Price; }}
        public decimal Balance {get {return _order.Balance;}}
        public decimal Executed {get {return _order.Volume - _order.Balance;}}
        public DateTime LastChangeTime {get {return _order.LastChangeTime; }}
        public DateTime Time {get {return _order.Time; }}
        public TimeSpan? Latency => (_order as OrderEx).Return(o => o.Latency, null);
        public TimeSpan? CancelLatency => (_order as OrderEx).Return(o => o.CancelLatency, null);
        public string StrategyId {get {return Strategy.With(vms => vms.Id);}}

        public Order NativeOrder {get {return _order;}}

        public OrderInfo(Controller controller, Order o) : base(controller) {
            _order = o;
            OrderEx oex = null;

            AddNotifier(_order);
            Notifier(_order).AddExternalProperty(() => Id);
            Notifier(_order).AddExternalProperty(() => State);
            Notifier(_order).AddExternalProperty(() => Direction);
            Notifier(_order).AddExternalProperty(() => Volume);
            Notifier(_order).AddExternalProperty(() => Price);
            Notifier(_order).AddExternalProperty(() => Time);
            Notifier(_order).AddExternalProperty(() => LastChangeTime);
            Notifier(_order).AddExternalProperty(() => Balance);
            Notifier(_order).AddExternalProperty(() => Balance, () => Executed);
            Notifier(_order).AddExternalProperty(() => oex.Latency);
            Notifier(_order).AddExternalProperty(() => oex.CancelLatency);

            Activate();
        }

        public void ReplaceOrder(Order newOrder) {
            ReplaceConnectorObject(ref _order, newOrder);

            RaisePropertyChanged(nameof(StrategyId));
        }
    }

    /// <summary>Класс-посредник для отображения информации о стакане в пользовательском интерфейсе.</summary>
    public class MarketDepthInfo : ConnectorNotifiableObject {
        public const int Depth = 5;

        Dictionary<decimal, int> _ordersByPrice = new Dictionary<decimal, int>(); 

        readonly SecurityInfo _secInfo;

        DateTime _lastUpdateTime;
        MarketDepth _lastDepth;

        ObservableCollection<MarketDepthQuoteInfo> _bids;
        ObservableCollection<MarketDepthQuoteInfo> _offers;

        public ObservableCollection<MarketDepthQuoteInfo> Offers { get { return _offers ?? (_offers = new ObservableCollection<MarketDepthQuoteInfo>()); }}
        public ObservableCollection<MarketDepthQuoteInfo> Bids { get { return _bids ?? (_bids = new ObservableCollection<MarketDepthQuoteInfo>()); }}

        public DateTime? LastUpdateTime {get {return _lastDepth.Return(md => md.LastChangeTime, (DateTime?)null);}}

        Connector Connector {get {return Controller.Connector;}}

        public string LastExchangeTradeStr { get {
            var trade = _secInfo.LastTrade;
            var time = trade.Return(t => t.Time, default(DateTime));
            if(!IsActive || time == default(DateTime)) return null;
            return "{0}@{1} {2:HH:mm:ss}".Put(trade.Volume, trade.Price, time);
        }}

        bool _forceUpdate;

        public MarketDepthInfo(SecurityInfo si) : base(si.Controller) {
            _secInfo = si;

            (si as OptionInfo).Do(o => Controller.ConfigProvider.ValuationParams.ListOrItemChanged += ValuationParamsOnListOrItemChanged);
        }

        void ValuationParamsOnListOrItemChanged(bool isListChnage) {
            RobotData.Dispatcher.MyGuiAsync(HighlightBidOffer, true);
        }

        public override void Init() {
            if(_secInfo.NativeSecurity.Connector == null) {
                _lastDepth = null;
                return;
            }

            _lastDepth = Connector.GetMarketDepth(_secInfo.Id);
        }

        void UpdateOrdersByPrice() {
            var newDict = new Dictionary<decimal, int>();
            var orders = RobotData.AllActiveOrders.Where(o => o.SecurityId == _secInfo.Id).ToList();
            foreach(var o in orders) {
                if(!newDict.ContainsKey(o.Price))
                    newDict[o.Price] = 0;
                newDict[o.Price] += (o.Direction == Sides.Buy ? 1 : -1)*(int)o.Balance;
            }
            _ordersByPrice = newDict;
        }

        void HighlightBidOffer() {
            var vp = (_secInfo as OptionInfo).With(o => o.CfgValuationParams);
            if(vp == null)
                return;

            if(Bids.Count < Depth) CreateQuotesLists();
            var vol = vp.ValuationQuoteMinVolume;
            bool foundBid, foundOffer;

            foundBid = foundOffer = false;

            for(var i = 0; i < Depth; ++i) {
                var q = Offers[Depth - i - 1]; // reverse order
                q.Highlight = !foundOffer && (foundOffer = q.Volume - Math.Abs(q.OwnOrder) >= vol);

                q = Bids[i];
                q.Highlight = !foundBid && (foundBid = q.Volume - Math.Abs(q.OwnOrder) >= vol);
            }

        }

        void UpdateMarketDepth() {
            if(Bids.Count < Depth) CreateQuotesLists();

            var ld = _lastDepth;
            if(ld == null || (ld.LastChangeTime <= _lastUpdateTime && !_forceUpdate))
                return;
            
            _lastUpdateTime = ld.LastChangeTime;
            _forceUpdate = false;

            var asks = ld.Asks;
            var bids = ld.Bids;

            for(var i = 0; i < Depth; ++i) {
                var q = Offers[Depth - i - 1]; // reverse order
                if(i < asks.Length) {
                    q.Price = asks[i].Price;
                    q.Volume = (int)asks[i].Volume;
                    q.OwnOrder = _ordersByPrice.TryGetValue(q.Price);
                } else {
                    q.Reset();
                }

                q = Bids[i];
                if(i < bids.Length) {
                    q.Price = bids[i].Price;
                    q.Volume = (int)bids[i].Volume;
                    q.OwnOrder = _ordersByPrice.TryGetValue(q.Price);
                } else {
                    q.Reset();
                }
            }

            HighlightBidOffer();
        }

        public override void Activate() {
            if(IsActive) return;
            IsActive = true;

            _lastUpdateTime = DateTime.MinValue;

            if(Bids.Count < Depth) CreateQuotesLists();

            foreach(var q in Bids.Concat(Offers))
                q.Reset();

            Controller.ConnectorGUISubscriber.ConnectorReset += OnConnectionReset;
            Controller.ConnectorGUISubscriber.NewOrder += OnOrdersChanged;
            Controller.ConnectorGUISubscriber.OrderChanged += OnOrdersChanged;
            Controller.ConnectorGUISubscriber.NewMarketDepth     += OnMarketDepthChanged;
            Controller.GUIMarketDepthChanged += OnMarketDepthChanged;
            Controller.UpdateMarketDepth(_secInfo.Id);
        }

        public override void Deactivate() {
            if(!IsActive) return;
            IsActive = false;

            Controller.ConnectorGUISubscriber.ConnectorReset -= OnConnectionReset;
            Controller.ConnectorGUISubscriber.NewOrder -= OnOrdersChanged;
            Controller.ConnectorGUISubscriber.OrderChanged -= OnOrdersChanged;
            Controller.ConnectorGUISubscriber.NewMarketDepth     -= OnMarketDepthChanged;
            Controller.GUIMarketDepthChanged -= OnMarketDepthChanged;

            OnPropertyChanged(() => LastExchangeTradeStr);
        }

        void OnConnectionReset(Connector obj) {
            _lastDepth = null;
        }

        void OnMarketDepthChanged(Connector c, MarketDepth marketDepth) {
            if(marketDepth.Security.Id == _secInfo.Id)
                _lastDepth = marketDepth;

            RobotData.AddGuiOneTimeActionByKey("depth-" + _secInfo.Id, UpdateData);
        }

        void OnOrdersChanged(Connector c, Order order) {
            _forceUpdate = true;
        }

        public void UpdateData() {
            UpdateOrdersByPrice();
            UpdateMarketDepth();
            OnPropertyChanged(() => LastExchangeTradeStr);
        }

        void CreateQuotesLists() {
            Bids.Clear(); Offers.Clear();
            for(var i = 0; i < Depth; ++i) {
                Bids.Add(new MarketDepthQuoteInfo());
                Offers.Add(new MarketDepthQuoteInfo());
            }
        }

        public class MarketDepthQuoteInfo : ViewModelBase {
            bool _highlight;
            int _ownOrder;
            int _volume;
            decimal _price;

            public MarketDepthQuoteInfo() {}

            public bool Highlight {get {return _highlight;} set {SetField(ref _highlight, value); }}
            public int OwnOrder {get {return _ownOrder;} set {SetField(ref _ownOrder, value); }}
            public int Volume {get {return _volume;} set {SetField(ref _volume, value); }}
            public decimal Price {get {return _price;} set {SetField(ref _price, value); }}

            public void Reset() {
                Volume = 0;
                Price = 0;
                OwnOrder = 0;
                Highlight = false;
            }
        }

   }

    /// <summary>Класс-посредник для отображения информации о сделке в пользовательском интерфейсе.</summary>
    public sealed class MyTradeInfo : ConnectorNotifiableObject {
        MyTradeEx _myTrade;

        VMStrategy Strategy {get {return (_myTrade.Order as RobotOptionOrder).With(o => o.OrderAction.VMStrategy); }}

        public MyTradeInfo(Controller ctl, MyTradeEx mt) : base(ctl) {
            OrderEx oex = null;
            Trade t = null;
            _myTrade = mt;

            AddNotifier(_myTrade);
            Notifier(_myTrade).AddExternalProperty(() => TradeIv);

            AddNotifier(_myTrade.Order);
            Notifier(_myTrade.Order).AddExternalProperty(() => oex.CancelLatency, () => OrderCancelLatency);

            AddNotifier(_myTrade.Trade);
            Notifier(_myTrade.Trade).AddExternalProperty(() => t.OrderDirection, () => IsAggressive);

            Activate();
        }

        public string SecurityId {get {return _myTrade.Order.Security.Id; }}
        public SecurityTypes? SecurityType {get {return _myTrade.Order.Security.Type; }}
        public string SecurityCode {get {return _myTrade.Order.Security.Code; }}
        public string SecurityShortName {get {return _myTrade.Order.Security.ShortName; }}
        public long Id {get {return _myTrade.Trade.Id; }}
        public DateTime Time {get {return _myTrade.Trade.Time; }}
        public decimal Price {get {return _myTrade.Trade.Price; }}
        public int Volume {get {return (int)_myTrade.Trade.Volume; }}
        public Sides OrderDirection {get {return _myTrade.Order.Direction; }}
        public long OrderId {get {return _myTrade.Order.Id; }}
        public double? TradeIv {get {return _myTrade.TradeIv; }}
        public string StrategyId {get {return Strategy.With(vms => vms.Id);}}
        public TimeSpan? OrderCancelLatency => (_myTrade.Order as OrderEx).Return(o => o.CancelLatency, null);

        public bool? IsAggressive {
            get {
                var dir = _myTrade.Trade.OrderDirection;
                return dir == null ? (bool?)null : (dir.Value == OrderDirection);
            }
        }

        public void ReplaceTrade(MyTradeEx mt) {
            ReplaceConnectorObject(ref _myTrade, mt);
        }
    }

    /// <summary>Класс-посредник для отображения информации об обязательствах маркетмейкера по инструменту.</summary>
    public sealed class SecurityMMInfo : ConnectorNotifiableObject {
        MMInfoRecord _record;
        SecurityInfo _secInfo;
        bool _securitySubscribed;

        public IMMInfoRecord Record => _record;

        public SecurityMMInfo(Controller ctl, MMInfoRecord record) : base(ctl) {
            if(record == null) throw new ArgumentNullException(nameof(record));

            _record = record;
            _secInfo = RobotData.GetSecurityByIsinId(record.IsinId);

            if(_secInfo == null)
                Subscribe();
            else
                (_secInfo as OptionInfo).Do(o => o.HandleAddMMInfo(this));

            Activate();
        }

        void SecurityInfoOnSecurityReplaced(SecurityInfo si) {
            if(si.PlazaIsinId != IsinId)
                return;

            Unsubscribe();

            _secInfo = si;
            (_secInfo as OptionInfo).Do(o => o.HandleAddMMInfo(this));

            RaisePropertyChanged(nameof(Security));
            RaisePropertyChanged(nameof(SecurityCode));
        }

        void Subscribe() {
            if(_securitySubscribed)
                return;

            _securitySubscribed = true;
            SecurityInfo.NativeSecurityReplaced += SecurityInfoOnSecurityReplaced;
        }

        void Unsubscribe() {
            if(!_securitySubscribed)
                return;

            _securitySubscribed = false;
            SecurityInfo.NativeSecurityReplaced -= SecurityInfoOnSecurityReplaced;
        }

        public MMInfoRecordKey Key          => _record.Key;
        public SecurityInfo Security        => _secInfo;
        public OptionInfo Option            => _secInfo as OptionInfo;
        public string SecurityCode          => Security.Return(s => s.Code, IsinId.ToString());

        public long ReplId                  => _record.ReplId;
        public long ReplAct                 => _record.ReplAct;
        public int IsinId                   => _record.IsinId;
        public int SessionId                => _record.SessionId;
        public decimal Spread               => _record.Spread;
        public decimal PriceEdgeSell        => _record.PriceEdgeSell;
        public int AmountSells              => _record.AmountSells;
        public decimal PriceEdgeBuy         => _record.PriceEdgeBuy;
        public int AmountBuys               => _record.AmountBuys;
        public decimal MarketMakingSpread   => _record.MarketMakingSpread;
        public int MarketMakingAmount       => _record.MarketMakingAmount;
        public bool SpreadSign              => _record.SpreadSign;
        public bool AmountSign              => _record.AmountSign;
        public decimal PercentTime          => _record.PercentTime;
        public DateTime PeriodStart         => _record.PeriodStart;
        public DateTime PeriodEnd           => _record.PeriodEnd;
        public string ClientCode            => _record.ClientCode;
        public bool ActiveSign              => _record.ActiveSign;
        public decimal FillMin              => _record.FillMin;
        public decimal FillPartial          => _record.FillPartial;
        public decimal FillTotal            => _record.FillTotal;
        public bool IsFillMin               => _record.IsFillMin;
        public bool IsFillPartial           => _record.IsFillPartial;
        public bool IsFillTotal             => _record.IsFillTotal;
        public decimal CStrikeOffset        => _record.CStrikeOffset;

        public void UpdateRecord(MMInfoRecord record) {
            if(!record.Key.Equals(_record.Key))
                throw new InvalidOperationException($"expected equal keys ({record.Key} != {_record.Key})");

            var diff = record.PropertyDiff(_record);

            _record = record;

            diff.ForEach(RaisePropertyChanged);
        }

        protected override void DisposeManaged() {
            Unsubscribe();
            (_secInfo as OptionInfo).Do(o => o.HandleRemoveMMInfo(this));
            base.DisposeManaged();
        }
    }

    public abstract class RobotDataObject : ViewModelBaseNotifyAction, IActiveObject, IInitializable {
        protected static readonly Logger _log = new Logger();
        public Controller Controller {get; private set;}
        public ConfigProvider ConfigProvider {get {return Controller.ConfigProvider;}}
        //public Connector.IConnectorSubscriber ConnectorSubscriber {get {return Controller.ConnectorGUISubscriber; }}
        protected RobotData RobotData {get {return Controller.RobotData; }}
        protected RobotLogger RobotLogger {get {return Controller.RobotLogger;}}

        protected RobotDataObject(Controller ctl) {
            Controller = ctl;
        }

        public virtual void Init() {}

        public abstract void Activate();
        public abstract void Deactivate();
        public virtual bool IsActive { get; protected set; }

        protected override void DisposeManaged() {
            Deactivate();
            base.DisposeManaged();
        }
    }

    public abstract class ConnectorNotifiableObject : RobotDataObject {
        readonly Dictionary<INotifyPropertyChanged, NotifyPropertyMapper> _mappers = new Dictionary<INotifyPropertyChanged, NotifyPropertyMapper>();

        protected override Action<string> NotifyAction {get {return Notify;}}

        protected ConnectorNotifiableObject(Controller ctl) : base(ctl) {}

        protected void AddNotifier(INotifyPropertyChanged obj) {
            _mappers.Add(obj, new NotifyPropertyMapper(obj) { NotifyPropertyChangedAction = name => OnPropertyChanged(name) });
        }

        void Notify(string name) {
            RobotData.AddGuiOneTimeActionByKey(Tuple.Create(_mappers, name), () => RaisePropertyChanged(name));
        }

        protected void ReplaceConnectorObject<T>(ref T oldObj, T newObj) where T:INotifyPropertyChanged {
            if(object.ReferenceEquals(oldObj, newObj))
                return;

            var mapper = _mappers[oldObj];
            _mappers.Remove(oldObj);
            oldObj = newObj;
            _mappers[newObj] = mapper;
            mapper.ReplacePropertyObject(newObj);
        }

        protected NotifyPropertyMapper Notifier(INotifyPropertyChanged obj) {
            return _mappers[obj];
        }

        public override void Activate() {
            try {
                _mappers.Values.ForEach(m => m.Activate());
            } finally {
                IsActive = true;
            }
        }

        public override void Deactivate() {
            try {
                _mappers.Values.ForEach(m => m.Deactivate());
            } finally {
                IsActive = false;
            }
        }
    }
}
