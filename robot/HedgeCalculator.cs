using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Ecng.Collections;
using Ecng.Common;
using StockSharp.BusinessEntities;
using StockSharp.Messages;

namespace OptionBot.robot {
    public interface IHedgeCalculatorInputData {
        IEnumerable<Tuple<OptionInfo, int>> InputOptions {get;}
        IReadOnlyDictionary<string, Tuple<object, object>> StrikeStates {get;}
        int FuturePosition {get;}
        decimal FutureBid {get;}
        decimal FutureAsk {get;}
        DateTime Now {get;}
    }

    public class HedgeCalculatorInputData : IHedgeCalculatorInputData {
        public IEnumerable<Tuple<OptionInfo, int>> InputOptions {get; set;}
        public IReadOnlyDictionary<string, Tuple<object, object>> StrikeStates {get; set;}
        public int FuturePosition {get; set;}
        public decimal FutureBid {get; set;}
        public decimal FutureAsk {get; set;}
        public DateTime Now {get; set;}
    }

    /// <summary>
    /// Калькулятор для модуля хеджирования.
    /// </summary>
    public class HedgeCalculator {
        static readonly Logger _log = new Logger();
        static readonly TimeSpan _warningPeriod = TimeSpan.FromMinutes(5);
        DateTime _lastWarnTime;

        #region calc fields

        bool _calculating;
        readonly Robot _robot;
        readonly FuturesInfo _future;

        readonly List<string> _messages = new List<string>();
        public string Messages => string.Join(", ", _messages);

        public bool LastUpdateSuccessful {get; private set;}

        public decimal CalculatedHedgedFuturePosition {get; private set;}
        public decimal Exposition {get; private set;}
        public decimal VegaPortfolio {get; private set;}
        public decimal VegaCallPortfolio {get; private set;}
        public decimal VegaPutPortfolio {get; private set;}
        public decimal GammaPortfolio {get; private set;}
        public decimal ThetaPortfolio {get; private set;}
        public decimal VannaPortfolio {get; private set;}
        public decimal VommaPortfolio {get; private set;}

        readonly List<Tuple<string, decimal, double, double, IOptionModelInputData>> _lastCalcDetails = new List<Tuple<string, decimal, double, double, IOptionModelInputData>>();

        public string LastCalcDetails {
            get {
                return string.Join(", ", 
                    _lastCalcDetails.Select(t => "({0}: pos={1} delta={2:0.#####}({3}) ba={4}/{5} fba={6}/{7})"
                        .Put(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5.OptionCalcBid, t.Item5.OptionCalcAsk, t.Item5.FutureCalcBid, t.Item5.FutureCalcAsk)));
            }
        }

        Dictionary<string, OptionInfo> _myOptions; 

        #endregion

        public HedgeCalculator(Robot robot, FuturesInfo future) {
            _robot = robot;
            _future = future;
        }

        /// <summary>
        /// Обновить список опционов.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void UpdateOptionsInfo() {
            _messages.Clear();
            _myOptions = _future.DerivedOptions.ToDictionary(o => o.NativeSecurity.Id);
        }

        public IEnumerable<Tuple<OptionInfo, int>> CreateInputOptionsList(IEnumerable<Position> positions) {
            var list = new List<Tuple<OptionInfo, int>>();
            var ids = new HashSet<string>();

            foreach(var p in positions.Where(p => p.Security.OptionType != null)) {
                var option = _myOptions.TryGetValue(p.Security.Id);
                if(option == null || !option.IsOptionActive)
                    continue;

                var realpos = (int)_robot.GetRealPosition(p.Security.Id);
                if(realpos == 0)
                    continue;

                if(option.NativeSecurity != p.Security) {
                    _log.Dbg.AddErrorLog("OptionInfo.NativeSecurity differs from Position.Security");
                    continue;
                }

                if(!ids.Add(option.Id)) {
                    _log.Dbg.AddErrorLog("found duplicate position with same security id");
                    continue;
                }

                list.Add(Tuple.Create(option, realpos));
            }

            return list;
        }

        /// <summary>
        /// Пересчитать параметры хеджирования на основе актуальных данных.
        /// </summary>
        /// <returns></returns>
        public bool Update(IHedgeCalculatorInputData input) {
            if(_calculating) _log.Dbg.AddErrorLog("Update({0}) called in parallel", _future.Code);

            _calculating = true;
            try {
                return UpdateImpl(input);
            } finally {
                _calculating = false;
            }
        }

        bool UpdateImpl(IHedgeCalculatorInputData input) {
            LastUpdateSuccessful = false;
            CalculatedHedgedFuturePosition = Exposition = VegaPortfolio = VegaCallPortfolio = VegaPutPortfolio = 0;
            _messages.Clear();
            _lastCalcDetails.Clear();
            var noVpList = new List<OptionInfo>();

            double vegaPort, vegaCallPort, vegaPutPort;
            double gammaPort, thetaPort, vannaPort, vommaPort;
            double calcPos;

            vegaPort = vegaCallPort = vegaPutPort = gammaPort = thetaPort = calcPos = vannaPort = vommaPort = 0d;

            var errors = new List<string>();

            foreach(var tpl in input.InputOptions) {
                // option,call,put: can use ONLY stable properties, since this method is called from a separate thread
                var option = tpl.Item1;
                var pos = tpl.Item2;

                if(pos == 0) 
                    continue;

                var curveHedging = option.Series.CfgSeries.CurveHedging;

                double deltaBid;

                var tuple = input.StrikeStates[option.Series.SeriesId.Id];
                var vp = option.CfgValuationParamsByState(option.OptionType == OptionTypes.Call ? tuple.Item1 : tuple.Item2);
                if(vp == null) {
                    noVpList.Add(option);
                    option.EmpiricDelta = 0d;
                    continue;
                }

                var data = option.Model.LastData;
                var isActive = option.TradingModule.ModuleState == StrategyState.Active;

                if(!isActive) {
                    deltaBid = 0;
                    errors.Add($"({option.Code} module not active)");
                } else if(!data.CalcSuccessful) {
                    deltaBid = 0;
                    errors.Add($"({option.Code} b/o={data.Input.OptionCalcBid}/{data.Input.OptionCalcAsk}, err={data.Error})");
                } else {
                    deltaBid = data.DeltaBid;
                }

                var deltaShift = (double)vp.EmpiricDeltaShift;
                double delta, vega, gamma, theta, vanna, vomma;

                var curveModelStatus = data.CurveModelStatus;
                if(curveHedging && curveModelStatus == CurveModelStatus.Valuation) {
                    delta = data.CurveDelta;
                    vega = data.CurveVega;
                    gamma = data.CurveGamma;
                    theta = data.CurveTheta;
                    vanna = data.CurveVanna;
                    vomma = data.CurveVomma;
                } else {
                    delta = deltaBid;
                    vega = data.Vega;
                    gamma = data.Gamma;
                    theta = data.Theta;
                    vanna = data.Vanna;
                    vomma = data.Vomma;
                }

                option.EmpiricDelta = delta + deltaShift;
                vegaPort += vega * pos;
                gammaPort += gamma * pos;
                thetaPort += theta * pos;
                vannaPort += vanna * pos;
                vommaPort += vomma * pos;

                _lastCalcDetails.Add(Tuple.Create(option.Code, (decimal)pos, option.EmpiricDelta, deltaShift, data.Input));

                if(option.OptionType == OptionTypes.Call) {
                    calcPos -= pos * option.EmpiricDelta;
                    vegaCallPort += vega * pos;
                } else {
                    calcPos -= pos * (option.EmpiricDelta - 1);
                    vegaPutPort += vega * pos;
                }
            }

            CalculatedHedgedFuturePosition = calcPos.ToDecimalChecked();

            if(noVpList.Count > 0) {
                var msg = "нет valuation_params для опционов с позицией: ({0})".Put(string.Join(",", noVpList.Select(o => o.Code)));
                //errors.Add(msg);

                if(input.Now - _lastWarnTime > _warningPeriod) {
                    _lastWarnTime = input.Now;
                    _log.AddWarningLog("{0}: {1}", _future.Code, msg);
                }
            }

            if(errors.Any()) {
                CalculatedHedgedFuturePosition = 0;
                AddMsg("empiric_delta error: {0}".Put(string.Join(", ", errors)));
                return LastUpdateSuccessful;
            }

            VegaPortfolio = vegaPort.ToDecimalChecked();
            VegaCallPortfolio = vegaCallPort.ToDecimalChecked();
            VegaPutPortfolio = vegaPutPort.ToDecimalChecked();
            GammaPortfolio = gammaPort.ToDecimalChecked();
            ThetaPortfolio = thetaPort.ToDecimalChecked();
            VannaPortfolio = vannaPort.ToDecimalChecked();
            VommaPortfolio = vommaPort.ToDecimalChecked();
            Exposition = input.FuturePosition - CalculatedHedgedFuturePosition;

            return LastUpdateSuccessful = true;
        }

        void AddMsg(string message) { _messages.Add(message); }
    }
}
