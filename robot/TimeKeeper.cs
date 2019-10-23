using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Timers;
using Ecng.Common;
using SNTP;
using SNTP.Data;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Logging;
using Timer = System.Timers.Timer;

namespace OptionBot.robot {
    /// <summary>Класс, автоматически обновляющий точное время по протоколу NTP 1 раз в 15 минут.</summary>
    public class TimeKeeper : Disposable {
        static readonly Logger _log = new Logger("TimeKeeper");
        readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(15);
        readonly Timer _timer;
        readonly Connector _connector;

        const int _averageCount = 5;

        readonly decimal _maxDiff = 20;
        readonly SimpleMovingAverage _averageTimediff;
        readonly Queue<double> _lastLatencies = new Queue<double>(_averageCount);

        DateTime _lastSyncTime;

        public TimeKeeper(Connector connector) {
            _connector = connector;
            _timer = new Timer { AutoReset = true };
            _timer.Elapsed += Update;
            _connector.DefaultSubscriber.ConnectorReset += OnConnectorReset;
            _connector.DefaultSubscriber.ConnectionStateChanged += OnConnectorStateChanged;
            _averageTimediff = new SimpleMovingAverage(_averageCount);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        void OnConnectorReset(Connector connector) {
            _timer.Stop();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        void OnConnectorStateChanged(Connector connector, ConnectionState state, Exception ex) {
            if(IsDisposed) return;

            if(state != ConnectionState.Connected && state != ConnectionState.Synchronizing) {
                _timer.Stop();
            } else if(!_timer.Enabled) {
                StartTimer();
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        void StartTimer() {
            var elapsedTime = SteadyClock.Now - _lastSyncTime;
            if(elapsedTime >= _updateInterval) {
                elapsedTime = TimeSpan.Zero;
                ThreadPool.QueueUserWorkItem(o => Update(null, null));
            }

            _timer.Interval = (_updateInterval - elapsedTime).TotalMilliseconds;
            _timer.Start();
        }

        public event Action TimeUpdated;

        [MethodImpl(MethodImplOptions.Synchronized)]
        void Update(object sender, ElapsedEventArgs elapsedEventArgs) {
            
            if(sender != null && (!_timer.Enabled || IsDisposed))
                return;

            var elapsedTime = SteadyClock.Now - _lastSyncTime;
            if(sender != null && elapsedTime < _updateInterval) {
                _timer.Stop();
                _timer.Interval = (_updateInterval - elapsedTime).TotalMilliseconds;
                _timer.Start();
                return;
            }

            _timer.Interval = _updateInterval.TotalMilliseconds;
            _log.AddInfoLog("Получение локального временного сдвига.");
            var offset = NtpGetLocalTimeOffset();
            if(offset != TimeSpan.Zero) {
                LoggingHelper.NowOffset = offset;
                _lastSyncTime = SteadyClock.Now;
                TimeUpdated.SafeInvoke();
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        void HandleOrderRegistered(DateTime registerLocalMarketTime, DateTime registerServerMarketTime, TimeSpan latency) {
            _log.Dbg.AddDebugLog("HandleOrderRegistered2: localMarketTime={0:HH:mm:ss.fff}, serverMarketTime={1:HH:mm:ss.fff}, latency={2}", registerLocalMarketTime, registerServerMarketTime, latency);
            var diff = (decimal) (registerServerMarketTime - (registerLocalMarketTime - TimeSpan.FromTicks(latency.Ticks / 2))).TotalMilliseconds;
            _averageTimediff.Process(diff);
            if(_averageTimediff.IsFormed) {
                var average = _averageTimediff.CurrentValue;
                if(Math.Abs(average) > _maxDiff) {
                    _log.Dbg.AddWarningLog("latency NowOffset fix: {0:0.#####}ms", average);
                    LoggingHelper.NowOffset += TimeSpan.FromMilliseconds((double)average);
                    _averageTimediff.Reset();
                } else {
                    _log.Dbg.AddDebugLog("average diff = {0:0.###}ms", average);
                }

                _lastSyncTime = SteadyClock.Now;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void HandleOrderRegistered(DateTime registerLocalMarketTime, OrderEx order) {

            var latencyRegistration = order.Latency;
            if(latencyRegistration == null || latencyRegistration <= TimeSpan.Zero) {
                _log.Dbg.AddDebugLog("wrong latency: {0}", latencyRegistration);
                return;
            }

            var latency = latencyRegistration.Value.TotalMilliseconds;
            _log.Dbg.AddDebugLog("HandleOrderRegistered1: latency={0:F3}ms", latency);

            _lastLatencies.Enqueue(latency);
            while(_lastLatencies.Count > _averageCount)
                _lastLatencies.Dequeue();

            if(_lastLatencies.Count < _averageCount)
                return;

            var limit = 2d * _lastLatencies.Average();
            var average = _lastLatencies.Where(lat => lat < limit).Average();

            if(latency > average) return; // do not use big latency in delay calculation

            var registerServerMarketTime = order.Time;

            if(!registerServerMarketTime.IsDefault()) {
                HandleOrderRegistered(registerLocalMarketTime, registerServerMarketTime, latencyRegistration.Value);
                return;
            }

            order.WhenChanged()
                .Do(() => {
                    registerServerMarketTime = order.Time;
                    if(!registerServerMarketTime.IsDefault())
                        ThreadPool.QueueUserWorkItem(o => HandleOrderRegistered(registerLocalMarketTime, registerServerMarketTime, latencyRegistration.Value));
                })
                .Until(() => !registerServerMarketTime.IsDefault() || order.IsInFinalState())
                .Apply();
        }

        protected override void DisposeManaged() {
            _timer.Stop();
            _connector.DefaultSubscriber.ConnectorReset -= OnConnectorReset;
            _connector.DefaultSubscriber.ConnectionStateChanged -= OnConnectorStateChanged;
        }

        #region NTP

        static int _curNtpServerIndex;

        static readonly string[] defaultNtpServers = {
            "10.138.0.1",
            "ntp3.vniiftri.ru",
            "ntp.psn.ru",
            "zeus.limescope.net",
            "0.ru.pool.ntp.org",
            "ntp21.vniiftri.ru",
            "0.europe.pool.ntp.org",
            "ntp2.kansas.net",
            "ntp1.kansas.net",
            "ntp1.pucpr.br",
            "ntp1.belbone.be",
            "130.88.200.4",
            "ntp.home-dn.net",
            "194.149.67.130",
            "91.203.254.33",
            "62.117.76.142"
        };

        static TimeSpan NtpGetLocalTimeOffset() {
            var curplusnum = _curNtpServerIndex + defaultNtpServers.Length;
            for(var i = _curNtpServerIndex; i < curplusnum; ++i) {
                try {
                    _curNtpServerIndex = i % defaultNtpServers.Length;
                    var server = defaultNtpServers[_curNtpServerIndex];
                    var offset = NtpGetLocalTimeOffset(server);
                    if(offset == TimeSpan.MinValue) continue;

                    _log.AddInfoLog("Получено локальное смещение {0} через NTP сервер {1}".Put(offset, server));
                    return offset;
                } catch {}
            }
            _log.AddErrorLog("Все NTP сервера вернули ошибки. Будет использовано локальное время.");
            return TimeSpan.Zero;
        }

        static TimeSpan NtpGetMarketTimeOffset(Exchange exchange) {
            var curplusnum = _curNtpServerIndex + defaultNtpServers.Length;
            for(var i = _curNtpServerIndex; i < curplusnum; ++i) {
                try {
                    _curNtpServerIndex = i % defaultNtpServers.Length;
                    var server = defaultNtpServers[_curNtpServerIndex];
                    var offset = NtpGetMarketTimeOffset(server);
                    if(offset == TimeSpan.MinValue) continue;

                    _log.AddInfoLog("Получено смещение {0} через NTP сервер {1}".Put(offset, server));
                    return offset;
                } catch {}
            }
            var defaultOffset = GetDefaultMarketTimeOffset(exchange);
            _log.AddErrorLog("Все NTP сервера вернули ошибки. Будет использовано смещение {0}".Put(defaultOffset.ToString()));
            return defaultOffset;
        }

        static TimeSpan NtpGetLocalTimeOffset(string server, int timeoutMs = 5000) {
            var localTimeNtp = SNTPClient.GetNow(new RemoteSNTPServer(server), timeoutMs);
            var localNow = DateTime.Now;

            if(localTimeNtp == DateTime.MinValue) {
                _log.AddWarningLog("Ошибка получения времени через NTP сервер '{0}'", server);
                return TimeSpan.MinValue;
            }

            return localTimeNtp - localNow;
        }

        static TimeSpan NtpGetMarketTimeOffset(string server, int timeoutMs = 5000) {
            var now = DateTime.Now;
            var exchangeUtcOffset = Exchange.Moex.TimeZoneInfo.GetUtcOffset(now);

            var localTimeNtp = SNTPClient.GetNow(new RemoteSNTPServer(server), timeoutMs);
            if(localTimeNtp == DateTime.MinValue) {
                _log.AddWarningLog("Ошибка получения времени через NTP сервер '{0}'", server);
                return TimeSpan.MinValue;
            }

            var myRealUTCOffset = DateTime.Now.Subtract(localTimeNtp.ToUniversalTime());
            return exchangeUtcOffset.Subtract(myRealUTCOffset);
        }

        public static TimeSpan GetDefaultMarketTimeOffset(Exchange exchange) {
            var now = DateTime.Now;
            var exchangeUtcOffset = exchange.TimeZoneInfo.GetUtcOffset(now);
            var myUtcOffset = TimeZone.CurrentTimeZone.GetUtcOffset(now);
            return myUtcOffset.Subtract(exchangeUtcOffset);
        }

        #endregion
    }

    class SimpleMovingAverage {
        readonly int _length;
        readonly Queue<decimal> _values = new Queue<decimal>();

        public SimpleMovingAverage(int len) {
            if(len < 1) throw new ArgumentException("len");

            _length = len;
            Reset();
        }

        public bool IsFormed {get; private set;}
        public decimal CurrentValue {get; private set;}

        public void Process(decimal val) {
            while(_values.Count >= _length)
                _values.Dequeue();

            _values.Enqueue(val);
            CurrentValue = _values.Sum() / _values.Count;
            IsFormed = _values.Count == _length;
        }

        public void Reset() {
            _values.Clear();
            IsFormed = false;
            CurrentValue = 0;
        }
    }
}
