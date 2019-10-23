using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AsyncHandler;
using Ecng.Collections;
using Ecng.Common;
using OptionBot.Config;
using MathNet.Numerics;
using MathNet.Numerics.Statistics;
using StockSharp.Messages;

namespace OptionBot.robot {
    public class CurveManager : Disposable {
        static readonly TimeSpan _periodicInterval = TimeSpan.FromMilliseconds(500);
        readonly Logger _log = new Logger(nameof(CurveManager));
        readonly Controller _controller;

        HandlerThread _curveThread;
        HTCancellationToken _periodicTimer;

        RobotData RobotData => _controller.RobotData;
        ConfigProvider ConfigProvider => _controller.ConfigProvider;
        IConfigGeneral CfgGeneral => ConfigProvider.General.Effective;
        Connector Connector => _controller.Connector;
        Connector.IConnectorSubscriber ConnectorSubscriber => _controller.ConnectorGUISubscriber;
        Scheduler Scheduler => _controller.Scheduler;

        bool _canCalcByInitialDelay;
        bool IsConnected => RobotData.IsConnected;
        bool CanCalculate {
            get {
                if(!IsConnected || !Scheduler.MarketPeriod.IsMarketOpen())
                    return false;

                if(_canCalcByInitialDelay)
                    return true;

                var tper = Scheduler.TradingPeriod;
                var hedgeStart = Scheduler.DeltaHedgeStartTime;

                if(tper == null || hedgeStart.IsDefault())
                    return false;

                var now = Connector.GetMarketTime();
                var begin = CfgGeneral.PreCurveBegin;

                _canCalcByInitialDelay = (now >= hedgeStart - TimeSpan.FromSeconds(begin));

                return _canCalcByInitialDelay;
            }
        }

        TimeSpan _curveInterval, _curveDiscrete, _preCurveBegin;
        int _curveDelayLimit, _preCurveDelayLimit;
        int _curveArrayVolume;
        TimeSpan _preCurveInterval, _preCurveDiscrete;

        readonly List<SeriesCurveState> _seriesStates = new List<SeriesCurveState>();

        public event Action<Exception> Error;

        public enum ResetType {Full, Manual}

        #region init/deinit

        public CurveManager(Controller ctl) {
            _controller = ctl;
            _curveThread = CreateCurveThread();
            ConfigProvider.General.EffectiveConfigChanged += OnGeneralCfgUpdated;
            OptionSeriesInfo.NewSeriesInfo += OnNewSeries;
            Scheduler.PeriodChanged += SchedulerOnPeriodChanged;

            UpdateGeneralParams();
        }

        protected override void DisposeManaged() {
            _log.Dbg.AddDebugLog("Disposing " + nameof(CurveManager));
            _curveThread.Dispose();
        }

        #endregion

        #region user config

        static readonly HashSet<string> _generalCfgCurveNames = new HashSet<string> {
            nameof(IConfigGeneral.CurveInterval),
            nameof(IConfigGeneral.CurveDiscrete),
            nameof(IConfigGeneral.PreCurveBegin),
            nameof(IConfigGeneral.CurveDelayLimit),
            nameof(IConfigGeneral.PreCurveDelayLimit),
        };

        void OnGeneralCfgUpdated(ICfgPairGeneral pair, string[] names) {
            if(!names.Any(n => _generalCfgCurveNames.Contains(n)) || _seriesStates.Count <= 0)
                return;

            _log.AddInfoLog("Конфиг кв был обновлен. Сброс параметров кв...");
            UpdateGeneralParams();
            ResetAll();
        }

        void OnSeriesConfigChanged(ICfgPairSeries pair, string[] strings) {
            var cfg = (IConfigSeries)((ICloneable)pair.Effective).Clone();

            _curveThread.ExecuteAsync(() => {
                var state = _seriesStates.FirstOrDefault(s => s.Series.SeriesId == pair.Effective.SeriesId);
                if(state == null) {
                    _log.Dbg.AddErrorLog($"OnSeriesConfigUpdate: state not found for '{pair.Effective.SeriesId.StrFutDate}'");
                    return;
                }

                _log.AddInfoLog($"Конфиг кв был обновлен. Сброс параметров кв для серии '{state.Series.SeriesId.StrFutDate}'...");
                state.Reset(ResetType.Full, cfg);
            });
        }

        void UpdateGeneralParams() {
            _curveThread.ExecuteAsync(() => {
                var cfg = CfgGeneral;

                _curveInterval = TimeSpan.FromSeconds(cfg.CurveInterval);
                _curveDiscrete = TimeSpan.FromSeconds(cfg.CurveDiscrete);
                _preCurveBegin = TimeSpan.FromSeconds(cfg.PreCurveBegin);
                _curveDelayLimit = cfg.CurveDelayLimit;
                _preCurveDelayLimit = cfg.PreCurveDelayLimit;

                _curveArrayVolume = cfg.CurveInterval / cfg.CurveDiscrete;
                _preCurveInterval = _preCurveBegin;
                _preCurveDiscrete = TimeSpan.FromSeconds(_preCurveInterval.TotalSeconds / _curveArrayVolume);
            });
        }

        #endregion

        #region curve thread

        HandlerThread CreateCurveThread(int prevId = 0) {
            var t = new HandlerThread("curve_thread") {
                OnCrashHandler = OnCurveThreadCrash, PropagateExceptions = false
            };
            t.Start();
            t.Post(() => _log.Dbg.AddInfoLog("Created curve thread (id={0}).{1}", Thread.CurrentThread.ManagedThreadId, prevId==0?"":" Previous thread ({0}) crashed.".Put(prevId)));

            _periodicTimer = t.PeriodicAction(OnPeriodicTimer, _periodicInterval);

            return t;
        }

        void OnCurveThreadCrash(object thread, Exception e) {
            _log.AddErrorLog("Необработанное исключение в потоке CurveManager. Поток будет заменен новым.\n{0}", e);
            _curveThread.Dispose();
            _curveThread = CreateCurveThread(Thread.CurrentThread.ManagedThreadId);

            Error?.Invoke(e);
        }

        #endregion

        void OnNewSeries(OptionSeriesInfo series) {
            _curveThread.ExecuteAsync(() => {
                var ss = _seriesStates.FirstOrDefault(s => s.Series.SeriesId == series.SeriesId);
                if(ss != null) {
                    _log.Dbg.AddErrorLog($"series already in list ({series.SeriesId.StrFutDate})");
                    return;
                }

                _log.Dbg.AddInfoLog("new series " + series.SeriesId.StrFutDate);
                var state = new SeriesCurveState(this, series);
                _seriesStates.Add(state);
                series.Config.EffectiveConfigChanged += OnSeriesConfigChanged;
            });
        }

        void SchedulerOnPeriodChanged(Scheduler scheduler, MarketPeriodType oldMarketPeriod, RobotPeriodType oldRobotPeriod) {
            if(oldMarketPeriod.IsMarketOpen() && !Scheduler.MarketPeriod.IsMarketOpen()) {
                _log.Dbg.AddInfoLog($"Market has closed. Resetting all curve params. {_seriesStates.Count} series in list.");
                _canCalcByInitialDelay = false;
                ResetAll();
            }
        }

        void ResetAll() {
            _log.Dbg.AddDebugLog("ResetAll(): count=" + _seriesStates.Count);
            _curveThread.ExecuteAsync(() => _seriesStates.ForEach(s => s.Reset(ResetType.Full)));
        }

        void OnPeriodicTimer() {
            _seriesStates.ForEach(s => {
                try {
                    s.CheckUpdate();
                } catch(Exception e) {
                    _log.Dbg.AddErrorLog($"Error updating {s.Series.SeriesId.StrFutDate}: \n" + e);
                    s.Reset(ResetType.Full);
                }
            });
        }

        public void ManualResetSeries(OptionSeriesInfo series, ResetType rtype = ResetType.Manual) {
            _curveThread.ExecuteAsync(() => _seriesStates.FirstOrDefault(s => s.Series.SeriesId == series.SeriesId)?.Reset(rtype));
        }

        class SeriesCurveState {
            Logger _log => _parent._log;
            static readonly TimeSpan _initTime = TimeSpan.FromSeconds(5);
            readonly CurveManager _parent;
            IConfigSeries _config;
            bool _initialized;
            bool _oldCanCalculate;
            ResetType? _reset;

            DateTime _now;
            DateTime _lastPreCurveUpdateTime, _lastCurveUpdateTime;
            DateTime _pcaEmptyStartTime;

            Controller Controller => _parent._controller;
            RobotData RobotData => _parent.RobotData;
            public OptionSeriesInfo Series {get;}
            DateTime NextUpdateTime {get; set;}
            bool CanCalculate => _config.CalculateCurve && _parent.CanCalculate && Series.IsSelectedForTrading;

            TimeSpan _preCurveInterval  => _parent._preCurveInterval;
            TimeSpan _curveInterval     => _parent._curveInterval;
            TimeSpan _preCurveBegin     => _parent._preCurveBegin;
            TimeSpan _preCurveDiscrete  => _parent._preCurveDiscrete;
            TimeSpan _curveDiscrete     => _parent._curveDiscrete;
            int _preCurveDelayLimit     => _parent._preCurveDelayLimit;
            int _curveDelayLimit        => _parent._curveDelayLimit;
            int _curveArrayVolume       => _parent._curveArrayVolume;

            readonly List<DateTime> _updateTimes = new List<DateTime>();

            public SeriesCurveState(CurveManager parent, OptionSeriesInfo series) {
                _parent = parent;
                Series = series;
                _config = (IConfigSeries)((ICloneable)Series.CfgSeries).Clone();
            }

            public void CheckUpdate() {
                _now = _parent.Connector.GetMarketTime();
                if(!_initialized) {
                    if(Series.LastOptionAddTime.IsDefault() || _now - Series.LastOptionAddTime < _initTime)
                        return;
                    _initialized = true;
                }

                var canCalc = CanCalculate;
                if(!canCalc) {
                    if(!_oldCanCalculate)
                        return;

                    // market close or disconnect etc happened
                    Reset(ResetType.Full);
                }

                _oldCanCalculate = canCalc;

                if(_reset == null && _now < NextUpdateTime)
                    return;

                try {
                    if(_reset != null) {
                        var r = _reset.Value;
                        _reset = null;
                        var newCfg = new CalculatedSeriesConfig(Series, false);
                        if(r == ResetType.Manual) {
                            newCfg.CopyFrom((CalculatedSeriesConfig)Series.CalcConfig);
                            newCfg.CurveModelStatus = CurveModelStatus.Reset;
                            newCfg.ClearCurveArray(CurveConfigType.Curve);
                        }

                        SetNewConfig(newCfg);
                        return;
                    }

                    if(canCalc && _now >= NextUpdateTime)
                        Update();

                } finally {
                    NextUpdateTime = _updateTimes.Count > 0 ? _updateTimes.Min() : _now;
                    NextUpdateTime = Util.Max(_now, NextUpdateTime);
                }
            }

            void Update() {
                var strikeStateCall = Series.GetActiveStrikesState(OptionTypes.Call);
                var strikeStatePut = Series.GetActiveStrikesState(OptionTypes.Put);
                var options = GetSuitableOptions(Series.OptionsSafe, strikeStateCall, strikeStatePut);

                var cfg = new CalculatedSeriesConfig(Series, false);
                cfg.CopyFrom((CalculatedSeriesConfig)Series.CalcConfig);
                var change = false;
                var preCurveTimeTrigger = _now - _lastPreCurveUpdateTime >= _preCurveDiscrete;

                try {
                    var pcaIsFull = !(cfg.MaxPreCurveSnap < _curveArrayVolume);
                    change |= TryAddReplaceSnapToPCA(cfg, options);

                    if(!pcaIsFull)
                        return;

                    if(_config.PreCalculationTrigger && preCurveTimeTrigger) {
                        change = true;

                        var pars = (CurveParams)cfg.CurveType(CurveTypeParam.Bid, CurveConfigType.PreCurve);
                        FitCurve(cfg, pars, cfg.PreCurveArray, CurveTypeParam.Bid, CurveConfigType.PreCurve);

                        pars = (CurveParams)cfg.CurveType(CurveTypeParam.Offer, CurveConfigType.PreCurve);
                        FitCurve(cfg, pars, cfg.PreCurveArray, CurveTypeParam.Offer, CurveConfigType.PreCurve);
                    }

                    var justCopied = false;
                    if(cfg.CurveArray.Count == 0) {
                        change = true;
                        CopyPreCurveToCurve(cfg);
                        justCopied = true;
                        _lastCurveUpdateTime = _now;
                        cfg.CurveModelStatus = CurveModelStatus.Valuation;
                    } else {
                        change |= TryReplaceSnapToCA(cfg, options);
                    }

                    var caIsFull = !(cfg.MaxCurveSnap < _curveArrayVolume);

                    if(caIsFull && (preCurveTimeTrigger || justCopied)) {
                        change = true;

                        var bidPars = (CurveParams)cfg.CurveType(CurveTypeParam.Bid, CurveConfigType.Curve);
                        FitCurve(cfg, bidPars, cfg.CurveArray, CurveTypeParam.Bid, CurveConfigType.Curve);

                        var offerPars = (CurveParams)cfg.CurveType(CurveTypeParam.Offer, CurveConfigType.Curve);
                        FitCurve(cfg, offerPars, cfg.CurveArray, CurveTypeParam.Offer, CurveConfigType.Curve);

                        var badObs  = bidPars.Observations < _config.MinObservations || offerPars.Observations < _config.MinObservations;
                        var badCorr = bidPars.Correlation < _config.MinCorrelation   || offerPars.Correlation < _config.MinCorrelation;
                        var badErr  = bidPars.StdError > _config.MaxStdError         || offerPars.StdError > _config.MaxStdError;

                        cfg.CurveModelStatus = badObs || badCorr || badErr ? CurveModelStatus.BadStats : CurveModelStatus.Valuation;
                    }
                } catch {
                    change = false;
                    throw;
                } finally {
                    if(change)
                        SetNewConfig(cfg);

                    if(cfg.MaxPreCurveSnap > _curveArrayVolume)
                        _log.Dbg.AddWarningLog($"warning: MaxPreCurveSnap > _curveArrayVolume ({cfg.MaxPreCurveSnap} > {_curveArrayVolume}), series={Series.SeriesId.StrFutDate}");

                    if(cfg.MaxCurveSnap > _curveArrayVolume)
                        _log.Dbg.AddWarningLog($"warning: MaxCurveSnap > _curveArrayVolume ({cfg.MaxCurveSnap} > {_curveArrayVolume}), series={Series.SeriesId.StrFutDate}");
                }
            }

            // returns true if changed anything
            bool TryAddReplaceSnapToPCA(CalculatedSeriesConfig cfg, OptionWrapper[] options) {
                if(cfg.MaxPreCurveSnap > 0 && _now - _lastPreCurveUpdateTime < _preCurveDiscrete)
                    return false;

                var pcaIsEmpty = cfg.PreCurveArray.IsEmpty();
                var pcaIsFull = !(cfg.MaxPreCurveSnap < _curveArrayVolume);
                var enoughLiquidOptions = options.Length >= _config.MinCurveInstruments;

                if(!enoughLiquidOptions) {
                    var change = false;

                    if(pcaIsEmpty) {
                        // если pca остается пустой слишком долго - переходим в состояние illiquid
                        var timeout = TimeSpan.FromTicks(_preCurveDelayLimit * _preCurveDiscrete.Ticks);
                        if(_pcaEmptyStartTime.IsDefault()) {
                            _pcaEmptyStartTime = _now;
                        } else if(_now - _pcaEmptyStartTime > timeout) {
                            change = true;
                            cfg.CurveModelStatus = CurveModelStatus.Illiquid;
                        }
                    } else {
                        _pcaEmptyStartTime = default(DateTime);
                        change = true;
                        ++cfg.PreCurveDelay;
                        if(cfg.PreCurveDelay > _preCurveDelayLimit) {
                            cfg.ClearCurveArray(CurveConfigType.PreCurve);
                            cfg.PreCurveDelay = 0;
                            //cfg.CurveModelStatus = CurveModelStatus.Illiquid;
                        }
                    }

                    return change;
                }

                cfg.PreCurveDelay = 0;

                var nextSnap = cfg.MaxPreCurveSnap + 1;

                if(pcaIsEmpty) {
                    nextSnap = 1;
                } else if(pcaIsFull) {
                    var minTime = cfg.PreCurveArray.Min(s => s.SnapTime);
                    var minSnap = cfg.PreCurveArray.First(s => s.SnapTime == minTime);
                    nextSnap = minSnap.Snap;
                    cfg.RemoveSnaps(CurveConfigType.PreCurve, nextSnap);
                }

                foreach(var o in options) {
                    var snap = new CurveSnap(cfg) {
                        Snap = nextSnap,
                        SnapTime = _now,
                        Option = o.Option,
                        CfgType = CurveConfigType.PreCurve,
                        Moneyness = o.ModelData.Moneyness,
                        MarketIvBid = o.ModelData.MarketIvBid,
                        MarketIvOffer = o.ModelData.MarketIvOffer,
                    };

                    cfg.AddSnap(snap);
                }

                _lastPreCurveUpdateTime = _now;
                SetNextUpdateTime(_now + _preCurveDiscrete);

                return true;
            }

            // returns true if changed anything
            bool TryReplaceSnapToCA(CalculatedSeriesConfig cfg, OptionWrapper[] options) {
                var caIsFull = !(cfg.MaxCurveSnap < _curveArrayVolume);
                if(!caIsFull) {
                    _log.AddErrorLog($"TryReplaceSnapToCA({Series.SeriesId.StrFutDate}): curve_array is not full");
                    cfg.CurveModelStatus = CurveModelStatus.Reset;
                    return true;
                }

                if(_now - _lastCurveUpdateTime < _curveDiscrete)
                    return false;

                var enoughLiquidOptions = options.Length >= _config.MinCurveInstruments;

                if(!enoughLiquidOptions) {
                    ++cfg.CurveDelay;
                    if(cfg.CurveDelay > _curveDelayLimit) {
                        cfg.ClearCurveArray(CurveConfigType.Curve);
                        cfg.CurveDelay = 0;
                        cfg.CurveModelStatus = CurveModelStatus.Reset;
                    }

                    return true;
                }

                cfg.CurveDelay = 0;

                var minTime = cfg.CurveArray.Min(s => s.SnapTime);
                var minSnap = cfg.CurveArray.First(s => s.SnapTime == minTime);
                var nextSnap = minSnap.Snap;
                cfg.RemoveSnaps(CurveConfigType.Curve, nextSnap);

                foreach(var o in options) {
                    var snap = new CurveSnap(cfg) {
                        Snap = nextSnap,
                        SnapTime = _now,
                        Option = o.Option,
                        CfgType = CurveConfigType.Curve,
                        Moneyness = o.ModelData.Moneyness,
                        MarketIvBid = o.ModelData.MarketIvBid,
                        MarketIvOffer = o.ModelData.MarketIvOffer,
                    };

                    cfg.AddSnap(snap);
                }

                _lastCurveUpdateTime = _now;
                SetNextUpdateTime(_now + _curveDiscrete);

                return true;
            }

            void CopyPreCurveToCurve(CalculatedSeriesConfig cfg) {
                cfg.ClearCurveArray(CurveConfigType.Curve);
                foreach(var snap in cfg.PreCurveArray) {
                    var newSnap = new CurveSnap(cfg);
                    newSnap.CopyFrom((CurveSnap)snap);
                    newSnap.CfgType = CurveConfigType.Curve;
                    cfg.AddSnap(newSnap);
                }
            }

            void ResetCurveTypeParams(CurveParams pars, ICurveParams ini) {
                pars.Reset();
                pars.A0 = ini.A0;
                pars.A1 = ini.A1;
                pars.A2 = ini.A2;
                pars.A3 = ini.A3;
            }

            OptionWrapper[] GetSuitableOptions(OptionInfo[] allOptions, object strikeStateCalls, object strikeStatePuts) {
                var result = new List<OptionWrapper>();

                foreach(var o in allOptions) {
                    var shift = o.Strike.AtmShiftByState(o.OptionType, o.OptionType == OptionTypes.Call ? strikeStateCalls : strikeStatePuts);
                    if(shift == null || o.TradingModule.ModuleState != StrategyState.Active)
                        continue;

                    var shiftCheck = o.OptionType == OptionTypes.Call ?
                                        shift.ShiftValue >= -_config.CurveItmInstruments && shift.ShiftValue <= _config.CurveInstruments - _config.CurveItmInstruments :
                                        shift.ShiftValue >= -(_config.CurveInstruments - _config.CurveItmInstruments) && shift.ShiftValue <= _config.CurveItmInstruments;

                    if(!shiftCheck)
                        continue;

                    var modelData = o.Model.LastData;
                    if(!modelData.CalcSuccessful || modelData.GreeksRegime != GreeksRegime.Liquid)
                        continue;

                    result.Add(new OptionWrapper(o, shift, modelData));
                }

                return result.ToArray();
            }

            void FitCurve(CalculatedSeriesConfig cfg, CurveParams ct, IReadOnlyList<ICurveSnap> ca, CurveTypeParam param, CurveConfigType cfgType) {
                if(param == CurveTypeParam.Ini)
                    throw new ArgumentException(nameof(param));

                if(!ReferenceEquals(ct.Parent, cfg))
                    throw new ArgumentException("wrong parent object");

                var start = SteadyClock.Now;
                var ini = cfg.CurveType(param, cfgType);
                ResetCurveTypeParams(ct, ini);

                var groups = ca.GroupBy(s => s.Option.Id).ToArray();
                var initialOptCount = groups.Length;

                var options = groups.Select(g => {
                        var opt = g.First();
                        return new OptionWrapper(opt.Option, null, opt.Option.Model.LastData);
                    }).Where(w => w.ModelData.GreeksRegime == GreeksRegime.Liquid && w.ModelData.CalcSuccessful)
                    .OrderBy(w => w.Option.Strike.Strike)
                    .ToArray();

                foreach(var w in options) {
                    ct.AddModelValue(new CurveModelValue(ct, w.Option) {
                        MarketIv = param == CurveTypeParam.Bid ? w.ModelData.MarketIvBid : w.ModelData.MarketIvOffer,
                        Moneyness = w.ModelData.Moneyness,
                    });
                }

                var polyOrder = ct.Model == CurveTypeModel.Cube ? 3 : (ct.Model == CurveTypeModel.Parabola ? 2 : 1);
                var moneynessArr = ct.ModelValues.Select(mv => mv.Moneyness).ToArray();
                var mktIvArr = ct.ModelValues.Select(mv => mv.MarketIv).ToArray();

                if(mktIvArr.Length < polyOrder + 1)
                    throw new InvalidOperationException($"mktIvArr array is too small (expected at least {polyOrder+1} elements, actual={mktIvArr.Length})");

                var result = Fit.Polynomial(moneynessArr, mktIvArr, polyOrder);

                if(result.Length < polyOrder + 1)
                    throw new InvalidOperationException($"Fit.Polynomial returned too small array. expected={polyOrder+1}, actual={result.Length}");

                ct.A0 = result[0];
                ct.A1 = result[1];

                switch(polyOrder) {
                    case 2:
                        ct.A2 = result[2];
                        break;
                    case 3:
                        ct.A2 = result[2];
                        ct.A3 = result[3];
                        break;
                }

                ct.Residual = options.Sum(w => CalcPoly(ct.Model, w.ModelData.Moneyness, result));
                ct.StdError = Math.Sqrt(ct.Residual / (ct.Observations - 2));

                foreach(var mv in ct.ModelValues.Cast<CurveModelValue>()) {
                    mv.ModelIv = CalcPoly(ct.Model, mv.Moneyness, result);
                }

                ct.Correlation = Correlation.Pearson(ct.ModelValues.Select(mv => mv.ModelIv).ToArray(), mktIvArr);

                var time = SteadyClock.Now - start;

                var ccc = 0;
                var resultsStr = result.Select(r => $"A{ccc++}={r:0.###}").Join(",");
                _log.Dbg.AddInfoLog($"FitCurve({Series.SeriesId.StrFutDate}, {cfgType}, {param}): model={ct.Model}, time={time.TotalMilliseconds:0.###}ms options=({initialOptCount},{options.Length}), result=[{resultsStr}]");
            }

            void SetNextUpdateTime(DateTime time) {
                _updateTimes.Add(time);
            }

            public void Reset(ResetType rtype, IConfigSeries newCfg = null) {
                _log.Dbg.AddDebugLog($"{Series.SeriesId.StrFutDate}: Reset({rtype})");
                _reset = rtype;
                if(newCfg != null)
                    _config = newCfg;
            }

            void SetNewConfig(CalculatedSeriesConfig newCfg) {
                var oldCfg = Series.CalcConfig;
                var oldStatus = oldCfg.CurveModelStatus;
                var newStatus = newCfg.CurveModelStatus;
                if(oldStatus != newStatus)
                    _log.Dbg.AddDebugLog($"status change: {oldStatus} ==> {newStatus}, curve_delay({oldCfg.CurveDelay} ==> {newCfg.CurveDelay}), pre_curve_delay({oldCfg.PreCurveDelay} ==> {newCfg.PreCurveDelay})");

                Series.UpdateCalcConfig(newCfg);
                newCfg.Log(Controller.RobotLogger);
            }

            double CalcPoly(CurveTypeModel model, double x, double[] pars) {
                int parabola, cube;
                double p3, p2;
                p3 = p2 = parabola = cube = 0;

                switch(model) {
                    case CurveTypeModel.Parabola:
                        parabola = 1;
                        p2 = pars[2];
                        break;
                    case CurveTypeModel.Cube:
                        parabola = cube = 1;
                        p2 = pars[2];
                        p3 = pars[3];
                        break;
                }

                var x2 = x * x;
                var x3 = x2 * x;

                return p3 * cube * x3 +
                       p2 * parabola * x2 +
                       pars[1] * x +
                       pars[0];
            }

            struct OptionWrapper {
                public OptionInfo Option {get;}
                public OptionStrikeShift Shift {get;}
                public IOptionModelData ModelData {get;}

                public OptionWrapper(OptionInfo o, OptionStrikeShift shift, IOptionModelData modelData) {
                    Option = o;
                    Shift = shift;
                    ModelData = modelData;
                }
            }
        }
    }
}
