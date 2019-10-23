using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Ecng.Common;
using Ecng.Xaml;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using System.Collections.ObjectModel;

namespace OptionBot.robot {
    [Serializable]
    [DataContract]
    public class OptionSeriesId : Equatable<OptionSeriesId> {
        // for details on security code parsing, see http://moex.com/s205
        static readonly Regex _optionsSeriesRegex = new Regex(@"\d([^\d])(.)(.+?)$", RegexOptions.Compiled | RegexOptions.RightToLeft);
        static readonly SecurityIdGenerator _secIdGenerator = new SecurityIdGenerator();

        static readonly ThreadSafeObservableCollection<OptionSeriesId> _allSeriesIds = new ThreadSafeObservableCollection<OptionSeriesId>();
        static readonly HashSet<OptionSeriesId> _allSeriesIdsSet = new HashSet<OptionSeriesId>();

        public static ObservableCollection<OptionSeriesId> AllIds => _allSeriesIds;

        string _id;
        string _futureCode;
        int _hashCode;

        [DataMember] string _futureId, _seriesCode;
        [DataMember] DateTime _expirationDate;

        public string Id {get { return _id ?? (_id = "{0}-{1}-{2:dd.MM.yyyy}".Put(FutureCode, SeriesCode, ExpirationDate)); }}

        public string FutureId {get {return _futureId;}}
        public string SeriesCode {get {return _seriesCode;}}
        public string FutureCode {get {return _futureCode ?? (_futureCode = _secIdGenerator.Split(_futureId).Item1);}}
        public DateTime ExpirationDate {get {return _expirationDate;}}

        public string StrFutSerCodeShortDate {get {return "{0}-{1}-{2:dd.MMM}".Put(FutureCode, SeriesCode, ExpirationDate); }}
        public string StrSerCodeShortDate {get {return "{0}-{1:dd.MMM}".Put(SeriesCode, ExpirationDate); }}
        public string StrFutDate => $"{FutureCode}-{ExpirationDate:ddMMMyy}";

        OptionSeriesId(string futId, string seriesCode, DateTime expirationDate) {
            _futureId = futId;
            _seriesCode = seriesCode;
            _expirationDate = expirationDate;

            OnNewObject();
        }

        public override string ToString() { return Id; }

        public override int GetHashCode() { return EnsureGetHashCode(); }

        int EnsureGetHashCode() {
            if (_hashCode == 0)
                _hashCode = Id.GetHashCode();

            return _hashCode;
        }

        public override OptionSeriesId Clone() {
            return new OptionSeriesId(FutureId, SeriesCode, ExpirationDate);
        }

        protected override bool OnEquals(OptionSeriesId other) {
            return
                FutureCode == other.FutureCode &&
                SeriesCode == other.SeriesCode &&
                ExpirationDate == other.ExpirationDate;
        }

        [OnDeserialized]
        void DeserializedHandler(StreamingContext context) {
            OnNewObject();
        }

        void OnNewObject() {
            lock(_allSeriesIdsSet) {
                if(_allSeriesIdsSet.Contains(this))
                    return;

                _allSeriesIdsSet.Add(this);
                _allSeriesIds.Add(this);
            }
        }

        public static OptionSeriesId Create(Security option) {
            OptionInfo.EnsureCorrectOption(option);

            var matches = _optionsSeriesRegex.Matches(option.Code);

            if(matches.Count == 0)
                throw new InvalidOperationException("Unable to parse option code '{0}'".Put(option.Code));

            var match = matches[matches.Count-1];

            var oType = match.Groups[1].Value;
            var monthCallPut = match.Groups[2].Value;
            var rest = match.Groups[3].Value;

            var serCode = "{0}*{1}".Put(oType, rest);

            // ReSharper disable once PossibleInvalidOperationException
            return new OptionSeriesId(option.UnderlyingSecurityId, serCode, option.ExpiryDate.Value);
        }
    }
}
