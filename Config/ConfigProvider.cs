using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Ecng.Common;
using MoreLinq;
using OptionBot.robot;
using OptionBot.Xaml;
using StockSharp.Messages;

namespace OptionBot.Config {
    public partial class ConfigProvider : ViewModelBase, IRobotDataUpdater {
        static readonly Logger _log = new Logger(nameof(ConfigProvider));

        #region filenames
        public const string SettingsDirectory = "Settings";
        const string FilenameGeneralSettings = "general.botcfg";
        const string FilenameVPSettings = "vp.botcfg";
        const string FilenameFuturesSettings = "futures.botcfg";
        const string FilenameTradingPeriodsSettings = "trading_periods.botcfg";
        const string FilenameStrategiesSettings = "strategies.botcfg";
        const string FilenameSeriesSettings = "series.botcfg";
        const string FilenameVPDefaultSettings = "default_vp.botcfg";
        const string FilenameFutureDefaultSettings = "default_future.botcfg";
        const string FilenameStrategyRegularDefaultSettings = "default_regular.botcfg";
        const string FilenameStrategyMMDefaultSettings = "default_mm.botcfg";
        const string FilenameStrategyVegaHedgeDefaultSettings = "default_vegahedge.botcfg";
        const string FilenameStrategyGammaHedgeDefaultSettings = "default_gammahedge.botcfg";
        const string FilenameSecuritySelectionSettings = "security_selection.botcfg";
        public const string FilenameUISettings = "ui.botcfg";
        #endregion

        #region fields/properties

        readonly ISaveableConfiguration[] _configs;

        static readonly TimeSpan _saveDelay = TimeSpan.FromSeconds(15);
        DateTime _lastSaveTime;
        readonly Dictionary<object, Action> _saveActions = new Dictionary<object, Action>();

        bool _inRealtimeMode = true;

        readonly Controller _controller;
        RobotData RobotData => _controller.RobotData;

        readonly FileCfgGeneral      _general;
        readonly FileCfgVPList       _valuationParams;
        readonly FileCfgStrategyList _strategies;
        readonly FileCfgSeriesList   _series;
        readonly FileCfgFutureList   _futures;
        readonly FileCfgPairTradingPeriodList _tradingPeriods;
        readonly FileCfgSecSel       _configSecSel;

        readonly FileCfgVP          _defaultValuationParams;
        readonly FileCfgFuture      _defaultFuture;
        readonly FileCfgStrategy    _defaultRegularStrategy;
        readonly FileCfgStrategy    _defaultMMStrategy;
        readonly FileCfgStrategy    _defaultVegaHedgeStrategy;
        readonly FileCfgStrategy    _defaultGammaHedgeStrategy;

        public ICfgPairGeneral       General => _general;
        public ICfgPairVPList        ValuationParams => _valuationParams;
        public ICfgPairStrategyList  Strategies => _strategies;
        public ICfgPairSeriesList    Series => _series;
        public ICfgPairFutureList    Futures => _futures;
        public ICfgPairTradingPeriodList TradingPeriods => _tradingPeriods;
        public ICfgPairSecSel        ConfigSecuritySelection => _configSecSel;

        public ICfgPairVP            DefaultValuationParams => _defaultValuationParams;
        public ICfgPairFuture        DefaultFuture => _defaultFuture;
        public ICfgPairStrategy      DefaultRegularStrategy => _defaultRegularStrategy;
        public ICfgPairStrategy      DefaultMMStrategy => _defaultMMStrategy;
        public ICfgPairStrategy      DefaultVegaHedgeStrategy => _defaultVegaHedgeStrategy;
        public ICfgPairStrategy      DefaultGammaHedgeStrategy => _defaultGammaHedgeStrategy;

        public ConfigUI              UI {get;}

        #endregion

        #region init/deinit

        public ConfigProvider(Controller controller) {
            _controller = controller;

            var cfgs = new List<ISaveableConfiguration>();

            cfgs.Add(_general           = FileCfgGeneral.LoadFromFile(FName(FilenameGeneralSettings), (u,c) => new FileCfgGeneral(u, c), () => new ConfigGeneral()));
            cfgs.Add(_valuationParams   = FileCfgVPList.LoadFromFile(FName(FilenameVPSettings), (u, n) => new FileCfgVP(u, n), (e, n, c) => new FileCfgVPList(e, n, c)));
            cfgs.Add(_strategies        = FileCfgStrategyList.LoadFromFile(FName(FilenameStrategiesSettings), (u, n) => new FileCfgStrategy(u, n), (e, n, c) => new FileCfgStrategyList(e, n, c)));
            cfgs.Add(_series            = FileCfgSeriesList.LoadFromFile(FName(FilenameSeriesSettings), (u, n) => new FileCfgSeries(u, n), (e, n, c) => new FileCfgSeriesList(e, n, c)));
            cfgs.Add(_futures           = FileCfgFutureList.LoadFromFile(FName(FilenameFuturesSettings), (u, n) => new FileCfgFuture(u, n), (e, n, c) => new FileCfgFutureList(e, n, c)));
            cfgs.Add(_tradingPeriods    = FileCfgPairTradingPeriodList.LoadFromFile(FName(FilenameTradingPeriodsSettings), (u, n) => new FileCfgTradingPeriod(u, n), (e, n, c) => new FileCfgPairTradingPeriodList(e, n, c)));
            cfgs.Add(_configSecSel      = FileCfgSecSel.LoadFromFile(FName(FilenameSecuritySelectionSettings), (u,c) => new FileCfgSecSel(u, c), () => new ConfigSecuritySelection()));

            cfgs.Add(_defaultValuationParams    = FileCfgVP.LoadFromFile(FName(FilenameVPDefaultSettings),                       (u,c) => new FileCfgVP(u, c),       () => new ConfigValuationParameters()));
            cfgs.Add(_defaultFuture             = FileCfgFuture.LoadFromFile(FName(FilenameFutureDefaultSettings),               (u,c) => new FileCfgFuture(u, c),   () => new ConfigFuture()));
            cfgs.Add(_defaultRegularStrategy    = FileCfgStrategy.LoadFromFile(FName(FilenameStrategyRegularDefaultSettings),    (u,c) => new FileCfgStrategy(u, c), () => new ConfigRegularStrategy()));
            cfgs.Add(_defaultMMStrategy         = FileCfgStrategy.LoadFromFile(FName(FilenameStrategyMMDefaultSettings),         (u,c) => new FileCfgStrategy(u, c), () => new ConfigMMStrategy()));
            cfgs.Add(_defaultVegaHedgeStrategy  = FileCfgStrategy.LoadFromFile(FName(FilenameStrategyVegaHedgeDefaultSettings),  (u,c) => new FileCfgStrategy(u, c), () => new ConfigVegaGammaHedgeStrategy(StrategyType.VegaHedge)));
            cfgs.Add(_defaultGammaHedgeStrategy = FileCfgStrategy.LoadFromFile(FName(FilenameStrategyGammaHedgeDefaultSettings), (u,c) => new FileCfgStrategy(u, c), () => new ConfigVegaGammaHedgeStrategy(StrategyType.GammaHedge)));

            cfgs.Add(UI = FName(FilenameUISettings).LoadFromXml<ConfigUI>() ?? new ConfigUI());

            _tradingPeriods.Initialize();

            foreach(var pair in new IConfigPair[] {_defaultValuationParams, _defaultFuture, _defaultRegularStrategy, _defaultMMStrategy, _defaultVegaHedgeStrategy, _defaultGammaHedgeStrategy})
                pair.RealtimeUpdate = false;

            _configs = cfgs.ToArray();
            _configs.ForEach(c => c.ConfigurationChanged += OnConfigurationChanged);

            _general.CanUpdateConfig += DefaultVerifyConfig;
            _configSecSel.CanUpdateConfig += DefaultVerifyConfig;
            _valuationParams.List.ForEach(pair => pair.CanUpdateConfig += DefaultVerifyConfig);
            _strategies.List.ForEach(pair => pair.CanUpdateConfig += DefaultVerifyConfig);
            _series.List.ForEach(pair => pair.CanUpdateConfig += DefaultVerifyConfig);
            _futures.List.ForEach(pair => pair.CanUpdateConfig += DefaultVerifyConfig);
            _tradingPeriods.List.ForEach(pair => pair.CanUpdateConfig += DefaultVerifyConfig);

            UpdateRealtimeMode(true);
        }

        protected override void DisposeManaged() {
            _configs.ForEach(c => c.SaveEffectiveConfig());

            base.DisposeManaged();
        }

        #endregion

        static void DefaultVerifyConfig(IReadOnlyConfiguration cfg, CanUpdateEventArgs args) {
            cfg.VerifyConfig(args.Errors);

            if(args.Errors?.Count > 0)
                _log.AddWarningLog("Ошибки обновления конфига:\n" + args.Errors.Join("\n"));
        }

        void OnConfigurationChanged(ISaveableConfiguration config) {
            AddSaveAction(config, config.SaveEffectiveConfig);
        }

        void AddSaveAction(object key, Action action) {
            if(!RobotData.Dispatcher.CheckAccess())
                _log.Dbg.AddWarningLog("AddSaveAction called from non-gui thread");

            RobotData.Dispatcher.MyGuiAsync(() => {
                _saveActions[key] = action;
            });
        }

        public void UpdateRealtimeMode(bool realtime) {
            _inRealtimeMode = realtime;

            _general.RealtimeUpdate = realtime;
            _valuationParams.List.ForEach(pair => pair.RealtimeUpdate = realtime);
            _futures.List.ForEach(pair => pair.RealtimeUpdate = realtime);
            _series.List.ForEach(pair => pair.RealtimeUpdate = realtime);
            _tradingPeriods.List.ForEach(pair => pair.RealtimeUpdate = realtime);
        }

        public ICfgPairStrategy DefaultConfigByStrategyType(StrategyType stype) {
            switch(stype) {
                case StrategyType.Regular:      return _defaultRegularStrategy;
                case StrategyType.MM:           return _defaultMMStrategy;
                case StrategyType.VegaHedge:    return _defaultVegaHedgeStrategy;
                case StrategyType.GammaHedge:   return _defaultGammaHedgeStrategy;
            }
            throw new ArgumentException("stype");
        }

        #region lists add/remove

        public ICfgPairFuture GetFutureConfig(FuturesInfo future) {
            var cfg = (FileCfgFuture) _futures.List.FirstOrDefault(c => c.Effective.SecurityId == future.Id);

            if(cfg != null)
                return cfg;

            cfg = new FileCfgFuture(new ConfigFuture {SecurityId = future.Id}, null) {RealtimeUpdate = false};
            cfg.UI.CopyFrom((ConfigFuture)_defaultFuture.Effective);
            cfg.UI.SecurityId = future.Id;

            var errors = new List<string>();
            cfg.TryToApplyChanges(errors);
            if(errors.Any()) {
                _log.Dbg.AddErrorLog("Unable to initialized new future config: {0}", string.Join(";", errors));
                cfg.UndoUIChanges();
            }

            cfg.RealtimeUpdate = _inRealtimeMode;
            cfg.CanUpdateConfig += DefaultVerifyConfig;
            _futures.Add(cfg);

            return cfg;
        }

        public ICfgPairSeries GetSeriesConfig(OptionSeriesId seriesId) {
            var cfg = (FileCfgSeries)_series.List.FirstOrDefault(c => c.Effective.SeriesId == seriesId);

            if(cfg != null)
                return cfg;

            cfg = new FileCfgSeries(new ConfigSeries {SeriesId = seriesId}, null) {RealtimeUpdate = _inRealtimeMode};

            cfg.CanUpdateConfig += DefaultVerifyConfig;
            _series.Add(cfg);

            return cfg;
        }

        public bool DeleteFutureConfig(ICfgPairFuture cfg) {
            var result = _futures.Remove((FileCfgFuture)cfg);
            cfg.CanUpdateConfig -= DefaultVerifyConfig;
            return result;
        }

        public ICfgPairVP CreateNewVPConfig(OptionSeriesId serId, OptionTypes type, int shift) {
            var cfg = new FileCfgVP(new ConfigValuationParameters {
                SeriesId = serId,
                OptionType = type,
                AtmStrikeShift = shift
            }, null) {RealtimeUpdate = false};

            cfg.UI.CopyFrom((ConfigValuationParameters)_defaultValuationParams.Effective);
            cfg.UI.SeriesId = serId;
            cfg.UI.OptionType = type;
            cfg.UI.AtmStrikeShift = shift;
            
            var errors = new List<string>();
            cfg.TryToApplyChanges(errors);
            if(errors.Any()) {
                _log.Dbg.AddErrorLog("Unable to initialized new VP record: {0}", string.Join(";", errors));
                cfg.UndoUIChanges();
            }
            
            cfg.RealtimeUpdate = _inRealtimeMode;
            cfg.CanUpdateConfig += DefaultVerifyConfig;
            _valuationParams.Add(cfg);

            return cfg;
        }

        public bool DeleteVPConfig(ICfgPairVP cfg) {
            var result = _valuationParams.Remove((FileCfgVP)cfg);
            cfg.CanUpdateConfig -= DefaultVerifyConfig;
            return result;
        }

        public ICfgPairStrategy CreateNewStrategyConfig(StrategyType straType, OptionSeriesId seriesId, OptionTypes otype, int atmShift) {
            ConfigStrategy newCfg;

            FileCfgStrategy initFrom;

            switch(straType) {
                case StrategyType.Regular:
                    newCfg = new ConfigRegularStrategy();
                    initFrom = _defaultRegularStrategy;
                    break;
                case StrategyType.MM:
                    newCfg = new ConfigMMStrategy();
                    initFrom = _defaultMMStrategy;
                    break;
                case StrategyType.VegaHedge:
                    newCfg = new ConfigVegaGammaHedgeStrategy(StrategyType.VegaHedge);
                    initFrom = _defaultVegaHedgeStrategy;
                    break;
                case StrategyType.GammaHedge:
                    newCfg = new ConfigVegaGammaHedgeStrategy(StrategyType.GammaHedge);
                    initFrom = _defaultGammaHedgeStrategy;
                    break;
                default:
                    throw new ArgumentException("straType");
            }

            newCfg.SeriesId = seriesId;
            newCfg.OptionType = otype;
            newCfg.AtmStrikeShift = atmShift;

            var cfg = new FileCfgStrategy(newCfg, null) {RealtimeUpdate = false};

            cfg.UI.CopyFrom((ConfigStrategy)initFrom.Effective);
            cfg.UI.SeriesId = seriesId;
            cfg.UI.OptionType = otype;
            cfg.UI.AtmStrikeShift = atmShift;

            var errors = new List<string>();
            cfg.TryToApplyChanges(errors);
            if(errors.Any()) {
                _log.Dbg.AddErrorLog("Unable to initialized new strategy config: {0}", string.Join(";", errors));
                cfg.UndoUIChanges();
            }

            cfg.RealtimeUpdate = true;
            cfg.CanUpdateConfig += DefaultVerifyConfig;
            _strategies.Add(cfg);

            return cfg;
        }

        public static ICfgPairVP CreateVPPair(IConfigValuationParameters cfg, string filename = null) {
            var result = new FileCfgVP((ConfigValuationParameters)((ICloneable)cfg).Clone(), null);
            result.CanUpdateConfig += DefaultVerifyConfig;
            return result;
        }

        public static ICfgPairStrategy CreateStrategyPair(IConfigStrategy cfg, string filename = null) {
            var result = new FileCfgStrategy((ConfigStrategy)((ICloneable)cfg).Clone(), filename);
            result.CanUpdateConfig += DefaultVerifyConfig;
            return result;
        }

        public bool DeleteStrategyConfig(ICfgPairStrategy cfg) {
            var result = _strategies.Remove((FileCfgStrategy)cfg);
            cfg.CanUpdateConfig -= DefaultVerifyConfig;
            return result;
        }

        #endregion

        public static string FName(string fileName) {
            return SettingsDirectory + Path.DirectorySeparatorChar + fileName;
        }

        public void UpdateData() {
            if(_saveActions.Count == 0)
                return;

            var now = SteadyClock.Now;

            if(_lastSaveTime == default(DateTime)) {
                _lastSaveTime = now;
                return;
            }

            if(now - _lastSaveTime < _saveDelay)
                return;

            var actions = _saveActions.Values.ToList();
            _saveActions.Clear();
            _lastSaveTime = now;

            ThreadPool.QueueUserWorkItem(o => actions.ForEach(a => a()));
        }

        #region helper types

        class FileCfgGeneral : FileConfig<ConfigGeneral, IConfigGeneral, ICfgPairGeneral>, ICfgPairGeneral {
            public FileCfgGeneral(ConfigGeneral uiConfig, string filename) : base(uiConfig, filename) {}
            public override string ConfigName {get {return "Общие параметры";} set {} }
        }

        class FileCfgVP : FileConfig<ConfigValuationParameters, IConfigValuationParameters, ICfgPairVP>, ICfgPairVP {
            public FileCfgVP(ConfigValuationParameters uiConfig, string filename) : base(uiConfig, filename) {}

            string _altName;

            public override string ConfigName { get {return _altName ?? Effective.Id;} set {_altName = value;} }
        }

        class FileCfgFuture : FileConfig<ConfigFuture, IConfigFuture, ICfgPairFuture>, ICfgPairFuture {
            public FileCfgFuture(ConfigFuture uiConfig, string filename) : base(uiConfig, filename) {}
            public override string ConfigName {get {return "Фьючерс {0}".Put(Effective.SecurityId);} set {} }
        }

        class FileCfgStrategy : FileConfig<ConfigStrategy, IConfigStrategy, ICfgPairStrategy>, ICfgPairStrategy {
            public FileCfgStrategy(ConfigStrategy uiConfig, string filename) : base(uiConfig, filename) { }

            string _altName;

            public override string ConfigName { get {
                return _altName ?? "{0} {1} {2}".Put(Effective.StrategyType, 
                                         OptionStrikeShift.ShiftToString(Effective.AtmStrikeShift),
                                         Effective.SeriesId.StrFutSerCodeShortDate);
            } set {_altName = value;} }
        }

        class FileCfgSeries : FileConfig<ConfigSeries, IConfigSeries, ICfgPairSeries>, ICfgPairSeries {
            public FileCfgSeries(ConfigSeries uiConfig, string filename) : base(uiConfig, filename) { }

            public override string ConfigName { get { return Effective.SeriesId.StrFutDate; } set {} }
        }

        class FileCfgSecSel : FileConfig<ConfigSecuritySelection, IConfigSecuritySelection, ICfgPairSecSel>, ICfgPairSecSel {
            public FileCfgSecSel(ConfigSecuritySelection uiConfig, string filename) : base(uiConfig, filename) {}
            public override string ConfigName {get {return "Доступные инструменты";} set {} }
        }

        class FileCfgTradingPeriod : FileConfig<ConfigTradingPeriod, IConfigTradingPeriod, ICfgPairTradingPeriod>, ICfgPairTradingPeriod {
            public FileCfgTradingPeriod(ConfigTradingPeriod uiConfig, string filename) : base(uiConfig, filename) {}
            public override string ConfigName {get {return $"Торговый период '{EnumToStringConverter.Instance.Convert(Effective.PeriodType)}'";} set {}}
        }

        class FileCfgVPList : FileConfigList<FileCfgVP, ConfigValuationParameters, IConfigValuationParameters, ICfgPairVP>, ICfgPairVPList {
            public FileCfgVPList(IEnumerable<ConfigValuationParameters> uiConfigs, string filename, Func<ConfigValuationParameters, string, FileCfgVP> fileCfgCreator) 
                : base(uiConfigs, filename, fileCfgCreator) {}
        }

        class FileCfgStrategyList : FileConfigList<FileCfgStrategy, ConfigStrategy, IConfigStrategy, ICfgPairStrategy>, ICfgPairStrategyList {
            public FileCfgStrategyList(IEnumerable<ConfigStrategy> uiConfigs, string filename, Func<ConfigStrategy, string, FileCfgStrategy> fileCfgCreator) 
                : base(uiConfigs, filename, fileCfgCreator) {}
        }

        class FileCfgSeriesList : FileConfigList<FileCfgSeries, ConfigSeries, IConfigSeries, ICfgPairSeries>, ICfgPairSeriesList {
            public FileCfgSeriesList(IEnumerable<ConfigSeries> uiConfigs, string filename, Func<ConfigSeries, string, FileCfgSeries> fileCfgCreator) 
                : base(uiConfigs, filename, fileCfgCreator) {}
        }

        class FileCfgFutureList : FileConfigList<FileCfgFuture, ConfigFuture, IConfigFuture, ICfgPairFuture>, ICfgPairFutureList {
            public FileCfgFutureList(IEnumerable<ConfigFuture> uiConfigs, string filename, Func<ConfigFuture, string, FileCfgFuture> fileCfgCreator) 
                : base(uiConfigs, filename, fileCfgCreator) {}
        }

        class FileCfgPairTradingPeriodList : FileConfigList<FileCfgTradingPeriod, ConfigTradingPeriod, IConfigTradingPeriod, ICfgPairTradingPeriod>, ICfgPairTradingPeriodList {
            public FileCfgPairTradingPeriodList(IEnumerable<ConfigTradingPeriod> uiConfigs, string filename, Func<ConfigTradingPeriod, string, FileCfgTradingPeriod> fileCfgCreator) 
                : base(uiConfigs, filename, fileCfgCreator) {}

            bool _initialized;

            public void Initialize() {
                if(_initialized)
                    return;

                _initialized = true;

                if(!List.Any()) {
                    var ptypes = new[] {
                        TradingPeriodType.MainBeforeClearing,
                        TradingPeriodType.MainAfterClearing,
                        TradingPeriodType.Evening,
                    };

                    foreach(var t in ptypes) {
                        var cfg = new ConfigTradingPeriod {PeriodType = t};
                        cfg.Reset();
                        Add(new FileCfgTradingPeriod(cfg, null));
                    }
                }

                foreach(var pair in List) {
                    pair.CanUpdateConfig += (c, args) => {
                        if(args.ChangedOnly(nameof(IConfigTradingPeriod.StopMMByTimePercent)))
                            args.AllowInstantUpdate = true;
                    };
                }
            }

            public ICfgPairTradingPeriod this[TradingPeriodType ptype] { get {
                 return List.FirstOrDefault(p => p.Effective.PeriodType == ptype);
            }}
        }

        #endregion
    }

    public interface IConfigPair {
        bool RealtimeUpdate {get; set;}
        bool IsEffectiveConfigUpToDate {get;}
        string ConfigName {get; set;}
        
        void TryToApplyChanges(List<string> errors);
        void UndoUIChanges();
    }

    public interface IConfigPair<out T, out R, out I> : IConfigPair where I:IConfigPair<T, R, I> {
        T UI {get;}
        R Effective {get;}

        event Action<R, CanUpdateEventArgs> CanUpdateConfig;
        event Action<I, string[]> EffectiveConfigChanged;
        event Action<I> UIEffectiveDifferent;
    }

    public interface IConfigPairList<out T, out R, out I> where I : IConfigPair<T, R, I> {
        IEnumerable<I> List {get;}

        event Action<bool> ListOrItemChanged;
    }

    public interface ICfgPairGeneral    : IConfigPair<ConfigGeneral, IConfigGeneral, ICfgPairGeneral> {}
    public interface ICfgPairVP         : IConfigPair<ConfigValuationParameters, IConfigValuationParameters, ICfgPairVP> {}
    public interface ICfgPairFuture     : IConfigPair<ConfigFuture, IConfigFuture, ICfgPairFuture> {}
    public interface ICfgPairStrategy   : IConfigPair<ConfigStrategy, IConfigStrategy, ICfgPairStrategy> {}
    public interface ICfgPairSecSel     : IConfigPair<ConfigSecuritySelection, IConfigSecuritySelection, ICfgPairSecSel> {}
    public interface ICfgPairTradingPeriod : IConfigPair<ConfigTradingPeriod, IConfigTradingPeriod, ICfgPairTradingPeriod> {}
    public interface ICfgPairSeries     : IConfigPair<ConfigSeries, IConfigSeries, ICfgPairSeries> {}

    public interface ICfgPairVPList         : IConfigPairList<ConfigValuationParameters, IConfigValuationParameters, ICfgPairVP> {}
    public interface ICfgPairFutureList     : IConfigPairList<ConfigFuture, IConfigFuture, ICfgPairFuture> {}
    public interface ICfgPairStrategyList   : IConfigPairList<ConfigStrategy, IConfigStrategy, ICfgPairStrategy> {}
    public interface ICfgPairSeriesList     : IConfigPairList<ConfigSeries, IConfigSeries, ICfgPairSeries> {}

    public interface ICfgPairTradingPeriodList : IConfigPairList<ConfigTradingPeriod, IConfigTradingPeriod, ICfgPairTradingPeriod> {
        ICfgPairTradingPeriod this[TradingPeriodType ptype] {get;}
    }
}
