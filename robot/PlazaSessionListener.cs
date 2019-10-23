using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Ecng.Common;
using Ecng.ComponentModel;
using StockSharp.Logging;
using StockSharp.Messages;
using StockSharp.Plaza;

namespace OptionBot.robot
{
    [Flags]
    public enum PlazaInterClearingState {
        [Description("Неопределен")]    Undefined       = 0x00,
        [Description("Назначен")]       FutureAssigned  = 0x01,
        [Description("Отменен")]        Canceled        = 0x02,
        [Description("Активен")]        Active          = 0x04,
        [Description("Завершение")]     Ending          = 0x08,
        [Description("Завершен")]       Done            = 0x10
    }

    /// <summary>
    /// Расширение адаптера плазы для получения информации по расписанию торговых сессий.
    /// </summary>
    class PlazaSessionListener : PlazaExtension {
        public event Action<SessionTableRecord> SessionRecordInserted;

        public PlazaSessionListener(PlazaTraderEx trader) : base(trader) {
            var sessionColumns = _trader.TableRegistry.ColumnRegistry.Session;

            var list = new[] {
                sessionColumns.SessionId,
                sessionColumns.OptSessionId,
                sessionColumns.BeginTime,
                sessionColumns.EndTime,
                sessionColumns.State,
                sessionColumns.InterClearingBeginTime,
                sessionColumns.InterClearingEndTime,
                sessionColumns.InterClearingState,
                sessionColumns.EveningOn,
                sessionColumns.EveningBeginTime,
                sessionColumns.EveningEndTime,
                sessionColumns.EndTime,
                sessionColumns.MorningOn,
                sessionColumns.MorningBeginTime,
                sessionColumns.MorningEndTime,
            };

            RegisterTableHandler(_trader.TableRegistry.Session, list, OnSessionInserted);
        }

        void OnSessionInserted(PlazaRecord r) {
            var columns = _trader.TableRegistry.ColumnRegistry.Session;

            var record = new SessionTableRecord {
                SessionId = r.GetInt(columns.SessionId),
                OptSessionId = r.GetInt(columns.OptSessionId),
                BeginTime = r.GetDateTime(columns.BeginTime),
                EndTime = r.GetDateTime(columns.EndTime),
                State = (SessionStates)r.GetInt(columns.State),
                InterClearingBeginTime = r.GetDateTime(columns.InterClearingBeginTime),
                InterClearingEndTime = r.GetDateTime(columns.InterClearingEndTime),
                InterClearingState = (PlazaInterClearingState)r.GetInt(columns.InterClearingState),
                EveningOn = r.GetBool(columns.EveningOn),
                EveningBeginTime = r.GetDateTime(columns.EveningBeginTime),
                EveningEndTime = r.GetDateTime(columns.EveningEndTime),
                MorningOn = r.GetBool(columns.MorningOn),
                MorningBeginTime = r.GetDateTime(columns.MorningBeginTime),
                MorningEndTime = r.GetDateTime(columns.MorningEndTime)
            };

            _trader.AddInfoLog("session inserted: {0}", record);

            SessionRecordInserted.SafeInvoke(record);
        }
    }

    public class SessionTableRecord {
        public int SessionId {get; set;}
        public int OptSessionId {get; set;}
        public DateTime BeginTime {get; set;}
        public DateTime EndTime {get; set;}
        public SessionStates State {get; set;}
        public DateTime InterClearingBeginTime {get; set;}
        public DateTime InterClearingEndTime {get; set;}
        public PlazaInterClearingState InterClearingState {get; set;}
        public bool EveningOn {get; set;}
        public DateTime EveningBeginTime {get; set;}
        public DateTime EveningEndTime {get; set;}
        public bool MorningOn {get; set;}
        public DateTime MorningBeginTime {get; set;}
        public DateTime MorningEndTime {get; set;}

        public override string ToString() {
            return "{0}-{1}\nstate:{2}\nclearingState:{3}({4}-{5})\nmain({6} -- {7})\nmorning({8}, {9} -- {10})\nevening({11}, {12} -- {13})"
                .Put(SessionId, OptSessionId, State, InterClearingState, InterClearingBeginTime, InterClearingEndTime,
                     BeginTime, EndTime, MorningOn, MorningBeginTime, MorningEndTime, EveningOn, EveningBeginTime, EveningEndTime);
        }

        public Range<DateTime>[] GetSessionRanges() {
            var list = new List<Range<DateTime>>();
            
            list.Add(new Range<DateTime>(BeginTime, EndTime));

            if(InterClearingDefined)
                list.Add(new Range<DateTime>(InterClearingBeginTime, InterClearingEndTime));

            if(EveningOn)
                list.Add(new Range<DateTime>(EveningBeginTime, EveningEndTime));

            if(MorningOn)
                list.Add(new Range<DateTime>(MorningBeginTime, MorningEndTime));

            return list.ToArray();
        }

        /// <summary>
        /// Реальное начало торговой сессии для даты (с учетом EveningOn/MorningOn)
        /// </summary>
        /// <returns></returns>
        public DateTime GetStartTime(DateTime dt) {
            dt = dt.Date;

            var list = new List<DateTime>();

            if(BeginTime.Date == dt)
                list.Add(BeginTime);
            if(EveningOn && EveningBeginTime.Date == dt)
                list.Add(EveningBeginTime);
            if(MorningOn && MorningBeginTime.Date == dt)
                list.Add(MorningBeginTime);

            return list.Count == 0 ? default(DateTime) : list.Min();
        }

        public bool InterClearingDefined {
            get {
                return  InterClearingState != PlazaInterClearingState.Undefined &&
                        (InterClearingState & PlazaInterClearingState.Canceled) == 0;
            }
        }
    }
}
