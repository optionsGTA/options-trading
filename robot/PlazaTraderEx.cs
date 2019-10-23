using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using AsyncHandler;
using Ecng.Common;
using Ecng.Interop;
using OptionBot.Config;
using StockSharp.BusinessEntities;
using StockSharp.Logging;
using StockSharp.Messages;
using StockSharp.Plaza;

namespace OptionBot.robot {
    /// <summary>
    /// Адаптер для подключения к шлюзу плазы.
    /// </summary>
    public sealed class PlazaTraderEx : PlazaTrader {
        readonly Logger _log = new Logger("PlazaTraderEx");

        readonly Connector _connector;
        public TransactionRateController TranRateController {get;}
        public Controller Controller => _connector.Controller;
        public RobotData RobotData => Controller.RobotData;
        RobotHeart Heart => Controller.Heart;

        public RealtimeMonitor RealtimeMonitor {get; private set;}

        public DateTime LastOptGroupCancelTime {get; private set;}

        readonly Func<HandlerThread> _robotThreadGetter;
        HandlerThread RobotThread => _robotThreadGetter();

        readonly HashSet<int> _watchTransactions = new HashSet<int>(); 

        IConfigGeneral CfgGeneral => Controller.ConfigProvider.General.Effective;

        DateTime _lastDepthUpdateTime;

        #region init/deinit

        public PlazaTraderEx(Connector connector, Func<HandlerThread> robotThreadGetter) {
            _log.RegisterSource(this, LogTarget.Dbg);
            LogLevel = LogLevels.Debug;

            _connector = connector;
            _robotThreadGetter = robotThreadGetter;
            TranRateController = new TransactionRateController(this, robotThreadGetter);
            Address = CfgGeneral.PlazaAddress.To<IPEndPoint>();
            AppName = "OptionBot";
            Name = "PlazaTraderEx1";

            var cgateKey = CfgGeneral.PlazaCGateKey.With(k => k.Trim());
            if(cgateKey.IsEmpty() || cgateKey.Length < 5)
                throw new InvalidOperationException("Ключ CGate задан неверно.");

            IsCGate = true;
            CGateKey = cgateKey;

            OrdersLogFilterFutures = RobotData.AllFutures.Select(f => f.Code.ToUpperInvariant()).ToArray();
            if(!OrdersLogFilterFutures.Any())
                throw new InvalidOperationException("Все фьючерсы, с которыми будет работать робот, должны быть добавлены в список перед подключением.");

            _log.AddInfoLog("Инициализация plaza адаптера. адрес={0}, фьючерсы={1}, processor={2}", CfgGeneral.PlazaAddress, string.Join(",", OrdersLogFilterFutures), TransactionAdapter.InMessageProcessor.ToString());

            EntityFactory = new EntityFactoryEx();

            Tables.Clear();
            Tables.Add(TableRegistry.Portfolios);
            Tables.Add(TableRegistry.Positions);
            Tables.Add(TableRegistry.Session);
            Tables.Add(TableRegistry.OrdersLogFuture);
            Tables.Add(TableRegistry.OrdersLogOption);
            Tables.Add(TableRegistry.TradeOption);
            Tables.Add(TableRegistry.AnonymousOrdersBook);
            Tables.Add(TableRegistry.AnonymousOrdersBookInfo);
            Tables.Add(TableRegistry.AnonymousOrdersLog);
            Tables.Add(TableRegistry.SessionContentsFuture);
            Tables.Add(TableRegistry.SessionContentsOption);
            Tables.Add(TableRegistry.VarMarginFuture); // добавить таблицы для получения информации о вариационной марже
            Tables.Add(TableRegistry.VarMarginOption);
            Tables.Add(TableRegistry.CommonFuture);
            Tables.Add(TableRegistry.CommonOption);
            Tables.Add(TableRegistry.MarketMakingOption);

            DefaultFutureDepthTable = null; // не получать стакан для фьючерсов
            DefaultOptionDepthTable = null;

            CreateDepthFromOrdersLog = true;
            CreateTradesFromOrdersLog = false; // LastTrade is set anyway

            UpdateSecurityOnEachEvent = true; // обновлять best bid/ask при каждом обновлении стакана

            TableRegistry.StreamRegistry.IsFastRepl = CfgGeneral.PlazaUseFastRepl;

            Connected += () => HandlePlazaConnection(true, null);
            Disconnected += () => HandlePlazaConnection(false, null);
            ConnectionError += e => HandlePlazaConnection(false, e);
            ExportError += e => HandlePlazaConnection(false, e);

            const string TableLogDir = "TableLog";
            (TableLogDir + "\\").CreateDirIfNotExists();

            var now = DateTime.Now;

//            TableRegistry.AnonymousOrdersLog.LogTableData = true;
            TableRegistry.AnonymousOrdersBook.LogTableData = true;
            TableRegistry.AnonymousOrdersBook.LogFileName = "{0}\\orders_book_{1:yyyyMMddHHmmss}.csv".Put(TableLogDir, now);
            TableRegistry.AnonymousOrdersBookInfo.LogTableData = true;
            TableRegistry.AnonymousOrdersBookInfo.LogFileName = "{0}\\info_{1:yyyyMMddHHmmss}.csv".Put(TableLogDir, now);

//            try { File.Delete("info"); } catch {}
//            try { File.Delete("orders"); } catch {}
//            try { File.Delete("orders_log"); } catch {}

            OrdersRegisterFailed += OnOrdersFailed;
            OrdersCancelFailed += OnOrdersFailed;

            var sessionListener = new PlazaSessionListener(this);
            sessionListener.SessionRecordInserted += OnSessionRecordInserted;

            MarketDepthsChanged += OnMarketDepthsChanged;

            // ReSharper disable once ObjectCreationAsStatement
            new PlazaPortfolioListener(this);

            // ReSharper disable once ObjectCreationAsStatement
            //new PlazaCommonListener(this);

            var transactionListener = new PlazaTransactionListener(this);
            transactionListener.NewTransaction += OnNewTransaction;

            RealtimeMonitor = new RealtimeMonitor(_connector.Controller, _connector);

            Controller.ConfigProvider.General.EffectiveConfigChanged += GeneralOnEffectiveConfigChanged;
            GeneralOnEffectiveConfigChanged(null, null);

            TransactionIdGenerator = new MyTransactionIdGenerator();

            TransactionManager.ProcessResponse += TransactionManagerOnProcessResponse;

            var mmInfoListener = new PlazaMMInfoListener(this);
            mmInfoListener.MMInfoInserted += OnMMInfoRecordInserted;

            // настройка параметров автоматического переподключения
            TransactionAdapter.SessionHolder.ReConnectionSettings.Interval = TimeSpan.FromSeconds(15);
            TransactionAdapter.SessionHolder.ReConnectionSettings.AttemptCount = 10000;
            ReConnectionSettings.ConnectionSettings.Interval = TimeSpan.FromSeconds(15);
            ReConnectionSettings.ConnectionSettings.AttemptCount = 10000;
            ReConnectionSettings.ConnectionSettings.Restored += () => HandlePlazaConnection(true, null);

            Heart.Heartbeat += OnHeartbeat;
        }

        protected override void DisposeManaged() {
            _log.Dbg.AddInfoLog("Disposing PlazaTrader...");
            Controller.ConfigProvider.General.EffectiveConfigChanged -= GeneralOnEffectiveConfigChanged;
            Heart.Heartbeat -= OnHeartbeat;
            RealtimeMonitor.Dispose();
            TranRateController.Dispose();
            StopExport();
            Thread.Sleep(TimeSpan.FromSeconds(3));
            base.DisposeManaged();
        }

        protected override PlazaTransactionMessageAdapter CreateTransactionAdapter(PlazaSessionHolder holder) {
            return new MyTransactionMessageAdapter(holder, this);
        }

        void OnHeartbeat() {
            if(IsConnected)
                SendHeartbeat();
        }

        void GeneralOnEffectiveConfigChanged(ICfgPairGeneral a, string[] b) {
            PollTimeOut = CfgGeneral.UseZeroPollTimeout ? 0u : 1u;
            ReportMessageProcessorStatus = CfgGeneral.LogMessageProcessor;
        }

        #endregion

        #region connection

        void HandlePlazaConnection(bool connected, Exception e) {
            _log.AddInfoLog("Соединение с плазой {0}.", connected ? "установлено" : "разорвано");
            if(!connected)
                return;

            _log.AddInfoLog("Старт экспорта.");
            try {
                StartExport();
            } catch(Exception ex) {
                Error.SafeInvoke("Ошибка старта экспорта:", ex);
                return;
            }

            // когда адаптер переходит в состояние Connected, это еще не означает полной синхронизации данных
            // поэтому ожидаем когда каждый из потоков перейдет в состояние Online
            WaitPlazaOnlineState(false, IsOnline);
        }

        int _plazaOnlineTryCount;
        void WaitPlazaOnlineState(bool timeout, bool isOnline) {
            if(!timeout) _plazaOnlineTryCount = 0;


            if(!isOnline) {
                const int timeoutSize = 30;
                _log.AddWarningLog("({0}) синхронизация... lastUpdate={1}", ++_plazaOnlineTryCount, _lastDepthUpdateTime);

                var plazaOnline = false;
                // периодически проверяем, перешли ли все плаза-потоки в сотояние Online
                // если перешли, то вызываем обработчик
                RobotThread.When(250, timeout ? timeoutSize : 10,
                                 () => plazaOnline = IsOnline,
                                 () => !IsConnected,
                                 () => WaitPlazaOnlineState(true, plazaOnline));

                return;
            }

            _log.AddInfoLog("Синхронизация завершена.");
            Online.SafeInvoke();
        }

        public bool IsConnected => ConnectionState == ConnectionStates.Connected;

        public bool IsOnline { get {
            try {
                var online = StreamManager.Streams.Any() && StreamManager.IsOnline();
                _log.Dbg.AddInfoLog("online: {0}, {1}", online, string.Join(",", StreamManager.Streams.Select(s => "{0}:{1}".Put(s.Name, s.IsOnline))));
                return online;
            } catch(Exception e) {
                _log.Dbg.AddErrorLog("IsOnline: {0}", e);
                return false;
            }
        }}

        #endregion

        #region watch transactions 

        // internal connector thread here
        void TransactionManagerOnProcessResponse(Transaction transaction) {
            lock(_watchTransactions) {
                if(!_watchTransactions.Remove(transaction.OriginTransaction.Return(tr => tr.Id, 0)))
                    return;
            }

            RobotThread.ExecuteAsync(() => {
                var t = transaction;
                _log.Dbg.AddInfoLog("response: name={0}, id={1}, error={2}, dir={3}, isin={4}, clientcode={5}, comment={6}, broker={7}, orderCount={8}, msg={9}",
                    t.Name, t.Id, t.ErrorInfo, t.Direction, t.Isin, t.ClientCode, t.Comment, t.BrokerCode, t.OrderCount, t.Message);
                t = t.OriginTransaction;
                _log.Dbg.AddInfoLog("orig: name={0}, id={1}, error={2}, dir={3}, isin={4}, clientcode={5}, comment={6}, broker={7}, orderCount={8}, msg={9}",
                    t.Name, t.Id, t.ErrorInfo, t.Direction, t.Isin, t.ClientCode, t.Comment, t.BrokerCode, t.OrderCount, t.Message);

                if(transaction.ErrorInfo == null)
                    _log.AddInfoLog("Операция {0} успешно завершена.", transaction.OriginTransaction.Name);
                else
                    _log.AddErrorLog("{0}: {1}", transaction.OriginTransaction.Name, transaction.ErrorInfo.Message);
            });
        }

        #endregion

        #region plaza handlers

        void OnMarketDepthsChanged(IEnumerable<MarketDepth> depths) {
            var depth = ((MarketDepth[])depths)[0]; // always single element in current StockSharp
            _lastDepthUpdateTime = depth.LastChangeTime;
            MarketDepthChanged?.Invoke(depth);
            RealtimeMonitor.OnNewMarketData(depth.LastChangeTime);
        }

        void OnSessionRecordInserted(SessionTableRecord sessionTableRecord) {
            SessionRecordInserted?.Invoke(sessionTableRecord);
        }

        void OnNewTransaction(PlazaTransactionInfo info) {
            NewTransaction?.Invoke(info);
        }

        void OnMMInfoRecordInserted(MMInfoRecord mmInfoRecord) {
            NewMMInfo?.Invoke(mmInfoRecord);
        }

        #region handle order errors

        public const int PlazaErrTransactionLimitExceeded   = 9999;
        public const int PlazaErrNotEnoughMoney             = 332;
        public const int PlazaErrOrderForMoveNotFound       = 50;
        public const int PlazaErrOrderForDelNotFound        = 14;
        public const int PlazaErrTimeout                    = 24580;
        public const int PlazaErrTradingOpInClearing        = 380;
        public const int PlazaErrCancelInClearing           = 381;
        public const int PlazaErrMoveInClearing             = 382;

        public static bool IsOpInClearingError(int code) {
            return code == PlazaErrTradingOpInClearing ||
                   code == PlazaErrCancelInClearing ||
                   code == PlazaErrMoveInClearing;
        }

        // количество неизвестных ошибок, при превышении которого робот будет автоматически остановлен
        const int PlazaOtherErrorLimit = 10;

        // ошибки, после которых робот будет остановлен
        public static readonly int[] PlazaErrorsFatal   = { };

        // эти ошибки могут происходить при нормальной работе робота
        public static readonly int[] PlazaErrorsIgnore  = {
            PlazaErrOrderForMoveNotFound,
            PlazaErrOrderForDelNotFound,
            PlazaErrTransactionLimitExceeded,
            PlazaErrNotEnoughMoney
        };

        readonly List<Exception> _otherOrderErrors = new List<Exception>();

        void OnOrdersFailed(IEnumerable<OrderFail> orderFails) {
            foreach(var fail in orderFails) {
                var plazaError = fail.Error as PlazaException;
                if(plazaError != null) {
                    if(plazaError.ErrorCode == PlazaErrTransactionLimitExceeded || plazaError.IsFloodControlError) {
                        TranRateController.HandlePlazaTransactionLimitExceeded(fail);
                    }

                    if(PlazaErrorsFatal.Contains(plazaError.ErrorCode)) {
                        Error.SafeInvoke("Order fatal error", plazaError);
                        return;
                    }

                    if(PlazaErrorsIgnore.Contains(plazaError.ErrorCode) || plazaError.IsFloodControlError)
                        continue;

                    _otherOrderErrors.Add(plazaError);
                    _log.AddWarningLog("Order error ({0}/{1}): {2}", _otherOrderErrors.Count, PlazaOtherErrorLimit, plazaError);
                } else if(!(fail.Error is TransactionLimitExceededException)) {
                    _otherOrderErrors.Add(fail.Error);
                    _log.AddWarningLog("Unexpected order error ({0}/{1}): {2}", _otherOrderErrors.Count, PlazaOtherErrorLimit, fail.Error);
                }
            }

            if(_otherOrderErrors.Count > PlazaOtherErrorLimit) {
                Error.SafeInvoke("Too many plaza errors.", null);
                _otherOrderErrors.Clear();
            }
        }

        #endregion

        #region plazatrader overrides

        void RejectOrderTransaction(Order order, bool isRegister) {
            var fail = EntityFactory.CreateOrderFail(order, new TransactionLimitExceededException());

            if(isRegister) {
                _log.Dbg.AddWarningLog("reject register transId={0}", order.TransactionId);
                order.State = OrderStates.Failed;
                order.Messages.Add(fail.Error.Message);
                RaiseOrderFailed(fail);
            } else {
                _log.Dbg.AddWarningLog("reject cancel transId={0}, orderId={1}", order.TransactionId, order.Id);
                order.Messages.Add(fail.Error.Message);
                RaiseOrderFailed(fail, true);
            }
        }

        public bool IsTransactionLimitFail(OrderFail fail) {
            return (fail.Error as PlazaException).Return(e => e.IsFloodControlError || e.ErrorCode == PlazaErrTransactionLimitExceeded, false) ||
                   fail.Error is TransactionLimitExceededException;
        }

        public bool IsNotEnoughMoneyFail(OrderFail fail) {
            return (fail.Error as PlazaException).Return(e => e.ErrorCode, 0) == PlazaErrNotEnoughMoney;
        }

        public bool IsOrderNotFoundFail(OrderFail fail) {
            var code = (fail.Error as PlazaException).Return(e => e.ErrorCode, 0);
            return code == PlazaErrOrderForDelNotFound || code == PlazaErrOrderForMoveNotFound;
        }

        /// <summary>
        /// Отмена заявок по всем инструментам за одну транзакцию (1 транзакция на каждый тип инструмента)
        /// </summary>
        /// <param name="portfolio">Портфель - нужен для определения кода клиента.</param>
        /// <param name="types">Тип инструмента для удаления заявок. Если null, то удаляются заявки по фьючерсам и опционам.</param>
        public void CancelAllOrders(Portfolio portfolio, SecurityTypes? types = null) {
            //_log.Dbg.AddDebugLog($"CancelAllOrders({portfolio?.Name}, {types})");

            if(types == null || types == SecurityTypes.Option)
                if(TranRateController.TryAddTransaction(TransactionType.GroupCancel, true, () => CancelAllOrders(portfolio, SecurityTypes.Option)))
                    CancelOrders(SecurityTypes.Option, portfolio);

            if(types == null || types == SecurityTypes.Future)
                if(TranRateController.TryAddTransaction(TransactionType.GroupCancel, true, () => CancelAllOrders(portfolio, SecurityTypes.Future)))
                    CancelOrders(SecurityTypes.Future, portfolio);
        }

        void CancelOrders(SecurityTypes type, Portfolio portfolio) {
            if(type == SecurityTypes.Option)
                LastOptGroupCancelTime = SteadyClock.Now;

            TransactionAdapter.SendInMessage(new ActionMessage($"cancelorders({type}, {portfolio.Name})", () => {
                var msg = new OrderGroupCancelMessage {
                    TransactionId = TransactionIdGenerator.GetNextId(),
                    SecurityType = type,
                    PortfolioName = portfolio.Name
                };

                var tr = TransactionManager.Factory.CreateCancelGroup(msg);

                lock(_watchTransactions) {
                    if(!_watchTransactions.Add(tr.Id))
                        _log.Dbg.AddWarningLog("transaction id {0} is already in _watchTransactions set");
                }

                tr.SetOrderType(null)
                  .SetSide(null)
                  .SetClientCode(portfolio.Name);

                _log.Dbg.AddInfoLog("trId={0}; canceling all orders for type={1}...", tr.Id, type);

                Heart.DelayNextHeartbeat();
                TransactionManager.SendTransaction(tr);
            }));
        }

        protected override void OnCancelOrders(long transactionId, bool? isStopOrder = null, Portfolio portfolio = null, Sides? direction = null, ExchangeBoard board = null, Security security = null) {
            if(IsDisposed || !IsConnected) { _log.Dbg.AddWarningLog("OnCancelOrders: wrong state. connected={0}, disposed={1}", IsConnected, IsDisposed); return; }

            if(security?.Type == SecurityTypes.Option)
                LastOptGroupCancelTime = SteadyClock.Now;

            if(TranRateController.TryAddTransaction(TransactionType.GroupCancel, false, () => CancelOrders(isStopOrder, portfolio, direction, board, security))) {
                base.OnCancelOrders(transactionId, isStopOrder, portfolio, direction, board, security);
            }
        }

        protected override void OnRegisterOrder(Order order) {
            if(TranRateController.TryAddTransaction(TransactionType.NewOrder)) {
                (order as OrderEx).Do(o => o.HandleBeforeSend());
                order.PerfAnalyzer.StoreById(order.TransactionId);
                order.PerfAnalyzer.Restart("BeforeSend(register)", () => Controller.RobotLogger.OrderPerfLogger.Log(order));

                base.OnRegisterOrder(order);
            } else {
                RejectOrderTransaction(order, true);
            }
        }

        protected override void OnCancelOrder(Order order, long transId) {
            if(TranRateController.TryAddTransaction(TransactionType.CancelOrder)) {
                (order as OrderEx).Do(o => o.HandleBeforeCancel());
                base.OnCancelOrder(order, transId);
            } else {
                RejectOrderTransaction(order, false);
            }
        }
        #endregion

        #region FutMoveOrder

        protected override void OnReRegisterOrder(Order oldOrder, Order newOrder) {
            if(!TranRateController.TryAddTransaction(TransactionType.MoveOrder)) {
                RejectOrderTransaction(newOrder, true);
                return;
            }

            (newOrder as OrderEx).Do(o => o.HandleBeforeSend());
            (oldOrder as OrderEx).Do(o => o.HandleBeforeCancel());
            newOrder.PerfAnalyzer.StoreById(newOrder.TransactionId);
            newOrder.PerfAnalyzer.Restart("BeforeSend(move)", () => Controller.RobotLogger.OrderPerfLogger.Log(newOrder));

            base.OnReRegisterOrder(oldOrder, newOrder);
        }

        protected override void OnReRegisterOrderPair(Order oldOrder1, Order newOrder1, Order oldOrder2, Order newOrder2) {
            if((newOrder1.Volume == 0) != (newOrder2.Volume == 0))
                throw new InvalidOperationException("OnReRegisterOrderPair: both orders must have equally zero or non-zero volumes");

            if(!TranRateController.TryAddTransaction(TransactionType.MoverOrderPair)) {
                RejectOrderTransaction(newOrder1, true);
                RejectOrderTransaction(newOrder2, true);
                return;
            }

            newOrder2.PerfAnalyzer = newOrder1.PerfAnalyzer;

            newOrder1.PerfAnalyzer.StoreById(newOrder1.TransactionId);
            newOrder1.PerfAnalyzer.StoreById(newOrder2.TransactionId);

            newOrder1.PerfAnalyzer.Restart("BeforeSend(move2)", () => Controller.RobotLogger.OrderPerfLogger.Log(newOrder1));

            (newOrder1 as OrderEx).Do(o => o.HandleBeforeSend());
            (newOrder2 as OrderEx).Do(o => o.HandleBeforeSend());
            (oldOrder1 as OrderEx).Do(o => o.HandleBeforeCancel());
            (oldOrder2 as OrderEx).Do(o => o.HandleBeforeCancel());
            base.OnReRegisterOrderPair(oldOrder1, newOrder1, oldOrder2, newOrder2);
        }

        #endregion
        #endregion

        #region events

        public event Action Online; // событие перехода всех потоков плазы в состояние Online
        public event Action<string, Exception> Error;
        public event Action<SessionTableRecord> SessionRecordInserted;
        public event Action<PlazaTransactionInfo> NewTransaction;
        public event Action<MMInfoRecord> NewMMInfo;
        public event Action<MarketDepth> MarketDepthChanged;

        #endregion
    }

    class MyTransactionMessageAdapter : PlazaTransactionMessageAdapter {
        readonly PlazaTraderEx _parent;

        public MyTransactionMessageAdapter(PlazaSessionHolder sessionHolder, PlazaTraderEx parent) : base(sessionHolder) {
            _parent = parent;
        }

        protected override void OnSendInMessage(Message message) {
            switch (message.Type) {
                case MessageTypes.OrderRegister:
                case MessageTypes.OrderReplace:
                case MessageTypes.OrderPairReplace:
                case MessageTypes.OrderCancel:
                case MessageTypes.OrderGroupCancel:
                case MessageTypes.Heartbeat:
                    _parent.Controller.Heart.DelayNextHeartbeat();
                    break;
            }

            base.OnSendInMessage(message);
        }
    }

    class TransactionLimitExceededException : Exception {
        public TransactionLimitExceededException(string message = null) : base(message ?? "transaction rejected locally because of transaction limit") {}
    }
}
