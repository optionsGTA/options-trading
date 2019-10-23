using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using Ecng.Collections;
using Ecng.Common;
using Ecng.Serialization;
using MoreLinq;
using StockSharp.Logging;

namespace OptionBot
{
    static class LogExt {
        //[Conditional("LOGGING_ENABLED")]
        public static void AddInfoLog(this ILogPrefixedReceiver receiver, string message, params object[] args) {
            receiver.AddMessage(LogLevels.Info, receiver.LogPrefix + message, args);
        }

        //[Conditional("LOGGING_ENABLED")]
        public static void AddDebugLog(this ILogPrefixedReceiver receiver, string message, params object[] args) {
            receiver.AddMessage(LogLevels.Debug, receiver.LogPrefix + message, args);
        }

        //[Conditional("LOGGING_ENABLED")]
        public static void AddWarningLog(this ILogPrefixedReceiver receiver, string message, params object[] args) {
            receiver.AddMessage(LogLevels.Warning, receiver.LogPrefix + message, args);
        }

        //[Conditional("LOGGING_ENABLED")]
        public static void AddErrorLog(this ILogPrefixedReceiver receiver, string message, params object[] args) {
            receiver.AddMessage(LogLevels.Error, receiver.LogPrefix + message, args);
        }

        private static void AddMessage(this ILogPrefixedReceiver receiver, LogLevels level, string message, params object[] args) {
            if(receiver == null) throw new ArgumentNullException("receiver");
            if(level < receiver.LogLevel) return;

            receiver.AddLog(new LogMessage(receiver, receiver.CurrentTime, level, message, args));
        }
    }

    [Flags] public enum LogTarget { Dbg = 0x01, UI = 0x02, All = Dbg | UI }

    public enum LoggerDefaultRoots {Main}

    /// <summary>Отладочный логгер.</summary>
    public class Logger : Logger<LoggerDefaultRoots> {
        public Logger(string name=null, LoggerDefaultRoots? root = LoggerDefaultRoots.Main, bool rootRegister = true) : base(name, root, rootRegister) {}
    }
    public class LoggerRoot : LoggerRoot<LoggerDefaultRoots> {
        public LoggerRoot(string name=null, LoggerDefaultRoots? root = LoggerDefaultRoots.Main) : base(name, root) {}
    }

    public class LoggerRoot<LoggerRootT> : Logger<LoggerRootT> where LoggerRootT : struct, IComparable, IFormattable, IConvertible {
        static readonly Dictionary<LoggerRootT, LoggerRoot<LoggerRootT>> _roots = new Dictionary<LoggerRootT, LoggerRoot<LoggerRootT>>();

        internal static LoggerRoot<LoggerRootT> FindLoggerRoot(LoggerRootT root) {
            LoggerRoot<LoggerRootT> result;
            _roots.TryGetValue(root, out result);
            return result;
        }

        public LoggerRoot(string name=null, LoggerRootT? root = null) : base(name, root, false) {
            if(_roots.ContainsKey(Root)) throw new ArgumentException("Root for '{0}' was already set.".Put(Root));
            _roots.Add(Root, this);
            LoggerRoot = this;
            Logger<LoggerRootT>.HandleSetRoot(Root, this);
        }
    }

    public interface ILogPrefixGetter {
        string LogPrefix {get;}
    }

    public interface ILogPrefixedReceiver : ILogReceiver, ILogPrefixGetter { }

    /// <summary>Отладочный логгер.</summary>
    public class Logger<LoggerRootT> : BaseLogReceiver, ILogPrefixedReceiver where LoggerRootT : struct, IComparable, IFormattable, IConvertible {
        #region root register
        public LoggerRootT Root {get; private set;}

        public LoggerRoot<LoggerRootT> LoggerRoot { get; protected set; }

        private static readonly HashSet<Logger<LoggerRootT>> _tmpSet = new HashSet<Logger<LoggerRootT>>();

        protected static void HandleSetRoot(LoggerRootT root, LoggerRoot<LoggerRootT> loggerRoot) {
            foreach(var logger in _tmpSet) {
                if(!EqualityComparer<LoggerRootT>.Default.Equals(logger.Root, root)) continue;

                if(logger.LoggerRoot != null) throw new InvalidOperationException("LoggerRoot of '{0}' was already set".Put(logger.Name));
                logger.LoggerRoot = loggerRoot;
                loggerRoot.RegisterSource(logger);
            }
        }

        private void RootRegister() {
            var loggerRoot = LoggerRoot<LoggerRootT>.FindLoggerRoot(Root);
            if(loggerRoot != null) {
                LoggerRoot = loggerRoot;
                loggerRoot.RegisterSource(this);
                return;
            }

            _tmpSet.Add(this);
        }
        #endregion

        public ILogPrefixedReceiver UI {get; private set;}
        public ILogPrefixedReceiver Dbg {get; private set;}
        private ILogPrefixedReceiver All {get; set;}

        void ILogReceiver.AddLog(LogMessage msg) { All.AddLog(msg); }

        public string LogPrefix {get {return PrefixGetter.Return(pg => pg.LogPrefix, string.Empty);}}
        public ILogPrefixGetter PrefixGetter {get; set;}

        public ILogPrefixedReceiver AsReceiver() {return this;}

        public Logger(string name = null, LoggerRootT? root = null, bool rootRegister=true) {
            Name = name ?? Util.GetCallingTypeName(GetType()==typeof(Logger<LoggerRootT>) ? 1 : 2);
            Root = root ?? (LoggerRootT)Enum.GetValues(typeof(LoggerRootT)).GetValue(0);

            Id = Guid.NewGuid();
            UI = new ReceiverWrapper(this, LogTarget.UI);
            Dbg = new ReceiverWrapper(this, LogTarget.Dbg);
            All = new ReceiverWrapper(this, LogTarget.All);
            if(rootRegister) RootRegister();
        }

        readonly ConditionalWeakTable<ILogSource, SourceFilter> _sourceFilters = new ConditionalWeakTable<ILogSource, SourceFilter>();

        public void RegisterSource(ILogSource source, LogTarget target = LogTarget.All, LogLevels filterLevel = LogLevels.Inherit) {
            var childLogger = source as Logger<LoggerRootT>;
            if(childLogger == null) {
                SourceFilter filter;
                _sourceFilters.TryGetValue(source, out filter);
                if(filter != null) throw new InvalidOperationException("source was already regitered before");

                _sourceFilters.Add(source, filter = new SourceFilter(source, filterLevel));

                switch(target) {
                    case LogTarget.All: filter.Log += AddLogFromUpperLevels;    break;
                    case LogTarget.Dbg: filter.Log += AddLogFromUpperLevelsDbg; break;
                    case LogTarget.UI:  filter.Log += AddLogFromUpperLevelsUI;  break;
                }
            } else {
                childLogger.Parent = this;
            }
        }

        public void DeregisterSource(ILogSource source) {
            try {
                var childLogger = source as Logger<LoggerRootT>;
                if(childLogger == null) {
                    SourceFilter filter;
                    _sourceFilters.TryGetValue(source, out filter);
                    if(filter != null) {
                        filter.Log -= AddLogFromUpperLevels;
                        filter.Log -= AddLogFromUpperLevelsUI;
                        filter.Log -= AddLogFromUpperLevelsDbg;

                        filter.Dispose();
                        _sourceFilters.Remove(source);
                    }
                } else {
                    childLogger.Parent = null;
                }
            } catch(Exception e) {
                this.AddWarningLog("unable to remove source: {0}", e);
            }
        }

        public void RegisterListener(ILogListener listener, LogTarget target = LogTarget.All, bool guiThreadOnly = false) {
            Manager.AddListener(listener, guiThreadOnly ? Dispatcher.FromThread(Thread.CurrentThread) : null, target);
        }

        void AddLogFromUpperLevels(LogMessage msg)  { All.AddLog(msg); }
        void AddLogFromUpperLevelsUI(LogMessage msg)  { UI.AddLog(msg); }
        void AddLogFromUpperLevelsDbg(LogMessage msg)  { Dbg.AddLog(msg); }

        class SourceFilter : ILogSource {
            readonly Ecng.Common.WeakReference<ILogSource> _source;
            LogLevels _filter;

            public SourceFilter(ILogSource source, LogLevels filter) {
                _filter = filter;
                _source = new Ecng.Common.WeakReference<ILogSource>(source);
                _source.Target.Log += SourceOnLog;
            }

            void SourceOnLog(LogMessage msg) {
                if(msg.Level < LogLevel) return;
                Log.SafeInvoke(msg);
            }

            public void Dispose() { _source.Target.Do(s => s.Log -= SourceOnLog); }

            public Guid Id {get{ return _source.Target.Return(s => s.Id, Guid.Empty); }}
            public string Name {get{ return _source.Target.With(s => s.Name); }}
            public ILogSource Parent { get { return _source.Target.With(s => s.Parent); } set { var t = _source.Target; if(t != null) t.Parent = value; } }

            public LogLevels LogLevel {
                get {
                    var sourceLevel = _source.Target.Return(s => s.LogLevel, LogLevels.Inherit);
                    return _filter > sourceLevel ? _filter : sourceLevel;
                }
                set { _filter = value; }
            }
            public DateTime CurrentTime {get{ return _source.Target.Return(s => s.CurrentTime, default(DateTime)); }}
            public event Action<LogMessage> Log;
        }

        private class ReceiverWrapper : ILogPrefixedReceiver {
            internal class LogMessageEx : LogMessage {
                public ILogSource LocalSource {get; private set;}

                public static LogMessageEx GetMessage(Logger<LoggerRootT> logger, LogMessage msg, ILogSource src) {
                    return msg as LogMessageEx ?? new LogMessageEx(logger, msg, src);
                }
                private LogMessageEx(Logger<LoggerRootT> logger, LogMessage msg, ILogSource source) : 
                    base(msg.Source is ReceiverWrapper ? logger : msg.Source, msg.Time, msg.Level, () => msg.Message) {

                    LocalSource = source;
                }
            }

            private readonly Logger<LoggerRootT> _logger;
            public LogTarget Target {get; private set;}

            public ReceiverWrapper(Logger<LoggerRootT> logger, LogTarget target) {
                Target = target; _logger = logger;
                Log += _logger.RaiseLog;
            }

            public Guid Id {get { return _logger.Id; }}
            public string Name { get { return _logger.Name; }}
            public ILogSource Parent { get { return _logger.Parent; } set { _logger.Parent = value; }}

            public LogLevels LogLevel { get { return _logger.LogLevel; } set { _logger.LogLevel = value; } }

            public bool IsLogEnabled { get { return _logger.LogLevel != LogLevels.Off; }}

            public event Action<LogMessage> Log;
            public void AddLog(LogMessage message) {
                if(!IsLogEnabled || message.Source.LogLevel > message.Level) return;
                var msg = LogMessageEx.GetMessage(_logger, message, this);
                Log.SafeInvoke(msg);
            }

            public DateTime CurrentTime {
                get { return LoggingHelper.Now; }
            }

            public void Dispose() {
            }

            public string LogPrefix {get {return _logger.LogPrefix;}}
        }

        private class ListenerWrapper : ILogListener {
            public readonly ILogListener Listener;
            public LogTarget Target {get; set;}
            public Dispatcher Dispatcher {get; private set;}

            public ListenerWrapper(ILogListener listener, Dispatcher disp, LogTarget target) {
                if(listener is ListenerWrapper) throw new ArgumentException("parameter cannot be of type ListenerWrapper");
                Dispatcher = disp;
                Target = target;
                Listener = listener;
            }

            public void WriteMessages(IEnumerable<LogMessage> messages) {
                foreach(var msg in messages) WriteMessage(msg);
            }

            public void WriteMessage(LogMessage message) {
                if(Dispatcher != null && !Dispatcher.CheckAccess())
                    Dispatcher.MyGuiAsync(() => Listener.WriteMessages(new[] {message}));
                else
                    Listener.WriteMessages(new[] {message});
            }

            public void Load(SettingsStorage storage) {
                throw new NotImplementedException();
            }

            public void Save(SettingsStorage storage) {
                throw new NotImplementedException();
            }
        }

        private MyLogManager _manager;
        private MyLogManager Manager {get {
            if(_manager != null) return _manager;
            _manager=new MyLogManager();
            _manager.AddReceiver(this);
            return _manager;
        }}

        private class MyLogManager : Disposable {
            private readonly IList<ListenerWrapper> _listeners;
            private readonly IList<ILogSource> _sources;

            public MyLogManager() {
                _listeners = new List<ListenerWrapper>();
                _sources = new SourceList(this);
            }

            private void LogHandler(LogMessage msg) {
                var source = (ReceiverWrapper)((ReceiverWrapper.LogMessageEx)msg).LocalSource;
                _listeners.Where(listener => (source.Target & listener.Target) != 0).ForEach(listener => listener.WriteMessage(msg));
            }

            public void AddReceiver(ILogSource receiver) { _sources.Add(receiver); }

            public void AddListener(ILogListener newListener, Dispatcher dispatcher, LogTarget target) {
                var listener = _listeners.FirstOrDefault(l => l.Listener==newListener && l.Dispatcher==dispatcher);
                if(listener != null) 
                    listener.Target |= target;
                else
                    _listeners.Add(new ListenerWrapper(newListener, dispatcher, target));
            }

            public override void Dispose() {
                _sources.Clear();
                base.Dispose();
            }

            private class SourceList : BaseList<ILogSource> {
                private readonly MyLogManager _mgr;
                public SourceList(MyLogManager mgr) {
                    if(mgr == null) throw new ArgumentNullException("mgr");
                    _mgr = mgr;
                }

                protected override bool OnAdding(ILogSource item) {
                    item.Log += _mgr.LogHandler;
                    return base.OnAdding(item);
                }

                protected override bool OnClearing() {
                    foreach(var source in this) OnRemoving(source);
                    return base.OnClearing();
                }

                protected override bool OnRemoving(ILogSource item) {
                    item.Log -= _mgr.LogHandler;
                    return base.OnRemoving(item);
                }
            }
        }
    }
}
