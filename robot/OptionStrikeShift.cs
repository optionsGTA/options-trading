using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Ecng.Common;
using OptionBot.Config;
using StockSharp.Messages;

namespace OptionBot.robot {
    public class OptionStrikeShift : ConnectorNotifiableObject {
        static readonly Logger _log = new Logger();
        readonly OptionSeriesId _seriesId;
        readonly int _shiftValue;
        readonly OptionTypes _optionType;
        OptionSeriesInfo _series;
        ICfgPairVP _vp;
        string _id;

        VMStrategy _regular, _mm, _vegaHedge, _gammaHedge;

        public string Id {get {return _id ?? (_id = "{0}_{1}_{2}".Put(_seriesId, ShiftString, _optionType));}}

        public OptionSeriesInfo Series {get {return _series;}}
        public OptionTypes OptionType {get {return _optionType;}}
        public int ShiftValue {get {return _shiftValue;}}
        public string ShiftString {get {return ShiftToString(_shiftValue);}}

        public OptionStrikeInfo Strike {get {return _series.With(s => s.StrikeByShift(_optionType, _shiftValue));}}
        public OptionInfo Option {get {return Strike.With(s => _optionType == OptionTypes.Call ? s.Call : s.Put);}}

        public ICfgPairVP VP {get {return _vp;} private set {SetField(ref _vp, value);}}

        public VMStrategy Regular {get {return _regular;} private set {SetField(ref _regular, value);}}
        public VMStrategy MM {get {return _mm;} private set {SetField(ref _mm, value);}}
        public VMStrategy VegaHedge {get {return _vegaHedge;} private set {SetField(ref _vegaHedge, value);}}
        public VMStrategy GammaHedge {get {return _gammaHedge;} private set {SetField(ref _gammaHedge, value);}}

        public event Action ShiftUpdated;

        OptionStrikeShift(Controller ctl, OptionSeriesId seriesId, OptionTypes otype, int shift) : base(ctl) {
            _seriesId = seriesId;
            _shiftValue = shift;
            _optionType = otype;

            TryUpdateVP(true);
            ConfigProvider.ValuationParams.ListOrItemChanged += TryUpdateVP;

            _series = RobotData.AllOptionSeries.FirstOrDefault(os => os.SeriesId == _seriesId);
            if(_series == null)
                RobotData.AllOptionSeries.CollectionChanged += AllOptionSeriesOnCollectionChanged;
            else
                _series.AtmStrikeChanged += SeriesOnAtmStrikeChanged;
        }

        void SeriesOnAtmStrikeChanged(OptionSeriesInfo series) {
            NotifyAll();
        }

        void TryUpdateVP(bool isListChange) {
            var list = ConfigProvider.ValuationParams.List.ToArray();
            VP = list.FirstOrDefault(pair => {
                var e = pair.Effective;
                return e.SeriesId == _seriesId && e.AtmStrikeShift == _shiftValue && e.OptionType == _optionType;
            });
        }

        void AllOptionSeriesOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args) {
            if(_series == null) {
                _series = RobotData.AllOptionSeries.FirstOrDefault(os => os.SeriesId == _seriesId);
                if(_series != null) {
                    _series.AtmStrikeChanged += SeriesOnAtmStrikeChanged;
                    RobotData.AllOptionSeries.CollectionChanged -= AllOptionSeriesOnCollectionChanged;
                    OnPropertyChanged(() => Series);
                    NotifyAll();
                }
            }
        }

        void NotifyAll() {
            OnPropertyChanged(() => Strike);
            OnPropertyChanged(() => Option);
            ShiftUpdated.SafeInvoke();
        }

        public static string ShiftToString(int shift) {
            return shift == 0 ? "ATM" : "{0:+#;-#}".Put(shift);
        }

        readonly static Dictionary<Tuple<OptionSeriesId, OptionTypes, int>, OptionStrikeShift> _shifts = new Dictionary<Tuple<OptionSeriesId, OptionTypes, int>, OptionStrikeShift>();

        public static OptionStrikeShift GetStrikeShift(Controller ctl, OptionSeriesId seriesId, OptionTypes otype, int shift) {
            lock(_shifts) {
                OptionStrikeShift result;
                var key = Tuple.Create(seriesId, otype, shift);
                if(_shifts.TryGetValue(key, out result))
                    return result;

                _shifts.Add(key, result = new OptionStrikeShift(ctl, seriesId, otype, shift));

                return result;
            }
        }

        public VMStrategy Strategy(StrategyType straType) {
            switch(straType) {
                case StrategyType.Regular: return Regular;
                case StrategyType.MM: return MM;
                case StrategyType.VegaHedge: return VegaHedge;
                case StrategyType.GammaHedge: return GammaHedge;
            }

            throw new InvalidOperationException("unexpected type: {0}".Put(straType));
        }

        void UpdateStrategy(VMStrategy strategy, StrategyType? straType = null) {
            if(strategy == null && straType == null)
                throw new InvalidOperationException("strategy and straType/oType are both null");

            if(strategy != null && strategy.OptionType != _optionType)
                throw new InvalidOperationException("invalid strategy type {0} (expected {1})".Put(strategy.OptionType, _optionType));

            var st = strategy == null ? straType.Value : strategy.StrategyType;

            switch(st) {
                case StrategyType.Regular:      Regular = strategy; return;
                case StrategyType.MM:           MM = strategy; return;
                case StrategyType.VegaHedge:    VegaHedge = strategy; return;
                case StrategyType.GammaHedge:   GammaHedge = strategy; return;
            }

            throw new InvalidOperationException("unexpected type: {0}".Put(st));
        }

        public void RegisterStrategy(VMStrategy strategy) {
            if(strategy == null) throw new ArgumentNullException("strategy");
            if(strategy.OptionType != _optionType) throw new ArgumentException("invalid strategy type {0} (expected {1})".Put(strategy.OptionType, _optionType));

            if(Strategy(strategy.StrategyType) != null)
                throw new InvalidOperationException("{0}: strategy of type {1} is already registered.".Put(Id, strategy.StrategyType));

            UpdateStrategy(strategy);
        }

        public void DeregisterStrategy(VMStrategy strategy) {
            if(strategy == null) throw new ArgumentNullException("strategy");

            var s = Strategy(strategy.StrategyType);

            if(s != strategy) {
                _log.Dbg.AddErrorLog("{0}: strategy of type {1} is not registered.".Put(Id, strategy.StrategyType));
                return;
            }

            UpdateStrategy(null, strategy.StrategyType);
        }
    }
}