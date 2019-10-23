using System;
using System.Collections.Generic;
using System.Timers;
using Ecng.Collections;
using Ecng.Common;

namespace OptionBot.robot {
    public class RobotHeart : Disposable {
        readonly Logger _log = new Logger();
        readonly Timer _timer;

        readonly CachedSynchronizedDictionary<string, Func<bool>> _heartbeatConditions = new CachedSynchronizedDictionary<string, Func<bool>>();
        readonly CachedSynchronizedDictionary<string, Action<Action>> _threadsToCheck = new CachedSynchronizedDictionary<string, Action<Action>>();

        static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(7);
        static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
        static readonly TimeSpan MinHeartbeatInterval = TimeSpan.FromSeconds(1);

        volatile bool _handlingTimer;
        DateTime _lastActivity, _lastHeartbeat;

        string _lastFailedCond;

        public event Action Heartbeat;

        public RobotHeart() {
            _timer = new Timer {
                AutoReset = true,
                Interval = CheckInterval.TotalMilliseconds
            };

            _timer.Elapsed += TimerTick;
            _timer.Start();
        }

        public void DelayNextHeartbeat() {
            _lastActivity = SteadyClock.Now;
        }

        public void SetConditionToCheck(string key, Func<bool> heartbeatCond) {
            if(heartbeatCond != null)
                _heartbeatConditions[key] = heartbeatCond;
            else
                _heartbeatConditions.Remove(key);
        }

        public void SetThreadToCheck(string key, Action<Action> postToThread) {
            if(postToThread != null)
                _threadsToCheck[key] = postToThread;
            else
                _threadsToCheck.Remove(key);
        }

        void TimerTick(object sender, ElapsedEventArgs e) {
            if(_handlingTimer)
                return;

            try {
                _handlingTimer = true;
                var now = SteadyClock.Now;
                if(now - _lastActivity < HeartbeatInterval)
                    return;

                if(!CheckConditions())
                    return;

                DelayNextHeartbeat();

                var posters = new Queue<KeyValuePair<string, Action<Action>>>(_threadsToCheck.CachedPairs);
                if(posters.Count > 0) {
                    _log.Dbg.AddDebugLog($"heartbeat: invoking thread posters ({posters.Count} items)");
                    WalkThreads(posters, () => DoHeartbeat(true));
                } else {
                    DoHeartbeat(false);
                }
            } finally {
                _handlingTimer = false;
            }
        }

        bool CheckConditions() {
            var condPairs = _heartbeatConditions.CachedPairs;
            foreach(var pair in condPairs) {
                var key = pair.Key;
                var cond = pair.Value;

                if(!cond()) {
                    if(_lastFailedCond != key) {
                        _lastFailedCond = key;
                        _log.Dbg.AddDebugLog($"{key} condition is false. heartbeat canceled.");
                    }
                    return false;
                }
            }

            return true;
        }

        void WalkThreads(Queue<KeyValuePair<string, Action<Action>>> posters, Action finalAction) {
            if(posters.Count == 0) {
                finalAction();
                return;
            }

            var pair = posters.Dequeue();
            _log.Dbg.AddDebugLog($"WalkThreads: posting to '{pair.Key}' ({posters.Count} left)");
            pair.Value(() => WalkThreads(posters, finalAction));
        }

        void DoHeartbeat(bool checkConditions) {
            if(checkConditions && !CheckConditions())
                return;

            var now = SteadyClock.Now;
            if(now - _lastHeartbeat < MinHeartbeatInterval) {
                _log.Dbg.AddWarningLog("too many heartbeats. ignoring.");
                return;
            }

            _lastHeartbeat = now;

            _log.Dbg.AddDebugLog("invoking heartbeat");
            DelayNextHeartbeat();
            Heartbeat?.Invoke();
        }

        protected override void DisposeManaged() {
            _timer.Dispose();

            base.DisposeManaged();
        }
    }
}
