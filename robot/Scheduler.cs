using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using Ecng.Collections;
using Ecng.Common;
using MoreLinq;
using OptionBot.Config;
using OptionBot.Xaml;
using StockSharp.Messages;

namespace OptionBot.robot {
    /// <summary>Класс, получающий информацию о торговых сессиях от адаптера плазы и составляющий расписание работы робота.</summary>
    public class Scheduler : Disposable, IRobotDataUpdater {
        readonly Logger _log = new Logger("Scheduler");
        static readonly TimeSpan _timeError = TimeSpan.FromSeconds(3);

        readonly Controller _controller;
        readonly List<SchedulerStateChangePoint> _schedule = new List<SchedulerStateChangePoint>();
        readonly SynchronizedDictionary<int, SessionTableRecord> _sessions = new SynchronizedDictionary<int, SessionTableRecord>();

        readonly DispatcherTimer _timer;

        RobotData RobotData => _controller.RobotData;
        ConfigProvider ConfigProvider => _controller.ConfigProvider;

        Connector Connector => _controller.Connector;
        Connector.IConnectorSubscriber ConnectorSubscriber => _controller.ConnectorGUISubscriber;

        EnumToStringConverter EnumToStr => EnumToStringConverter.Instance;

        /// <summary>Notifies when period changed. Parameters are previous values.</summary>
        public event Action<Scheduler, MarketPeriodType, RobotPeriodType> PeriodChanged;

        DateTime? _deltaHedgeStartTime;
        public DateTime DeltaHedgeStartTime => _deltaHedgeStartTime ?? default(DateTime);

        public MarketPeriodType MarketPeriod {get; private set;}
        TimeSpan MarketPeriodTimeLeft {get {
            var now = Connector.GetMarketTime();
            var next = _schedule.OfType<MarketPeriodStateChangePoint>().FirstOrDefault(point => point.MarketTime > now);
            return next == null ? TimeSpan.MaxValue : next.MarketTime - now;
        }}

        public RobotPeriodType RobotPeriod {get; private set;}
        TimeSpan RobotPeriodTimeLeft {get {
            var now = Connector.GetMarketTime();
            var next = _schedule.OfType<RobotPeriodStateChangePoint>().FirstOrDefault(point => point.MarketTime > now);
            return next == null ? TimeSpan.MaxValue : next.MarketTime - now;
        }}

        public TradingPeriodType? TradingPeriod {get; private set;}

        public Scheduler(Controller ctl) {
            _controller = ctl;
            _timer = new DispatcherTimer(DispatcherPriority.Background, RobotData.Dispatcher) { IsEnabled = false };
            _timer.Tick += TimerOnTick;

            ConfigProvider.TradingPeriods.ListOrItemChanged += OnTradingPeriodsConfigChanged;

            ConnectorSubscriber.SessionRecordInserted += OnSessionRecordInserted;

            MarketPeriod = MarketPeriodType.Pause;
            RobotPeriod = RobotPeriodType.Pause;
            TradingPeriod = null;

            _controller.Heart.SetConditionToCheck("curPeriod", () => MarketPeriod.IsMarketOpen());

            ConfigProvider.General.CanUpdateConfig += (general, args) => {
                var errTypes = new List<TradingPeriodType>();
                foreach(var tp in ConfigProvider.TradingPeriods.List)
                    if(tp.Effective.ShiftDeltaHedge < general.PreCurveBegin)
                        errTypes.Add(tp.Effective.PeriodType);

                if(errTypes.Count > 0)
                    args.Errors.Add($"{nameof(IConfigTradingPeriod.ShiftDeltaHedge)} < {nameof(IConfigGeneral.PreCurveBegin)} для типов: {string.Join(",", errTypes)}");
            };

            // add/remove of TradingPeriods not supported (not necessary yet)
            ConfigProvider.TradingPeriods.List.ForEach(pair => {
                pair.CanUpdateConfig += (period, args) => {
                    if(period.ShiftDeltaHedge < ConfigProvider.General.Effective.PreCurveBegin)
                        args.Errors.Add($"{nameof(period.ShiftDeltaHedge)} < {nameof(IConfigGeneral.PreCurveBegin)}");
                };
            });
        }

        void OnTradingPeriodsConfigChanged(bool isListChange) {
            _log.Dbg.AddInfoLog("scheduler settings updated");

            RobotData.Dispatcher.MyGuiAsync(() => {
                _schedule.Clear();

                foreach(var session in _sessions.Values) {
                    FillSchedule(session);
                }

                var now = Connector.GetMarketTime();
                var curSession = GetCurrentSession(now);
                if(curSession == null) {
                    _schedule.Clear();

                    UpdateState(null, now);
                    return;
                }

                UpdateState(curSession, now);
            });
        }

        /// <summary>Обновление расписание при получении новой информации от адаптера.</summary>
        void OnSessionRecordInserted(Connector connector, SessionTableRecord record) {
            var now = Connector.GetMarketTime();

            _schedule.RemoveWhere(p => p.Session.SessionId == record.SessionId);

            _sessions[record.SessionId] = record;

            FillSchedule(record);

            var curSession = GetCurrentSession(now);
            if(curSession == null) {
                _log.AddWarningLog("OnSessionRecordInserted: актуальная сессия не найдена.");

                _schedule.Clear();

                UpdateState(null, now);
                return;
            }

            UpdateState(curSession, now);
        }

        /// <summary>Проверка, можно ли в данный момент отменять заявки.</summary>
        public bool CanCancelOrders() {
            if(MarketPeriod.IsMarketOpen())
                return true;

            var record = _sessions.TryGetValue(RobotData.CurrentSessionId);
            if(record == null) {
                _log.Dbg.AddWarningLog("current session not found. id={0}", RobotData.CurrentSessionId);
                return false;
            }

            if(record.State == SessionStates.Ended || record.State == SessionStates.ForceStopped)
                return false;

            if(record.InterClearingState.HasFlag(PlazaInterClearingState.Active))
                return false;

            return true;
        }

        void UpdateMarketPeriod(MarketPeriodType newPeriod, DateTime now) {
            var oldPeriod = MarketPeriod;
            MarketPeriod = newPeriod;

            if(oldPeriod == newPeriod && _deltaHedgeStartTime != null)
                return;

            // следующая точка закрытия рынка
            var nextMktClosePoint = _schedule.FirstOrDefault(p => {
                var mpt = p as MarketPeriodStateChangePoint;
                if(mpt == null) return false;
                return mpt.MarketTime > now &&
                       ((mpt.Type == MarketPeriodStateChangePoint.PointType.StartPeriod && mpt.MarketPeriod?.IsMarketOpen() == false) ||
                        (mpt.Type == MarketPeriodStateChangePoint.PointType.EndPeriod && mpt.MarketPeriodWithOther?.IsMarketOpen() == true));
            });

            if(nextMktClosePoint == null)
                return;

            var thisDeltaHedgePt = _schedule.LastOrDefault(p => (p as RobotPeriodStateChangePoint)?.RobotPeriod == RobotPeriodType.DeltaHedge && p.MarketTime < nextMktClosePoint.MarketTime);
            _deltaHedgeStartTime = thisDeltaHedgePt?.MarketTime;
        }

        /// <summary>Составить расписание работы робота.</summary>
        void FillSchedule(SessionTableRecord session) {
            #region add session schedule 
            var begin   = new MarketPeriodStateChangePoint(session, session.BeginTime, MarketPeriodStateChangePoint.PointType.StartPeriod,  MarketPeriodType.MainSession);
            var end     = new MarketPeriodStateChangePoint(session, session.EndTime, MarketPeriodStateChangePoint.PointType.EndPeriod, null);

            begin.Other = end;      end.Other = begin;
            _schedule.Add(begin);   _schedule.Add(end);

            if(session.InterClearingDefined) {
                begin   = new MarketPeriodStateChangePoint(session, session.InterClearingBeginTime, MarketPeriodStateChangePoint.PointType.StartPeriod,  MarketPeriodType.InterClearing);
                end     = new MarketPeriodStateChangePoint(session, session.InterClearingEndTime, MarketPeriodStateChangePoint.PointType.EndPeriod, null);

                begin.Other = end;      end.Other = begin;
                _schedule.Add(begin);   _schedule.Add(end);
            }

            if(session.EveningOn) {
                begin   = new MarketPeriodStateChangePoint(session, session.EveningBeginTime, MarketPeriodStateChangePoint.PointType.StartPeriod,  MarketPeriodType.EveningSession);
                end     = new MarketPeriodStateChangePoint(session, session.EveningEndTime, MarketPeriodStateChangePoint.PointType.EndPeriod, null);

                begin.Other = end;      end.Other = begin;
                _schedule.Add(begin);   _schedule.Add(end);
            }
            #endregion

            _schedule.SortStable((p1, p2) => p1.MarketTime.CompareTo(p2.MarketTime));

            #region add robot schedule

            var main1Pair = ConfigProvider.TradingPeriods[TradingPeriodType.MainBeforeClearing];
            var main2Pair = ConfigProvider.TradingPeriods[TradingPeriodType.MainAfterClearing];
            var eveningPair = ConfigProvider.TradingPeriods[TradingPeriodType.Evening];

            if(main1Pair == null)    _log.AddErrorLog($"Торговый период '{EnumToStr.Convert(TradingPeriodType.MainBeforeClearing)}' не задан.");
            if(main2Pair == null)    _log.AddErrorLog($"Торговый период '{EnumToStr.Convert(TradingPeriodType.MainAfterClearing)}' не задан.");
            if(eveningPair == null)  _log.AddErrorLog($"Торговый период '{EnumToStr.Convert(TradingPeriodType.Evening)}' не задан.");

            if(main1Pair != null && main2Pair != null && eveningPair != null) {
                var main1   = main1Pair.Effective;
                var main2   = main2Pair.Effective;
                var evening = eveningPair.Effective;

                var duration = session.EndTime - session.BeginTime - (session.InterClearingDefined ? (session.InterClearingEndTime - session.InterClearingBeginTime) : TimeSpan.Zero);
                if(main1.ShiftStart + main1.ShiftEnd + main2.ShiftStart + main2.ShiftEnd > duration.TotalSeconds) {
                    _log.AddErrorLog("Некорректные значения параметров _shift для основной сессии.");
                } else if(main1.ShiftDeltaHedge > main1.ShiftStart) {
                    _log.AddErrorLog($"Параметр {nameof(main1.ShiftDeltaHedge)}={main1.ShiftDeltaHedge} не может быть больше чем {nameof(main1.ShiftStart)}={main1.ShiftStart} для '{EnumToStr.Convert(TradingPeriodType.MainBeforeClearing)}'.");
                } else if(main2.ShiftDeltaHedge > main2.ShiftStart) {
                    _log.AddErrorLog($"Параметр {nameof(main2.ShiftDeltaHedge)}={main2.ShiftDeltaHedge} не может быть больше чем {nameof(main2.ShiftStart)}={main2.ShiftStart} для '{EnumToStr.Convert(TradingPeriodType.MainAfterClearing)}'.");
                } else {
                    _schedule.Add(new RobotPeriodStateChangePoint(session, session.BeginTime + TimeSpan.FromSeconds(main1.ShiftDeltaHedge), RobotPeriodType.DeltaHedge));
                    _schedule.Add(new RobotPeriodStateChangePoint(session, session.BeginTime + TimeSpan.FromSeconds(main1.ShiftStart), RobotPeriodType.Active));
                    _schedule.Add(new RobotPeriodStateChangePoint(session, session.EndTime - TimeSpan.FromSeconds(main2.ShiftEnd), RobotPeriodType.Pause));

                    if(session.InterClearingDefined) {
                        _schedule.Add(new RobotPeriodStateChangePoint(session, session.InterClearingBeginTime - TimeSpan.FromSeconds(main1.ShiftEnd), RobotPeriodType.Pause));
                        _schedule.Add(new RobotPeriodStateChangePoint(session, session.InterClearingEndTime + TimeSpan.FromSeconds(main2.ShiftDeltaHedge), RobotPeriodType.DeltaHedge));
                        _schedule.Add(new RobotPeriodStateChangePoint(session, session.InterClearingEndTime + TimeSpan.FromSeconds(main2.ShiftStart), RobotPeriodType.Active));
                    }
                }

                if(session.EveningOn) {
                    duration = session.EveningEndTime - session.EveningBeginTime;
                    if(evening.ShiftStart + evening.ShiftEnd > duration.TotalSeconds) {
                        _log.AddErrorLog("Некорректные значения параметров _shift для вечерней сессии.");
                    } else if(evening.ShiftDeltaHedge > evening.ShiftStart) {
                        _log.AddErrorLog($"Параметр {nameof(evening.ShiftDeltaHedge)}={evening.ShiftDeltaHedge} не может быть больше чем {nameof(evening.ShiftStart)}={evening.ShiftStart} для '{EnumToStr.Convert(TradingPeriodType.Evening)}'.");
                    } else {
                        _schedule.Add(new RobotPeriodStateChangePoint(session, session.EveningBeginTime + TimeSpan.FromSeconds(evening.ShiftDeltaHedge), RobotPeriodType.DeltaHedge));
                        _schedule.Add(new RobotPeriodStateChangePoint(session, session.EveningBeginTime + TimeSpan.FromSeconds(evening.ShiftStart), RobotPeriodType.Active));
                        _schedule.Add(new RobotPeriodStateChangePoint(session, session.EveningEndTime - TimeSpan.FromSeconds(evening.ShiftEnd), RobotPeriodType.Pause));
                    }
                }
            }

            #endregion

            _schedule.SortStable((p1, p2) => p1.MarketTime.CompareTo(p2.MarketTime));

            var sb = new StringBuilder("Created schedule for session {0}:".Put(session.SessionId)).AppendLine();
            foreach(var point in _schedule) {
                sb.AppendLine(point.ToString());
            }

            _log.Dbg.AddInfoLog(sb.ToString());
        }

        void UpdateState(SessionTableRecord curSession, DateTime marketTime) {
            MarketPeriodStateChangePoint mpoint = null;
            RobotPeriodStateChangePoint rpoint = null;
            int mpointIndex, rpointIndex;
            mpointIndex = rpointIndex = -1;

            for(var i = 0; i < _schedule.Count; ++i) {
                if(_schedule[i].MarketTime - marketTime > _timeError)
                    break;

                if(_schedule[i] is MarketPeriodStateChangePoint) {
                    mpoint = (MarketPeriodStateChangePoint)_schedule[i];
                    mpointIndex = i;
                } else if(_schedule[i] is RobotPeriodStateChangePoint) {
                    rpoint = (RobotPeriodStateChangePoint)_schedule[i];
                    rpointIndex = i;
                }
            }

            var mPeriod = MarketPeriodType.Pause;
            var rPeriod = RobotPeriodType.Pause;

            if(mpoint != null) {
                if(mpoint.Type == MarketPeriodStateChangePoint.PointType.StartPeriod)
                    mPeriod = mpoint.MarketPeriod.Value;
                else {
                    for(var i = mpointIndex - 1; i >= 0; --i) {
                        var mp2 = _schedule[i] as MarketPeriodStateChangePoint;
                        if(mp2 != null && mp2.Type == MarketPeriodStateChangePoint.PointType.StartPeriod && (mp2.Other == null || mp2.Other.MarketTime > mpoint.MarketTime)) {
                            mPeriod = mp2.MarketPeriod.Value;
                            break;
                        }
                    }
                }
            }

            if(rpoint != null)
                rPeriod = rpoint.RobotPeriod;

            if(curSession == null) {
                if(mPeriod != MarketPeriod || rPeriod != RobotPeriod) {
                    var oldMarketPeriod = MarketPeriod;
                    var oldRobotPeriod = RobotPeriod;

                    UpdateMarketPeriod(mPeriod, marketTime);
                    RobotPeriod = rPeriod;
                    TradingPeriod = null;

                    _log.AddInfoLog($"MarketPeriod = {MarketPeriod}, RobotPeriod = {RobotPeriod}, TradingPeriod={TradingPeriod}");

                    PeriodChanged?.Invoke(this, oldMarketPeriod, oldRobotPeriod);
                }
                return;
            }

            var newMarketPeriod = SessionStateToMarketPeriod(curSession, marketTime);
            if(mPeriod != newMarketPeriod) {
                _log.Dbg.AddWarningLog("Market period: calculated={0}, received={1}", mPeriod, newMarketPeriod);
                mPeriod = newMarketPeriod;
            }

            if(MarketPeriod != mPeriod || RobotPeriod != rPeriod) {
                var oldMarketPeriod = MarketPeriod;
                var oldRobotPeriod = RobotPeriod;

                UpdateMarketPeriod(mPeriod, marketTime);
                RobotPeriod = rPeriod;
                if(MarketPeriod == MarketPeriodType.EveningSession)
                    TradingPeriod = TradingPeriodType.Evening;
                else if(MarketPeriod == MarketPeriodType.MainSession)
                    TradingPeriod = marketTime > curSession.InterClearingBeginTime ? TradingPeriodType.MainAfterClearing : TradingPeriodType.MainBeforeClearing;
                else
                    TradingPeriod = null;

                var periodStart = curSession.GetStartTime(marketTime);
                if(!periodStart.IsDefault())
                    RobotData.CurrentMarketDayStartTime = periodStart;
                RobotData.CurrentSessionId = curSession.SessionId;

                _log.AddInfoLog($"MarketPeriod = {MarketPeriod}, RobotPeriod = {RobotPeriod}, TradingPeriod={TradingPeriod}");

                PeriodChanged?.Invoke(this, oldMarketPeriod, oldRobotPeriod);
            }

            RescheduleTimer(marketTime);
        }

        /// <summary>Запустить таймер на смену состояния робота.</summary>
        void RescheduleTimer(DateTime now) {
            var point = _schedule.FirstOrDefault(p => p.MarketTime > now);

            if(point == null) return;

            _timer.Stop();
            _timer.Interval = point.MarketTime - now;
            _timer.Start();
        }

        /// <summary>Обработчик таймера.</summary>
        void TimerOnTick(object sender, EventArgs eventArgs) {
            _timer.Stop();
            var now = Connector.GetMarketTime();
            UpdateState(GetCurrentSession(now), now);
        }

        MarketPeriodType SessionStateToMarketPeriod(SessionTableRecord session, DateTime marketTime) {

            if((session.InterClearingState & PlazaInterClearingState.Active) == PlazaInterClearingState.Active ||
                (session.InterClearingState & PlazaInterClearingState.Ending) == PlazaInterClearingState.Ending)
                return MarketPeriodType.InterClearing;

            switch(session.State) {
                case SessionStates.Assigned:
                case SessionStates.Ended:
                case SessionStates.ForceStopped:
                case SessionStates.Paused:
                    return MarketPeriodType.Pause;

                case SessionStates.Active:
                    if(Util.GetDistanceFromTimeRange(marketTime, session.BeginTime, session.EndTime).Duration() <= _timeError)
                        return MarketPeriodType.MainSession;

                    if(session.MorningOn && Util.GetDistanceFromTimeRange(marketTime, session.MorningBeginTime, session.MorningEndTime).Duration() <= _timeError)
                        return MarketPeriodType.MorningSession;

                    if(session.EveningOn && Util.GetDistanceFromTimeRange(marketTime, session.EveningBeginTime, session.EveningEndTime).Duration() <= _timeError)
                        return MarketPeriodType.EveningSession;

                    break;

                default:
                    _log.AddErrorLog("Не удалось определить текущий период торговой сессии. time={0}, session=\n{1}", marketTime, session);
                    break;
            }

            return MarketPeriodType.Pause;
        }

        SessionTableRecord GetCurrentSession(DateTime marketTime) {
            var minTimeleft = TimeSpan.MaxValue;
            var minTimePassed = TimeSpan.MaxValue;
            SessionTableRecord result = null;
            var found = false;

            foreach(var sess in _sessions.Values) {
                var ranges = sess.GetSessionRanges();

                foreach(var range in ranges) {
                    if(range.Contains(marketTime)) {
                        result = sess;
                        return result;
                    }

                    var diff = range.Min - marketTime;

                    if(diff > TimeSpan.Zero && diff < minTimeleft) {
                        minTimeleft = diff;
                        result = sess;
                        found = true;
                    }

                    if(!found) {
                        diff = marketTime - range.Max;

                        if(diff > TimeSpan.Zero && diff < minTimePassed) {
                            minTimePassed = diff;
                            result = sess;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Базовый класс, представляющий собой пункт в расписании робота/сессии.</summary>
        abstract class SchedulerStateChangePoint {
            protected SchedulerStateChangePoint(SessionTableRecord session, DateTime marketTime) {
                Session = session;
                MarketTime = marketTime;
            }

            public SessionTableRecord Session {get; private set;}
            public DateTime MarketTime {get; private set;}
        }

        class MarketPeriodStateChangePoint : SchedulerStateChangePoint {
            public MarketPeriodStateChangePoint(SessionTableRecord session, DateTime marketTime, PointType type, MarketPeriodType? period)
                                                : base(session, marketTime) {
                if((type == PointType.StartPeriod && period == null) || (type == PointType.EndPeriod && period != null))
                    throw new ArgumentException("type,period");

                MarketPeriod = period;
                Type = type;
            }

            public enum PointType {StartPeriod, EndPeriod}

            public PointType Type {get; private set;}
            public MarketPeriodType? MarketPeriod {get; private set;}
            public MarketPeriodType? MarketPeriodWithOther => MarketPeriod ?? Other?.MarketPeriod;
            public MarketPeriodStateChangePoint Other {get; set;}

            public override string ToString() {
                return $"{MarketTime:MMMdd/HH:mm:ss}: (id={Session.SessionId}) Session: {Type} {(Type==PointType.StartPeriod ? MarketPeriod : Other.MarketPeriod)}";
            }
        }

        class RobotPeriodStateChangePoint : SchedulerStateChangePoint {
            public RobotPeriodStateChangePoint(SessionTableRecord session, DateTime marketTime, RobotPeriodType robotPeriod)
                : base(session, marketTime) {
                RobotPeriod = robotPeriod;
            }
            public RobotPeriodType RobotPeriod {get; private set;}

            public override string ToString() {
                return $"{MarketTime:MMMdd/HH:mm:ss}: (id={Session.SessionId}) Robot: {RobotPeriod}";
            }
        }

        /// <summary>Обновление данных по таймеру.</summary>
        public void UpdateData() {
            RobotData.MarketPeriodTimeLeft = MarketPeriodTimeLeft;
            RobotData.MarketPeriod = RobotData.MarketPeriodTimeLeft != TimeSpan.MaxValue ? MarketPeriod : (MarketPeriodType?)null;
            RobotData.RobotPeriodTimeLeft = RobotPeriodTimeLeft;
            RobotData.RobotPeriod = RobotData.RobotPeriodTimeLeft != TimeSpan.MaxValue ? RobotPeriod : (RobotPeriodType?)null;
        }
    }
}
