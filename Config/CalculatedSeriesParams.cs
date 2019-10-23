using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Ecng.Collections;
using MoreLinq;
using OptionBot.robot;

using CLINQ = ContinuousLinq;
using CLINQExt = ContinuousLinq.ContinuousQueryExtension;
using CLINQColl = ContinuousLinq.Collections;

namespace OptionBot.Config {
    public class CalculatedSeriesConfig : BaseConfig<CalculatedSeriesConfig, ICalculatedSeriesConfig>, ICalculatedSeriesConfig {
        static int _lastVersion;

        int _curveDelay;
        int _preCurveDelay;
        CurveModelStatus _curveModelStatus;
        int _maxCurveSnap;
        int _maxPreCurveSnap;

        readonly ObservableCollection<CurveParams> _ctParamsList = new ObservableCollection<CurveParams>();
        readonly IList<CurveSnap> _curveArray;
        readonly IList<CurveSnap> _preCurveArray;
        readonly CLINQ.ReadOnlyContinuousCollection<CurveSnap> _curveArrCombined;

        public OptionSeriesInfo Series {get;}
        public int Version {get;}
        public int CurveDelay { get { return _curveDelay; } set { SetField(ref _curveDelay, value); }}
        public int PreCurveDelay { get { return _preCurveDelay; } set { SetField(ref _preCurveDelay, value); }}
        public CurveModelStatus CurveModelStatus { get { return _curveModelStatus; } set { SetField(ref _curveModelStatus, value); }}
        public int MaxCurveSnap { get { return _maxCurveSnap; } set { SetField(ref _maxCurveSnap, value); }}
        public int MaxPreCurveSnap { get { return _maxPreCurveSnap; } set { SetField(ref _maxPreCurveSnap, value); }}

        public IReadOnlyList<ICurveParams> CtParams => _ctParamsList;
        public IReadOnlyList<ICurveSnap> CurveArray => (IReadOnlyList<ICurveSnap>)_curveArray;
        public IReadOnlyList<ICurveSnap> PreCurveArray => (IReadOnlyList<ICurveSnap>)_preCurveArray;
        public IEnumerable<ICurveSnap> CurveArrayCombined => _curveArrCombined;
        public ICurveParams CurveType(CurveTypeParam param, CurveConfigType cfgType) => _ctParamsList.First(ct => ct.ConfigType == cfgType && ct.Param == param);

        public CalculatedSeriesConfig(OptionSeriesInfo series, bool ui) {
            Version = Interlocked.Increment(ref _lastVersion);
            Series = series;

            if(ui) {
                var ca = new CLINQ.ContinuousCollection<CurveSnap>();
                var pca = new CLINQ.ContinuousCollection<CurveSnap>();

                _curveArray = ca;
                _preCurveArray = pca;

                var concat = CLINQExt.Concat(ca, pca);
                _curveArrCombined = CLINQExt.ThenBy(CLINQExt.OrderBy(concat, o => o.CfgType), o => o.Snap);
            } else {
                _curveArray = new List<CurveSnap>();
                _preCurveArray = new List<CurveSnap>();
            }

            CurveModelStatus = CurveModelStatus.Reset;

            _ctParamsList.Add(new CurveParams(this, CurveConfigType.PreCurve, CurveTypeParam.Ini, ui));
            _ctParamsList.Add(new CurveParams(this, CurveConfigType.PreCurve, CurveTypeParam.Bid, ui));
            _ctParamsList.Add(new CurveParams(this, CurveConfigType.PreCurve, CurveTypeParam.Offer, ui));
            _ctParamsList.Add(new CurveParams(this, CurveConfigType.Curve, CurveTypeParam.Ini, ui));
            _ctParamsList.Add(new CurveParams(this, CurveConfigType.Curve, CurveTypeParam.Bid, ui));
            _ctParamsList.Add(new CurveParams(this, CurveConfigType.Curve, CurveTypeParam.Offer, ui));
        }

        protected override bool OnEquals(CalculatedSeriesConfig other) {
            throw new NotImplementedException();
        }

        protected override void CopyFromImpl(CalculatedSeriesConfig other) {
            CurveDelay = other.CurveDelay;
            PreCurveDelay = other.PreCurveDelay;
            CurveModelStatus = other.CurveModelStatus;
            MaxCurveSnap = other.MaxCurveSnap;
            MaxPreCurveSnap = other.MaxPreCurveSnap;

            ((CurveParams)CurveType(CurveTypeParam.Ini, CurveConfigType.PreCurve)).CopyFrom((CurveParams)other.CurveType(CurveTypeParam.Ini, CurveConfigType.PreCurve));
            ((CurveParams)CurveType(CurveTypeParam.Bid, CurveConfigType.PreCurve)).CopyFrom((CurveParams)other.CurveType(CurveTypeParam.Bid, CurveConfigType.PreCurve));
            ((CurveParams)CurveType(CurveTypeParam.Offer, CurveConfigType.PreCurve)).CopyFrom((CurveParams)other.CurveType(CurveTypeParam.Offer, CurveConfigType.PreCurve));
            ((CurveParams)CurveType(CurveTypeParam.Ini, CurveConfigType.Curve)).CopyFrom((CurveParams)other.CurveType(CurveTypeParam.Ini, CurveConfigType.Curve));
            ((CurveParams)CurveType(CurveTypeParam.Bid, CurveConfigType.Curve)).CopyFrom((CurveParams)other.CurveType(CurveTypeParam.Bid, CurveConfigType.Curve));
            ((CurveParams)CurveType(CurveTypeParam.Offer, CurveConfigType.Curve)).CopyFrom((CurveParams)other.CurveType(CurveTypeParam.Offer, CurveConfigType.Curve));

            CopyDiff(_curveArray, other._curveArray);
            CopyDiff(_preCurveArray, other._preCurveArray);
        }

        void CopyDiff(ICollection<CurveSnap> toColl, ICollection<CurveSnap> fromColl) {
            var myDict = toColl.ToDictionary(s => s.Key);
            var otherDict = fromColl.ToDictionary(s => s.Key);

            var removed = myDict.Where(k => !otherDict.ContainsKey(k.Key));
            foreach(var kv in removed)
                toColl.Remove(kv.Value);

            foreach(var kv in otherDict) {
                var toVal = myDict.TryGetValue(kv.Key);
                if(toVal != null) {
                    toVal.CopyFrom(kv.Value);
                } else {
                    toVal = new CurveSnap(this);
                    toVal.CopyFrom(kv.Value);
                    toColl.Add(toVal);
                }
            }
        }

        protected override void SetDefaultValues() {
            CurveDelay = PreCurveDelay = 0;
            CurveModelStatus = CurveModelStatus.Reset;
            MaxCurveSnap = MaxPreCurveSnap = 0;
            _ctParamsList?.ClearOneByOne();
            _curveArray?.ClearOneByOne();
            _preCurveArray?.ClearOneByOne();
        }

        public void AddSnap(CurveSnap snap) {
            if(snap == null)
                throw new ArgumentNullException(nameof(snap));

            if(snap.ConfigVersion != Version)
                throw new ArgumentException("invalid version");

            if(snap.CfgType == CurveConfigType.Curve) {
                _curveArray.Add(snap);
                MaxCurveSnap = Math.Max(MaxCurveSnap, snap.Snap);
            } else {
                _preCurveArray.Add(snap);
                MaxPreCurveSnap = Math.Max(MaxPreCurveSnap, snap.Snap);
            }
        }

        public void RemoveSnaps(CurveConfigType ctype, int snap) {
            if(ctype == CurveConfigType.Curve) {
                _curveArray.RemoveWhere(ct => ct.Snap == snap);
                MaxCurveSnap = _curveArray.Max(ct => ct.Snap);
            } else {
                _preCurveArray.RemoveWhere(ct => ct.Snap == snap);
                MaxPreCurveSnap = _preCurveArray.Max(ct => ct.Snap);
            }
        }

        public void ClearCurveArray(CurveConfigType ctype) {
            if(ctype == CurveConfigType.Curve) {
                _curveArray?.ClearOneByOne();
                MaxCurveSnap = 0;
            } else {
                _preCurveArray?.ClearOneByOne();
                MaxPreCurveSnap = 0;
            }
        }

        public void Log(RobotLogger logger) {
            logger.CalcSeriesLog.Log(this);
            _ctParamsList.ForEach(p => {
                logger.CurveParamsLog.Log(p);
                p.ModelValues.ForEach(mv => logger.CurveModelValueLog.Log(mv));
            });
            _preCurveArray.ForEach(s => logger.CurveSnapLog.Log(s));
            _curveArray.ForEach(s => logger.CurveSnapLog.Log(s));
        }

        #region logger

        public static IEnumerable<string> GetLoggerFields() {
            return new[] {
                nameof(Series),
                nameof(Version),
                nameof(CurveDelay),
                nameof(PreCurveDelay),
                nameof(CurveModelStatus),
                nameof(CurveArray) + "Size",
                nameof(PreCurveArray) + "Size",
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                Series.SeriesId.Id,
                Version,
                CurveDelay,
                PreCurveDelay,
                CurveModelStatus,
                _curveArray.Count,
                _preCurveArray.Count,
            };
        }

        #endregion
    }

    public class CurveParams : BaseConfig<CurveParams, ICurveParams>, ICurveParams {
        static int _lastRecordId;

        double _a0, _a1, _a2, _a3;
        double _residual;
        double _stdError;
        double _correlation;

        readonly IList<CurveModelValue> _modelValues;
        readonly CLINQColl.OrderedReadOnlyContinuousCollection<CurveModelValue> _modelValuesOrdered;

        public CalculatedSeriesConfig Parent {get;}
        public int RecordId {get;}
        public int ConfigVersion => Parent.Version;
        public CurveConfigType ConfigType {get;}
        public CurveTypeParam Param {get;}
        public double A0 { get { return _a0; } set { SetField(ref _a0, value); }}
        public double A1 { get { return _a1; } set { SetField(ref _a1, value); }}
        public double A2 { get { return _a2; } set { SetField(ref _a2, value); }}
        public double A3 { get { return _a3; } set { SetField(ref _a3, value); }}
        public double Residual { get { return _residual; } set { SetField(ref _residual, value); }}
        public double StdError { get { return _stdError; } set { SetField(ref _stdError, value); }}
        public double Correlation { get { return _correlation; } set { SetField(ref _correlation, value); }}
        public int Observations => ModelValues.Count;
        public IReadOnlyList<ICurveModelValue> ModelValues => (IReadOnlyList<ICurveModelValue>)_modelValues;
        public IEnumerable<ICurveModelValue> ModelValuesOrdered => _modelValuesOrdered;

        public CurveTypeModel Model { get {
            var cfg = Parent.Series.CfgSeries;
            return ConfigType == CurveConfigType.PreCurve ?
                   (Param == CurveTypeParam.Ini ? cfg.PreCurveTypeIniModel : (Param == CurveTypeParam.Bid ? cfg.PreCurveTypeBidModel : cfg.PreCurveTypeOfferModel)) :
                   (Param == CurveTypeParam.Ini ? cfg.CurveTypeIniModel    : (Param == CurveTypeParam.Bid ? cfg.CurveTypeBidModel    : cfg.CurveTypeOfferModel));
        }}

        public CurveParams(CalculatedSeriesConfig parent, CurveConfigType ctype, CurveTypeParam param, bool ui) {
            RecordId = Interlocked.Increment(ref _lastRecordId);
            Parent = parent;
            ConfigType = ctype;
            Param = param;

            if(ui) {
                var coll = new CLINQ.ContinuousCollection<CurveModelValue>();
                _modelValues = coll;
                _modelValuesOrdered = CLINQExt.OrderBy(coll, o => o.Observation);
                coll.CollectionChanged += (sender, args) => { RaisePropertyChanged(nameof(Observations)); };
            } else {
                _modelValues = new List<CurveModelValue>();
            }
        }

        protected override bool OnEquals(CurveParams other) {
            return ReferenceEquals(this, other);
        }

        bool CompareModelValues(CurveParams other) {
            if(_modelValues.Count != other._modelValues.Count)
                return false;

            var firstHash = _modelValues.ToHashSet();
            var secondHash = other._modelValues.ToHashSet();

            return firstHash.SetEquals(secondHash);
        }

        protected override void CopyFromImpl(CurveParams other) {
            A0 = other.A0;
            A1 = other.A1;
            A2 = other.A2;
            A3 = other.A3;
            Residual = other.Residual;
            StdError = other.StdError;
            Correlation = other.Correlation;

            var myDict = _modelValues.ToDictionary(mv => mv.Option.Id);
            var otherDict = other._modelValues.ToDictionary(mv => mv.Option.Id);

            var removed = myDict.Where(k => !otherDict.ContainsKey(k.Key));
            foreach(var kv in removed)
                _modelValues.Remove(kv.Value);

            foreach(var kv in otherDict) {
                var toVal = myDict.TryGetValue(kv.Key);
                if(toVal != null) {
                    toVal.CopyFrom(kv.Value);
                } else {
                    toVal = new CurveModelValue(this, kv.Value.Option);
                    toVal.CopyFrom(kv.Value);
                    _modelValues.Add(toVal);
                }
            }
        }

        protected override void SetDefaultValues() {
            A0 = A1 = A2 = A3 = Residual = StdError = Correlation = 0;
            _modelValues?.ClearOneByOne();
        }

        public void AddModelValue(CurveModelValue val) {
            var mv = _modelValues.FirstOrDefault(m => m.Option.Id == val.Option.Id);
            if(mv != null)
                throw new InvalidOperationException(nameof(CurveParams) + ".AddModelValue: value with same option already in list: " + val.Option.Id);

            val.Observation = Observations + 1;
            _modelValues.Add(val);
        }

        #region logger

        public static IEnumerable<string> GetLoggerFields() {
            return new[] {
                nameof(RecordId),
                nameof(ConfigVersion),
                nameof(ConfigType),
                nameof(Param),
                nameof(Model),
                nameof(A0),
                nameof(A1),
                nameof(A2),
                nameof(A3),
                nameof(Residual),
                nameof(StdError),
                nameof(Correlation),
                nameof(Observations),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                RecordId,
                ConfigVersion,
                ConfigType,
                Param,
                Model,
                A0,
                A1,
                A2,
                A3,
                Residual,
                StdError,
                Correlation,
                Observations,
            };
        }

        #endregion
    }

    public class CurveModelValue : BaseConfig<CurveModelValue, ICurveModelValue>, ICurveModelValue {
        int _observation;
        double _moneyness;
        double _marketIv;
        double _modelIv;

        public CurveParams Parent {get;}
        public int ConfigVersion => Parent.ConfigVersion;
        public int ParentRecordId => Parent.RecordId;

        public OptionInfo Option {get;}
        public int Observation { get { return _observation; } set { SetField(ref _observation, value); }}
        public double Moneyness { get { return _moneyness; } set { SetField(ref _moneyness, value); }}
        public double MarketIv { get { return _marketIv; } set { SetField(ref _marketIv, value); }}
        public double ModelIv { get { return _modelIv; } set { SetField(ref _modelIv, value); }}

        public CurveModelValue(CurveParams parent, OptionInfo opt) {
            Parent = parent;
            Option = opt;
        }

        protected override bool OnEquals(CurveModelValue other) {
            return ReferenceEquals(this, other);
        }

        protected override void CopyFromImpl(CurveModelValue other) {
            Observation = other.Observation;
            Moneyness = other.Moneyness;
            MarketIv = other.MarketIv;
            ModelIv = other.ModelIv;
        }

        protected override void SetDefaultValues() {
            Observation = 0;
            Moneyness = MarketIv = ModelIv = 0d;
        }

        #region logger

        public static IEnumerable<string> GetLoggerFields() {
            return new[] {
                "isin",
                nameof(ConfigVersion),
                nameof(ParentRecordId),
                nameof(Observation),
                nameof(Moneyness),
                nameof(MarketIv),
                nameof(ModelIv),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                Option.Code,
                ConfigVersion,
                ParentRecordId,
                Observation,
                Moneyness,
                MarketIv,
                ModelIv,
            };
        }

        #endregion
    }

    public class CurveSnap : BaseConfig<CurveSnap, ICurveSnap>, ICurveSnap {
        #region key

        public struct KeyType {
            public CurveConfigType CfgType {get;}
            public int Snap {get;}
            public string OptionId {get;}

            public KeyType(CurveConfigType cfgType, int snap, OptionInfo opt) {
                CfgType = cfgType;
                Snap = snap;
                OptionId = opt.Id;
            }
        }

        KeyType _key;
        bool _keyUpdated = true;
        public KeyType Key { get {
            if(!_keyUpdated) return _key;

            _keyUpdated = false;
            _key = new KeyType(CfgType, Snap, Option);
            return _key;
        }}

        #endregion

        CurveConfigType _cfgType;
        int _snap;
        DateTime _snapTime;
        OptionInfo _option;
        double _moneyness, _marketIvBid, _marketIvOffer;

        public CalculatedSeriesConfig Parent {get;}
        public int ConfigVersion => Parent.Version;
        public CurveConfigType CfgType { get { return _cfgType; } set { _keyUpdated |= SetField(ref _cfgType, value); }}
        public int Snap { get { return _snap; } set { _keyUpdated |= SetField(ref _snap, value); }}
        public DateTime SnapTime { get { return _snapTime; } set { SetField(ref _snapTime, value); }}
        public double Moneyness { get { return _moneyness; } set { SetField(ref _moneyness, value); }}
        public double MarketIvBid { get { return _marketIvBid; } set { SetField(ref _marketIvBid, value); }}
        public double MarketIvOffer { get { return _marketIvOffer; } set { SetField(ref _marketIvOffer, value); }}

        string _optionKey;
        public string OptionKey => _optionKey;
        public OptionInfo Option {
            get { return _option; }
            set {
                var updated = SetField(ref _option, value);
                _keyUpdated |= updated;

                if(updated) {
                    _optionKey = _option == null ? "null" : $"{_option.Strike.Strike:00000000}{_option.OptionType}";
                    RaisePropertyChanged(nameof(OptionKey));
                }
            }
        }

        public CurveSnap(CalculatedSeriesConfig parent) {
            Parent = parent;
        }

        protected override bool OnEquals(CurveSnap other) {
            return ReferenceEquals(this, other);
        }

        protected override void CopyFromImpl(CurveSnap other) {
            CfgType = other.CfgType;
            Snap = other.Snap;
            SnapTime = other.SnapTime;
            Option = other.Option;
            Moneyness = other.Moneyness;
            MarketIvBid = other.MarketIvBid;
            MarketIvOffer = other.MarketIvOffer;
        }

        protected override void SetDefaultValues() {
            Snap = 0;
            SnapTime = default(DateTime);
            Option = null;
            Moneyness = MarketIvBid = MarketIvOffer = 0d;
        }

        #region logger

        public static IEnumerable<string> GetLoggerFields() {
            return new[] {
                nameof(ConfigVersion),
                nameof(CfgType),
                nameof(Snap),
                nameof(SnapTime),
                "isin",
                nameof(Moneyness),
                nameof(MarketIvBid),
                nameof(MarketIvOffer),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                ConfigVersion,
                CfgType,
                Snap,
                SnapTime,
                Option.Code,
                Moneyness,
                MarketIvBid,
                MarketIvOffer,
            };
        }

        #endregion
    }

    #region interfaces

    public interface ICalculatedSeriesConfig : IReadOnlyConfiguration {
        OptionSeriesInfo Series {get;}
        int CurveDelay {get;}
        int PreCurveDelay {get;}
        CurveModelStatus CurveModelStatus {get;}
        IReadOnlyList<ICurveSnap> CurveArray {get;}
        IReadOnlyList<ICurveSnap> PreCurveArray {get;}

        ICurveParams CurveType(CurveTypeParam param, CurveConfigType cfgType);

        IEnumerable<object> GetLoggerValues();
    }

    public interface ICurveParams : IReadOnlyConfiguration {
        CurveConfigType ConfigType {get;}
        CurveTypeParam Param {get;}
        CurveTypeModel Model {get;}
        double A0 {get;}
        double A1 {get;}
        double A2 {get;}
        double A3 {get;}
        double Residual {get;}
        double StdError {get;}
        double Correlation {get;}
        int Observations {get;}

        IReadOnlyList<ICurveModelValue> ModelValues {get;}

        IEnumerable<object> GetLoggerValues();
    }

    public interface ICurveModelValue : IReadOnlyConfiguration {
        int Observation {get;}
        double Moneyness {get;}
        double MarketIv {get;}
        double ModelIv {get;}

        IEnumerable<object> GetLoggerValues();
    }

    public interface ICurveSnap : IReadOnlyConfiguration {
        int Snap {get;}
        DateTime SnapTime {get;}
        OptionInfo Option {get;}
        double Moneyness {get;}
        double MarketIvBid {get;}
        double MarketIvOffer {get;}

        IEnumerable<object> GetLoggerValues();
    }

    #endregion
}
