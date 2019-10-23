using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using OptionBot.robot;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

namespace OptionBot.Config {
    [Serializable]
    [DataContract]
    [CategoryOrder(CategorySeries, 1)]
    public class ConfigSeries : BaseConfig<ConfigSeries, IConfigSeries>, IConfigSeries {
        const string CategorySeries = "Серия опционов";

        #region fields/properties

        [DataMember] OptionSeriesId _seriesId;
        [DataMember] int _curveInstruments, _curveItmInstruments, _minCurveInstruments, _minObservations;
        [DataMember] double _minCorrelation, _maxStdError;
        [DataMember] bool _preCalculationTrigger, _calculateCurve, _curveHedging;
        [DataMember] CurveTypeModel _curveTypeIniModel, _curveTypeBidModel, _curveTypeOfferModel;
        [DataMember] CurveTypeModel _preCurveTypeIniModel, _preCurveTypeBidModel, _preCurveTypeOfferModel;

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"series_id")]
        [Description(@"Серия опционов.")]
        public OptionSeriesId SeriesId {get {return _seriesId;} set{ SetField(ref _seriesId, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"curve_instruments")]
        [Description(@"Количество опционных инструментов каждого типа (кол/пут) для расчета параметров кривой волатильности.")]
        public int CurveInstruments {get {return _curveInstruments;} set{ SetField(ref _curveInstruments, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"curve_itm_instruments")]
        [Description(@"Количество инструментов 'в деньгах' для расчета параметров кв, служит для определения списка инструментов для расчета параметров кв.")]
        public int CurveItmInstruments {get {return _curveItmInstruments;} set{ SetField(ref _curveItmInstruments, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"min_curve_instruments")]
        [Description(@"Минимально достаточное количество ликвидных инструментов для расчета параметров кв.")]
        public int MinCurveInstruments {get {return _minCurveInstruments;} set{ SetField(ref _minCurveInstruments, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"min_observations")]
        [Description(@"Минимально допустимое количество ненулевых значений волатильности для использования готовой модели кв.")]
        public int MinObservations {get {return _minObservations;} set{ SetField(ref _minObservations, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"min_correlation")]
        [Description(@"Минимально допустимое значение коэффициента корреляции между исходными и смоделированными данными для использования готовой модели кв.")]
        public double MinCorrelation {get {return _minCorrelation;} set{ SetField(ref _minCorrelation, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"max_std_error")]
        [Description(@"Максимально допустимое значение стандартной ошибки прогноза для использования готовой модели кв.")]
        public double MaxStdError {get {return _maxStdError;} set{ SetField(ref _maxStdError, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"calculate_curve")]
        [Description(@"Включатель расчета кривой волатильности.")]
        public bool CalculateCurve {get {return _calculateCurve;} set{ SetField(ref _calculateCurve, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"pre_calculation_trigger")]
        [Description(@"Включатель подбора параметров кв на основании данных таблицы pre_curve_array().")]
        public bool PreCalculationTrigger {get {return _preCalculationTrigger;} set{ SetField(ref _preCalculationTrigger, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"curve_hedging")]
        [Description(@"Расчет греков с использованием кв.")]
        public bool CurveHedging {get {return _curveHedging;} set{ SetField(ref _curveHedging, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"curve_type_ini_model")]
        [Description(@"Вид модели кв для ini.")]
        public CurveTypeModel CurveTypeIniModel {get {return _curveTypeIniModel;} set{ SetField(ref _curveTypeIniModel, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"curve_type_bid_model")]
        [Description(@"Вид модели кв для bid.")]
        public CurveTypeModel CurveTypeBidModel {get {return _curveTypeBidModel;} set{ SetField(ref _curveTypeBidModel, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"curve_type_offer_model")]
        [Description(@"Вид модели кв для offer.")]
        public CurveTypeModel CurveTypeOfferModel {get {return _curveTypeOfferModel;} set{ SetField(ref _curveTypeOfferModel, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"pre_curve_type_ini_model")]
        [Description(@"Вид предварительной модели кв для ini.")]
        public CurveTypeModel PreCurveTypeIniModel {get {return _preCurveTypeIniModel;} set{ SetField(ref _preCurveTypeIniModel, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"pre_curve_type_bid_model")]
        [Description(@"Вид предварительной модели кв для bid.")]
        public CurveTypeModel PreCurveTypeBidModel {get {return _preCurveTypeBidModel;} set{ SetField(ref _preCurveTypeBidModel, value);}}

        [Category(CategorySeries)]
        [AutoPropertyOrder]
        [DisplayName(@"pre_curve_type_offer_model")]
        [Description(@"Вид предварительной модели кв для offer.")]
        public CurveTypeModel PreCurveTypeOfferModel {get {return _preCurveTypeOfferModel;} set{ SetField(ref _preCurveTypeOfferModel, value);}}

        #endregion

        public override void VerifyConfig(List<string> errors) {
            base.VerifyConfig(errors);

            if(CurveInstruments < 1) errors.Add(nameof(CurveInstruments));
            if(CurveItmInstruments < 0) errors.Add(nameof(CurveItmInstruments));
            if(MinCurveInstruments < 0) errors.Add(nameof(MinCurveInstruments));
            if(MinObservations < 0) errors.Add(nameof(MinObservations));
            if(MinCorrelation < -1 || MinCorrelation > 1) errors.Add(nameof(MinCorrelation));
            if(MaxStdError < 0) errors.Add(nameof(MaxStdError));
        }

        protected override void SetDefaultValues() {
            base.SetDefaultValues();

            CurveInstruments = MinCurveInstruments = 7;
            CurveItmInstruments = MinObservations = 1;
            MinCorrelation = MaxStdError = 0;
            PreCalculationTrigger = CalculateCurve = false;
            CurveHedging = true;

            CurveTypeIniModel = CurveTypeBidModel = CurveTypeOfferModel = CurveTypeModel.Parabola;
            PreCurveTypeIniModel = PreCurveTypeBidModel = PreCurveTypeOfferModel = CurveTypeModel.Parabola;
        }

        protected override void CopyFromImpl(ConfigSeries other) {
            base.CopyFromImpl(other);

            SeriesId = other.SeriesId;
            CurveInstruments = other.CurveInstruments;
            CurveItmInstruments = other.CurveItmInstruments;
            MinCurveInstruments = other.MinCurveInstruments;
            MinObservations = other.MinObservations;
            MinCorrelation = other.MinCorrelation;
            MaxStdError = other.MaxStdError;
            CalculateCurve = other.CalculateCurve;
            PreCalculationTrigger = other.PreCalculationTrigger;
            CurveHedging = other.CurveHedging;
            CurveTypeIniModel = other.CurveTypeIniModel;
            CurveTypeBidModel = other.CurveTypeBidModel;
            CurveTypeOfferModel = other.CurveTypeOfferModel;
            PreCurveTypeIniModel = other.PreCurveTypeIniModel;
            PreCurveTypeBidModel = other.PreCurveTypeBidModel;
            PreCurveTypeOfferModel = other.PreCurveTypeOfferModel;
        }

        protected override bool OnEquals(ConfigSeries other) {
            return
                base.OnEquals(other) &&
                    SeriesId == other.SeriesId &&
                    CurveInstruments == other.CurveInstruments &&
                    CurveItmInstruments == other.CurveItmInstruments &&
                    MinCurveInstruments == other.MinCurveInstruments &&
                    MinObservations == other.MinObservations &&
                    MinCorrelation.IsEqual(other.MinCorrelation) &&
                    MaxStdError.IsEqual(other.MaxStdError) &&
                    CalculateCurve == other.CalculateCurve &&
                    PreCalculationTrigger == other.PreCalculationTrigger &&
                    CurveHedging == other.CurveHedging &&
                    CurveTypeIniModel == other.CurveTypeIniModel &&
                    CurveTypeBidModel == other.CurveTypeBidModel &&
                    CurveTypeOfferModel == other.CurveTypeOfferModel &&
                    PreCurveTypeIniModel == other.PreCurveTypeIniModel &&
                    PreCurveTypeBidModel == other.PreCurveTypeBidModel &&
                    PreCurveTypeOfferModel == other.PreCurveTypeOfferModel;
        }

        public static IEnumerable<string> GetLoggerFields() {
            return new[] {
                nameof(SeriesId),
                nameof(CurveInstruments),
                nameof(CurveItmInstruments),
                nameof(MinCurveInstruments),
                nameof(MinObservations),
                nameof(MinCorrelation),
                nameof(MaxStdError),
                nameof(CalculateCurve),
                nameof(PreCalculationTrigger),
                nameof(CurveHedging),
                nameof(CurveTypeIniModel),
                nameof(CurveTypeBidModel),
                nameof(CurveTypeOfferModel),
                nameof(PreCurveTypeIniModel),
                nameof(PreCurveTypeBidModel),
                nameof(PreCurveTypeOfferModel),
            };
        }

        public IEnumerable<object> GetLoggerValues() {
            return new object[] {
                SeriesId,
                CurveInstruments,
                CurveItmInstruments,
                MinCurveInstruments,
                MinObservations,
                MinCorrelation,
                MaxStdError,
                CalculateCurve,
                PreCalculationTrigger,
                CurveHedging,
                CurveTypeIniModel,
                CurveTypeBidModel,
                CurveTypeOfferModel,
                PreCurveTypeIniModel,
                PreCurveTypeBidModel,
                PreCurveTypeOfferModel,
            };
        }
    }

    #region strategy config interfaces

    public interface IConfigSeries : IReadOnlyConfiguration {
        OptionSeriesId SeriesId {get;}
        int CurveInstruments {get;}
        int CurveItmInstruments {get;}
        int MinCurveInstruments {get;}
        int MinObservations {get;}
        double MinCorrelation {get;}
        double MaxStdError {get;}
        bool CalculateCurve {get;}
        bool PreCalculationTrigger {get;}
        bool CurveHedging {get;}
        CurveTypeModel CurveTypeIniModel {get;}
        CurveTypeModel CurveTypeBidModel {get;}
        CurveTypeModel CurveTypeOfferModel {get;}
        CurveTypeModel PreCurveTypeIniModel {get;}
        CurveTypeModel PreCurveTypeBidModel {get;}
        CurveTypeModel PreCurveTypeOfferModel {get;}

        IEnumerable<object> GetLoggerValues();
    }

    #endregion
}
