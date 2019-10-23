using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AsyncHandler;
using Ecng.Common;
using MoreLinq;
using OptionBot.Config;
using StockSharp.BusinessEntities;
using StockSharp.Logging;
using StockSharp.Messages;
using StockSharp.Plaza;

namespace OptionBot.robot {
    /// <summary>
    /// Класс-обертка для адаптера для подключения к шлюзу plaza.
    /// </summary>
    public class Connector : Disposable, ITimerFactory, IClock {
        readonly Logger _log;

        PlazaTraderEx _trader;
        public PlazaTraderEx Trader {
            get {return _trader;}
            set {
                if(_trader == value)
                    return;
                
                _log.Dbg.AddInfoLog("new PlazaTrader was set");

                _trader = value;
                ConnectorReset.SafeInvoke(this);
            }
        }

        public Controller Controller {get; private set;}
        RobotLogger RobotLogger {get {return Controller.RobotLogger;}}

        RobotData RobotData {get {return Controller.RobotData; }}
        IConfigGeneral Settings {get {return Controller.ConfigProvider.General.Effective; }}

        readonly Func<HandlerThread> _robotThreadGetter;
        HandlerThread RobotThread { get {return _robotThreadGetter(); }}

        /// <summary>
        /// Через этот объект происходит подписка на события Plaza-адаптера без перевода событий в другой поток.
        /// </summary>
        public IConnectorSubscriber DefaultSubscriber {get; private set;}

        #region init/deinit

        public Connector(Controller controller, Func<HandlerThread> robotThreadGetter) {
            _log = new Logger("Connector");
            Controller = controller;
            _robotThreadGetter = robotThreadGetter;
            DefaultSubscriber = new Subscriber(this, action => action());
            _log.Dbg.AddInfoLog("connector created");
        }

        protected override void DisposeManaged() {
            _log.Dbg.AddInfoLog("disposing of Connector...");
            try {
                DisposeTrader();
            } catch(Exception e) {
                _log.Dbg.AddWarningLog("dispose: {0}", e);
            }
            base.DisposeManaged();
        }

        #region subscription

        bool _subscribed;
        readonly Dictionary<string, object> _subscribedHandlers = new Dictionary<string, object>(); 

        const string _strConnected = "conn";
        const string _strDisconnected = "disconn";
        const string _strConnError = "connerr";
        const string _strConnRestored = "connrestor";
        const string _strConnOnline = "online";

        /// <summary>
        /// Подписка на события plaza адаптера.
        /// </summary>
        void SubscribeTrader() {
            if(_subscribed) { _log.Dbg.AddErrorLog("subscribe(): попытка повторной подписки"); return; }
            _subscribed = true;

            _subscribedHandlers.Clear();

            Trader.NewPortfolios        += TraderOnNewPortfolios;
            Trader.PortfoliosChanged    += TraderOnPortfoliosChanged;
            Trader.NewSecurities        += TraderOnNewSecurities;
            Trader.SecuritiesChanged    += TraderOnSecuritiesChanged;
            Trader.ProcessDataError     += TraderOnProcessDataError;
            Trader.NewTrades            += TraderOnNewTrades;
            Trader.NewMyTrades          += TraderOnNewMyTrades;
            Trader.NewOrders            += TraderOnNewOrders;
            Trader.OrdersChanged        += TraderOnOrdersChanged;
            Trader.OrdersRegisterFailed += TraderOnOrdersRegisterFailed;
            Trader.OrdersCancelFailed   += TraderOnOrdersCancelFailed;
            Trader.NewPositions         += TraderOnNewPositions;
            Trader.PositionsChanged     += TraderOnPositionsChanged;
            Trader.NewMarketDepths      += TraderOnNewMarketDepths;
            Trader.MarketDepthChanged   += TraderOnMarketDepthsChanged;

            _subscribedHandlers[_strConnected]      = new Action(() => HandleTraderConnection(ConnectionState.Connected, null));
            _subscribedHandlers[_strConnOnline]     = new Action(() => HandleTraderConnection(ConnectionState.Connected, null));
            _subscribedHandlers[_strDisconnected]   = new Action(() => HandleTraderConnection(ConnectionState.Disconnected, null));
            _subscribedHandlers[_strConnError]      = new Action<Exception>(e => HandleTraderConnection(ConnectionState.Connecting, e));
            _subscribedHandlers[_strConnRestored]   = new Action(() => HandleTraderConnection(ConnectionState.Connected, null));

            Trader.Connected                                        += (Action)_subscribedHandlers[_strConnected];
            Trader.Online                                           += (Action)_subscribedHandlers[_strConnOnline];
            Trader.Disconnected                                     += (Action)_subscribedHandlers[_strDisconnected];
            Trader.ConnectionError                                  += (Action<Exception>)_subscribedHandlers[_strConnError];
            Trader.ReConnectionSettings.ConnectionSettings.Restored += (Action)_subscribedHandlers[_strConnRestored];

            Trader.SessionRecordInserted += OnSessionRecordInserted;
            Trader.NewTransaction += TraderOnNewTransaction;
            Trader.NewMMInfo += TraderOnNewMmInfo;
            Trader.RealtimeMonitor.RealtimeStateChanged += OnRealtimeStateChanged;
            Trader.Error += TraderOnError;

            Trader.MessageProcessorStatusReport += OnMessageProcessorStatusReport;
        }

        /// <summary>
        /// Отписка от событий plaza адаптера.
        /// </summary>
        void UnsubscribeTrader() {
            if(!_subscribed) { _log.Dbg.AddErrorLog("unsubscribe(): не подписан"); return; }
            _subscribed = false;
            
            Trader.NewPortfolios        -= TraderOnNewPortfolios;
            Trader.PortfoliosChanged    -= TraderOnPortfoliosChanged;
            Trader.NewSecurities        -= TraderOnNewSecurities;
            Trader.SecuritiesChanged    -= TraderOnSecuritiesChanged;
            Trader.ProcessDataError     -= TraderOnProcessDataError;
            Trader.NewTrades            -= TraderOnNewTrades;
            Trader.NewMyTrades          -= TraderOnNewMyTrades;
            Trader.NewOrders            -= TraderOnNewOrders;
            Trader.OrdersChanged        -= TraderOnOrdersChanged;
            Trader.OrdersRegisterFailed -= TraderOnOrdersRegisterFailed;
            Trader.OrdersCancelFailed   -= TraderOnOrdersCancelFailed;
            Trader.NewPositions         -= TraderOnNewPositions;
            Trader.PositionsChanged     -= TraderOnPositionsChanged;
            Trader.NewMarketDepths      -= TraderOnNewMarketDepths;
            Trader.MarketDepthChanged   -= TraderOnMarketDepthsChanged;

            Trader.Connected                                        -= (Action)_subscribedHandlers[_strConnected];
            Trader.Online                                           -= (Action)_subscribedHandlers[_strConnOnline];
            Trader.Disconnected                                     -= (Action)_subscribedHandlers[_strDisconnected];
            Trader.ConnectionError                                  -= (Action<Exception>)_subscribedHandlers[_strConnError];
            Trader.ReConnectionSettings.ConnectionSettings.Restored -= (Action)_subscribedHandlers[_strConnRestored];

            Trader.SessionRecordInserted -= OnSessionRecordInserted;
            Trader.NewTransaction -= TraderOnNewTransaction;
            Trader.NewMMInfo -= TraderOnNewMmInfo;
            Trader.RealtimeMonitor.RealtimeStateChanged -= OnRealtimeStateChanged;
            Trader.Error -= TraderOnError;

            Trader.MessageProcessorStatusReport -= OnMessageProcessorStatusReport;
        }

        #endregion
        #endregion

        /// <summary>
        /// Подключиться к плазе.
        /// </summary>
        public void Connect() {
            _log.Dbg.AddInfoLog("Connect()");
            var state = RobotData.ConnectionState;
            if(state != ConnectionState.Disconnected) {
                _log.AddWarningLog("Невозможно выполнить подключение в состоянии {0}", state);
                return;
            }

            try {
                SetConnectionState(ConnectionState.Connecting);
                CreateTrader();
                Trader.Connect();
            } catch(Exception e) {
                _log.AddErrorLog("Ошибка инициализации соединения с терминалом plaza: {0}", e);
                SetConnectionState(ConnectionState.Disconnected);
            }
        }

        /// <summary>
        /// Отключиться от плазы.
        /// </summary>
        public void Disconnect() {
            _log.Dbg.AddInfoLog("Disconnect()");
            if(RobotData.ConnectionState == ConnectionState.Disconnected || RobotData.ConnectionState == ConnectionState.Disconnecting) {
                _log.AddWarningLog("Невозможно выполнить отключение в состоянии {0}", RobotData.ConnectionState);
                return;
            }

            SetConnectionState(ConnectionState.Disconnecting);

            Action<PlazaTraderEx, bool> onDisconnected = (trader, isDone) => {
                if(trader != Trader) return;
                if(!isDone) {
                    _log.Dbg.AddWarningLog("disconnect timeout. disposing...");
                    DisposeTrader();
                }
                SetConnectionState(ConnectionState.Disconnected);
            };

            try {
                Trader.StopExport();
                Trader.Disconnect();

                if(Trader.ConnectionState == ConnectionStates.Disconnected) {
                    onDisconnected(Trader, true);
                } else {
                    _log.Dbg.AddInfoLog("disconnecting...");

                    var trader = Trader;
                    var result = false;
                    RobotThread.When(250, 5,
                                     () => result = trader != Trader || trader.ConnectionState == ConnectionStates.Disconnected,
                                     null, () => onDisconnected(trader, result));
                }
            } catch(Exception e) {
                _log.Dbg.AddWarningLog("exception during Disconnect(): {0}", e);
            }
        }

        public MarketDepth GetMarketDepth(string secId) {
            var trader = Trader;
            if(trader == null) {
                _log.Dbg.AddWarningLog("GetMarketDepth: Unable to get market depth for {0}. Trader is null.", secId);
                return null;
            }

            var sec = trader.Securities.FirstOrDefault(s => s.Id == secId);
            if(sec == null) {
                _log.Dbg.AddWarningLog("GetMarketDepth: security '{0}' not found.", secId);
                return null;
            }

            return trader.GetMarketDepth(sec);
        }

        /// <summary>
        /// Принудительное обновление стакана по заданному инструменту.
        /// </summary>
        public void UpdateMarketDepth(string secId) {
            var depth = GetMarketDepth(secId);
            if(depth != null)
                MarketDepthChanged?.Invoke(this, depth);
            else
                _log.Dbg.AddWarningLog("UpdateMarketDepth({0}): market depth is null", secId);
        }

        #region market time

        public static readonly TimeSpan UTCOffsetMarket = Exchange.Moex.TimeZoneInfo.GetUtcOffset(DateTime.Now);
        public static readonly TimeSpan UTCOffsetLocal = TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now);
        public static readonly TimeSpan MarketLocalDiff = UTCOffsetMarket - UTCOffsetLocal;

        DateTime IClock.Now {get {return GetMarketTime();}}

        public IClock CreateClock() { return this; }

        public ITimer CreateTimer() {
            return new LocalTimer();
        }

        public IStopwatch CreateStopwatch() {
            return new LocalStopwatch();
        }

        /// <summary>
        /// Получить текущее рыночное время с учетом корректировки неточности локального времени.
        /// </summary>
        public DateTime GetMarketTime() {
            return DateTime.SpecifyKind(DateTime.UtcNow + UTCOffsetMarket + LoggingHelper.NowOffset, DateTimeKind.Unspecified);
        }

        public TimeSpan MarketDataDelay {get {return Trader.Return(t => t.RealtimeMonitor.LastMarketDelay, TimeSpan.Zero); }}

        #endregion

        #region Обработчики событий plaza адаптера

        [MethodImpl(MethodImplOptions.Synchronized)]
        void HandleTraderConnection(ConnectionState state, Exception e) {
            var online = Trader.IsOnline;
            SetConnectionState(state == ConnectionState.Connected ? 
                               online ? ConnectionState.Connected : ConnectionState.Synchronizing : 
                               state, e);
        }

        void TraderOnNewSecurities(IEnumerable<Security> securities) {
            securities.ForEach(s => {
                //if(s.Id.StartsWith("RI1") && s.Type == null) {}
                NewSecurity.SafeInvoke(this, s);
            });
        }

        void TraderOnSecuritiesChanged(IEnumerable<Security> securities) {
            securities.ForEach(s => SecurityChanged?.Invoke(this, s));
        }

        void TraderOnNewPortfolios(IEnumerable<Portfolio> portfolios) {
            portfolios.ForEach(p => NewPortfolio.SafeInvoke(this, p));
        }

        void TraderOnPortfoliosChanged(IEnumerable<Portfolio> portfolios) {
            portfolios.ForEach(p => PortfolioChanged.SafeInvoke(this, p));
        }

        void TraderOnNewPositions(IEnumerable<Position> positions) {
            positions.ForEach(p => NewPosition.SafeInvoke(this, p));
        }

        void TraderOnPositionsChanged(IEnumerable<Position> positions) {
            positions.ForEach(p => PositionChanged.SafeInvoke(this, p));
        }

        void TraderOnNewMarketDepths(IEnumerable<MarketDepth> depths) {
            //depths.ForEach(d => _log.Dbg.AddDebugLog("new depth: {0}, {1} quotes", d.Security.Id, d.Count));
            depths.ForEach(d => NewMarketDepth.SafeInvoke(this, d));
        }

        void TraderOnMarketDepthsChanged(MarketDepth depth) {
            //depths.ForEach(d => _log.Dbg.AddDebugLog("changed depth: {0}, {1} quotes", d.Security.Id, d.Count));
            MarketDepthChanged?.Invoke(this, depth);
        }

        void TraderOnNewOrders(IEnumerable<Order> orders) {
            orders.ForEach(o => NewOrder?.Invoke(this, o));
        }

        void TraderOnOrdersChanged(IEnumerable<Order> orders) {
            orders.ForEach(o => OrderChanged?.Invoke(this, o));
        }

        void TraderOnOrdersRegisterFailed(IEnumerable<OrderFail> orderFails) {
            orderFails.ForEach(fail => OrderFailed?.Invoke(this, fail));
        }

        void TraderOnOrdersCancelFailed(IEnumerable<OrderFail> orderFails) {
            orderFails.ForEach(fail => OrderCancelFailed?.Invoke(this, fail));
        }

        void TraderOnNewMyTrades(IEnumerable<MyTrade> myTrades) {
            myTrades.ForEach(mt => NewMyTrade?.Invoke(this, mt));
        }

        void TraderOnNewTrades(IEnumerable<Trade> trades) {
            NewTrades?.Invoke(this, trades);
        }

        void OnSessionRecordInserted(SessionTableRecord record) {
            _log.Dbg.AddInfoLog("CurMarketTime: {0:dd-MMM-yyyy HH:mm:ss.fff}", GetMarketTime());
            SessionRecordInserted?.Invoke(this, record);
        }

        void TraderOnNewTransaction(PlazaTransactionInfo info) {
            NewTransaction?.Invoke(this, info);
        }

        void TraderOnNewMmInfo(MMInfoRecord mmInfoRecord) {
            NewMMInfo?.Invoke(this, mmInfoRecord);
        }

        void OnRealtimeStateChanged() {
            RealtimeModeChanged?.Invoke(this, Trader.RealtimeMonitor.InRealtimeMode);
        }

        void TraderOnError(string message, Exception exception) {
            _log.Dbg.AddErrorLog(Util.FormatError(message, exception));
            Error.SafeInvoke(this, message, exception);
        }

        void OnMessageProcessorStatusReport(string processorName, MessageProcessorState state, int qSize, int msgSeqNum, string status) {
            RobotLogger.MsgProcessor.Log(processorName, state, qSize, msgSeqNum, status);
        }

        void TraderOnProcessDataError(Exception exception) {
            var plazaCode = (exception.InnerException as PlazaException)?.ErrorCode;
            if(plazaCode != null && PlazaTraderEx.IsOpInClearingError(plazaCode.Value)) {
                _log.Dbg.AddWarningLog("clearing op error: " + exception.InnerException.Message);
                return;
            }

            _log.AddErrorLog("Ошибка обработки данных: {0}", exception);
            Error?.Invoke(this, "Connector data error", exception);
        }

        #endregion

        #region helpers

        /// <summary>
        /// Установить новое состояние соединения.
        /// </summary>
        bool SetConnectionState(ConnectionState state, Exception e = null) {
            if(e != null) _log.AddWarningLog("Соединение: {0} => {1} {2}", RobotData.ConnectionState, state, Util.FormatError(null, e));

            if(RobotData.ConnectionState == state) { _log.Dbg.AddWarningLog("already in {0}", state); return false; }

            if(e == null) _log.AddInfoLog("Соединение: {0} => {1}", RobotData.ConnectionState, state);

            RobotData.ConnectionState = state;

            ConnectionStateChanged?.Invoke(this, RobotData.ConnectionState, e);

            return true;
        }

        /// <summary>
        /// Создать объект plaza адаптера.
        /// </summary>
        void CreateTrader() {
            if(Trader != null) {
                _log.Dbg.AddWarningLog("createtrader(): trader is not null. disposing...");
                DisposeTrader();
            }

            try {
                Trader = new PlazaTraderEx(this, _robotThreadGetter);
            } catch(Exception e) {
                DisposeTrader();
                _log.Dbg.AddErrorLog("CreateTrader excepion: {0}", e);
                throw;
            }
            
            SubscribeTrader();
        }

        /// <summary>
        /// Уничтожить объект plaza адаптера.
        /// </summary>
        void DisposeTrader() {
            if(Trader == null) {
                _log.Dbg.AddWarningLog("disposetrader(): nothing to dispose of");
                return;
            }
            _log.Dbg.AddInfoLog("DisposeTrader()");

            UnsubscribeTrader();

            try {
                Trader.StopExport();
                if(Trader.IsConnected) Trader.Disconnect();
            } catch(Exception e) {
                _log.Dbg.AddWarningLog("exception during DisposeTrader(): {0}", e);
            }

            Trader.Dispose();
            Trader = null;
        }

        #endregion

        #region События адаптера

        event Action<Connector> ConnectorReset;
        event Action<Connector, ConnectionState, Exception> ConnectionStateChanged;
        event Action<Connector, SessionTableRecord> SessionRecordInserted;
        event Action<Connector, PlazaTransactionInfo> NewTransaction;
        event Action<Connector, MMInfoRecord> NewMMInfo;
        event Action<Connector, Order> NewOrder;
        event Action<Connector, Order> OrderChanged;
        event Action<Connector, OrderFail> OrderFailed;
        event Action<Connector, OrderFail> OrderCancelFailed;
        event Action<Connector, MyTrade> NewMyTrade;
        event Action<Connector, MarketDepth> NewMarketDepth;
        event Action<Connector, MarketDepth> MarketDepthChanged;
        event Action<Connector, IEnumerable<Trade>> NewTrades;
        event Action<Connector, Portfolio> NewPortfolio;
        event Action<Connector, Portfolio> PortfolioChanged;
        event Action<Connector, Security> NewSecurity;
        event Action<Connector, Security> SecurityChanged;
        event Action<Connector, Position> NewPosition;
        event Action<Connector, Position> PositionChanged;
        event Action<Connector, bool> RealtimeModeChanged;
        event Action<Connector, string, Exception> Error;

        /// <summary>
        /// Класс, посредством которого происходит подписка на события адаптера для всех подписчиков не-стратегий.
        /// Поддерживает дополнительное действие при вызове каждого события (например перевод события в другой поток)
        /// </summary>
        public class Subscriber : ProxySubscriber, IConnectorSubscriber {
            readonly Connector _connector;

            public Subscriber(Connector connector, Action<Action> callback) : base(callback) {
                _connector = connector;
            }

            public event Action<Connector>                              ConnectorReset          { add { _connector.ConnectorReset += Proxy(value); } remove { _connector.ConnectorReset -= Proxy(value); }}
            public event Action<Connector, ConnectionState, Exception>  ConnectionStateChanged  { add { _connector.ConnectionStateChanged += Proxy(value); } remove { _connector.ConnectionStateChanged -= Proxy(value); }}
            public event Action<Connector, SessionTableRecord>          SessionRecordInserted   { add { _connector.SessionRecordInserted += Proxy(value); } remove { _connector.SessionRecordInserted -= Proxy(value); }}
            public event Action<Connector, PlazaTransactionInfo>        NewTransaction          { add { _connector.NewTransaction += Proxy(value); } remove { _connector.NewTransaction -= Proxy(value); }}
            public event Action<Connector, MMInfoRecord>                NewMMInfo               { add { _connector.NewMMInfo += Proxy(value); } remove { _connector.NewMMInfo -= Proxy(value); }}
            public event Action<Connector, Order>                       NewOrder                { add { _connector.NewOrder += Proxy(value); } remove { _connector.NewOrder -= Proxy(value); }}
            public event Action<Connector, Order>                       OrderChanged            { add { _connector.OrderChanged += Proxy(value); } remove { _connector.OrderChanged -= Proxy(value); }}
            public event Action<Connector, OrderFail>                   OrderFailed             { add { _connector.OrderFailed += Proxy(value); } remove { _connector.OrderFailed -= Proxy(value); }}
            public event Action<Connector, OrderFail>                   OrderCancelFailed       { add { _connector.OrderCancelFailed += Proxy(value); } remove { _connector.OrderCancelFailed -= Proxy(value); }}
            public event Action<Connector, MyTrade>                     NewMyTrade              { add { _connector.NewMyTrade += Proxy(value); } remove { _connector.NewMyTrade -= Proxy(value); }}
            public event Action<Connector, MarketDepth>                 NewMarketDepth          { add { _connector.NewMarketDepth += Proxy(value); } remove { _connector.NewMarketDepth -= Proxy(value); }}
            public event Action<Connector, MarketDepth>                 MarketDepthChanged      { add { _connector.MarketDepthChanged += Proxy(value); } remove { _connector.MarketDepthChanged -= Proxy(value); }}
            public event Action<Connector, IEnumerable<Trade>>          NewTrades               { add { _connector.NewTrades += Proxy(value); } remove { _connector.NewTrades -= Proxy(value); }}
            public event Action<Connector, Portfolio>                   NewPortfolio            { add { _connector.NewPortfolio += Proxy(value); } remove { _connector.NewPortfolio -= Proxy(value); }}
            public event Action<Connector, Portfolio>                   PortfolioChanged        { add { _connector.PortfolioChanged += Proxy(value); } remove { _connector.PortfolioChanged -= Proxy(value); }}
            public event Action<Connector, Security>                    NewSecurity             { add { _connector.NewSecurity += Proxy(value); } remove { _connector.NewSecurity -= Proxy(value); }}
            public event Action<Connector, Security>                    SecurityChanged         { add { _connector.SecurityChanged += Proxy(value); } remove { _connector.SecurityChanged -= Proxy(value); }}
            public event Action<Connector, Position>                    NewPosition             { add { _connector.NewPosition += Proxy(value); } remove { _connector.NewPosition -= Proxy(value); }}
            public event Action<Connector, Position>                    PositionChanged         { add { _connector.PositionChanged += Proxy(value); } remove { _connector.PositionChanged -= Proxy(value); }}
            public event Action<Connector, bool>                        RealtimeModeChanged     { add { _connector.RealtimeModeChanged += Proxy(value); } remove { _connector.RealtimeModeChanged -= Proxy(value); }}
            public event Action<Connector, string, Exception>           Error                   { add { _connector.Error += Proxy(value); } remove { _connector.Error -= Proxy(value); }}
        }

        /// <summary>
        /// Интерфейс подписчика на события адаптера.
        /// </summary>
        public interface IConnectorSubscriber {
            event Action<Connector> ConnectorReset;
            event Action<Connector, ConnectionState, Exception> ConnectionStateChanged;
            event Action<Connector, SessionTableRecord> SessionRecordInserted;
            event Action<Connector, PlazaTransactionInfo> NewTransaction;
            event Action<Connector, MMInfoRecord> NewMMInfo;
            event Action<Connector, Order> NewOrder;
            event Action<Connector, Order> OrderChanged;
            event Action<Connector, OrderFail> OrderFailed;
            event Action<Connector, OrderFail> OrderCancelFailed;
            event Action<Connector, MyTrade> NewMyTrade;
            event Action<Connector, MarketDepth> NewMarketDepth;
            event Action<Connector, MarketDepth> MarketDepthChanged;
            event Action<Connector, IEnumerable<Trade>> NewTrades;
            event Action<Connector, Portfolio> NewPortfolio;
            event Action<Connector, Portfolio> PortfolioChanged;
            event Action<Connector, Security> NewSecurity;
            event Action<Connector, Security> SecurityChanged;
            event Action<Connector, Position> NewPosition;
            event Action<Connector, Position> PositionChanged;
            event Action<Connector, bool> RealtimeModeChanged;
            event Action<Connector, string, Exception> Error;
        }

        #endregion
    }
}
