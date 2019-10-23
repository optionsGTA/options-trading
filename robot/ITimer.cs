using System;
using System.Diagnostics;
using System.Timers;
using Ecng.Common;

namespace OptionBot.robot {
    public delegate void MyTimerEventHandler();

    interface ITimerFactory {
        ITimer CreateTimer();
        IStopwatch CreateStopwatch();
        IClock CreateClock();
    }

    public interface IClock {
         DateTime Now {get;}
    }

    public interface ITimer : IDisposable {
        TimeSpan Interval {get; set;}
        void Start();
        void Stop();

        TimeSpan Timeleft {get;}

        event MyTimerEventHandler Fired;
    }

    public interface IStopwatch {
        void Start();
        void Stop();
        void Reset();
        bool IsRunning {get;}
        TimeSpan Elapsed {get;}
    }

    class LocalTimer : Disposable, ITimer {
        readonly Timer _timer;
        readonly Stopwatch _watch;

        public TimeSpan Interval {get; set;}

        public LocalTimer() {
            _watch = new Stopwatch();
            _timer = new Timer {AutoReset = false, Enabled = false};
            _timer.Elapsed += (sender, args) => {
                _watch.Stop();
                Fired?.Invoke();
            };
        }

        public void Start() {
            _watch.Reset(); _timer.Stop();

            _timer.Interval = Interval.TotalMilliseconds;
            
            _watch.Start(); _timer.Start();
        }

        public void Stop() {
            _watch.Stop();
            _timer.Stop();
        }

        public TimeSpan Timeleft {get {
            if(!_timer.Enabled) return TimeSpan.Zero;
            var left = Interval - _watch.Elapsed;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }}

        protected override void DisposeManaged() {
            Stop();
            _timer.Dispose();

            base.DisposeManaged();
        }

        public event MyTimerEventHandler Fired;
    }

    class LocalStopwatch : Stopwatch, IStopwatch {}

//    class LocalClock : IClock { public DateTime Now {get {return DateTime.UtcNow;}} }

//    class MarketClock : IClock {
//        readonly ITrader _trader;
//
//        public MarketClock(ITrader trader) {_trader = trader;}
//
//        public DateTime Now {get{return _trader.GetMarketTime(Exchange.Me);}}
//    }
}
