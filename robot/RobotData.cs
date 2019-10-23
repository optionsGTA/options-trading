using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Ecng.Collections;
using Ecng.Common;
using MoreLinq;
using OptionBot.Config;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Messages;

namespace OptionBot.robot {
    public class RobotData : ViewModelBaseNotifyAction, IRobotDataUpdater {
        readonly Logger _log = new Logger();

        #region fields

        readonly Controller _controller;
        readonly SecurityIdGenerator _secIdGenerator = new SecurityIdGenerator();
        
        ConnectionState _connState;
        MarketPeriodType? _marketPeriod;
        RobotPeriodType? _robotPeriod;
        int _numRunningStrategies;

        TimeSpan? _marketPeriodTimeLeft, _robotPeriodTimeLeft;

        readonly ObservableCollection<FuturesInfo> _allFutures = new ObservableCollection<FuturesInfo>();
        readonly ObservableCollection<OptionSeriesInfo> _allOptionSeries = new ObservableCollection<OptionSeriesInfo>();
        readonly ObservableCollection<OptionInfo> _allOptions = new ObservableCollection<OptionInfo>();
        readonly ObservableCollection<PortfolioInfo> _allPortfolios = new ObservableCollection<PortfolioInfo>();
        readonly ObservableCollection<PositionInfo> _allPositions = new ObservableCollection<PositionInfo>();
        readonly ObservableCollection<VMStrategy> _allStrategies = new ObservableCollection<VMStrategy>(); 
        readonly ObservableCollection<SecurityMMInfo> _allMMInfos = new ObservableCollection<SecurityMMInfo>();

        ObservableCollection<MyTradeInfo> _allMyTrades;
        ObservableCollection<OrderInfo> _allOrders;
        ActiveOrdersObservableCollection _activeOrders;

        readonly Dictionary<long, OrderInfo> _allOrdersByTransactionId = new Dictionary<long, OrderInfo>(); 
        readonly Dictionary<MMInfoRecordKey, SecurityMMInfo> _allMMInfosByKey = new Dictionary<MMInfoRecordKey, SecurityMMInfo>(); 

        int _numTransations, _numTransactionsPerSecond;
        DateTime _currentTime;
        TimeSpan _marketDataDelay;
        
        PortfolioInfo _activePortfolio;

        DateTime _currentMarketDayStartTime;
        int _currentSessionId;

        readonly DeferredUIAction _deferredReferenceUpdateHandler;

        readonly Dictionary<string, SecurityInfo> _allSecurities = new Dictionary<string, SecurityInfo>();
        readonly Dictionary<int, SecurityInfo> _allSecuritiesByIsinId = new Dictionary<int, SecurityInfo>();
        
        readonly Dictionary<long, MyTradeInfo> _myTradesByTradeId = new Dictionary<long, MyTradeInfo>();
        readonly Dictionary<string, int> _volumesBySec = new Dictionary<string, int>(); 

        readonly DeferredUIAction _deferredCheckStrategies;
        static readonly TimeSpan _autoStartStopCheckPeriod = TimeSpan.FromSeconds(5);
        DateTime _lastAutoStartStopCheckTime;

        public VolumeStats VolStats {get;}

        #endregion

        #region properties

        ConfigProvider ConfigProvider {get {return _controller.ConfigProvider;}}
        IConfigGeneral CfgGeneral {get {return ConfigProvider.General.Effective;}}
        Connector Connector {get {return _controller.Connector; }}
        Connector.IConnectorSubscriber ConnectorSubscriber {get {return _controller.ConnectorGUISubscriber; }}
        public Dispatcher Dispatcher {get; private set;}

        public bool IsDisconnected {get { return _connState == ConnectionState.Disconnected; }}
        public bool IsConnected {get { return _connState == ConnectionState.Connected; }}
        public bool IsRobotStopped {get { return _numRunningStrategies == 0; }}

        /// <summary>Текущее состояние подключения</summary>
        public ConnectionState ConnectionState {
            get { return _connState; }
            set { // setter is called from RobotThread
                var wasDisconnected = IsDisconnected;
                SetField(ref _connState, value);
                if(IsDisconnected != wasDisconnected) {
                    OnPropertyChanged(() => IsDisconnected);

                    if(wasDisconnected)
                        VolStats.Reset();
                }
            }
        }

        /// <summary>Количество работающих стратегий.</summary>
        public int NumRunningStrategies {
            get {return _numRunningStrategies; }
            set {
                var stopped = IsRobotStopped;
                SetField(ref _numRunningStrategies, value);

                if(IsRobotStopped != stopped) {
                    ConfigProvider.UpdateRealtimeMode(IsRobotStopped);
                    OnPropertyChanged(() => IsRobotStopped);
                }
            }
        }

        /// <summary>Торговый период на рынке</summary>
        public MarketPeriodType? MarketPeriod { get { return _marketPeriod; } set { SetField(ref _marketPeriod, value); }}
        /// <summary>Сколько времени осталось до окончания текущего периода</summary>
        public TimeSpan? MarketPeriodTimeLeft {get {return _marketPeriodTimeLeft; } set {SetField(ref _marketPeriodTimeLeft, value); }}

        /// <summary>Текущий период для робота</summary>
        public RobotPeriodType? RobotPeriod { get { return _robotPeriod; } set { SetField(ref _robotPeriod, value); }}
        /// <summary>Сколько времени осталось до окончания текущего периода</summary>
        public TimeSpan? RobotPeriodTimeLeft {get {return _robotPeriodTimeLeft; } set {SetField(ref _robotPeriodTimeLeft, value); }}

        /// <summary>Все фьючерсы</summary>
        public ObservableCollection<FuturesInfo> AllFutures { get { return _allFutures; } }
        /// <summary>Все опционы</summary>
        public ObservableCollection<OptionInfo> AllOptions { get { return _allOptions; } }

        public IEnumerable<SecurityInfo> AllSecurities => AllFutures.Concat(AllOptions.Cast<SecurityInfo>()); 

        /// <summary>Все портфели</summary>
        public ObservableCollection<PortfolioInfo> AllPortfolios { get { return _allPortfolios; } }

        /// <summary>все заявки</summary>
        public ObservableCollection<OrderInfo> AllOrders {get {return _allOrders; } set {SetField(ref _allOrders, value); }}
        /// <summary>фильтр всех активных заявок</summary>
        public ActiveOrdersObservableCollection AllActiveOrders {get {return _activeOrders; } set {SetField(ref _activeOrders, value); }}
        /// <summary>все позиции</summary>
        public ObservableCollection<PositionInfo> AllPositions {get {return _allPositions; }}
        /// <summary>все сделки</summary>
        public ObservableCollection<MyTradeInfo> AllMyTrades {get {return _allMyTrades; } set {SetField(ref _allMyTrades, value); }} 
        /// <summary>все обязательства мм</summary>
        public ObservableCollection<SecurityMMInfo> AllMMInfos => _allMMInfos;

        public IEnumerable<VMStrategy> AllStrategies {get {return _allStrategies;}} 
        public VMStrategy[] AllStrategiesArr {get { lock(_allStrategies) return _allStrategies.ToArray();}} 

        public ObservableCollection<OptionSeriesInfo> AllOptionSeries {get {return _allOptionSeries;}} 

        /// <summary>количество транзакций для отображения на экране</summary>
        public int NumTransactions { get { return _numTransations; } set {SetField(ref _numTransations, value); }}

        /// <summary>количество транзакций за последнюю секунду</summary>
        public int NumTransactionsPerSecond { get { return _numTransactionsPerSecond; } set {SetField(ref _numTransactionsPerSecond, value); }}

        /// <summary>количество активных заявок</summary>
        public int NumActiveOrders => AllActiveOrders.Count;

        public DateTime CurrentTime {get { return _currentTime; } set {SetField(ref _currentTime, value); }}
        public TimeSpan MarketDataDelay {get {return _marketDataDelay; } set{SetField(ref _marketDataDelay, value); }}

        readonly DateTime _robotStartTime;
        public DateTime RobotStartTime {get {return _robotStartTime;}}

        /// <summary>активный потфель, выбранный пользователем</summary>
        public PortfolioInfo ActivePortfolio {get { return _activePortfolio;  }}

        /// <summary>время начала активной торговой сессии (день/вечер раздельно)</summary>
        public DateTime CurrentMarketDayStartTime { get {return _currentMarketDayStartTime;} set {
            if(SetField(ref _currentMarketDayStartTime, value))
                _log.Dbg.AddInfoLog($"current period start time = {value:dd.MMM.yyyy HH:mm:ss}");
        }}

        /// <summary>идентификатор активной торговой сессии</summary>
        public int CurrentSessionId { get {return _currentSessionId;} set {
            if(SetField(ref _currentSessionId, value)) {
                _log.Dbg.AddInfoLog($"current session id = {value}");
                _deferredCheckStrategies.DeferredExecute();
                CurrentSessionIdChanged?.Invoke();
            }
        }}

        protected override Action<string> NotifyAction {get {return Notify;}}

        public event Action StrategyListChanged;
        public event Action<PositionInfo> PositionChanged;
        public event Action RobotReset;
        public event Action<SecurityInfo> NewSecurity;
        public event Action CurrentSessionIdChanged;

        #endregion

        public RobotData(Controller controller) {
            _controller = controller;
            Dispatcher = Application.Current.Dispatcher;

            SecurityInfo.NewSecurityInfo += OnNewSecurityInfo;
            SecurityInfo.NativeSecurityReplaced += SecurityInfoOnNativeSecurityReplaced;

            ConnectorSubscriber.ConnectionStateChanged += OnConnectionStateChanged;
            ConnectorSubscriber.NewPortfolio += OnNewPortfolio;
            ConnectorSubscriber.NewSecurity += OnNewSecurity;
            ConnectorSubscriber.NewPosition += OnNewPosition;
            ConnectorSubscriber.NewOrder += OnNewOrder;
            ConnectorSubscriber.NewMyTrade += OnNewMyTrade;
            ConnectorSubscriber.NewMMInfo += OnNewMMInfo;

            PropertyChanged += OnRobotDataPropertyChanged;

            _robotStartTime = Connector.GetMarketTime();

            SetOrdersCollections(new ObservableCollection<OrderInfo>());
            SetMyTradesCollection(new ObservableCollection<MyTradeInfo>());

            _deferredReferenceUpdateHandler = new DeferredUIAction(Dispatcher, OnReferenceDataUpdated, TimeSpan.FromSeconds(2));

            AddGuiOneTimeAction(() => {
                ConfigProvider.Futures.List.ToList().ForEach(fcfg => GetFuture(_secIdGenerator.Split(fcfg.Effective.SecurityId).Item1));
                ConfigProvider.Strategies.List.ToList().ForEach(cfg => _allStrategies.Add(new VMStrategy(_controller, cfg)));
                _log.Dbg.AddInfoLog("Restored {0} futures and {1} strategies.", _allFutures.Count, _allStrategies.Count);

                _controller.Robot.RobotStateChanged += OnRobotStateChanged;

                _controller.Scheduler.PeriodChanged += (scheduler, oldMarketPeriod, oldRobotPeriod) => {
                    if(!oldMarketPeriod.IsMarketOpen() && scheduler.MarketPeriod.IsMarketOpen())
                        HandleMarketOpen();
                };

                ConfigProvider.TradingPeriods.ListOrItemChanged += isListChange => {
                    _deferredCheckStrategies.DeferredExecute();
                };
            });

            _allStrategies.CollectionChanged += (sender, args) => StrategyListChanged.SafeInvoke();

            _deferredCheckStrategies = new DeferredUIAction(Dispatcher, () => RecheckAutoStartStopStrategies(true), TimeSpan.FromSeconds(1));

            VolStats = new VolumeStats(_controller);
        }

        protected override void DisposeManaged() {
            AllFutures.ForEach(f => f.Dispose());
            AllOptions.ForEach(o => o.Dispose());
            base.DisposeManaged();
        }

        void OnRobotDataPropertyChanged(object sender, PropertyChangedEventArgs args) {
            if(args.PropertyName == Util.PropertyName(() => ActivePortfolio)) {
                AllFutures.ForEach(fi => fi.UpdatePosition());
                AllOptions.ForEach(oi => oi.UpdatePosition());
            }
        }

        /// <summary>обработчик получения нового портфеля от адаптера</summary>
        void OnNewPortfolio(Connector connector, Portfolio portfolio) {
            GetPortfolio(portfolio, true);

            OnReferenceDataUpdated();
        }

        /// <summary>обработчик получения нового инструмента от адаптера</summary>
        void OnNewSecurity(Connector connector, Security security) {
            _log.Dbg.AddDebugLog("onnewsec: {0}", security.Id);

            var sec = GetSecurity(security);

            sec?.UpdatePosition();

            _deferredReferenceUpdateHandler.DeferredExecute();
            _deferredCheckStrategies.DeferredExecute();
        }

        /// <summary>обработчик получения новой заявки от адаптера</summary>
        void OnNewOrder(Connector connector, Order order) {
            var sessStartTime = CurrentMarketDayStartTime;
            var oi = _allOrdersByTransactionId.TryGetValue(order.TransactionId);
            //var oi = AllOrders.FirstOrDefault(o => o.TransactionId == order.TransactionId);
            if(oi != null) {
                if(order.IsInFinalState() && order.LastChangeTime < sessStartTime) {
                    AllOrders.Remove(oi);
                    _allOrdersByTransactionId.Remove(order.TransactionId);
                    _log.Dbg.AddDebugLog("removed old order {0} {1}/{2} {3} {4}@{5}", oi.SecurityCode, oi.TransactionId, oi.Id, oi.Direction, oi.Volume, oi.Price);
                } else {
                    oi.ReplaceOrder(order);
                }
            } else {
                if(!order.IsInFinalState() || order.LastChangeTime >= sessStartTime || order.Time >= sessStartTime) {
                    AllOrders.Add(oi = new OrderInfo(_controller, order));
                    _allOrdersByTransactionId[order.TransactionId] = oi;
                } else {
                    _log.Dbg.AddDebugLog("ignoring old order {0} {1}/{2} {3} {4}@{5}", order.Security.Code, order.TransactionId, order.Id, order.Direction, order.Volume, order.Price);
                }
            }
        }

        /// <summary>обработчик получения новой позиции от адаптера</summary>
        void OnNewPosition(Connector connector, Position position) {
            var sec = GetSecurity(position.Security, position.CurrentValue != 0);
            if(sec == null) return; // unsupported security type

            var port = GetPortfolio(position.Portfolio);
            var pi = AllPositions.FirstOrDefault(p => p.Portfolio.Name == position.Portfolio.Name && p.Security.Id == position.Security.Id);
            if(pi != null) {
                pi.ReplacePosition(position);
            } else {
                pi = new PositionInfo(_controller, port, sec, position);
                pi.Changed += info => PositionChanged.SafeInvoke(info);
                AllPositions.Add(pi);
            }

            sec.UpdatePosition();
        }

        void OnNewSecurityInfo(SecurityInfo si) {
            lock(_allSecurities)
                _allSecurities[si.Id] = si;

            NewSecurity?.Invoke(si);
        }

        void SecurityInfoOnNativeSecurityReplaced(SecurityInfo si) {
            _allSecuritiesByIsinId[si.PlazaIsinId] = si;
        }

        public SecurityInfo GetSecurityById(string secId) {
            lock(_allSecurities)
                return _allSecurities.TryGetValue(secId);
        }

        /// <summary>NOT thread safe. Use from GUI thread only.</summary>
        public SecurityInfo GetSecurityByIsinId(int isinId) {
            return _allSecuritiesByIsinId.TryGetValue(isinId);
        }

        /// <summary>получить объект SecurityInfo по инструменту или создать новый при отсутствии</summary>
        SecurityInfo GetSecurity(Security security, bool createNewFuture = false) {
            if(security == null)
                throw new ArgumentNullException(nameof(security));

            SecurityInfo si;
            switch(security.Type) {
                case SecurityTypes.Future:
                    si = AllFutures.FirstOrDefault(fi => fi.Id == security.Id);
                    if(si != null) {
                        if(!object.ReferenceEquals(si.NativeSecurity, security))
                            si.ReplaceSecurity(security);
                    } else if(createNewFuture) {
                        AllFutures.Add((FuturesInfo)(si = Util.CreateInitializable(() => new FuturesInfo(_controller, security))));
                    }
                    break;
                case SecurityTypes.Option:
                    if(security.UnderlyingSecurityId.IsEmpty())
                        _log.Dbg.AddWarningLog("Option without underlying future: {0}", security.Id);

                    var future = AllFutures.FirstOrDefault(f => f.Id == security.UnderlyingSecurityId);
                    if(future == null)
                        return null;

                    si = AllOptions.FirstOrDefault(oi => oi.Id == security.Id);
                    if(si != null) {
                        if(!object.ReferenceEquals(si.NativeSecurity, security))
                            si.ReplaceSecurity(security);
                    } else {
                        AllOptions.Add((OptionInfo)(si = Util.CreateInitializable(() => new OptionInfo(_controller, security, future))));
                    }
                    break;
                default:
                    return null;
            }

            return si;
        }

        public SecurityInfo GetFuture(string code) {
            if(code.Length < 2 || code.Length > 15) throw new ArgumentException("code");

            var id = _secIdGenerator.GenerateId(code, ExchangeBoard.Forts);
            var fi = AllFutures.FirstOrDefault(f => f.Id == id);
            if(fi != null) return fi;

            var secId = new ComplexSecurityId {
                StockSharpId = new StockSharpSecurityId(code, ExchangeBoard.Forts.Code)
            };

            AllFutures.Add(fi = Util.CreateInitializable(() => new FuturesInfo(_controller, new Security(secId, id) {
                Code = code,
                Name = code,
                Type = SecurityTypes.Future,
            })));

            return fi;
        }

        public void RemoveFuture(FuturesInfo fut) {
            if(!IsDisconnected) {
                _log.AddErrorLog("can't remove future in this state {0}", ConnectionState);
                return;
            }

            if(!IsRobotStopped) {
                _log.AddErrorLog("robot was not stopped: active={0}", NumRunningStrategies);
                return;
            }

            try {
                var id = fut.Id;
                var strategies = _allStrategies.Where(s => s.CfgStrategy.SeriesId.FutureId == id).ToArray();
                var series = _allOptionSeries.Where(os => os.Future.Id == id).ToArray();
                var options = _allOptions.Where(o => o.Future.Id == id).ToArray();

                _allStrategies.RemoveRange(strategies);
                strategies.ForEach(s => {
                    ConfigProvider.DeleteStrategyConfig(s.Config);
                    s.Dispose();
                });

                _allOptions.RemoveRange(options);
                options.ForEach(o => o.Dispose());

                _allOptionSeries.RemoveRange(series);
                series.ForEach(os => os.Dispose());

                _allFutures.Remove(fut);
                ConfigProvider.DeleteFutureConfig(fut.Config);
                fut.Dispose();

                _log.AddInfoLog("Фьючерс {0} удален. ({1} стратегий, {2} опционов, {3} серий)", id, strategies.Length, options.Length, series.Length);
            } catch(Exception e) {
                _log.AddErrorLog("Ошибка во время удаления фьючерса: {0}", e);
            }
        }

        /// <summary>получить объект PortfolioInfo по инструменту или создать новый при отсутствии</summary>
        PortfolioInfo GetPortfolio(Portfolio portfolio, bool replace = false) {
            var result = AllPortfolios.FirstOrDefault(p => p.Name == portfolio.Name);
            if(result == null)
                AllPortfolios.Add(result = new PortfolioInfo(_controller, portfolio));
            else if(replace)
                result.ReplacePortfolio(portfolio);

            return result;
        }

        public OptionSeriesInfo TryAddOptionSeries(OptionSeriesInfo serInfo) {
            var si = _allOptionSeries.FirstOrDefault(os => os.SeriesId == serInfo.SeriesId);
            if(si != null) {
                _log.Dbg.AddWarningLog("Duplicate option series: {0}", serInfo.SeriesId);
                return si;
            }

            _allOptionSeries.Add(serInfo);
            return serInfo;
        }

        /// <summary>обработчик смены состояния подключения</summary>
        void OnConnectionStateChanged(Connector c, ConnectionState newstate, Exception ex) {
            if(newstate != ConnectionState.Connected)
                return;

            HandleMarketOpen();

            OnReferenceDataUpdated();
        }

        void OnRobotStateChanged(int numActive) {
            NumRunningStrategies = numActive;
        }

        DateTime _lastOpenHandlerTime;

        /// <summary>Обработать событие открытия рынка, удалить ордера, оставшиеся с предыдущей торговой сессии (для оптимизации - полезно при большом количестве ордеров)</summary>
        void HandleMarketOpen() {
            Dispatcher.CheckThread();

            var startTime = CurrentMarketDayStartTime;
            if(startTime == default(DateTime)) return;

            var before = AllOrders.Count;

            if(before > 0) {
                var orders = AllOrders.Where(o => !o.NativeOrder.IsInFinalState() || o.LastChangeTime >= startTime || o.LastChangeTime == default(DateTime));
                var newOrdersColl = new ObservableCollection<OrderInfo>(orders);
                SetOrdersCollections(newOrdersColl);
                var after = AllOrders.Count;

                _log.Dbg.AddInfoLog($"Удалено {before-after} из {before} ордеров (осталось {after}). startTime={startTime:dd.MMM.yyyy HH:mm:ss}");
            }

            before = AllMyTrades.Count;
            if(before > 0) {
                var trades = AllMyTrades.Where(t => t.Time >= startTime);
                var newTradesColl = new ObservableCollection<MyTradeInfo>(trades);
                SetMyTradesCollection(newTradesColl);
                var after = AllMyTrades.Count;
                _log.Dbg.AddInfoLog($"Удалено {before-after} из {before} сделок (осталось {after}). startTime={startTime:dd.MMM.yyyy HH:mm:ss}");

                if(before != after) {
                    lock(_volumesBySec) {
                        _volumesBySec.Clear();
                        foreach(var mt in AllMyTrades) {
                            int vol;
                            _volumesBySec.TryGetValue(mt.SecurityId, out vol);
                            _volumesBySec[mt.SecurityId] = vol + mt.Volume;
                        }
                    }
                }
            }

            if(_lastOpenHandlerTime < startTime) {
                _lastOpenHandlerTime = startTime;
                AllOptions.ForEach(o => o.OwnMMVolume = o.VolumeDiff = o.OwnMMVolumeActive = 0);
            }
        }

        static readonly TimeSpan _myTradeMMDelay = TimeSpan.FromMinutes(3);
        static readonly TimeSpan _myTradeCalcMMDelay = TimeSpan.FromSeconds(3);

        /// <summary>обработчик получения собственной сделки</summary>
        void OnNewMyTrade(Connector c, MyTrade mt) {
            var myTrade = mt as MyTradeEx;
            if(myTrade == null) {
                _log.Dbg.AddErrorLog("unexpected type of mytrade: {0}", mt.GetType().Name);
                return;
            }

            var trade = _myTradesByTradeId.TryGetValue(mt.Trade.Id);
            if(trade != null) {
                trade.ReplaceTrade(myTrade);
            } else if(myTrade.Trade.Time >= CurrentMarketDayStartTime) {
                trade = new MyTradeInfo(_controller, myTrade);
                _myTradesByTradeId[trade.Id] = trade;
                AllMyTrades.Add(trade);

                var opt = GetSecurityById(trade.SecurityId) as OptionInfo;
                if(opt != null && opt.IsMMOption) {
                    var now = Connector.GetMarketTime();
                    if(now - trade.Time < _myTradeMMDelay) {
                        AddGuiOneTimeDelayedAction(() => {
                            opt.OwnMMVolume += trade.Volume;
                            var aggr = trade.IsAggressive;
                            if(aggr == true)
                                opt.OwnMMVolumeActive += trade.Volume;
                            else if(aggr == null)
                                _log.Dbg.AddErrorLog("IsAggressive is unknown for tradeId=" + trade.Id);
                        }, _myTradeCalcMMDelay);
                    }
                }

                lock(_volumesBySec) {
                    int sumVol;
                    _volumesBySec.TryGetValue(trade.SecurityId, out sumVol);
                    _volumesBySec[trade.SecurityId] = trade.Volume + sumVol;
                }
            } else {
                _log.Dbg.AddWarningLog($"Ignoring old MyTrade: tradeId={myTrade.Trade.Id}, orderId={myTrade.Order.Id}, time={myTrade.Trade.Time:dd.MMM.yyyy HH:mm:ss.fff}");
            }
        }

        void SetOrdersCollections(ObservableCollection<OrderInfo> allOrders) {
            _allOrdersByTransactionId.Clear();
            foreach(var o in allOrders)
                _allOrdersByTransactionId[o.TransactionId] = o;

            AllOrders = allOrders;
            AllActiveOrders = new ActiveOrdersObservableCollection(AllOrders);
            AllActiveOrders.CollectionChanged += ActiveOrdersOnCollectionChanged;
        }

        void SetMyTradesCollection(ObservableCollection<MyTradeInfo> myTrades) {
            _myTradesByTradeId.Clear();
            foreach(var mt in myTrades)
                _myTradesByTradeId[mt.Id] = mt;

            AllMyTrades = myTrades;
            AllMyTrades.CollectionChanged += AllMyTradesOnCollectionChanged;
        }

        void OnNewMMInfo(Connector connector, MMInfoRecord record) {
            try {
                var info = _allMMInfosByKey.TryGetValue(record.Key);

                if(!record.ActiveSign) {
                    _allMMInfosByKey.Remove(record.Key);
                    if(info != null) {
                        _allMMInfos.Remove(info);
                        info.Dispose();
                    }

                    return;
                }

                if(info == null) {
                    info = new SecurityMMInfo(_controller, record);
                    _allMMInfosByKey[info.Key] = info;
                    _allMMInfos.Add(info);
                } else {
                    info.UpdateRecord(record);
                }

                _deferredCheckStrategies.DeferredExecute();
            } finally {
                _controller.RobotLogger.MMObligations.Log(record);
            }
        }

        /// <summary>Объем собственных сделок по инструменту за текущую сессию (день/вечер отдельно).</summary>
        public int GetOwnVolumeBySecurity(SecurityInfo si) {
            lock(_volumesBySec)
                return _volumesBySec.TryGetValue(si.Id);
        }

        public int GetOwnSessionVolumeByOptionSeries(OptionSeriesInfo si) {
            lock(_volumesBySec)
                return si.Options.Sum(o => _volumesBySec.TryGetValue(o.Id));
        }

        void ActiveOrdersOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs) {
            OnPropertyChanged(() => NumActiveOrders);
        }

        void AllMyTradesOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args) {
            if(args.NewItems == null) return;

            foreach(var mti in args.NewItems.Cast<MyTradeInfo>()) {
                var id = mti.SecurityId;
                var type = mti.SecurityType;
                if(id == null || type == null)
                    continue;

                var si = type.Value == SecurityTypes.Option ? (SecurityInfo)AllOptions.FirstOrDefault(o => o.Id == id) :
                    type.Value == SecurityTypes.Future ? AllFutures.FirstOrDefault(f => f.Id == id) : null;

                if(si == null) continue;

                si.MyLastTrade = mti;
            }
        }

        public void AddStrategy(VMStrategy strategy) {
            lock(_allStrategies)
                _allStrategies.Add(strategy);
        }

        public void RemoveStrategy(VMStrategy strategy) {
            lock(_allStrategies)
                _allStrategies.Remove(strategy);
        }

        void OnReferenceDataUpdated() {
            if(ConnectionState != ConnectionState.Connected)
                return;

            SetActivePortfolio(CfgGeneral.Portfolio);
        }

        public void SetActivePortfolio(string name) {
            if(ActivePortfolio.With(pi => pi.Name) == name) {
                _log.Dbg.AddWarningLog("SetActivePortfolio(): already '{0}'", name);
                return;
            }

            var pinfo = AllPortfolios.FirstOrDefault(p => p.Name == name);

            if(_activePortfolio != null) {
                _activePortfolio.Deactivate();
                _activePortfolio = null;
            }

            _activePortfolio = pinfo;
            _activePortfolio.Do(p => p.Activate());
            OnPropertyChanged(() => ActivePortfolio);

            _log.Dbg.AddInfoLog("SetActivePortfolio({0}): set to '{1}'", name, _activePortfolio.With(pi => pi.Name));

            RobotReset.SafeInvoke();
        }

        void RecheckAutoStartStopStrategies(bool force) {
            var now = DateTime.UtcNow;
            if(!force && (now - _lastAutoStartStopCheckTime < _autoStartStopCheckPeriod))
                return;

            _lastAutoStartStopCheckTime = now;

            var sessId = CurrentSessionId;

            if(sessId == 0)
                return;

            var autoStrategies = AllStrategies.Where(s => s.Config.Effective.AutoStartStop).ToArray();
            var mmInfos = _allMMInfos.Where(mmi => mmi.SessionId == sessId && mmi.Security != null).ToDictionary(mmi => mmi.Security.Code);

            SendMMReportIfNecessary(mmInfos.Values);

            var toActivate = new List<VMStrategy>();
            var toDeactivate = new List<VMStrategy>();
            var tperiod = _controller.Scheduler.TradingPeriod;
            var autoStop = tperiod != null && ConfigProvider.TradingPeriods[tperiod.Value].Effective.StopMMByTimePercent;

            foreach(var strategy in autoStrategies) {
                var opt = strategy.Option;
                if(opt == null) {
                    if(strategy.IsActive)
                        toDeactivate.Add(strategy);

                    continue;
                }

                if(strategy.StrategyType == StrategyType.MM) {
                    var info = mmInfos.TryGetValue(opt.Code);

                    if(info != null) {
                        if(!strategy.IsActive) {
                            if(!autoStop || !info.IsFillTotal)
                                toActivate.Add(strategy);
                        } else if(autoStop && info.IsFillTotal) {
                            toDeactivate.Add(strategy);
                        }
                    } else {
                        if(strategy.IsActive)
                            toDeactivate.Add(strategy);
                    }
                } else {
                    var pos = opt.Position?.CurrentValue;
                    if(pos != null && pos.Value != 0) {
                        if(!strategy.IsActive)
                            toActivate.Add(strategy);
                    } else {
                        if(strategy.IsActive)
                            toDeactivate.Add(strategy);
                    }
                }
            }

            toDeactivate.ForEach(s => {
                try {
                    s.SetActive(false);
                    _log.AddInfoLog($"Автоматически деактивирована стратегия {s.Id}");
                } catch(Exception e) {
                    _log.Dbg.AddErrorLog($"Unable to deactivate {s.Id}:\n{e}");
                }
            });

            toActivate.ForEach(s => {
                try {
                    s.SetActive(true);
                    _log.AddInfoLog($"Автоматически активирована стратегия {s.Id}");
                } catch(Exception e) {
                    _log.Dbg.AddErrorLog($"Unable to activate {s.Id}:\n{e}");
                }
            });
        }

        DateTime _mmUnfulfillTime, _mmLastReportTime;

        void SendMMReportIfNecessary(IEnumerable<SecurityMMInfo> mmInfos) {
            var cfg = CfgGeneral;

            if(!_controller.Scheduler.MarketPeriod.IsMarketOpen())
                return;

            var tperiod = _controller.Scheduler.TradingPeriod;
            var autoStop = tperiod != null && ConfigProvider.TradingPeriods[tperiod.Value].Effective.StopMMByTimePercent;

            var delayFirst = cfg.MMDelayFirst;
            var delayPeriodic = cfg.MMDelayPeriodic;
            var unfulfilled = mmInfos.Where(mmi => mmi.Option.Return(o => o.Series.IsMMReportEnabled, false) && mmi.SessionId == CurrentSessionId && (mmi.AmountSign || mmi.SpreadSign) && (!autoStop || !mmi.IsFillTotal))
                                     .Select(mmi => Tuple.Create(mmi.SecurityCode, mmi.AmountSign == mmi.SpreadSign ? "amount,spread" : mmi.AmountSign ? "amount" : "spread", mmi.PeriodEnd, (mmi.Security as OptionInfo)?.Series.SeriesId.Id))
                                     .ToArray();

            var now = Connector.GetMarketTime();

            if(unfulfilled.Length == 0) {
                _mmUnfulfillTime = _mmLastReportTime = default(DateTime);
            } else {
                var needToSend = false;
                var reportType = string.Empty;

                if(_mmUnfulfillTime.IsDefault()) {
                    _mmUnfulfillTime = now;
                } else if(_mmLastReportTime.IsDefault()) {
                    needToSend = now - _mmUnfulfillTime > TimeSpan.FromSeconds(delayFirst);
                    reportType = "first";
                } else {
                    needToSend = now - _mmLastReportTime > TimeSpan.FromSeconds(delayPeriodic);
                    if(needToSend)
                        reportType = "periodic";
                }

                if(needToSend) {
                    var message = "MM obligations are not fulfilled for the following securities:\n\n" +
                                  unfulfilled.GroupBy(t => t.Item4)
                                             .OrderBy(g => g.Key)
                                             .Select(g => {
                                                 var items = g.ToArray();
                                                 var prefix = $"{g.Key} ({items.Length} securities):\n";

                                                 return prefix + items.OrderBy(t => t.Item1)
                                                                      .Select(t => $"   {t.Item1}: {t.Item2}, endPeriod={t.Item3:dMMMyy HH:mm:ss}")
                                                                      .Join("\n");
                                             }).Join("\n");

                    _mmLastReportTime = now;
                    _controller.SendEmail($"MM obligations report ({reportType}, {unfulfilled.Length} securities)", message, true);
                }
            }
        }

        #region gui update

        readonly HashSet<IRobotDataUpdater> _registeredUpdaters = new HashSet<IRobotDataUpdater>();
        readonly ConcurrentQueue<Tuple<object, Action>> _guiActions = new ConcurrentQueue<Tuple<object, Action>>();
        readonly LinkedList<Tuple<DateTime, Action>> _delayedGuiActions = new LinkedList<Tuple<DateTime, Action>>();

        public void RegisterUpdater(IRobotDataUpdater updater) {
            lock(_registeredUpdaters)
                _registeredUpdaters.Add(updater);
        }

        public void DeregisterUpdater(IRobotDataUpdater updater) {
            lock(_registeredUpdaters)
                _registeredUpdaters.Remove(updater);
        }

        public void AddGuiOneTimeAction(Action action) {
            _guiActions.Enqueue(Tuple.Create((object)null, action));
        }

        public void AddGuiOneTimeDelayedAction(Action action, TimeSpan delay) {
            var t = Tuple.Create(SteadyClock.Now + delay, action);
            lock(_delayedGuiActions)
                _delayedGuiActions.AddLast(t);
        }

        public void AddGuiOneTimeActionByKey(object key, Action action) {
            _guiActions.Enqueue(Tuple.Create(key, action));
        }

        void Notify(string name) {
            AddGuiOneTimeActionByKey(Tuple.Create(this, name), () => RaisePropertyChanged(name));
        }

        readonly List<Tuple<object, Action>> _tmpActions = new List<Tuple<object, Action>>(100);
        readonly HashSet<object> _tmpActionsHash = new HashSet<object>(); 

        /// <summary>обновление данных по таймеру для отображения на экране актуальной информации</summary>
        public void UpdateData() {
            CurrentTime = Connector.GetMarketTime();
            MarketDataDelay = Connector.MarketDataDelay;
            NumTransactions = _controller.TransactionListener.NumTransactions;
            NumTransactionsPerSecond = Connector.Trader.Return(t => t.TranRateController.LastSecondTransactionsCached, 0);

            IRobotDataUpdater[] updaters;
            lock(_registeredUpdaters)
                updaters = _registeredUpdaters.ToArray();

            updaters.ForEach(u => u.UpdateData());

            var count = _guiActions.Count;
            if(count > 0) {
                _tmpActions.Clear();

                for(var i = 0; i < count; ++i) {
                    Tuple<object, Action> t;

                    if(_guiActions.TryDequeue(out t))
                        _tmpActions.Add(t);
                    else
                        _log.Dbg.AddErrorLog("count returned {0} elements but dequeue[{1}] failed", count, i);
                }

                _tmpActionsHash.Clear();
                foreach(var t in _tmpActions) {
                    if(t.Item1 == null) {
                        t.Item2();
                    } else if(!_tmpActionsHash.Contains(t.Item1)) {
                        _tmpActionsHash.Add(t.Item1);
                        t.Item2();
                    }
                }
            }

            if(_delayedGuiActions.Count > 0) {
                var now = SteadyClock.Now;
                var nodes = new List<LinkedListNode<Tuple<DateTime, Action>>>();
                lock(_delayedGuiActions) {
                    for(var node = _delayedGuiActions.First; node != null; node = node.Next) {
                        if(node.Value.Item1 >= now)
                            nodes.Add(node);
                    }

                    nodes.ForEach(n => _delayedGuiActions.Remove(n));
                }

                if(nodes.Count > 0)
                    nodes.OrderBy(n => n.Value.Item1).ForEach(n => n.Value.Item2());
            }

            RecheckAutoStartStopStrategies(false);

            VolStats.UpdateData();
        }

        #endregion
    }
}
