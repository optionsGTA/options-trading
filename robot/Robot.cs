using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AsyncHandler;
using Ecng.Collections;
using Ecng.Common;
using MoreLinq;
using OptionBot.Config;
using StockSharp.Algo;
using StockSharp.Algo.Strategies;
using StockSharp.BusinessEntities;
using StockSharp.Messages;

namespace OptionBot.robot {
    [Flags]
    public enum CanTradeState {
        TradingDisabled     = 0x00,
        CanCalculate        = 0x01,
        CanClosePositions   = 0x02,
        CanOpenPositions    = 0x04
    }

    /// <summary>
    /// Класс, содержащий основную логику для запуска/остановки торговых стратегий по всем инструментам.
    /// </summary>
    public class Robot : Disposable {
        #region fields/properties
        readonly Logger _log = new Logger("робот");

        bool _disposing, _stopping;
        bool _fatalErrorState;

        public Controller Controller {get; private set;}
        public Scheduler Scheduler {get {return Controller.Scheduler;}}
        RobotData RobotData {get {return Controller.RobotData;}}
        ConfigProvider ConfigProvider {get {return Controller.ConfigProvider;}}
        public IConfigGeneral CfgGeneral {get {return Controller.ConfigProvider.General.Effective; }}
        Connector Connector {get {return Controller.Connector; }}
        ConnectionState ConnectionState {get {return RobotData.ConnectionState; }}
        public PlazaTraderEx Trader {get {return Connector.Trader;}}

        readonly Func<HandlerThread> _robotThreadGetter;
        public HandlerThread RobotThread { get {return _robotThreadGetter(); }}

        public CanTradeState CanTradeState {get; private set;}

        public RobotLogger RobotLogger {get {return Controller.RobotLogger; }}

        public decimal TransactionsCommission {get; private set;}

        public bool AllStopped {get; private set;} = true;

        HTCancellationToken _closePositionsAction;
        DateTime _lastClosePosRequest;

        Mutex _mutex;

        public CurveManager CurveManager {get;}

        readonly Dictionary<string, FutureTradingModule> _futureModules = new Dictionary<string, FutureTradingModule>();
        readonly Dictionary<string, OptionTradingModule> _optionModules = new Dictionary<string, OptionTradingModule>();
        readonly SynchronizedDictionary<string, SecurityMainStrategy> _securityStrategies = new SynchronizedDictionary<string, SecurityMainStrategy>();

        #endregion

        public event Action RecalculateRequired;
        public event Action<int> RobotStateChanged;
        public event Action CanTradeStateChanged;
        public event Action Deinitialized;

        static readonly TimeSpan _periodicInterval = TimeSpan.FromSeconds(1);
        readonly HTCancellationToken _timerToken;
        public event Action Periodic;

        #region init/deinit

        public Robot(Controller controller, Func<HandlerThread> robotThreadGetter) {
            Controller = controller;
            _robotThreadGetter = robotThreadGetter;

            ConfigProvider.General.EffectiveConfigChanged += (pair, names) => RobotThread.ExecuteAsync(() => OnSettingsUpdated(pair, names));
            Controller.TransactionListener.NumTransactionsChanged += () => RobotThread.ExecuteAsync(OnNumTransactionsChanged);

            Connector.DefaultSubscriber.PortfolioChanged += OnPortfolioChanged;
            Connector.DefaultSubscriber.NewPosition += OnPositionChanged;
            Connector.DefaultSubscriber.PositionChanged += OnPositionChanged;
            Connector.DefaultSubscriber.NewOrder += OnOrderChanged;
            Connector.DefaultSubscriber.OrderChanged += OnOrderChanged;
            Connector.DefaultSubscriber.OrderFailed += OnOrderFailed;
            Connector.DefaultSubscriber.OrderCancelFailed += OnOrderCancelFailed;
            Connector.DefaultSubscriber.NewMyTrade += OnNewMyTrade;
            Connector.DefaultSubscriber.RealtimeModeChanged += OnRealtimeModeChanged;
            Connector.DefaultSubscriber.Error += (connector, msg, err) => OnConnectorError(msg, err);
            Connector.DefaultSubscriber.ConnectorReset += OnConnectorReset;
            Connector.DefaultSubscriber.ConnectionStateChanged += (c, state, ex) => RobotThread.ExecuteAsync(() => OnConnectionStateChanged(c, state, ex));

            RobotData.StrategyListChanged += () => RecheckStrategiesState("strategy list change");
            RobotData.RobotReset += () => RecheckStrategiesState("robotdata reset");
            ConfigProvider.ConfigSecuritySelection.EffectiveConfigChanged += (sel, strings) => RecheckStrategiesState("cfg selection", true);
            ConfigProvider.ValuationParams.ListOrItemChanged += isListChange => RecheckStrategiesState("cfg VP");
            OptionSeriesInfo.AnySeriesAtmStrikeChanged += (ser, oldCall, oldPut) => RecheckStrategiesState("ATM");
            Scheduler.PeriodChanged += (scheduler, oldMarketPeriod, oldRobotPeriod) => RecheckStrategiesState("period");
            SecurityInfo.NativeSecurityReplaced += si => RecheckStrategiesState($"native sec replaced ({si.Code})");

            RobotStateChanged += OnRobotStateChanged;

            _timerToken = RobotThread.PeriodicAction(() => Periodic?.Invoke(), _periodicInterval);

            CurveManager = new CurveManager(Controller);
            CurveManager.Error += CurveManagerOnError;
        }

        protected override void DisposeManaged() {
            if(!RobotThread.InThread()) { RobotThread.ExecuteAsync(DisposeManaged); return; }
            if(_disposing) { _log.Dbg.AddWarningLog("Robot.Dispose: already disposing"); return; }

            _disposing = true;

            _log.Dbg.AddInfoLog("Robot.Dispose()");

            Util.CancelDelayedAction(ref _closePositionsAction);
            StopEverything();

            var disposed = false;

            Action dispose = () => {
                _log.Dbg.AddInfoLog("Robot.Dispose() proceed...");
                var numss = _securityStrategies.Count;
                if(numss > 0) {
                    _log.Dbg.AddErrorLog($"_securityStrategies were not cleared ({numss})");
                    CheckClearSecurityStrategies(true);
                }

                disposed = true;
                DisposeMutex();
                CurveManager.Dispose();

                base.DisposeManaged();

                Deinitialized.SafeInvoke();
            };

            if(!AllStopped) {
                RobotStateChanged += num => {
                    if(!AllStopped || disposed)
                        return;

                    dispose();
                };
            } else {
                dispose();
            }
        }

        void CheckClearSecurityStrategies(bool forceStop = false) {
            var numSecStra = _securityStrategies.Count;
            if(numSecStra <= 0) return;

            _log.Dbg.AddInfoLog($"CheckClearSecurityStrategies: {numSecStra} strategies in list");
            _securityStrategies.Values.Where(s => !s.IsStopping).ForEach(s => {
                UnsubscribeSecStrategy(s);
                try {
                    s.Stop("checkclear", forceStop);
                } catch(Exception e) {
                    _log.Dbg.AddErrorLog(Util.FormatError($"Unable to stop {s.Name}", e));
                }

                if(forceStop)
                    s.Dispose();
            });

            _securityStrategies.Clear();
        }

        #endregion

        #region event handlers

        void OnSettingsUpdated(ICfgPairGeneral pair, string[] strings) {
            if(!strings.Contains(nameof(CfgGeneral.Portfolio)))
                return;

            _log.Dbg.AddInfoLog("Portfolio changed: {0}", CfgGeneral.Portfolio);

            OptionTradingModule[] modules;
            lock(_optionModules)
                modules = _optionModules.Values.ToArray();

            if(modules.Any(m => !m.ModuleState.Inactive()))
                StopEverything();
            else
                RecheckStrategiesState("portfolio update");
        }

        DateTime _lastTransactionMessageTime;
        readonly TimeSpan _transactionMessageInterval = TimeSpan.FromMinutes(1);

        void OnNumTransactionsChanged() {
            var now = DateTime.UtcNow;

            if(now - _lastTransactionMessageTime < _transactionMessageInterval) {
                UpdateCanTradeState(RobotData.ActivePortfolio, false);
            } else {
                _lastTransactionMessageTime = now;
                UpdateCanTradeState(RobotData.ActivePortfolio, false, true);
            }
        }

        void OnRobotStateChanged(int numRunningStrategies) {
            if(_disposing)
                return;

            if(RobotData.IsConnected && numRunningStrategies == 0 && _ordersWereSent)
                _ordersWereSent = !TryCancelOrders();

            if(_stopping && AllStopped) {
                _log.Dbg.AddDebugLog("AllStopped");
                _stopping = false;
                // force async to give other RobotStateChanged handlers a chance to handle AllStopped
                RobotThread.ExecuteForceAsync(() => RecheckStrategiesState("AllStopped", true));
            }
        }

        bool _ordersCanceledOnConnect;

        void OnConnectionStateChanged(Connector c, ConnectionState state, Exception ex) {
            if(!_ordersCanceledOnConnect && state.CanCancelOrders() && RobotData.IsRobotStopped) {
                _ordersCanceledOnConnect = TryCancelOrders();
            }

            if(!state.CanCancelOrders())
                _ordersCanceledOnConnect = false;

            Util.CancelDelayedAction(ref _closePositionsAction);
            RecheckStrategiesState("conn={0}".Put(state));
        }

        void CurveManagerOnError(Exception exception) {
            SetFatalErrorState("Ошибка CurveManager", exception);
        }

        void SetFatalErrorState(string message, Exception exception) {
            RobotThread.ExecuteAsync(() => {
                if(_fatalErrorState) {
                    _log.Dbg.AddErrorLog($"Повторная ошибка: {Util.FormatError(message, exception)}");
                    return;
                }

                _fatalErrorState = true;

                _log.AddErrorLog($"Необработанная ошибка. Робот будет остановлен: {Util.FormatError(message, exception)}");

                try {
                    Controller.SendEmail("Робот остановлен из-за ошибок", message, true);
                } catch(Exception err) {
                    _log.Dbg.AddErrorLog($"Unable to send email: {err}");
                }

                StopEverything();
            });
        }

        #region Обработчики, исполняемые в потоке адаптера

        void OnNewMyTrade(Connector c, MyTrade mt) {
            if(mt.Trade.Time < RobotData.RobotStartTime) return;

            var myTrade = mt as MyTradeEx;
            if(myTrade == null) return;

            var sec = myTrade.Order.Security;
            var isOption = sec.Type == SecurityTypes.Option;
            var future = RobotData.GetSecurityById(isOption ? sec.UnderlyingSecurityId : sec.Id) as FuturesInfo;

            if(future == null) {
                _log.Dbg.AddErrorLog("OnNewMyTrade: Unable to get future for mytrade secId={0}: mt={1}", sec.Id, mt);
                return;
            }

            myTrade.CurVegaPortfolio = future.VegaPortfolio;
            myTrade.CurVarMargin = RobotData.ActivePortfolio.Return(p => p.VariationMargin, 0);
            myTrade.CurFutQuote = new PricePair(future.BestBidPrice, future.BestAskPrice);
            myTrade.CurPosition = (int)GetRealPosition(sec.Id, false);

            if(isOption) {
                (myTrade.Order as RobotOptionOrder).Do(o => {
                    var lastCalc = o.Option.Model.LastData;

                    if(lastCalc != null) {
                        myTrade.CurIvBid = lastCalc.IvBid;
                        myTrade.CurIvOffer = lastCalc.IvOffer;
                        myTrade.CurMarketIvBid = lastCalc.MarketIvBid;
                        myTrade.CurMarketIvOffer = lastCalc.MarketIvOffer;
                    }

                    if(o.ParentStrategy is MMStrategy) {
                        RobotThread.ExecuteAsync(() => {
                            Controller.SendEmail($"MM trade: {myTrade.Order.Direction} {myTrade.Trade.Volume}@{myTrade.Trade.Price}", null, true);
                        });
                    }
                });
            }
        }

        void OnOrderChanged(Connector c, Order o) {
            (o as OrderEx).Do(order => {
                order.HandleOrderChanged();

                if(order.State == OrderStates.Pending)
                    _ordersWereSent = true;
            });
        }

        void OnOrderFailed(Connector c, OrderFail fail) {
            (fail.Order as OrderEx).Do(o => o.HandleOrderChanged(fail));
        }

        void OnOrderCancelFailed(Connector c, OrderFail fail) {
            (fail.Order as OrderEx).Do(o => o.HandleCancelFailed());
        }

        void OnPositionChanged(Connector c, Position pos) {
            _log.Dbg.AddInfoLog("Trader position update: {0}-{1}: {2}", pos.Security.Id, pos.Portfolio.Name, pos.CurrentValue);

            RobotLogger.Positions.LogPosition(pos);
        }

        void OnPortfolioChanged(Connector c, Portfolio portfolio) {
            RobotLogger.Portfolio.LogPorfolio((PortfolioEx)portfolio);

            RobotThread.ExecuteAsync(() => UpdateCanTradeState(RobotData.ActivePortfolio, false));
        }

        void OnRealtimeModeChanged(Connector connector, bool inRealtime) {
            if(inRealtime) return;

            RobotThread.ExecuteAsync(() => {
                if(!RobotData.IsRobotStopped)
                    TryCancelOrders(SecurityTypes.Option);
            });
        }

        void OnConnectorError(string message, Exception exception) {
            SetFatalErrorState(message, exception);
        }

        void OnConnectorReset(Connector c) {
            _fatalErrorState = false;
            var numss = _securityStrategies.Count;
            if(numss > 0) {
                _log.Dbg.AddErrorLog($"_securityStrategies were not cleared ({numss})");
                CheckClearSecurityStrategies(true);
            }
        }

        #endregion

        #endregion

        #region interface

        public void RegisterModule(OptionTradingModule module) {
            lock(_optionModules) {
                if(_optionModules.ContainsKey(module.Option.Id)) {
                    _log.Dbg.AddErrorLog("RegisterModule({0}): already registered", module.Option.Id);
                    return;
                }

                _optionModules[module.Option.Id] = module;
                module.ModuleStateChanged += m => RecheckStrategiesState("modstate({0})".Put(module.Option.Code));
            }
        }

        public void RegisterModule(FutureTradingModule module) {
            lock(_futureModules) {
                if(_futureModules.ContainsKey(module.Future.Id)) {
                    _log.Dbg.AddErrorLog("RegisterModule({0}): already registered", module.Future.Id);
                    return;
                }

                _futureModules[module.Future.Id] = module;
                module.ModuleStateChanged += m => RecheckStrategiesState("modstate({0})".Put(module.Future.Code));
            }
        }

        volatile bool _needToRecheck;
        bool _ordersWereSent;
        bool _recheckingStrategies;

        public void RecheckStrategiesState(string reason, bool forceCantTradeMessage = false) {
            if(!RobotThread.InThread()) {
                _needToRecheck = true;
                RobotThread.ExecuteAsync(() => {
                    if(_needToRecheck)
                        RecheckStrategiesState(reason, forceCantTradeMessage);
                    else
                        _log.Dbg.AddDebugLog($"RecheckStrategiesState({reason}): canceled check");
                }, true);

                return;
            }

            if(_recheckingStrategies)
                return;

            try {
                _recheckingStrategies = true;
                RecheckStrategiesStateImpl(reason, forceCantTradeMessage);
            } finally {
                _recheckingStrategies = false;
            }
        }

        void RecheckStrategiesStateImpl(string reason, bool forceCantTradeMessage = false) {
            _needToRecheck = false;

            var pinfo = RobotData.ActivePortfolio;
            UpdateCanTradeState(pinfo, forceCantTradeMessage);

            var ctstate = CanTradeState;
            var isConnected = RobotData.IsConnected;
            _log.Dbg.AddDebugLog("RecheckStrategiesStates({0}): conn={1}, cantrade={2}, isError={3}", reason, isConnected, ctstate, _fatalErrorState);

            if(pinfo == null) { _log.Dbg.AddWarningLog("Потфель не выбран."); return; }

            if(!EnsureGetMutex(pinfo))
                return;

            OptionTradingModule[] modules;

            lock(_optionModules)
                modules = _optionModules.Values.ToArray();

            Util.CancelDelayedAction(ref _closePositionsAction);

            // Start modules for calculated options
            foreach(var module in modules) {
                try {
                    var online = module.Option.IsOnline;
                    var canCalculate = ctstate.CanCalculate() && online && !_fatalErrorState;

                    if(canCalculate && module.ModuleState.Inactive() && module.Option.Strike.IsStrikeCalculated && module.Option.AtmShift != null)
                        module.Start();
                    else if((!module.Option.Strike.IsStrikeCalculated || module.Option.AtmShift == null || !canCalculate) && module.ModuleState.CanStop() && !module.Stopping)
                        module.Stop(!isConnected);

                    if(ctstate.CanCalculate() && !_fatalErrorState && !online)
                        _log.Dbg.AddWarningLog($"option {module.Option.Code} is not online");
                } catch(Exception e) {
                    _log.AddErrorLog("{0}: ошибка старта/стопа модуля: {1}", module.Option.Code, e);
                    module.Option.Series.ForceDeactivateSeries();
                    module.Option.Strike.ForceTurnOffCalculation();
                }
            }

            var modules2 = modules.Where(m => !m.Stopping && m.ModuleState == StrategyState.Active).ToArray();

            // start/stop strategies
            foreach(var module in modules2) {
                var option = module.Option;
                var shift = option.AtmShift;

                if(shift == null) {
                    _log.Dbg.AddWarningLog("ATM shift is null in main module check loop");
                    continue;
                }

                foreach(var t in module.OptionStrategies) {
                    var straType = t.Item1;
                    var wrapper = t.Item2;
                    var pos = GetRealPosition(module.Option.Id, false);

                    var vms = shift.Strategy(straType);
                    if(vms == null) {
                        if(wrapper.State.CanStop()) {
                            _log.Dbg.AddDebugLog("VMStrategy is null. stopping strategy {0}-{1}", option.Code, straType);
                            wrapper.Stop(!isConnected);
                        }
                        continue;
                    }

                    var isActive = vms.IsActive;
                    var canStartStrategies = option.CanStartStrategies;
                    var modCanStartStrategies = module.CanStartOptionStrategies;

                    try {
                        if(isActive && canStartStrategies && modCanStartStrategies && !_fatalErrorState &&
                           (ctstate.CanOpenPositions() || (pos != 0 && ctstate.CanTrade()))) {

                            if(wrapper.State.Inactive()) {
                                var errors = new List<string>();
                                if(vms.CheckStartConfig(errors)) {
                                    wrapper.Start();
                                } else {
                                    _log.AddErrorLog("{0}: Ошибки конфигурации. Стратегия будет деактивирована.\n{0}", string.Join("\n", errors));
                                    vms.ForceDeactivateStrategy();
                                }
                            }
                        } else if(wrapper.State.CanStop()) {
                            _log.Dbg.AddDebugLog("stopping strategy {0} (canTrade={1}, IsActive={2}, optionCanStartStrategies={3}, module.CanStartStrategies={4}", vms.Id, ctstate, isActive, canStartStrategies, modCanStartStrategies);
                            wrapper.Stop(!isConnected);
                        }
                    } catch(Exception e) {
                        _log.AddErrorLog("{0}: ошибка старта/стопа стратегии: {1}", vms.Id, e);
                        vms.ForceDeactivateStrategy();
                    }
                }
            }

            var totalActiveStrategies = 0;
            var totalActiveCalcModules = 0;
            var totalActiveFutureModules = 0;
            var byFuture = modules.GroupBy(m => m.Option.Future).Select(g => Tuple.Create(
                            g.Key, 
                            g.Count(m => !m.ModuleState.Inactive()),
                            g.Sum(m => m.NumActiveChildren)));

            foreach(var t in byFuture) {
                var fut = t.Item1;
                var numCalcModules = t.Item2;
                var numActiveChildren = t.Item3;
                totalActiveStrategies += numActiveChildren;
                totalActiveCalcModules += numCalcModules;

                if(!fut.TradingModule.ModuleState.Inactive())
                    ++totalActiveFutureModules;

                fut.TradingModule.UpdateState(numCalcModules, numActiveChildren);
            }

            if(totalActiveStrategies == 0)
                _numMoneyErrors = 0;

            var modulesStopped = totalActiveStrategies == 0 &&
                                 totalActiveCalcModules == 0 &&
                                 totalActiveFutureModules == 0;

            var numSecMainStrategies = _securityStrategies.Count;
            AllStopped = modulesStopped && numSecMainStrategies == 0;

            if(modulesStopped && numSecMainStrategies > 0 && ctstate == CanTradeState.TradingDisabled) {
                CheckClearSecurityStrategies(!isConnected);
                numSecMainStrategies = _securityStrategies.Count;
                AllStopped = modulesStopped && numSecMainStrategies == 0;
            }

            _log.Dbg.AddDebugLog($"Robot state: (fut,calc,stra,secMainStra)=({totalActiveFutureModules},{totalActiveCalcModules},{totalActiveStrategies},{numSecMainStrategies})");
            RobotStateChanged?.Invoke(totalActiveStrategies);
        }

        public void StopEverything(bool forceDeactivateFuture = false) {
            if(!RobotThread.InThread()) { RobotThread.ExecuteAsync(() => StopEverything(forceDeactivateFuture), true); return; }

            _log.Dbg.AddDebugLog($"StopEverything({forceDeactivateFuture})");

            OptionTradingModule[] modules;

            lock(_optionModules)
                modules = _optionModules.Values.ToArray();

            var isConnected = RobotData.IsConnected;
            _stopping = true;

            if(forceDeactivateFuture)
                RobotData.AllFutures.ForEach(f => f.ForceDeactivateFuture());

            foreach(var module in modules) {
                _log.Dbg.AddDebugLog("Module {0}: state={1}, NumActiveChildren={2}", module.Option.Code, module.ModuleState, module.NumActiveChildren);

                try {
                    if(module.ModuleState.CanStop() && !module.Stopping)
                        module.Stop(!isConnected);
                } catch(Exception e) {
                    _log.Dbg.AddErrorLog("{0}: error stoping module: {1}", module.Option.Code, e);
                }
            }

            RecheckStrategiesState("StopEverything");
        }

        public void ForceRecalculate() {
            RecalculateRequired?.Invoke();
        }

        #region cancel orders/close positions

        public bool CancelOrders() {
            return TryCancelOrders(null, true);
        }

        /// <summary>
        /// Отмена заявок по заданным типам инструментов.
        /// </summary>
        public bool TryCancelOrders(SecurityTypes? secTypes = null, bool showErrors = false) {
            _log.Dbg.AddInfoLog("TryCancelOrders(types={0}, {1})", secTypes, showErrors);
            var logger = showErrors ? _log : _log.Dbg;

            if(!ConnectionState.CanCancelOrders()) { logger.AddErrorLog("Заявки нельзя отменять, пока соединение не подключено."); return false; }
            var pName = CfgGeneral.Portfolio;
            if(pName.IsEmpty()) { logger.AddErrorLog("Чтобы отменять заявки, необходимо выбрать портфель."); return false; }

            var portfolio = Trader.Portfolios.FirstOrDefault(p => p.Name == pName);
            if(portfolio == null) { logger.AddErrorLog("не найден портфель {0}", pName); return false;}

            if(!Scheduler.CanCancelOrders()) {
                logger.AddErrorLog("Сейчас нельзя отменять заявки.");
                return false;
            }

            logger.AddInfoLog("Отмена всех {0}заявок...", secTypes == null ? string.Empty : secTypes+" ");
            Trader.CancelAllOrders(portfolio, secTypes);

            return true;
        }

        /// <summary>
        /// Закрытие всех позиций.
        /// </summary>
        public void ClosePositions() {
            ClosePositions(true);
        }

        void ClosePositions(bool cancelOrders) {
            if(ConnectionState != ConnectionState.Connected) { _log.AddErrorLog("Нельзя закрыть позиции, пока соединение не подключено."); return; }
            if(!RobotData.IsRobotStopped) { _log.AddErrorLog("Нельзя закрыть позиции пока есть неостановленные стратегии"); return; }
            if(RobotData.ActivePortfolio == null) { _log.AddErrorLog("Чтобы закрывать позиции, необходимо выбрать портфель."); return; }

            var portfolio = Trader.Portfolios.FirstOrDefault(p => p.Name == RobotData.ActivePortfolio.Name);
            if(portfolio == null) { _log.AddErrorLog("не найден портфель {0}", RobotData.ActivePortfolio.Name); return;}

            Trader.Positions.GroupBy(p => p.Security).ForEach(g => GetSecurityStrategy(g.Key.Id));

            var strategies = _securityStrategies.Values.Where(ss => 
                            ss.Position != 0 &&
                            ss.Portfolio.Name == portfolio.Name).ToList();

            var now = DateTime.UtcNow;

            if(!strategies.Any()){ _log.AddInfoLog("Не найдено открытых позиций."); return; }
            if(!Scheduler.MarketPeriod.IsMarketOpen()) { _log.AddErrorLog("Невозможно закрыть позиции в неторговый период."); return; }
            if(now - _lastClosePosRequest < TimeSpan.FromSeconds(5)) { _log.AddErrorLog("Предыдущий запрос на закрытие позиций был послан менее 5 секунд назад."); return; }

            // перед тем как закрывать позиции, отменяем все ордера
            if(cancelOrders) {
                Util.CancelDelayedAction(ref _closePositionsAction);
                if(CancelOrders()) // повторно вызываем отмену позиций с задержкой в секунду, чтобы успели отмениться все заявки
                    _closePositionsAction = RobotThread.DelayedAction(() => ClosePositions(false), TimeSpan.FromMilliseconds(1000), null, "delay(close pos)");

                return;
            }

            _lastClosePosRequest = DateTime.UtcNow;

            strategies.ForEach(s => s.ClosePosition());
        }

        #region handle not enough money

        const int NumAllowedMoneyErrorsBeforeStop = 30;
        static readonly TimeSpan NotEnoughMoneyStopInterval = TimeSpan.FromSeconds(3);
        int _numMoneyErrors;
        DateTime _lastNotEnoughMoneyStop;

        public void HandleNotEnoughMoney(SecurityInfo secInfo) {
            RobotThread.ExecuteAsync(() => {
                var now = DateTime.UtcNow;

                if(now - _lastNotEnoughMoneyStop > NotEnoughMoneyStopInterval)
                    ++_numMoneyErrors;

                if(/*secInfo.Type.IsFuture() || */_numMoneyErrors > NumAllowedMoneyErrorsBeforeStop) {
                    _log.AddErrorLog("Ошибка нехватки средств по инструменту {0} ({1}/{2}). Робот будет остановлен.", secInfo.Code, _numMoneyErrors, NumAllowedMoneyErrorsBeforeStop);

                    _numMoneyErrors = 0;
                    _lastNotEnoughMoneyStop = now;

                    StopEverything(true);
                } else {
                    _log.AddWarningLog("Ошибка нехватки средств по инструменту {0} ({1}/{2}).", secInfo.Id, _numMoneyErrors, NumAllowedMoneyErrorsBeforeStop);
                }
            });
        }

        #endregion

        #endregion

        #endregion

        #region CanTradeState

        bool _sendMoneyLimitEmail = true;

        /// <summary>
        /// Метод, обновляющий глобальное разрешение на исполнение заявок для всех стратегий.
        /// </summary>
        void UpdateCanTradeState(PortfolioInfo pinfo, bool forceCantTradeMessage, bool printTransactions = false) {
            if(_disposing) {
                SetCanTradeState(CanTradeState.TradingDisabled, "disposing", forceCantTradeMessage);
                return;
            }

            if(_fatalErrorState) {
                SetCanTradeState(CanTradeState.TradingDisabled, "робот отключен из-за ошибок. необходимо переподключиться или перезапустить приложение", forceCantTradeMessage);
                return;
            }

            if(pinfo == null) {
                SetCanTradeState(CanTradeState.TradingDisabled, "портфель не установлен", forceCantTradeMessage);
                return;
            }

            if(!RobotData.IsConnected) {
                SetCanTradeState(CanTradeState.TradingDisabled, "отсутствует подключение", forceCantTradeMessage);
                return;
            }

            if(_stopping) {
                SetCanTradeState(CanTradeState.TradingDisabled, "stopping", forceCantTradeMessage);
                return;
            }

            var mPeriod = Scheduler.MarketPeriod;
            if(!mPeriod.IsMarketOpen()) {
                SetCanTradeState(CanTradeState.CanCalculate, "неторговый период на рынке", forceCantTradeMessage);
                return;
            }

            var rPeriod = Scheduler.RobotPeriod;
            if(!rPeriod.IsRobotActivePeriod()) {
                SetCanTradeState(CanTradeState.CanCalculate, "неторговый период по расписанию работы робота", forceCantTradeMessage);
                return;
            }

            var capitalLimit = CfgGeneral.CapitalLimit;
            var drawdownLimit = CfgGeneral.DrawdownLimit;
            var vm = pinfo.VariationMargin;
            var drawdown = Math.Abs(vm / (capitalLimit != 0 ? capitalLimit : 1));
            if(vm < 0 && drawdown > drawdownLimit) {
                var msg = "лимит потерь превышен. VM={0}, Drawdown={1:0.####}, DrawdownLimit={2:0.####}".Put(vm, drawdown, drawdownLimit);
                SetCanTradeState(CanTradeState.CanCalculate, msg, forceCantTradeMessage);

                if(_sendMoneyLimitEmail) {
                    _sendMoneyLimitEmail = false;
                    Controller.SendEmail(msg);
                }
                return;
            }

            var cfg = CfgGeneral;
            var trRatio = cfg.TransactionsRatio;
            var trFeeRatio = cfg.TransactionsFeeRatio;
            var dealsRatio = cfg.DealsRatio;
            var trCommLimit = cfg.TransactionsCommissionLimit;
            var trOpenPosDayLimit = cfg.TransactionOpenPosDayLimit;

            var numTran = RobotData.NumTransactions;
            var fortsCommissions = pinfo.Commission;
            TransactionsCommission = trFeeRatio * Math.Max(numTran * trRatio - fortsCommissions * dealsRatio, 0);
            var freeTransactions = trRatio == 0 || fortsCommissions == 0 ? 0 : fortsCommissions * dealsRatio / trRatio;

            freeTransactions = Math.Max(trOpenPosDayLimit, freeTransactions);

            var transactionMsg = "Transactions={0}, Commission={1}, TransactionsCommission={2}, Free={3}".Put(numTran, fortsCommissions, TransactionsCommission, freeTransactions);

            if(rPeriod.IsRobotActivePeriod() && numTran < Math.Min(trOpenPosDayLimit, freeTransactions)) {
                _sendMoneyLimitEmail = true;
                SetCanTradeState(CanTradeState.CanOpenPositions, transactionMsg, forceCantTradeMessage);
                return;
            }

            if(TransactionsCommission < trCommLimit) {
                _sendMoneyLimitEmail = true;
                SetCanTradeState(CanTradeState.CanClosePositions, transactionMsg, forceCantTradeMessage);
                return;
            }

            SetCanTradeState(CanTradeState.TradingDisabled, transactionMsg, forceCantTradeMessage);
        }

        void SetCanTradeState(CanTradeState newState, string msg, bool forceCantTradeMessage) {
            newState = newState.Normalize();

            var oldState = CanTradeState;
            if(oldState == newState) {
                if(forceCantTradeMessage && !newState.CanTrade())
                    _log.AddWarningLog("ЗАЯВКИ НЕЛЬЗЯ ДЕЛАТЬ. {0}", msg);

                return;
            }

            CanTradeState = newState;

            if(newState.CanTrade() != oldState.CanTrade() || (forceCantTradeMessage && !newState.CanTrade()))
                _log.AddWarningLog(newState.CanTrade() ? "ЗАЯВКИ МОЖНО ДЕЛАТЬ. {0}" : "ЗАЯВКИ НЕЛЬЗЯ ДЕЛАТЬ. {0}", msg);

            _log.Dbg.AddInfoLog("CanTradeState={0}; {1}", newState, msg);

            CanTradeStateChanged.SafeInvoke();
        }

        #endregion

        #region mutex

        static readonly TimeSpan _waitMutexTime = TimeSpan.FromSeconds(1);
        string _currentMutexName;

        bool EnsureGetMutex(PortfolioInfo pinfo) {
            var pname = pinfo.Name;
            var name = "robot_mutex_{0}".Put(pname);

            if(_mutex != null && name == _currentMutexName)
                return true;

            _currentMutexName = name;
            DisposeMutex();

            _mutex = new Mutex(false, name);

            try {
                if(!_mutex.WaitOne(_waitMutexTime)) {
                    _log.AddErrorLog("Робот для портфеля '{0}' уже запущен.", pname);
                    DisposeMutex();
                    return false;
                }
            } catch(AbandonedMutexException) {
                try {
                    if(!_mutex.WaitOne(_waitMutexTime)) {
                        _log.AddErrorLog("Робот для портфеля '{0}' уже запущен.", pname);
                        DisposeMutex();
                        return false;
                    }
                } catch(AbandonedMutexException) {
                    _log.AddErrorLog("Не удалось получить mutex для портфеля '{0}'. При повторных ошибках следует перезапустить все экземпляры робота или перезагрузить систему.", pname);
                    DisposeMutex();
                    return false;
                }
            }

            return true;
        }

        void DisposeMutex() {
            if(_mutex != null) {
                _mutex.Dispose();
                _mutex = null;
            }
        }

        #endregion

        #region security main strategy

        public SecurityMainStrategy GetSecurityStrategy(string secId) {
            bool isNew;

            var s = _securityStrategies.SafeAdd(secId, id => new SecurityMainStrategy(this, RobotData.GetSecurityById(secId)), out isNew);

            if(isNew) {
                s.PositionError += SecurityStrategyOnPositionError;
                s.ProcessStateChanged += SecurityStrategyOnProcessStateChanged;
                s.Start();
                s.EventStarted.Wait();
            } else if(!s.WasStarted) {
                s.EventStarted.Wait();
            }

            return s;
        }

        void SecurityStrategyOnProcessStateChanged(Strategy s) {
            var strategy = (SecurityMainStrategy)s;
            var secId = strategy.Security?.Id;

            if(strategy.ProcessState == ProcessStates.Stopped) {
                _log.Dbg.AddDebugLog($"SecurityMainStrategy({secId}) is stopped");
                UnsubscribeSecStrategy(strategy);

                if(string.IsNullOrEmpty(secId)) {
                    _log.Dbg.AddErrorLog("SecurityStrategyOnProcessStateChanged: secId is empty");
                } else {
                    _securityStrategies.Remove(secId);
                }

                strategy.Dispose();

                RecheckStrategiesState($"SecurityMainStrategy({secId}) is stopped");
            }
        }

        void UnsubscribeSecStrategy(SecurityMainStrategy strategy) {
            strategy.PositionError -= SecurityStrategyOnPositionError;
            strategy.ProcessStateChanged -= SecurityStrategyOnProcessStateChanged;
        }

        void SecurityStrategyOnPositionError(SecurityMainStrategy strategy) {
            RobotThread.ExecuteAsync(() => {
                _log.AddErrorLog("{0}: Обнаружено несовпадение реальной и расчетной позиций.", strategy.Security.Code);

                if(RobotData.IsRobotStopped) {
                    strategy.SyncPosition();
                    return;
                }

                _log.AddErrorLog("Робот будет остановлен.");
                StopEverything();
            });
        }

        public decimal GetRealPosition(string secId, bool addSecurityStrategy = true) {
            if(addSecurityStrategy)
                return GetSecurityStrategy(secId).Position;

            var s = _securityStrategies.TryGetValue(secId);

            if(s != null) {
                var pos = s.Position;
                //_log.Dbg.AddDebugLog("GetRealPosition1({0}) = {1}", secId, pos);

                return pos;
            } else {
                var pos = Trader.Positions
                    .FirstOrDefault(p => p.Portfolio.Name == RobotData.ActivePortfolio.Name && p.Security.Id == secId)
                    .Return(p => p.CurrentValue, 0);
                //_log.Dbg.AddDebugLog("GetRealPosition2({0}) = {1}", secId, pos);

                return pos;
            }
        }

        #endregion
    }
}
