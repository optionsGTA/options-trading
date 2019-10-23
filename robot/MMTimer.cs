using System;
using System.Runtime.InteropServices;

namespace OptionBot.robot {
    public static class MMTimer {
        #region set hi timer resolution

        static readonly object _mmTimerLock = new object();
        static readonly Logger _log = new Logger("mmtimer");
        static int _instanceCounter;
        static uint _resolution;
        const double _ntDiv = 10000;

        public static void InitializeMMTimer() {
            lock(_mmTimerLock) {
                if(++_instanceCounter > 1) return;

                var caps = default(MMTimerNative.TimerCaps);
                var ret = MMTimerNative.timeGetDevCaps(ref caps, (uint) Marshal.SizeOf(caps));
                if(ret != MMTimerNative.TIMERR_NOERROR) {
                    _log.Dbg.AddWarningLog("Unable to get mm timer info. return value={0}", ret);
                    return;
                }

                _resolution = caps.periodMin > 1 ? caps.periodMin : 1;
                _log.Dbg.AddInfoLog("timer caps: min={0}ms, max={1}ms, trying to set resolution to {2}", caps.periodMin, caps.periodMax, _resolution);

                uint ntMin, ntMax, ntActual;
                ret = MMTimerNative.NtQueryTimerResolution(out ntMin, out ntMax, out ntActual);
                _log.Dbg.AddInfoLog("NtQueryTimerResolution({0}): actual={1}ms, min={2}ms, max={3}ms", ret, ntActual/_ntDiv, ntMin/_ntDiv, ntMax/_ntDiv);

                ret = MMTimerNative.timeBeginPeriod(_resolution);
                if(ret != MMTimerNative.TIMERR_NOERROR) {
                    _log.Dbg.AddWarningLog("Unable to set timer resolution. return value={0}", ret);
                } else {
                    _log.Dbg.AddInfoLog("Susscessfully initialized timer to {0}ms resolution.", _resolution);
                }

                ret = MMTimerNative.NtQueryTimerResolution(out ntMin, out ntMax, out ntActual);
                _log.Dbg.AddInfoLog("new NtQueryTimerResolution({0}): actual={1}ms, min={2}ms, max={3}ms", ret, ntActual/_ntDiv, ntMin/_ntDiv, ntMax/_ntDiv);
            }
        }

        public static void DisposeMMTimer(bool force = false) {
            lock(_mmTimerLock) {
                if(_instanceCounter < 1) return;
                
                if(force)
                    _instanceCounter = 0;
                else
                    --_instanceCounter;

                if(_instanceCounter > 0) return;

                uint ntMin, ntMax, ntActual;
                var ret = MMTimerNative.NtQueryTimerResolution(out ntMin, out ntMax, out ntActual);
                _log.Dbg.AddInfoLog("Restoring timer resolution. Before({0}): actual={1}ms, min={2}ms, max={3}ms.", ret, ntActual/_ntDiv, ntMin/_ntDiv, ntMax/_ntDiv);

                ret = MMTimerNative.timeEndPeriod(_resolution);
                if(ret != MMTimerNative.TIMERR_NOERROR) {
                    _log.Dbg.AddWarningLog("Unable to restore timer resolution. return value={0}", ret);
                }

                ret = MMTimerNative.NtQueryTimerResolution(out ntMin, out ntMax, out ntActual);
                _log.Dbg.AddInfoLog("Restoring timer resolution. After({0}): actual={1}ms, min={2}ms, max={3}ms.", ret, ntActual/_ntDiv, ntMin/_ntDiv, ntMax/_ntDiv);
            }
        }

        #endregion

        private static class MMTimerNative {
            public const uint TIMERR_NOERROR = 0;
            public const uint TIMERR_BASE = 96;
            public const uint TIMERR_NOCANDO = TIMERR_BASE + 1;
            public const uint MMSYSERR_INVALPARAM = 11;
            public const uint TIME_KILL_SYNCHRONOUS = 0x0100;

            public const uint TIME_ONESHOT = 0x0000;
            public const uint TIME_PERIODIC = 0x0001;

            [StructLayout(LayoutKind.Sequential)]
            public struct TimerCaps {
                public uint periodMin;
                public uint periodMax;
            }

            [DllImport("winmm.dll", SetLastError=true)]
            public static extern uint timeGetDevCaps(ref TimerCaps timeCaps, uint sizeTimeCaps);
        
            [DllImport("winmm.dll")]
            public static extern uint timeBeginPeriod(uint period);

            [DllImport("winmm.dll")]
            public static extern uint timeEndPeriod(uint period);

            [DllImport("winmm.dll")]
            public static extern uint timeSetEvent(uint delay, uint resolution, IntPtr timerProc, IntPtr user, uint fuEvent);

            [DllImport("winmm.dll")]
            public static extern uint timeKillEvent(uint timerID);

            [DllImport("ntdll.dll", SetLastError=true)]
            public static extern uint NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint ActualResolution);        
        }
    }
}
