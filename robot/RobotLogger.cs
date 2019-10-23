using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using AsyncHandler;
using Ecng.Collections;
using Ecng.Common;
using Ecng.ComponentModel;
using MoreLinq;
using OptionBot.Config;
using OptionBot.Xaml;
using StockSharp.BusinessEntities;
using StockSharp.Messages;

namespace OptionBot.robot {
    /// <summary>Логгер робота.</summary>
    public class RobotLogger : Disposable {
        #region fields/properties

        readonly Logger _log = new Logger("RobotLogger");

        readonly Controller _controller;

        DateTime _lastFlushTime;
        readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(5);
        HTCancellationToken _restartToken;

        string DirName;

        public HandlerThread LoggerThread {get;}
        public bool IsStarted {get; private set;}

        readonly IClock Clock;

        readonly Dictionary<string, LoggerFile> _files = new Dictionary<string, LoggerFile>();
        readonly Dictionary<string, LoggerModule> _loggers = new Dictionary<string, LoggerModule>();

        long _logId;
        long NextLogId => Interlocked.Increment(ref _logId);

        PortfolioLogger _portfolioLogger;
        PositionLogger _positionsLogger;
        OrderLogger _orderLogger;
        TradeLogger _tradeLogger;
        AtmStrikesLogger _atmStrikesLogger;
        VPLogger _vpLogger;
        SeriesLogger _seriesLogger;
        ConfigGeneralLogger _configGeneralLogger;
        ConfigFutureLogger _configFutureLogger;
        OrderPerformanceLogger _orderPerformanceLogger;
        DataThreadLogger _msgLogger;
        MessageProcessorLogger _msgProcessorLogger;
        MMObligationsLogger _mmObligationsLogger;
        VolumeStatsLogger _volStatsLogger;
        CalcSeriesLogger _calcSeriesLogger;
        CurveParamsLogger _curveParamsLogger;
        CurveModelValueLogger _curveModelValueLogger;
        CurveSnapLogger _curveSnapLogger;

        public ConfigGeneralLogger CfgGeneral {get {return _configGeneralLogger ?? (_configGeneralLogger = GetLoggerModule(ConfigGeneralLogger.ModuleName, () => new ConfigGeneralLogger(this))); }}
        public ConfigFutureLogger CfgFuture {get {return _configFutureLogger ?? (_configFutureLogger = GetLoggerModule(ConfigFutureLogger.ModuleName, () => new ConfigFutureLogger(this))); }}
        public PortfolioLogger Portfolio {get { return _portfolioLogger ?? (_portfolioLogger = GetLoggerModule(PortfolioLogger.ModuleName, () => new PortfolioLogger(this))); }}
        public PositionLogger Positions {get { return _positionsLogger ?? (_positionsLogger = GetLoggerModule(PositionLogger.ModuleName, () => new PositionLogger(this))); }}
        public OrderLogger Orders {get { return _orderLogger ?? (_orderLogger = GetLoggerModule(OrderLogger.ModuleName, () => new OrderLogger(this))); }}
        public TradeLogger Trades {get { return _tradeLogger ?? (_tradeLogger = GetLoggerModule(TradeLogger.ModuleName, () => new TradeLogger(this))); }}
        public AtmStrikesLogger AtmStrikes {get { return _atmStrikesLogger ?? (_atmStrikesLogger = GetLoggerModule(AtmStrikesLogger.ModuleName, () => new AtmStrikesLogger(this))); }}
        public MessageProcessorLogger MsgProcessor {get { return _msgProcessorLogger ?? (_msgProcessorLogger = GetLoggerModule(MessageProcessorLogger.ModuleName, () => new MessageProcessorLogger(this))); }}
        public MMObligationsLogger MMObligations {get {return _mmObligationsLogger ?? (_mmObligationsLogger = GetLoggerModule(MMObligationsLogger.ModuleName, () => new MMObligationsLogger(this))); }}
        public VolumeStatsLogger VolumeStats {get {return _volStatsLogger ?? (_volStatsLogger = GetLoggerModule(VolumeStatsLogger.ModuleName, () => new VolumeStatsLogger(this))); }}
        public CalcSeriesLogger CalcSeriesLog {get {return _calcSeriesLogger ?? (_calcSeriesLogger = GetLoggerModule(CalcSeriesLogger.ModuleName, () => new CalcSeriesLogger(this))); }}
        public CurveParamsLogger CurveParamsLog {get {return _curveParamsLogger ?? (_curveParamsLogger = GetLoggerModule(CurveParamsLogger.ModuleName, () => new CurveParamsLogger(this))); }}
        public CurveModelValueLogger CurveModelValueLog {get {return _curveModelValueLogger ?? (_curveModelValueLogger = GetLoggerModule(CurveModelValueLogger.ModuleName, () => new CurveModelValueLogger(this))); }}
        public CurveSnapLogger CurveSnapLog {get {return _curveSnapLogger ?? (_curveSnapLogger = GetLoggerModule(CurveSnapLogger.ModuleName, () => new CurveSnapLogger(this))); }}

        public HedgeLogger HedgeModule(FuturesInfo future) { return GetLoggerModule(future.LogName, () => new HedgeLogger(this, future)); }
        public FutureQuotesLogger Future(FuturesInfo future) { return GetLoggerModule(future.LogName+"_Quotes", () => new FutureQuotesLogger(this, future)); }
        public OptionLogger Option(OptionInfo option) { return GetLoggerModule(option.LogName, () => new OptionLogger(this, option)); }

        public LatencyLogger LatencyMain(OptionInfo option) { return GetLoggerModule("latency" + option.LogName, () => new LatencyLogger(this, "main", option.Code)); }
        public LatencyLogger LatencyMainCalc(OptionInfo option) { return GetLoggerModule("latency_ivcalc" + option.LogName, () => new LatencyLogger(this, "iv_calc", option.Code)); }
        public LatencyLogger LatencyHedge(FuturesInfo future) { return GetLoggerModule("latency_hedge" + future.LogName, () => new LatencyLogger(this, "hedge", future.Code)); }
        public LatencyLogger LatencyHedgeCalc(FuturesInfo future) { return GetLoggerModule("latency_hedgecalc" + future.LogName, () => new LatencyLogger(this, "hedge_calc", future.Code)); }
        public LatencyLogger LatencyOrder {get {return GetLoggerModule("latency_order", () => new LatencyLogger(this, "order", string.Empty));}}

        public StrategyLogger Strategy(StrategyType type) {
            return GetLoggerModule(StrategyLogger.GetModuleName(type), () => new StrategyLogger(this, type));
        }

        public VPLogger VP {get { return _vpLogger ?? (_vpLogger = GetLoggerModule(VPLogger.ModuleName, () => new VPLogger(this))); }}
        public SeriesLogger Series {get { return _seriesLogger ?? (_seriesLogger = GetLoggerModule(SeriesLogger.ModuleName, () => new SeriesLogger(this))); }}

        public OptionOrderActionLogger OptionOrderAction {get {return GetLoggerModule(OptionOrderActionLogger.ModuleName, () => new OptionOrderActionLogger(this)); }}

        public ModelExtLogger ModelExt(OptionInfo option) { return GetLoggerModule("modelext" + option.LogName, () => new ModelExtLogger(this, option)); }

        public OrderPerformanceLogger OrderPerfLogger {get {return _orderPerformanceLogger ?? (_orderPerformanceLogger = GetLoggerModule(OrderPerformanceLogger.ModuleName, () => new OrderPerformanceLogger(this))); }}
        public DataThreadLogger MsgLogger {get {return _msgLogger ?? (_msgLogger = GetLoggerModule(DataThreadLogger.ModuleName, () => new DataThreadLogger(this))); }}

        #endregion

        public RobotLogger(Controller ctl) {
            _logId = Properties.Settings.Default.RobotLogId;
            LoggerThread = new HandlerThread("logger") {ThreadPriority = ThreadPriority.BelowNormal};
            Clock = ctl.Connector;
            _controller = ctl;
            LoggerThread.Start();
        }

        protected override void DisposeManaged() {
            _log.Dbg.AddInfoLog("RobotLogger.Dispose()");

            Action dispose = () => {
                _loggers.Values.ForEach(l => l.Flush());
                Stop();
                Properties.Settings.Default.RobotLogId = _logId;
                Properties.Settings.Default.Save();
                LoggerThread.Dispose();
            };

            if(LoggerThread.IsAlive)
                LoggerThread.ExecuteAsync(dispose);
            else
                dispose();

            base.DisposeManaged();
        }

        /// <summary>Метод, выполняющий логирование в отдельном потоке.</summary>
        void AddLog(LoggerFile loggerFile, Func<string> getMessage) {
            LoggerThread.ExecuteAsync(() => {
                if(!IsStarted) {
                    _log.Dbg.AddWarningLog(loggerFile.Name + ": " + getMessage());
                    return;
                }

                var msg = getMessage();
                var file = loggerFile.OutStream;

                if(file != null) {
                    try   { file.WriteLine(msg); } 
                    catch { _log.Dbg.AddInfoLog(loggerFile.Name + ": " + msg); }
                } else {
                    _log.Dbg.AddInfoLog(loggerFile.Name + ": " + msg);
                }

                var now = SteadyClock.Now;
                if(now - _lastFlushTime > _flushInterval) {
                    _lastFlushTime = now;

                    LoggerFile[] loggerFiles;
                    lock(_files)
                        loggerFiles = _files.Values.ToArray();

                    loggerFiles.ForEach(l => l.Flush());
                }
            });
        }

        LoggerFile GetLoggerFile(LoggerModule module) {
            var name = module.Filename;
            var nameLower = name.ToLower(Util.RuCulture);
            LoggerFile mod;
            lock(_files)
                mod = _files.TryGetValue(nameLower);
            var type = module.GetType();
            if(mod != null) {
                if(mod.ModuleType != type)
                    throw new InvalidOperationException("wrong type for loggertype. expected {0}, got {1}".Put(mod.ModuleType.Name, type.Name));
                return mod;
            }

            var logFile = new LoggerFile(this, type, name, module.GetFieldNames());
            lock(_files)
                _files[nameLower] = logFile;

            LoggerThread.ExecuteAsync(() => { if(IsStarted) logFile.Start(); });
            return logFile;
        }

        T GetLoggerModule<T>(string name, Func<T> creator) where T:LoggerModule {
            lock(_loggers) {
                var mod = _loggers.TryGetValue(name);
                var modT = mod as T;
                if(modT != null) return modT;
                if(mod != null) throw new InvalidOperationException("logger '{0}' already exists".Put(name));

                var logger = creator();
                logger.InitModule();
                _loggers[name] = logger;

                return logger;
            }
        }

        #region start/stop

        public void Start() {
            LoggerThread.ExecuteSyncCheck(() => {
                if(IsStarted) {
                    _log.Dbg.AddWarningLog("start(): logger is already started. stopping...");
                    Stop();
                }
                IsStarted = true;

                var now = Clock.Now;
                ScheduleRestart(now);

                try {
                    _log.Dbg.AddInfoLog("start()");
                    DirName = "RobotLogs\\{0:ddMMMyyyy}".Put(now);

                    if(!Directory.Exists(DirName)) {
                        _logId = 0;
                        Directory.CreateDirectory(DirName);
                    }

                    LoggerFile[] loggerFiles;
                    lock(_files)
                        loggerFiles = _files.Values.ToArray();

                    loggerFiles.ForEach(l => l.Start());
                } catch(Exception e) {
                    _log.AddErrorLog("ошибка открытия файлов для логирования: {0}", e);
                    Stop();
                }
            });
        }

        void Stop() {
            LoggerThread.ExecuteSyncCheck(() => {
                if(!IsStarted) {
                    _log.Dbg.AddWarningLog("stop(): logger is not started");
                    return;
                }
                _log.Dbg.AddInfoLog("stop()");

                Util.CancelDelayedAction(ref _restartToken);
                IsStarted = false;

                LoggerFile[] loggerFiles;
                lock(_files)
                    loggerFiles = _files.Values.ToArray();

                loggerFiles.ForEach(l => l.Stop());
            });
        }

        void ScheduleRestart(DateTime now) {
            Util.CancelDelayedAction(ref _restartToken);
            var restartTime = now.Date + TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1);

            LoggerThread.DelayedAction(() => {
                if(!IsStarted) return;

                _log.AddInfoLog("Рестарт модуля логирования...");
                Stop();
                Start();
            }, restartTime - now, null, "restart logger");
        }

        #endregion

        /// <summary>Базовый класс для модуля логирования.</summary>
        class LoggerFile : Disposable {
            public const string HeaderPrefix = "id;time;";
            readonly RobotLogger _parent;
            readonly string[] _fieldNames;

            public string Name {get; private set;}
            public StreamWriter OutStream {get; private set;}

            public Type ModuleType {get; private set;}

            public LoggerFile(RobotLogger parent, Type moduleType, string name, IEnumerable<string> fieldNames) {
                _parent = parent;
                ModuleType = moduleType;
                _fieldNames = fieldNames.ToArray();
                Name = name;
            }

            protected override void DisposeManaged() {
                Stop();
                base.DisposeManaged();
            }

            public void Log(LoggerModule module, DateTime time = default(DateTime)) {
                try {
                    if(time == default(DateTime)) time = _parent.Clock.Now;
                    var values = module.GetValues().ToArray();
                    if(values[0] == null) values[0] = string.Empty; // RTFM for string.Join(string, params object[])
                    _parent.AddLog(this, () => "{0};{1:HH:mm:ss.fff};{2}".Put(_parent.NextLogId, time, string.Join(";", values)));
                } catch(Exception e) {
                    _parent._log.Dbg.AddWarningLog("{0}: logger exception: {1}", Name, e);
                }
            }

            public void Log(Func<string> getMessage, DateTime time = default(DateTime)) {
                try { 
                    if(time == default(DateTime)) time = _parent.Clock.Now;
                    var message = getMessage();
                    _parent.AddLog(this, () => "{0};{1:HH:mm:ss.fff};{2}".Put(_parent.NextLogId, time, message));
                } catch(Exception e) {
                    _parent._log.Dbg.AddWarningLog("{0}: logger exception: {1}", Name, e);
                }
            }

            public void Log(string message, DateTime time = default(DateTime)) {
                try { 
                    if(time == default(DateTime)) time = _parent.Clock.Now;
                    _parent.AddLog(this, () => "{0};{1:HH:mm:ss.fff};{2}".Put(_parent.NextLogId, time, message));
                } catch(Exception e) {
                    _parent._log.Dbg.AddWarningLog("{0}: logger exception: {1}", Name, e);
                }
            }

            public void Flush() {
                if(OutStream != null) OutStream.Flush();
            }

            public void Start() {
                try {
                    if(OutStream != null) {
                        _parent._log.Dbg.AddWarningLog("{0}: already started", Name);
                        Stop();
                    }
                    _parent._log.Dbg.AddInfoLog("Initializing module {0}", Name);
                    var fname = _parent.DirName + Path.DirectorySeparatorChar + Name + ".csv";
                    var exists = File.Exists(fname);
                    OutStream = new StreamWriter(fname, true, Encoding.GetEncoding(1251));
                    if(!exists) WriteHeader();
                } catch(Exception e) {
                    _parent._log.AddErrorLog("Не удалось стартовать модуль логирования {0}: {1}", Name, e);
                }
            }

            public void Stop() {
                var f = OutStream;
                OutStream = null;
                f?.Dispose();
            }

            void WriteHeader() {
                _parent.AddLog(this, () => HeaderPrefix + string.Join(";", _fieldNames));
            }
        }

        public abstract class LoggerModule {
            protected readonly RobotLogger _parent;
            bool _initialized;
            LoggerFile _file;
            Tuple<string, Func<object>>[] _fields;

            protected Tuple<string, Func<object>>[] Fields => _fields;

            protected HandlerThread LoggerThread => _parent.LoggerThread;

            public string Filename {get;}

            protected LoggerModule(RobotLogger parent, string filename) {
                _parent = parent;
                Filename = filename;
            }

            public virtual void InitModule() {
                _fields = GetFields().ToArray();
                _file = _parent.GetLoggerFile(this);
                _initialized = true;
            }

            public virtual IEnumerable<object> GetValues() {
                return _fields.Select(t => t.Item2());
            }

            public virtual IEnumerable<string> GetFieldNames() {
                return _fields.Select(f => f.Item1);
            }

            public virtual void Flush() { }

            protected abstract TupleList<string, Func<object>> GetFields();

            public void Commit() {
                if(!_initialized) {
                    _parent._log.Dbg.AddErrorLog("Module {0} ({1}) is not initialized.", GetType().Name, Filename);
                    return;
                }

                _file.Log(this);
            }
        }

        /// <summary>Логгер конфигурации.</summary>
        public class ConfigGeneralLogger : LoggerModule {
            public const string ModuleName = "CfgGeneral";

            IConfigGeneral _settings;

            public ConfigGeneralLogger(RobotLogger parent) : base(parent, ModuleName) { }

            public void Log(IConfigGeneral settings) {
                _settings = settings;
                Commit();
            }

            protected override TupleList<string, Func<object>> GetFields() {
                var fields = new TupleList<string, Func<object>>();
                fields.AddRange(ConfigGeneral.GetLoggerFields().Select(name => Tuple.Create(name, (Func<object>)null)));
                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return _settings.GetLoggerValues();
            }
        }

        /// <summary>Логгер MM info.</summary>
        public class MMObligationsLogger : LoggerModule {
            public const string ModuleName = "MMObligations";

            MMInfoRecord _rec;

            public MMObligationsLogger(RobotLogger parent) : base(parent, ModuleName) { }

            public void Log(MMInfoRecord rec) {
                _rec = rec;
                Commit();
            }

            protected override TupleList<string, Func<object>> GetFields() {
                var fields = new TupleList<string, Func<object>>();
                fields.AddRange(MMInfoRecord.GetLoggerFields().Select(name => Tuple.Create(name, (Func<object>)null)));
                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return _rec.GetLoggerValues();
            }
        }

        /// <summary>Логгер конфигурации.</summary>
        public class ConfigFutureLogger : LoggerModule {
            public const string ModuleName = "CfgFuture";

            IConfigFuture _config;

            public ConfigFutureLogger(RobotLogger parent) : base(parent, ModuleName) { }

            public void Log(IConfigFuture cfg) {
                _config = cfg;
                Commit();
            }

            protected override TupleList<string, Func<object>> GetFields() {
                var fields = new TupleList<string, Func<object>>();
                fields.AddRange(ConfigFuture.GetLoggerFields().Select(name => Tuple.Create(name, (Func<object>)null)));
                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return _config.GetLoggerValues();
            }
        }

        public class StrategyLogger : LoggerModule {
            readonly StrategyType _strategyType;
            readonly object _lock = new object();

            VMStrategy _strategy;

            public StrategyLogger(RobotLogger parent, StrategyType strategyType) : base(parent, GetModuleName(strategyType)) {
                _strategyType = strategyType;
            }

            public static string GetModuleName(StrategyType strategyType) { return "Strategies_" + strategyType; }

            protected override TupleList<string, Func<object>> GetFields() {
                // if this list changed, also change Take(N) in GetValues()
                var fields = new TupleList<string, Func<object>> {
                    {"strategy_id",     () => _strategy.Id },
                    {"enabled",         () => _strategy.IsActive },
                    {"state",           () => _strategy.State },
                    {"future",          () => _strategy.CfgStrategy.SeriesId.FutureCode },
                    {"option",          () => _strategy.Option.With(o => o.Code) },
                    {"option_exp_date", () => _strategy.Option.With(o => o.Series.ExpirationDate.ToString("dd.MM.yyyy")) },
                    {"option_can_trade",() => _strategy.Option.With(o => o.CanStartStrategies.ToString()) },
                };

                ConfigStrategy.GetLoggerFields().ForEach(name => fields.Add(name, null));

                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return Fields.Take(7).Select(f => f.Item2()).Concat(_strategy.CfgStrategy.GetLoggerValues());
            }

            public void LogStrategy(VMStrategy strategy) {
                lock(_lock) {
                    _strategy = strategy;
                    Commit();
                }
            }
        }

        public class VPLogger : LoggerModule {
            public const string ModuleName = "ValuationParams";
            VPWrapper _wrapper;

            public VPLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                // if this list changed, also change Take(N) in GetValues()
                var fields = new TupleList<string, Func<object>> {
                    {"option", () => _wrapper.Option.With(o => o.Code) },
                };

                ConfigValuationParameters.GetLoggerFields().ForEach(name => fields.Add(name, null));

                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return Fields.Take(1).Select(f => f.Item2()).Concat(_wrapper.Effective.GetLoggerValues());
            }

            public void LogVP(VPWrapper wrapper) {
                _wrapper = wrapper;
                Commit();
            }
        }

        /// <summary>Логгер задержек.</summary>
        public class LatencyLogger : LoggerModule {
            readonly string _moduleName;
            string _name2;
            DateTime _startTime, _endTime;
            readonly Stopwatch _watch = new Stopwatch();

            public LatencyLogger(RobotLogger parent, string modName, string name2) : base(parent, "Latency") {
                _moduleName = modName;
                _name2 = name2;
            }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "name",               () => _moduleName },
                    { "name2",              () => _name2 },
                    { "start_time",         () => "{0:HH:mm:ss.fff}".Put(_startTime) },
                    { "end_time",           () => "{0:HH:mm:ss.fff}".Put(_endTime) },
                    { "time_ms",            () => "{0:0.#####}".Put((_endTime-_startTime).TotalMilliseconds) },
                };
            }

            public void StartTimer() {
//                    if(_watch.IsRunning) _parent._parent._log.Dbg.AddWarningLog("LatencyLogger({0}).start: watch is already running", _name2);
                _watch.Restart();
            }

            public void StopAndCommit() {
                if(!_watch.IsRunning) { _parent._log.Dbg.AddWarningLog("LatencyLogger({0}).stop: watch is not running", _name2); return; }

                _watch.Stop();
    
                var time = _parent.Clock.Now;
                _startTime = time - _watch.Elapsed;
                _endTime = time;
                Commit();
            }

            public void LogOrderLatency(OrderEx order) {
                var diff = order.Latency;

                if(diff == null) {
                    _parent._log.Dbg.AddWarningLog("LogOrderLatency: latency is not calculated for {0}", order.TransactionId);
                    return;
                }

                var time = _parent.Clock.Now;
                _startTime = time - diff.Value;
                _endTime = time;
                _name2 = order.TransactionId.ToString();

                Commit();
            }

            public void LogHeartbeat(DateTime serverTime) {
                var time = _parent.Clock.Now;
                _startTime = serverTime;
                _endTime = time;
                Commit();
            }
        }

        /// <summary>Логгер модуля хеджирования.</summary>
        public class HedgeLogger : LoggerModule {
            readonly FuturesInfo _future;

            MarketDepthPair _bestPair;
            int _position;
            FuturesInfo.FutureParams _p;

            public HedgeLogger(RobotLogger parent, FuturesInfo fut) : base(parent, fut.Code) {
                _future = fut;
            }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "code",               () => _future.Code },
                    { "is_active",          () => _future.IsActive },
                    { "bid",                () => _bestPair.Bid.Return(q => q.Price, 0) },
                    { "offer",              () => _bestPair.Ask.Return(q => q.Price, 0) },
                    { "balance",            () => _position },
                    { "delta_exposition",   () => _future.Exposition },
                    { "se_time",            () => _future.BidAskTime.With2(dt => "{0:HH:mm:ss.fff}".Put(dt)) },
                    { "vega_portfolio",     () => _p.VegaPortfolio },
                    { "vega_call_buy_limit",() => _p.VegaCallBuyLimit },
                    { "vega_put_buy_limit", () => _p.VegaPutBuyLimit },
                    { "vega_call_sell_limit",() => _p.VegaCallSellLimit },
                    { "vega_put_sell_limit",() => _p.VegaPutSellLimit },
                    { "vega_buy_limit",     () => _p.VegaBuyLimit },
                    { "vega_sell_limit",    () => _p.VegaSellLimit },
                    { "vega_buy_target",    () => _p.VegaBuyTarget },
                    { "vega_sell_target",   () => _p.VegaSellTarget },
                    { "mm_vega_buy_limit",  () => _p.MMVegaBuyLimit },
                    { "mm_vega_sell_limit", () => _p.MMVegaSellLimit },
                    { "gamma_portfolio",    () => _p.GammaPortfolio },
                    { "gamma_buy_limit",    () => _p.GammaBuyLimit },
                    { "gamma_sell_limit",   () => _p.GammaSellLimit },
                    { "gamma_buy_target",   () => _p.GammaBuyTarget },
                    { "gamma_sell_target",  () => _p.GammaSellTarget },
                    { "mm_gamma_buy_limit", () => _p.MMGammaBuyLimit },
                    { "mm_gamma_sell_limit",() => _p.MMGammaSellLimit },
                    { "vanna_portfolio",    () => _p.VannaPortfolio },
                    { "vanna_long_limit",   () => _p.VannaLongLimit },
                    { "vanna_short_limit",  () => _p.VannaShortLimit },
                    { "vomma_portfolio",    () => _p.VommaPortfolio },
                    { "vomma_long_limit",   () => _p.VommaLongLimit },
                    { "vomma_short_limit",  () => _p.VommaShortLimit },
                    { "theta_portfolio",    () => _p.ThetaPortfolio },
                };
            }

            public void Log(int position) {
                _bestPair = _future.NativeSecurity.BestPair;
                if(_bestPair == null) return;

                _position = position;
                _p = _future.Params;

                Commit();
            }
        }

        /// <summary>Логгер котировок фьючерса.</summary>
        public class FutureQuotesLogger : LoggerModule {
            DateTime _localTime, _serverTime;
            decimal _bid, _offer;
            decimal _prevBid, _prevOffer;

            public FutureQuotesLogger(RobotLogger parent, FuturesInfo fut) : base(parent, fut.Code+"_Quotes") { }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "bid",                () => _bid },
                    { "offer",              () => _offer },
                    { "local_time",         () => $"{_localTime:HH:mm:ss.fff}" },
                    { "server_time",        () => $"{_serverTime:HH:mm:ss.fff}" },
                };
            }

            public void LogQuotes(MarketDepth depth) {
                var bid = depth.BestBid?.Price ?? 0m;
                var offer = depth.BestAsk?.Price ?? 0m;

                if(_prevBid == bid && _prevOffer == offer)
                    return;

                _prevBid = bid;
                _prevOffer = offer;

                var localTime = _parent.Clock.Now;
                var serverTime = depth.LastChangeTime;

                LoggerThread.ExecuteForceAsync(() => {
                    _localTime = localTime;
                    _serverTime = serverTime;
                    _bid = bid;
                    _offer = offer;

                    Commit();
                });
            }
        }

        public class OptionLogger : LoggerModule {
            readonly OptionInfo _option;
            IOptionModelData _lastModelData;
            MarketDepthPair _bestPair;
            Trade _lastTrade;

            readonly RobotOptionOrder[] _optionOrders = new RobotOptionOrder[16];

            public OptionLogger(RobotLogger parent, OptionInfo option) : base(parent, option.Code) {
                _option = option;
            }

            public OptionLogger FreezeData() {
                _lastModelData = _option.Model.LastData;
                _lastTrade = _option.NativeSecurity.LastTrade;
                _bestPair = _option.NativeSecurity.With(s => s.BestPair);
                return this;
            }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "future",             () => _option.Future.Code },
                    { "series",             () => _option.Series.SeriesId },
                    { "code",               () => _option.Code },
                    { "type",               () => _option.OptionType },
                    { "bid",                () => _bestPair.Bid.Return(b => b.Price, 0) },
                    { "offer",              () => _bestPair.Ask.Return(a => a.Price, 0) },
                    { "balance",            () => _option.PositionValue },
                    { "strike",             () => _option.Strike.Strike },
                    { "expiration_date",    () => "{0:dd-MMM-yyyy}".Put(_option.Series.ExpirationDate) },
                    { "min_price_step",     () => _option.MinStepSize },
                    { "se_time_quote",      () => _option.BidAskTime },
                    { "se_deal_volume",     () => _lastTrade.With(t => "{0}".Put(t.Volume)) },
                    { "se_deal_time",       () => _lastTrade.With(t => "{0:HH:mm:ss.fff}".Put(t.Time)) },
                    { "se_deals_today",     () => _option.NativeSecurity.Volume },
                    { "se_opened_position", () => _option.NativeSecurity.OpenInterest },
                    { "our_deal_time",      () => _option.MyLastTrade.With(t => "{0:HH:mm:ss.fff}".Put(t.Time)) },
                    { "volume_diff",        () => _option.VolumeDiff },
                    { "our_deals_today",    () => _option.OwnVolume },
                    { "our_mm_deals_today", () => _option.OwnMMVolume },
                    { "is_active",          () => _option.IsActive },
                    { "atm_strike_shift",   () => _option.AtmShift.Return(shift => shift.ShiftValue.ToString(CultureInfo.InvariantCulture), "NA") },
                    { "iv_bid",             () => _lastModelData.IvBid },
                    { "iv_offer",           () => _lastModelData.IvOffer },
                    { "mkt_spread",         () => _lastModelData.MarketSpread },
                    { "current_spread",     () => _lastModelData.CurrentSpread },
                    { "target_spread",      () => _lastModelData.ValuationTargetSpread },
                    { "market_iv_bid",      () => _lastModelData.MarketIvBid },
                    { "market_iv_offer",    () => _lastModelData.MarketIvOffer },
                    { "iv_spread",          () => _lastModelData.IvSpread },
                    { "market_iv_spread",   () => _lastModelData.MarketIvSpread },
                    { "iv_average",         () => _lastModelData.IvAverage },
                    { "market_iv_average",  () => _lastModelData.MarketIvAverage },
                    { "calc_iv_bid",        () => _lastModelData.Input.OptionCalcBid },
                    { "calc_iv_offer",      () => _lastModelData.Input.OptionCalcAsk },
                    { "gamma",              () => _lastModelData.Gamma },
                    { "empiric_delta",      () => _option.EmpiricDelta },
                    { "delta_bid",          () => _lastModelData.DeltaBid },
                    { "vega",               () => _lastModelData.Vega },
                    { "theta",              () => _lastModelData.Theta },
                    { "moneyness",          () => _lastModelData.Moneyness },
                    { "strike_moneyness",   () => _lastModelData.StrikeMoneyness },
                    { "initial_delta",      () => _lastModelData.InitialDelta },
                    { "best_delta_expectation", () => _lastModelData.StrikeMoneyness },
                    { "illiquid_delta",     () => _lastModelData.IlliquidDelta },
                    { "illiquid_iv",        () => _lastModelData.IlliquidIv },
                    { "greeks_regime",      () => _lastModelData.GreeksRegime },
                    { "illiquid_vega",      () => _lastModelData.IlliquidVega },
                    { "illiquid_gamma",     () => _lastModelData.IlliquidGamma },
                    { "illiquid_theta",     () => _lastModelData.IlliquidTheta },
                    { "active_strategies",  () => _option.TradingModule.ActiveStrategiesString },
                    { "reg_buy_open",       () => GetOrderBalance(StrategyType.Regular,     Sides.Buy, true) },
                    { "reg_buy_close",      () => GetOrderBalance(StrategyType.Regular,     Sides.Buy, false) },
                    { "reg_sell_open",      () => GetOrderBalance(StrategyType.Regular,     Sides.Sell, true) },
                    { "reg_sell_close",     () => GetOrderBalance(StrategyType.Regular,     Sides.Sell, false) },
                    { "mm_buy_open",        () => GetOrderBalance(StrategyType.MM,          Sides.Buy, true) },
                    { "mm_buy_close",       () => GetOrderBalance(StrategyType.MM,          Sides.Buy, false) },
                    { "mm_sell_open",       () => GetOrderBalance(StrategyType.MM,          Sides.Sell, true) },
                    { "mm_sell_close",      () => GetOrderBalance(StrategyType.MM,          Sides.Sell, false) },
                    { "vega_buy_open",      () => GetOrderBalance(StrategyType.VegaHedge,   Sides.Buy, true) },
                    { "vega_buy_close",     () => GetOrderBalance(StrategyType.VegaHedge,   Sides.Buy, false) },
                    { "vega_sell_open",     () => GetOrderBalance(StrategyType.VegaHedge,   Sides.Sell, true) },
                    { "vega_sell_close",    () => GetOrderBalance(StrategyType.VegaHedge,   Sides.Sell, false) },
                    { "gamma_buy_open",     () => GetOrderBalance(StrategyType.GammaHedge,  Sides.Buy, true) },
                    { "gamma_buy_close",    () => GetOrderBalance(StrategyType.GammaHedge,  Sides.Buy, false) },
                    { "gamma_sell_open",    () => GetOrderBalance(StrategyType.GammaHedge,  Sides.Sell, true) },
                    { "gamma_sell_close",   () => GetOrderBalance(StrategyType.GammaHedge,  Sides.Sell, false) },
                };
            }

            public void AddStrategyOrder(RobotOptionOrder order, StrategyType sType, Sides direction, bool isOpenPosOrder) {
                _optionOrders[GetOrderIndex(sType, direction, isOpenPosOrder)] = order;
            }

            public void RemoveStrategyOrder(RobotOptionOrder order, StrategyType sType, Sides direction, bool isOpenPosOrder) {
                var index = GetOrderIndex(sType, direction, isOpenPosOrder);
                if(_optionOrders[index] == order)
                    _optionOrders[index] = null;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int GetOrderBalance(StrategyType sType, Sides direction, bool isOpenPosOrder) {
                return _optionOrders[GetOrderIndex(sType, direction, isOpenPosOrder)].Return(o => (int)o.Balance, 0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int GetOrderIndex(StrategyType sType, Sides direction, bool isOpenPosOrder) {
                return ((int)sType << 2) + ((int)direction << 1) + Convert.ToInt32(isOpenPosOrder);
            }
        }

        /// <summary>Логгер портфеля.</summary>
        public class PortfolioLogger : LoggerModule {
            public const string ModuleName = "Portfolio";

            PortfolioEx _portfolio;

            public PortfolioLogger(RobotLogger parent) : base(parent, ModuleName) {
            }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "name",               () => _portfolio.Name },
                    { "money",              () => _portfolio.CurrentValue },
                    { "blocked_money",      () => _portfolio.BlockedMoney },
                    { "free_money",         () => _portfolio.FreeMoney },
                    { "var_margin",         () => _portfolio.VariationMargin },
                    { "commission",         () => _portfolio.Commission },
                };
            }

            public PortfolioLogger LogPorfolio(PortfolioEx portfolio) {
                _portfolio = portfolio;
                Commit();
                return this;
            }
        }

        /// <summary>Логгер позиций.</summary>
        public class PositionLogger : LoggerModule {
            public const string ModuleName = "Positions";

            readonly Ecng.Common.WeakReference<Position> _position = new Ecng.Common.WeakReference<Position>(null);

            public PositionLogger(RobotLogger parent) : base(parent, ModuleName) {
            }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "name",               () => _position.Target.Security.ShortName },
                    { "code",               () => _position.Target.Security.Code },
                    { "position",           () => _position.Target.CurrentValue },
                };
            }

            public PositionLogger LogPosition(Position p) {
                _position.Target = p;
                Commit();
                return this;
            }
        }

        /// <summary>Логгер заявок.</summary>
        public class OrderLogger : LoggerModule {
            public const string ModuleName = "Orders";

            readonly Ecng.Common.WeakReference<OrderEx> _order;
            string _comment;

            public OrderLogger(RobotLogger parent) : base(parent, ModuleName) {
                _order = new Ecng.Common.WeakReference<OrderEx>(null);
            }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "order_id",           () => _order.Target.Id }, // Target always not null when action is called
                    { "transaction_id",     () => _order.Target.TransactionId },
                    { "state",              () => _order.Target.State },
                    { "code",               () => _order.Target.Security.Code },
                    { "dir",                () => _order.Target.Direction },
                    { "volume",             () => _order.Target.Volume },
                    { "price",              () => _order.Target.Price },
                    { "balance",            () => _order.Target.Balance },
                    { "name",               () => _order.Target.Security.ShortName },
                    { "reg_time",           () => _order.Target.Time == DateTime.MinValue ? "" : "{0:HH:mm:ss.fff}".Put(_order.Target.Time) },
                    { "last_change_time",   () => "{0:HH:mm:ss.fff}".Put(_order.Target.LastChangeTime) },
                    { "reg_latency_ms",     () => _order.Target.Latency.Return2(lat => "{0:0.###}".Put(lat.TotalMilliseconds), null) },
                    { "comment",            () => _comment },
                };
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            public OrderLogger LogOrder(OrderEx o) {
                _comment = null;
                _order.Target = o;
                Commit();
                return this;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            public OrderLogger LogOrderFail(OrderFail fail) {
                var order = fail.Order as OrderEx;
                if(order == null) {
                    _parent._log.Dbg.AddWarningLog("LogOrderFail: unable to convert {0} to OrderEx", fail.Order.TransactionId);
                    return this;
                }

                _comment = fail.Error.ToString();
                _order.Target = order;
                Commit();
                return this;
            }
        }

        /// <summary>Логгер сделок.</summary>
        public class TradeLogger : LoggerModule {
            public const string ModuleName = "Trades";

            static readonly TimeSpan _logPeriod = TimeSpan.FromSeconds(5);
            static readonly TimeSpan _logDelay = TimeSpan.FromSeconds(10);

            readonly Queue<Tuple<DateTime, MyTradeEx>> _logBuffer = new Queue<Tuple<DateTime, MyTradeEx>>();

            HTCancellationToken _logTimer;
            MyTradeEx _mtInfo;
            Security Security {get {return _mtInfo.Order.Security;}}
            bool IsOption {get {return Security.Type == SecurityTypes.Option;}}

            public TradeLogger(RobotLogger parent) : base(parent, ModuleName) {}

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "order_id",           () => _mtInfo.Order.Id },
                    { "trade_id",           () => _mtInfo.Trade.Id },
                    { "trade_time",         () => "{0:HH:mm:ss.fff}".Put(_mtInfo.Trade.Time) },
                    { "base",               () => IsOption ? Security.UnderlyingSecurityId : "" },
                    { "code",               () => Security.Code },
                    { "type",               () => Security.OptionType },
                    { "dir",                () => _mtInfo.Order.Direction },
                    { "is_active",          () => _mtInfo.Trade.OrderDirection.Return2(od => (od == _mtInfo.Order.Direction).ToString(), string.Empty) },
                    { "volume",             () => _mtInfo.Trade.Volume },
                    { "price",              () => _mtInfo.Trade.Price },
                    { "iv",                 () => _mtInfo.TradeIv },
                    { "strike",             () => IsOption ? "{0}".Put(Security.Strike) : "" },
                    { "exp_date",           () => "{0:dd-MM-yyyy}".Put(Security.ExpiryDate) },
                    { "cur_vm",             () => _mtInfo.CurVarMargin },
                    { "cur_fut_bid",        () => _mtInfo.CurFutQuote.Bid },
                    { "cur_fut_offer",      () => _mtInfo.CurFutQuote.Ask },
                    { "cur_pos",            () => _mtInfo.CurPosition },
                    { "cur_iv_bid",         () => IsOption ? _mtInfo.CurIvBid.ToString("0.#####") : string.Empty },
                    { "cur_iv_offer",       () => IsOption ? _mtInfo.CurIvOffer.ToString("0.#####") : string.Empty },
                    { "cur_mkt_iv_bid",     () => IsOption ? _mtInfo.CurMarketIvBid.ToString("0.#####") : string.Empty },
                    { "cur_mkt_iv_offer",   () => IsOption ? _mtInfo.CurMarketIvOffer.ToString("0.#####") : string.Empty },
                    { "cur_vega_portfolio", () => _mtInfo.CurVegaPortfolio },
                    { "strategy",           () => (_mtInfo.Order as RobotOptionOrder).With(o => o.OrderAction.VMStrategy.With(vms => vms.Id)) },
                };
            }

            public void LogMyTrade(MyTradeEx info) {
                var t = Tuple.Create(SteadyClock.Now, info);
                lock(_logBuffer)
                    _logBuffer.Enqueue(t);

                EnsureTimer();
            }

            void LogTrades(bool checkTime) {
                if(_logBuffer.Count == 0)
                    return;

                var list = new List<MyTradeEx>();
                var logBefore = checkTime ? SteadyClock.Now - _logDelay : DateTime.MaxValue;

                lock(_logBuffer) {
                    do {
                        var t = _logBuffer.Peek();
                        if(t.Item1 > logBefore)
                            break;

                        _logBuffer.Dequeue();
                        list.Add(t.Item2);
                    } while(_logBuffer.Count > 0);
                }

                foreach(var mt in list) {
                    _mtInfo = mt;
                    Commit();
                }
            }

            public override void Flush() {
                base.Flush();
                LogTrades(false);
            }

            void EnsureTimer() {
                if(_logTimer != null)
                    return;

                _logTimer = LoggerThread?.PeriodicAction(() => LogTrades(true), _logPeriod);
            }
        }

        public class AtmStrikesLogger : LoggerModule {
            public const string ModuleName = "AtmStrikes";
            OptionStrikeInfo _call, _put;
            Range<decimal> _callRange, _putRange;

            public AtmStrikesLogger(RobotLogger parent) : base(parent, ModuleName) {}

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "series",         () => _call.Series.SeriesId },
                    { "step",           () => _call.Series.MinStrikeStep },
                    { "atm_call",       () => _call.Strike },
                    { "atm_put",        () => _put.Strike },
                    { "call_from",      () => _callRange.Min },
                    { "call_to",        () => _callRange.Max },
                    { "put_from",       () => _putRange.Min },
                    { "put_to",         () => _putRange.Max },
                };
            }

            public void LogSeries(OptionSeriesInfo series) {
                _call = series.AtmCall;
                _put = series.AtmPut;
                _callRange = series.AtmCallRange;
                _putRange = series.AtmPutRange;

                if(_call == null || _put == null || _callRange == null || _putRange == null)
                    return;

                Commit();
            }
        }

        public class ModelExtLogger : LoggerModule {
            readonly OptionInfo _option;
            IOptionModelData _data;
            CalculatedStrategyParams _params;
            string _comment;
            RecalcReason _reason;
            string _strategy;
            int _recordId;
            int? _atmShift;

            int _numByFuture, _numByOption, _numByOther;

            int _skipCount;

            IConfigGeneral CfgGeneral => _parent._controller.ConfigProvider.General.Effective;

            public ModelExtLogger(RobotLogger parent, OptionInfo option) : base(parent, $"ModelExt-{option.Series.SeriesId.StrFutDate}") {
                _option = option;
            }

            protected override TupleList<string, Func<object>> GetFields() {
                var fields = new TupleList<string, Func<object>> {
                    { "code",                   () => _option.Code },
                    { "atm_shift",              () => _atmShift },
                    { "rec_id",                 () => _recordId },
                    { "strategy",               () => _strategy },
                    { "position",               () => _data.Input.Position },
                    { "bid",                    () => _data.Input.OptionCalcBid },
                    { "offer",                  () => _data.Input.OptionCalcAsk },
                    { "vol_bid",                () => _data.Input.OptionCalcBidVol },
                    { "vol_offer",              () => _data.Input.OptionCalcAskVol },
                    { "glass_bid_volume",       () => _data.Input.GlassBidVolume },
                    { "glass_offer_volume",     () => _data.Input.GlassOfferVolume },
                    { "fut_bid",                () => _data.Input.FutureCalcBid },
                    { "fut_offer",              () => _data.Input.FutureCalcAsk },
                    { "last_deal_time",         () => $"{_data.Input.LastDealTime:HH:mm:ss.fff}" },
                    { "iv_bid",                 () => _data.IvBid },
                    { "iv_offer",               () => _data.IvOffer },
                    { "vega",                   () => _data.Vega },
                    { "gamma",                  () => _data.Gamma },
                    { "theta",                  () => _data.Theta },
                    { "vanna",                  () => _data.Vanna },
                    { "vomma",                  () => _data.Vomma },
                    { "moneyness",              () => _data.Moneyness },
                    { "strike_moneyness",       () => _data.StrikeMoneyness },
                    { "greeks_regime",          () => _data.GreeksRegime },
                    { "initial_delta",          () => _data.InitialDelta },
                    { "best_delta_expectation", () => _data.BestDeltaExpectation },
                    { "empiric_delta",          () => _option.EmpiricDelta },
                    { "illiquid_delta",         () => _data.IlliquidDelta },
                    { "illiquid_iv",            () => _data.IlliquidIv },
                    { "illiquid_vega",          () => _data.IlliquidVega },
                    { "illiquid_gamma",         () => _data.IlliquidGamma },
                    { "illiquid_theta",         () => _data.IlliquidTheta },
                    { "illiquid_vanna",         () => _data.IlliquidVanna },
                    { "illiquid_vomma",         () => _data.IlliquidVomma },
                    { "market_bid",             () => _data.MarketBid },
                    { "market_offer",           () => _data.MarketOffer },
                    { "market_iv_bid",          () => _data.MarketIvBid },
                    { "market_iv_offer",        () => _data.MarketIvOffer },
                    { "market_average",         () => _data.MarketAverage },
                    { "market_spread",          () => _data.MarketSpread },
                    { "market_spread_reset_limit", () => _data.MarketSpreadResetLimit },
                    { "valuation_target_spread",() => _data.ValuationTargetSpread },
                    { "valuation_spread",       () => _data.ValuationSpread },
                    { "widening_mkt_iv",        () => _data.WideningMktIv },
                    { "narrowing_mkt_iv",       () => _data.NarrowingMktIv },
                    { "calculation_delta",      () => _data.CalculationDelta },
                    { "calculation_vega",       () => _data.CalculationVega },
                    { "calculation_gamma",      () => _data.CalculationGamma },
                    { "calculation_theta",      () => _data.CalculationTheta },
                    { "calculation_vanna",      () => _data.CalculationVanna },
                    { "calculation_vomma",      () => _data.CalculationVomma },
                    { "curve_model_status",     () => _data.CurveModelStatus },
                    { "curve_iv_bid",           () => _data.CurveIvBid },
                    { "curve_iv_offer",         () => _data.CurveIvOffer },
                    { "curve_bid",              () => _data.CurveBid },
                    { "curve_offer",            () => _data.CurveOffer },
                    { "curve_spread",           () => _data.CurveSpread },
                    { "curve_average",          () => _data.CurveAverage },
                    { "curve_iv_spread",        () => _data.CurveIvSpread },
                    { "curve_iv_average",       () => _data.CurveIvAverage },
                    { "curve_delta",            () => _data.CurveDelta },
                    { "curve_vega",             () => _data.CurveVega },
                    { "curve_gamma",            () => _data.CurveGamma },
                    { "curve_theta",            () => _data.CurveTheta },
                    { "curve_vanna",            () => _data.CurveVanna },
                    { "curve_vomma",            () => _data.CurveVomma },
                    { "error",                  () => _data.Error },
                    { "calcs_by_future",        () => _numByFuture },
                    { "calcs_by_option",        () => _numByOption },
                    { "calcs_by_other",         () => _numByOther },
                    { "valuation_status",       () => _data.ValuationStatus },
                    { "time_valuation_status",  () => _data.TimeValuationStatus },
                    { "aggressive_reset_time",  () => GetResetTimeStr(_data.AggressiveResetStartTime) },
                    { "conservative_reset_time",() => GetResetTimeStr(_data.ConservativeResetStartTime) },
                    { "quote_bid_reset_time",   () => GetResetTimeStr(_data.QuoteVolBidResetStartTime) },
                    { "quote_offer_reset_time", () => GetResetTimeStr(_data.QuoteVolOfferResetStartTime) },
                    { "reason",                 () => _reason.GetRecalcReasonDescription() },
                    { "comment",                () => _comment },
                };

                _skipCount = fields.Count;

                CalculatedStrategyParams.GetLoggerFields().ForEach(n => fields.Add(n, null));

                return fields;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            string GetResetTimeStr(DateTime? startTime) =>
                startTime == null ? string.Empty : $"{(_data.Input.Time - startTime.Value).TotalSeconds:0.###}";

            public override IEnumerable<object> GetValues() {
                var vals = Fields.Take(_skipCount).Select(f => f.Item2());

                if(_params != null)
                    vals = vals.Concat(_params.GetLoggerValues());

                return vals;
            }

            public ModelExtLogger Comment(string comment) { _comment = comment; return this; }

            public void Reset() {
                _comment = null;
            }

            public void Log(IOptionStrategy[] strategies, RecalcReason reason, int numLastRecalcsByFuture, int numLastRecalcsByOption, int numLastRecalcsByOther) {
                if(!CfgGeneral.ExtendedLog) return;

                ++_recordId;
                _data = _option.Model.LastData;
                _reason = reason;
                _numByFuture = numLastRecalcsByFuture;
                _numByOption = numLastRecalcsByOption;
                _numByOther = numLastRecalcsByOther;

                if(strategies == null) {
                    _params = null;
                    _strategy = "none";
                    _atmShift = _option.AtmShift?.ShiftValue;
                    Commit();
                    return;
                }

                foreach(var s in strategies) {
                    var vm = s.ActiveStrategy;
                    if(vm == null)
                        continue;

                    _params = vm.CalcParams;
                    _strategy = s.StrategyType.ToString();
                    _atmShift = vm.Shift.ShiftValue;

                    Commit();
                }
            }
        }

        public class OrderPerformanceLogger : LoggerModule {
            public const string ModuleName = "OrderPerformance";

            Order _order;
            PerformanceAnalyzer _pa;

            public OrderPerformanceLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "trans_id",   () => _order.TransactionId },
                    { "order_id",   () => _order.Id },
                    { "0-init",     () => $"{_pa.InitTime.TotalMilliseconds:F3}" },
                    { "1-publish",  () => $"{_pa.PublishTime.TotalMilliseconds:F3}" },
                    { "2-response", () => $"{_pa.ResponseTime.TotalMilliseconds:F3}" },
                    { "3-handle1",  () => $"{_pa.HandleTime1.TotalMilliseconds:F3}" },
                    { "4-handle2",  () => $"{_pa.HandleTime2.TotalMilliseconds:F3}" },
                };
            }

            public void Log(Order order) {
                if(order == null) return;

                LoggerThread.ExecuteAsync(() => {
                    _order = order;
                    _pa = order.PerfAnalyzer;
                    if(_pa != null)
                        Commit();
                });
            }
        }

        public class DataThreadLogger : LoggerModule {
            public const string ModuleName = "DataThread";

            int _seqNum, _qSize;
            double _diffMs;
            string _event, _message;

            public DataThreadLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "seq_num",    () => _seqNum },
                    { "event",      () => _event },
                    { "diff_ms",    () => "{0:F3}".Put(_diffMs) },
                    { "message",    () => _message },
                    { "qsize",      () => _qSize },
                };
            }

            public void Log(int seqNum, string ev, double diffMs, string msg, int qsize) {
                LoggerThread.ExecuteAsync(() => {
                    _seqNum = seqNum;
                    _event = ev;
                    _diffMs = diffMs;
                    _message = msg;
                    _qSize = qsize;

                    Commit();
                });
            }
        }

        public class OptionOrderActionLogger : LoggerModule {
            public const string ModuleName = "OptionsNewOrders";
            RecalculateState.OrderAction _action;
            RecalculateState.ActionCancelReason? _actionCancelReason;
            OrderState _order, _order2;
            OrderFail _fail;

            OrderWrapper OrderWrapper {get {return _action.Wrapper;}}
            OptionStrategy Strategy {get {return OrderWrapper.ParentStrategy;}}
            VMStrategy VMStrategy {get {return _action.VMStrategy;}}
            RecalculateState RecalcState {get {return _action.RecalcState; }}

            public OptionOrderActionLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "code",                   () => Strategy.SecurityInfo.Code },
                    { "pos_calc",               () => RecalcState.Position },
                    { "direction",              () => OrderWrapper.Direction },
                    { "open_close",             () => OrderWrapper.IsOpenPosOrder ? "open" : "close" },
                    { "volume",                 () => _action.Size },
                    { "price",                  () => _action.Price },
                    { "consider_price",         () => _action.ConsiderPrice },
                    { "local_number",           () => _order.Return(o => o.TransactionId, 0) },
                    { "se_number",              () => _order.Return(o => o.Id, 0) },
                    { "local_calc_time",        () => "{0:HH:mm:ss.fff}".Put(RecalcState.Time) },
                    { "se_approval_time",       () => _order.With(o => "{0:HH:mm:ss.fff}".Put(o.Time)) },
                    { "strategy_type",          () => VMStrategy.With(vms => vms.StrategyType.ToString()) },
                    { "strategy_shift",         () => VMStrategy.With(vms => vms.Shift.ShiftValue.ToString(CultureInfo.InvariantCulture)) },
                    { "action",                 () => _action.Action },
                    { "action_cancel_reason",   () => _actionCancelReason },
                    { "error",                  () => _fail.With(f => "{0}: {1}".Put(f.Error.GetType().Name, f.Error.Message)) },
                    { "ao_local_number",        () => _order2.Return(o => o.TransactionId, 0) },
                    { "ao_number",              () => _order2.Return(o => o.Id, 0) },
                    { "futures_bid",            () => RecalcState.FutQuote.Bid },
                    { "futures_offer",          () => RecalcState.FutQuote.Ask },
                    { "opt_bid",                () => RecalcState.OptQuote.Bid },
                    { "opt_offer",              () => RecalcState.OptQuote.Ask },
                    { "target_iv",              () => _action.TargetIv },
                    { "real_correction_points", () => _action.PriceCorrection },
                    { "market_bid",             () => RecalcState.ModelData.MarketBid },
                    { "market_offer",           () => RecalcState.ModelData.MarketOffer },
                    { "market_iv_bid",          () => RecalcState.ModelData.MarketIvBid },
                    { "market_iv_offer",        () => RecalcState.ModelData.MarketIvOffer },
                    { "vega_volume_limit",      () => _action.VegaVolumeLimit },
                    { "mm_vega_volume_limit",   () => _action.MMVegaVolumeLimit },
                    { "gamma_volume_limit",     () => _action.GammaVolumeLimit },
                    { "mm_gamma_volume_limit",  () => _action.MMGammaVolumeLimit },
                    { "vega_volume_target",     () => _action.VegaVolumeTarget },
                    { "gamma_volume_target",    () => _action.GammaVolumeTarget },
                    { "vanna_volume_limit",     () => _action.VannaVolumeLimit },
                    { "vanna_volume_target",    () => _action.VannaVolumeTarget },
                    { "vomma_volume_limit",     () => _action.VommaVolumeLimit },
                    { "vomma_volume_target",    () => _action.VommaVolumeTarget },
                    { "last_second_trans",      () => _action.LastSecondTransactions },
                };
            }

            public void LogCanceledOrderAction(RecalculateState.OrderAction action, RecalculateState.ActionCancelReason reason) {
                LoggerThread.ExecuteAsync(() => {
                    _actionCancelReason = reason;
                    _action = action;
                    _order = null;
                    _order2 = null;
                    _fail = null;

                    Commit();
                });
            }

            public void LogOrderCancel(RecalculateState.OrderAction action) {
                if(action.Action != RecalculateState.ActionType.Cancel)
                    throw new ArgumentException("action.Action");

                var moveCancelOrder = action.OrderToMoveOrCancel;
                var ostate = moveCancelOrder != null ? new OrderState(moveCancelOrder) : null;

                LoggerThread.ExecuteAsync(() => {
                    _actionCancelReason = null;
                    _action = action;
                    _order = ostate;
                    _order2 = ostate;
                    _fail = null;

                    Commit();
                });
            }

            public void LogOrder(RobotOptionOrder order, OrderFail fail = null) {
                var ostate = new OrderState(order);

                var moveCancelOrder = order.OrderAction.OrderToMoveOrCancel;
                var ostate2 = moveCancelOrder != null ? new OrderState(moveCancelOrder) : null;

                LoggerThread.ExecuteAsync(() => {
                    _actionCancelReason = null;
                    _action = order.OrderAction;
                    _order = ostate;
                    _order2 = ostate2;
                    _fail = fail;

                    Commit();
                });
            }

            class OrderState {
                public long TransactionId {get; private set;}
                public long Id {get; private set;}
                public DateTime Time {get; private set;}

                public OrderState(Order o) {
                    TransactionId = o.TransactionId;
                    Id = o.Id;
                    Time = o.Time;
                }
            }
        }

        public class MessageProcessorLogger : LoggerModule {
            public const string ModuleName = "MessageProcessor";
            readonly Stopwatch _watch = Stopwatch.StartNew();

            TimeSpan _statusTime;
            string _name, _status;
            MessageProcessorState _state;
            int _qSize, _msgSeqNum;

            public MessageProcessorLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                return new TupleList<string, Func<object>> {
                    { "status_time",    () => "{0:hh\\:mm\\:ss\\.ffff}".Put(_statusTime) },
                    { "name",           () => _name },
                    { "state",          () => _state },
                    { "q_size",         () => _qSize },
                    { "msg_seq_num",    () => _msgSeqNum },
                    { "status",         () => _status },
                };
            }

            public void Log(string name, MessageProcessorState state, int qSize, int msgSeqNum, string status) {
                var now = _watch.Elapsed;

                LoggerThread.Post(() => {
                    try {
                        _statusTime = now;
                        _name = name;
                        _state = state;
                        _qSize = qSize;
                        _msgSeqNum = msgSeqNum;
                        _status = status;

                        Commit();
                    } catch(Exception e) {
                        _parent._log.AddErrorLog("MessageProcessorLogger: {0}", e);
                    }
                });
            }
        }

        public class SeriesLogger : LoggerModule {
            public const string ModuleName = "Series";
            SeriesWrapper _wrapper;

            public SeriesLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                // if this list changed, also change Take(N) in GetValues()
                var fields = new TupleList<string, Func<object>>();

                ConfigSeries.GetLoggerFields().ForEach(name => fields.Add(name, null));

                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return _wrapper.Effective.GetLoggerValues();
            }

            public void LogSeries(SeriesWrapper wrapper) {
                _wrapper = wrapper;
                Commit();
            }
        }

        public class VolumeStatsLogger : LoggerModule {
            public const string ModuleName = "VolumeStats";
            VolumeStatsRecord _record;

            public VolumeStatsLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                var fields = new TupleList<string, Func<object>>();

                VolumeStatsRecord.GetLoggerFields().ForEach(name => fields.Add(name, null));

                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return _record.GetLoggerValues();
            }

            public void Log(VolumeStatsRecord record) {
                _record = record;
                Commit();
            }
        }

        public class CalcSeriesLogger : LoggerModule {
            public const string ModuleName = "CalcSeries";
            ICalculatedSeriesConfig _cfg;

            public CalcSeriesLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                var fields = new TupleList<string, Func<object>>();

                CalculatedSeriesConfig.GetLoggerFields().ForEach(name => fields.Add(name, null));

                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return _cfg.GetLoggerValues();
            }

            public void Log(ICalculatedSeriesConfig cfg) {
                _cfg = cfg;
                Commit();
            }
        }

        public class CurveSnapLogger : LoggerModule {
            public const string ModuleName = "CurveArray";
            ICurveSnap _snap;

            public CurveSnapLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                var fields = new TupleList<string, Func<object>>();

                CurveSnap.GetLoggerFields().ForEach(name => fields.Add(name, null));

                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return _snap.GetLoggerValues();
            }

            public void Log(ICurveSnap snap) {
                _snap = snap;
                Commit();
            }
        }

        public class CurveParamsLogger : LoggerModule {
            public const string ModuleName = "CurveType";
            ICurveParams _params;

            public CurveParamsLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                var fields = new TupleList<string, Func<object>>();

                CurveParams.GetLoggerFields().ForEach(name => fields.Add(name, null));

                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return _params.GetLoggerValues();
            }

            public void Log(ICurveParams pars) {
                _params = pars;
                Commit();
            }
        }

        public class CurveModelValueLogger : LoggerModule {
            public const string ModuleName = "CurveModelValue";
            ICurveModelValue _value;

            public CurveModelValueLogger(RobotLogger parent) : base(parent, ModuleName) { }

            protected override TupleList<string, Func<object>> GetFields() {
                var fields = new TupleList<string, Func<object>>();

                CurveModelValue.GetLoggerFields().ForEach(name => fields.Add(name, null));

                return fields;
            }

            public override IEnumerable<object> GetValues() {
                return _value.GetLoggerValues();
            }

            public void Log(ICurveModelValue val) {
                _value = val;
                Commit();
            }
        }
    }
}
