using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Ecng.Common;
using Ecng.ComponentModel;
using MoreLinq;
using OptionBot.Config;
using Ecng.Collections;

namespace OptionBot.robot {
    public class VolumeStats : ViewModelBase, IRobotDataUpdater {
        readonly Logger _log = new Logger(nameof(VolumeStats));
        readonly Controller _controller;
        readonly List<VolumeStatsRecord> _records = new List<VolumeStatsRecord>();
        readonly Dictionary<string, VolumeStatsRecord> _lastRecDict = new Dictionary<string, VolumeStatsRecord>();

        ObservableCollection<object> _gridDataSource;
        public ObservableCollection<object> GridDataSource {get {return _gridDataSource;} private set {SetField(ref _gridDataSource, value);}}

        VolumeStatsGroupType _groupType = VolumeStatsGroupType.SeriesPeriod;
        public VolumeStatsGroupType GroupType {get {return _groupType;} set {SetField(ref _groupType, value);}}

        public Controller Controller => _controller;
        RobotData RobotData => _controller.RobotData;
        Connector Connector => _controller.Connector;
        RobotLogger.VolumeStatsLogger VolumeStatsLogger => _controller.RobotLogger.VolumeStats;
        ICfgPairGeneral CfgGeneral => _controller.ConfigProvider.General;

        int _updatePeriod;
        DateTime _nextRecordTime;

        public VolumeStats(Controller controller) {
            _controller = controller;

            CfgGeneral.CanUpdateConfig += (cfg, args) => {
                const string name = nameof(CfgGeneral.Effective.VolumeGroupPeriod);
                if(!RobotData.IsDisconnected && args.Names.Contains(name)) {
                    args.Errors.Add($"Нельзя изменять параметр '{name}' при активном подключении к плазе.");
                }
            };

            PropertyChanged += (sender, args) => {
                if(args.PropertyName == nameof(GroupType))
                    GenerateSourceData();
            };
        }

        public void Reset() {
            if(!RobotData.Dispatcher.CheckAccess()) {
                RobotData.Dispatcher.MyGuiAsync(Reset);
                return;
            }

            _updatePeriod = CfgGeneral.Effective.VolumeGroupPeriod;
            _log.Dbg.AddDebugLog($"Reset: _updatePeriod={_updatePeriod}");
            GridDataSource = null;
            SetNextUpdateTime(Connector.GetMarketTime());
        }

        void SetNextUpdateTime(DateTime now) {
            if(_updatePeriod <= 0)
                return;

            var oldUpdateTime = _nextRecordTime;

            if(_nextRecordTime.IsDefault()) {
                var minutesFromMidnight = now.TimeOfDay.TotalMinutes;
                var nextPeriod = (int)(minutesFromMidnight / _updatePeriod) + 1;
                _nextRecordTime = now.Date + TimeSpan.FromMinutes(_updatePeriod * nextPeriod);
            } else {
                var per = TimeSpan.FromMinutes(_updatePeriod);
                do {
                    _nextRecordTime += per;
                } while(_nextRecordTime < now);
            }

            _log.Dbg.AddDebugLog($"NextRecordTime: {oldUpdateTime:HH:mm:ss} ==> {_nextRecordTime:HH:mm:ss}");
        }

        public void UpdateData() {
            var now = Connector.GetMarketTime();
            var dayStart = RobotData.CurrentMarketDayStartTime;
            if(now <= _nextRecordTime || _updatePeriod <= 0 || _nextRecordTime.IsDefault() || !RobotData.IsConnected || dayStart.IsDefault())
                return;

            var numRecords = 0;
            var thisRecTime = _nextRecordTime;
            SetNextUpdateTime(now);

            var oldSecurities = _records.Select(r => r.SecCode).ToHashSet();

            foreach(var opt in RobotData.AllOptions) {
                var mktVol = opt.Volume.ToInt32Checked();
                var ourVol = opt.OwnMMVolume;
                var ourActiveVol = opt.OwnMMVolumeActive;
                var prevRec = _lastRecDict.TryGetValue(opt.Id);

                if(prevRec == null)
                    _lastRecDict[opt.Id] = prevRec = new VolumeStatsRecord(dayStart, dayStart) {
                        SecCode = opt.Code,
                        SeriesId = opt.Series.SeriesId.StrFutDate,
                        MarketVol = mktVol,
                        RobotMMVol = ourVol,
                        RobotMMVolActive = ourActiveVol,
                        MarketDiff = 0,
                        RobotMMVolDiff = 0,
                        RobotMMVolActiveDiff = 0,
                    };

                opt.VolumeDiff += mktVol - prevRec.MarketVol;

                if(opt.OwnMMVolume > 0 || opt.IsMMOption || oldSecurities.Contains(opt.Code)) {
                    var prevTime = prevRec.GetRange().Max;

                    if(prevTime > thisRecTime) {
                        _log.Dbg.AddErrorLog($"prevTime({prevTime:HH:mm:ss.fff}) > thisRecTime({thisRecTime:HH:mm:ss.fff})");
                        continue;
                    }

                    var rec = new VolumeStatsRecord(prevTime, thisRecTime) {
                        SecCode = opt.Code,
                        SeriesId = opt.Series.SeriesId.StrFutDate,
                        MarketVol = mktVol,
                        RobotMMVol = ourVol,
                        RobotMMVolActive = ourActiveVol,
                        MarketDiff = mktVol - prevRec.MarketVol,
                        RobotMMVolDiff = ourVol - prevRec.RobotMMVol,
                        RobotMMVolActiveDiff = ourActiveVol - prevRec.RobotMMVolActive,
                    };

                    _records.Add(rec);
                    _lastRecDict[opt.Id] = rec;
                    VolumeStatsLogger.Log(rec);

                    ++numRecords;
                } else {
                    prevRec.MarketDiff = mktVol - prevRec.MarketVol;
                    prevRec.RobotMMVolDiff = ourVol - prevRec.RobotMMVol;
                    prevRec.MarketVol = mktVol;
                    prevRec.RobotMMVol = ourVol;
                }
            }

            _log.Dbg.AddDebugLog($"UpdateData: time={thisRecTime}, {numRecords} records added");

            if(numRecords > 0)
                GenerateSourceData();
        }

        void GenerateSourceData() {
            _log.Dbg.AddDebugLog($"GenerateSourceData: type={GroupType}, {_records.Count} records");
            switch(GroupType) {
                case VolumeStatsGroupType.None:
                    GridDataSource = new ObservableCollection<object>(_records.OrderBy(r => r.SeriesId).ThenBy(r => r.SecCode).ThenBy(r => r.Period));
                    break;
                case VolumeStatsGroupType.SecurityId: {
                    var e = _records.GroupBy(r => r.SecCode).Select(g => new {
                        Security = g.Key,
                        Period = VolumeStatsRecord.FormatRange(GetPeriod(g)),
                        Series = g.First().SeriesId,
                        MarketVol = g.Select(r => r.MarketVol).Max(),
                        RobotMMVol = g.Select(r => r.RobotMMVol).Max(),
                        RobotMMVolActive = g.Select(r => r.RobotMMVolActive).Max(),
                        MarketDiff = g.Select(r => r.MarketDiff).Sum(),
                        RobotMMVolDiff = g.Select(r => r.RobotMMVolDiff).Sum(),
                        RobotMMVolActiveDiff = g.Select(r => r.RobotMMVolActiveDiff).Sum(),
                    }).OrderBy(a => a.Security);
                    GridDataSource = new ObservableCollection<object>(e);
                    break;
                }
                case VolumeStatsGroupType.Series: {
                    var e = _records.GroupBy(r => r.SeriesId).Select(g => new {
                        Series = g.Key,
                        Period = VolumeStatsRecord.FormatRange(GetPeriod(g)),
                        MarketVol = g.GroupBy(r => r.SecCode).Select(g1 => g1.Max(r => r.MarketVol)).Sum(),
                        RobotMMVol = g.GroupBy(r => r.SecCode).Select(g1 => g1.Max(r => r.RobotMMVol)).Sum(),
                        RobotMMVolActive = g.GroupBy(r => r.SecCode).Select(g1 => g1.Max(r => r.RobotMMVolActive)).Sum(),
                        MarketDiff = g.Select(r => r.MarketDiff).Sum(),
                        RobotMMVolDiff = g.Select(r => r.RobotMMVolDiff).Sum(),
                        RobotMMVolActiveDiff = g.Select(r => r.RobotMMVolActiveDiff).Sum(),
                    }).OrderBy(a => a.Series);
                    GridDataSource = new ObservableCollection<object>(e);
                    break;
                }
                case VolumeStatsGroupType.SeriesPeriod: {
                    var e = _records.GroupBy(r => new {r.SeriesId, r.Period}).Select(g => new {
                        SeriesPeriod = $"{g.Key.SeriesId} ({g.Key.Period})",
                        MarketVol = g.GroupBy(r => r.SecCode).Select(g1 => g1.Max(r => r.MarketVol)).Sum(),
                        RobotMMVol = g.GroupBy(r => r.SecCode).Select(g1 => g1.Max(r => r.RobotMMVol)).Sum(),
                        RobotMMVolActive = g.GroupBy(r => r.SecCode).Select(g1 => g1.Max(r => r.RobotMMVolActive)).Sum(),
                        MarketDiff = g.Select(r => r.MarketDiff).Sum(),
                        RobotMMVolDiff = g.Select(r => r.RobotMMVolDiff).Sum(),
                        RobotMMVolActiveDiff = g.Select(r => r.RobotMMVolActiveDiff).Sum(),
                    }).OrderBy(a => a.SeriesPeriod);
                    GridDataSource = new ObservableCollection<object>(e);
                    break;
                }
            }
        }

        Range<DateTime> GetPeriod(IEnumerable<VolumeStatsRecord> records) {
            var min = DateTime.MaxValue;
            var max = DateTime.MinValue;

            foreach(var r in records) {
                var range = r.GetRange();
                if(range.Min < min) min = range.Min;
                if(range.Max > max) max = range.Max;
            }

            return min < max ? new Range<DateTime>(min, max) : null;
        }
    }

    public enum VolumeStatsGroupType {
        [Description("без группировки")] None,
        [Description("инструмент")] SecurityId,
        [Description("серия опционов")] Series,
        [Description("серия + период")] SeriesPeriod
    }

    public class VolumeStatsRecord {
        readonly Range<DateTime> _range;
        string _rangeStr;

        public VolumeStatsRecord(DateTime from, DateTime to) {
            if(from > to) throw new ArgumentException("from > to");

            _range = new Range<DateTime>(from, to);
        }

        public string SecCode {get; set;}
        public string SeriesId {get; set;}
        public string Period => _rangeStr ?? (_rangeStr = FormatRange(_range));
        public int MarketVol {get; set;}
        public int RobotMMVol {get; set;}
        public int RobotMMVolActive {get; set;}
        public int MarketDiff {get; set;}
        public int RobotMMVolDiff {get; set;}
        public int RobotMMVolActiveDiff {get; set;}

        public Range<DateTime> GetRange() {
            return _range;
        }

        public static string FormatRange(Range<DateTime> r) {
            return r != null ? $"{r.Min:HH:mm:ss} - {r.Max:HH:mm:ss}" : string.Empty;
        }

        public static IEnumerable<string> GetLoggerFields() {
            return new[] {
                nameof(SecCode),
                nameof(SeriesId),
                nameof(Period),
                nameof(MarketVol),
                nameof(RobotMMVol),
                nameof(RobotMMVolActive),
                nameof(MarketDiff),
                nameof(RobotMMVolDiff),
                nameof(RobotMMVolActiveDiff),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                SecCode,
                SeriesId,
                Period,
                MarketVol,
                RobotMMVol,
                RobotMMVolActive,
                MarketDiff,
                RobotMMVolDiff,
                RobotMMVolActiveDiff,
            };
        }
    }
}
