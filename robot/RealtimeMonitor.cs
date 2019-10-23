using System;
using Ecng.Common;
using OptionBot.Config;

namespace OptionBot.robot {
    /// <summary>
    /// Монитор данных реального времени.
    /// </summary>
    public class RealtimeMonitor : Disposable {
        readonly object _lock = new object();
        readonly Logger _log = new Logger("RealtimeMonitor");

        readonly TimeSpan _monitorUpdateInterval = TimeSpan.FromMilliseconds(250);
        DateTime _lastMonitorUpdateTime;

        DateTime _lastRealtimeWarning;

        // enter 'realtime' mode if market delay < _enterRealtimeModeTimediff OR 
        //                          (current market delay is bigger than previous one AND current market delay less than _cancelOrdersDelayLimit)
        TimeSpan _enterRealtimeModeTimediff; // MUST be less then _exitRealtimeModeTimediff
            
        // exit 'realtime' mode if during _exitRealtimeModeDelay time there was no market data with market delay less than _exitRealtimeModeTimediff
        TimeSpan _exitRealtimeModeTimediff;
        TimeSpan _exitRealtimeModeDelay;

        // max delay in realtime mode which allows trading desicions
        TimeSpan _maxCalcDelay;

        // last time when we've seen market data with market delay less than _exitRealtimeModeTimediff
        DateTime _lastRealtimeDataLocalTime;

        // timeout from the moment of entering to non-realtime mode 
        // to the moment when IsExitForced() start to return true (but only if market delay > _cancelOrdersDelayLimit)
        TimeSpan RecoverySynchronizationTimeout {get; set;}

        public bool InRealtimeMode {get; private set;}
        public bool CanCalculate {get; private set;}

        public event Action RealtimeStateChanged;
        public event Action CanCalculateChanged;

        TimeSpan _lastMarketDelay;
        public TimeSpan LastMarketDelay { get { return _lastMarketDelay; } private set {_lastMarketDelay = value;}}

        readonly IClock _marketClock;

        readonly Controller _controller;
        IConfigGeneral CfgGeneral {get {return _controller.ConfigProvider.General.Effective;}}

        public RealtimeMonitor(Controller controller, IClock marketClock) {
            _controller = controller;
            _marketClock = marketClock;
            _controller.ConfigProvider.General.EffectiveConfigChanged += OnSettingsUpdated;
            OnSettingsUpdated(null, null);
            Reset();
        }

        void OnSettingsUpdated(ICfgPairGeneral cfg, string[] strings) {
            _exitRealtimeModeDelay = TimeSpan.FromSeconds((double)CfgGeneral.RealtimeExitDelay);
            _enterRealtimeModeTimediff = TimeSpan.FromSeconds((double)CfgGeneral.RealtimeEnterTimeDiff);
            _exitRealtimeModeTimediff = TimeSpan.FromSeconds((double)CfgGeneral.RealtimeExitTimeDiff);
            _maxCalcDelay = TimeSpan.FromSeconds((double)CfgGeneral.RealtimeMaxCalcDelay);
        }

        void Reset() {
            InRealtimeMode = CanCalculate = true;
            LastMarketDelay = TimeSpan.Zero;
            _lastRealtimeDataLocalTime = DateTime.MaxValue;
            RecoverySynchronizationTimeout = TimeSpan.MaxValue;
        }

        /// <summary>
        /// Обработать новые данные от сервера. Если данные сильно отстают от реального времени то монитор автоматически сбросит режим реального времени.
        /// </summary>
        /// <param name="dataMarketTime"></param>
        public void OnNewMarketData(DateTime dataMarketTime) {
            if(IsDisposed) return;

            var now = SteadyClock.Now;
            if(now - _lastMonitorUpdateTime < _monitorUpdateInterval)
                return;

            var stateChanged = false;
            var oldCanCalculate = CanCalculate;

            lock(_lock) {
                if(now - _lastMonitorUpdateTime < _monitorUpdateInterval)
                    return;

                _lastMonitorUpdateTime = now;

                var marketTime = _marketClock.Now;

                var oldInRealTime = InRealtimeMode;

                if(!InRealtimeMode) {
                    HandleDataInRecoveryMode(dataMarketTime, now, marketTime);
                } else {
                    HandleDataInRealtimeMode(dataMarketTime, now, marketTime);
                }

                var inRealTime = InRealtimeMode;
                CanCalculate = inRealTime && LastMarketDelay <= _maxCalcDelay;

                if(oldInRealTime ^ inRealTime) {
                    stateChanged = true;
                    if(oldInRealTime) {
                        _lastRealtimeWarning = now;
                        var timeleft = RecoverySynchronizationTimeout - (now - _lastRealtimeDataLocalTime);
                        _log.AddWarningLog("Режим реального времени был сброшен. Задержка {0}. Режим будет восстановлен при снижении задержки до допустимого предела.", LastMarketDelay);
                    } else {
                        _log.AddWarningLog("Режим реального времени восстановлен. Задержка снизилась до допустимого предела.");
                    }
                } else if(!inRealTime) {
                    if(now - _lastRealtimeWarning > TimeSpan.FromSeconds(5)) {
                        _lastRealtimeWarning = now;
                        var timeleft = RecoverySynchronizationTimeout - (now - _lastRealtimeDataLocalTime);
                        if(timeleft > TimeSpan.Zero) {
                            _log.AddWarningLog("Задержка {0}. Режим реального времени будет восстановлен при снижении задержки до допустимого предела.", LastMarketDelay);
                        }
                    }
                }
            }

            if(stateChanged)
                RealtimeStateChanged?.Invoke();

            if(oldCanCalculate != CanCalculate) {
                _log.Dbg.AddDebugLog("CanCalculate: {0} => {1} (delay={2})", oldCanCalculate, CanCalculate, LastMarketDelay);
                CanCalculateChanged?.Invoke();
            }
        }

        void HandleDataInRecoveryMode(DateTime dataMarketTime, DateTime nowLocal, DateTime marketTime) {
            var previousDataMarketDelay = LastMarketDelay;

            if((LastMarketDelay = (marketTime - dataMarketTime)) < _exitRealtimeModeTimediff) {
                // if this dataMarketTime can be considered as realtime, remember time when we've seen it
                _lastRealtimeDataLocalTime = nowLocal;
            }

            if(InRealtimeMode) {
                // if called from HandleDataInRealtimeMode
                InRealtimeMode = false;
                // 1 minute of real time to recover from 1000 minutes delay
                RecoverySynchronizationTimeout = TimeSpan.FromTicks(LastMarketDelay.Ticks / 1000);
                var noless = TimeSpan.FromSeconds(15);
                // but no less than 15 seconds
                if(RecoverySynchronizationTimeout < noless) RecoverySynchronizationTimeout = noless;
            } else {
                // check if we can go back to realtime mode
                if(LastMarketDelay < _enterRealtimeModeTimediff || 
                    // if dataMarketTime market delay started to grow it may mean that all history data was processed, 
                    // so go realtime, but protect from mistakes with _exitRealtimeModeTimediff
                    (LastMarketDelay > previousDataMarketDelay && LastMarketDelay < _exitRealtimeModeTimediff)) {

                    LastMarketDelay = TimeSpan.MaxValue;
                    HandleDataInRealtimeMode(dataMarketTime, nowLocal, marketTime);

                }
            }
        }

        void HandleDataInRealtimeMode(DateTime dataMarketTime, DateTime nowLocal, DateTime marketTime) {
            if((LastMarketDelay = (marketTime - dataMarketTime)) < _exitRealtimeModeTimediff ||
                (_lastRealtimeDataLocalTime == DateTime.MaxValue)) {

                // if this dataMarketTime can be considered as realtime, remember time when we've seen it
                _lastRealtimeDataLocalTime = nowLocal;
            }

            if(!InRealtimeMode) {
                // if called from HandleDataInRecoveryMode()
                InRealtimeMode = true;  
                _lastRealtimeDataLocalTime = nowLocal;
            } else if(nowLocal - _lastRealtimeDataLocalTime >= _exitRealtimeModeDelay) {
                // if we haven't seen a realtime dataMarketTime in a long time (_exitRealtimeModeDelay), then exit realtime mode
                HandleDataInRecoveryMode(dataMarketTime, nowLocal, marketTime);
            }
        }
    }
}