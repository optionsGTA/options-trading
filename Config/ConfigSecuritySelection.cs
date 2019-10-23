using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Ecng.Collections;
using OptionBot.robot;

namespace OptionBot.Config {
    [Serializable]
    [DataContract]
    public class ConfigSecuritySelection : BaseConfig<ConfigSecuritySelection, IConfigSecuritySelection>, IConfigSecuritySelection {
        [DataMember] HashSet<string> _activeFutures = new HashSet<string>();
        [DataMember] HashSet<OptionSeriesId> _activeSeries = new HashSet<OptionSeriesId>();
        [DataMember] HashSet<OptionSeriesId> _sendMMReportSeries = new HashSet<OptionSeriesId>();
        [DataMember] Dictionary<OptionSeriesId, HashSet<decimal>> _calculatedStrikes = new Dictionary<OptionSeriesId, HashSet<decimal>>();
        [DataMember] HashSet<string> _activeOptions = new HashSet<string>();

        protected override void CopyFromImpl(ConfigSecuritySelection other) {
            _activeFutures = new HashSet<string>(other._activeFutures);
            _activeOptions = new HashSet<string>(other._activeOptions);
            _activeSeries = new HashSet<OptionSeriesId>(other._activeSeries);
            _sendMMReportSeries = new HashSet<OptionSeriesId>(other._sendMMReportSeries);
            _calculatedStrikes = new Dictionary<OptionSeriesId, HashSet<decimal>>();
            foreach(var kv in other._calculatedStrikes.ToList())
                _calculatedStrikes.Add(kv.Key, new HashSet<decimal>(kv.Value));

            // ReSharper disable once ExplicitCallerInfoArgument
            OnPropertyChanged("SecuritySelection");
        }

        protected override bool OnEquals(ConfigSecuritySelection other) {
            return base.OnEquals(other) &&
                   _activeFutures.SetEquals(other._activeFutures) &&
                   _activeOptions.SetEquals(other._activeOptions) &&
                   _activeSeries.SetEquals(other._activeSeries) &&
                   _sendMMReportSeries.SetEquals(other._sendMMReportSeries) &&
                   _calculatedStrikes.Count == other._calculatedStrikes.Count &&
                   _calculatedStrikes.All(kv => other._calculatedStrikes.TryGetValue(kv.Key).Return(hash => hash.SetEquals(kv.Value), false));
        }

        #region update availability

        public void SetActive(FuturesInfo futInfo, bool value) {
            _log.Dbg.AddDebugLog($"SetActive({futInfo.Id}, {value})");
            var changed = value ? _activeFutures.Add(futInfo.Id) : _activeFutures.Remove(futInfo.Id);

            // ReSharper disable once ExplicitCallerInfoArgument
            if(changed) OnPropertyChanged(futInfo.Id);
        }

        public void SetActive(OptionSeriesInfo serInfo, bool value) {
            _log.Dbg.AddDebugLog($"SetActive({serInfo.SeriesId}, {value})");
            var changed = value ? _activeSeries.Add(serInfo.SeriesId) : _activeSeries.Remove(serInfo.SeriesId);

            // ReSharper disable once ExplicitCallerInfoArgument
            if(changed) OnPropertyChanged(serInfo.SeriesId.Id);
        }

        public void SetMMReports(OptionSeriesInfo serInfo, bool value) {
            _log.Dbg.AddDebugLog($"SetMMReports({serInfo.SeriesId}, {value})");
            var changed = value ? _sendMMReportSeries.Add(serInfo.SeriesId) : _sendMMReportSeries.Remove(serInfo.SeriesId);

            // ReSharper disable once ExplicitCallerInfoArgument
            if(changed) OnPropertyChanged(serInfo.SeriesId.Id);
        }

        public void SetActive(OptionInfo option, bool value) {
            _log.Dbg.AddDebugLog($"SetActive({option.Id}, {value})");
            var changed = value ? _activeOptions.Add(option.Id) : _activeOptions.Remove(option.Id);

            // ReSharper disable once ExplicitCallerInfoArgument
            if(changed) OnPropertyChanged(option.Id);
        }

        public void SetStrikeCalculation(OptionStrikeInfo strikeInfo, bool value) {
            _log.Dbg.AddDebugLog($"SetStrikeCalculation({strikeInfo.StrikeId}, {value})");
            var hash = _calculatedStrikes.TryGetValue(strikeInfo.Series.SeriesId);
            if(hash == null) {
                if(!value) return;

                _calculatedStrikes.Add(strikeInfo.Series.SeriesId, hash = new HashSet<decimal>());
            }

            bool changed;
            if(value) {
                changed = hash.Add(strikeInfo.Strike);
            } else {
                changed = hash.Remove(strikeInfo.Strike);
                if(hash.Count == 0)
                    _calculatedStrikes.Remove(strikeInfo.Series.SeriesId);
            }

            // ReSharper disable once ExplicitCallerInfoArgument
            if(changed) OnPropertyChanged(strikeInfo.StrikeId);
        }

        #endregion

        #region trading availability

        public bool IsSelectedForTrading(FuturesInfo futInfo) {
            return IsActive(futInfo);
        }

        public bool IsSelectedForTrading(OptionSeriesInfo serInfo) {
            return IsSelectedForTrading(serInfo.Future) && 
                   IsActive(serInfo);
        }

        public bool IsSelectedForTrading(OptionInfo option) {
            return IsActive(option) && IsStrikeCalculated(option.Strike) && IsSelectedForTrading(option.Series);
        }

        #endregion

        #region individual availability

        public bool IsActive(FuturesInfo futInfo) {
            return _activeFutures.Contains(futInfo.Id);
        }

        public bool IsActive(OptionSeriesInfo serInfo) {
            return _activeSeries.Contains(serInfo.SeriesId);
        }

        public bool IsActive(OptionInfo option) {
            return _activeOptions.Contains(option.Id);
        }

        public bool IsStrikeCalculated(OptionStrikeInfo strikeInfo) {
            var hash = _calculatedStrikes.TryGetValue(strikeInfo.Series.SeriesId);
            return hash.Return(h => h.Contains(strikeInfo.Strike), false);
        }

        public bool IsMMReportEnabled(OptionSeriesInfo serInfo) {
            return _sendMMReportSeries.Contains(serInfo.SeriesId);
        }

        #endregion

        protected override void SetDefaultValues() {
            base.SetDefaultValues();

            _sendMMReportSeries = new HashSet<OptionSeriesId>();
        }
    }

    public interface IConfigSecuritySelection : IReadOnlyConfiguration {
        bool IsSelectedForTrading(FuturesInfo futInfo);
        bool IsSelectedForTrading(OptionSeriesInfo serInfo);
        bool IsSelectedForTrading(OptionInfo option);

        bool IsStrikeCalculated(OptionStrikeInfo strikeInfo);

        bool IsActive(FuturesInfo futInfo);
        bool IsActive(OptionSeriesInfo serInfo);
        bool IsActive(OptionInfo option);
        bool IsMMReportEnabled(OptionSeriesInfo serInfo);
    }
}
