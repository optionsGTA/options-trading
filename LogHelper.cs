using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace OptionBot {
    static class LogHelper {
        static readonly Stopwatch _watch = Stopwatch.StartNew();
        static readonly TimeSpan LogPeriod = TimeSpan.FromSeconds(2);
        static readonly ConcurrentDictionary<object, TimeSpan> _logTimes = new ConcurrentDictionary<object, TimeSpan>();

        public static bool CanLogMessage(object key) {
            var now = _watch.Elapsed;
            TimeSpan ts;

            if(_logTimes.TryGetValue(key, out ts)) {
                if(now - ts > LogPeriod) {
                    _logTimes[key] = now;
                    return true;
                }

                return false;
            }

            _logTimes[key] = now;
            return true;
        }
    }
}
