using System;
using System.Threading;
using AsyncHandler;
using Ecng.Common;
using NLogLogger;
using OptionBot.Config;
using StockSharp.BusinessEntities;
using RobotHandlerThread = AsyncHandler.HandlerThread;

namespace OptionBot.robot {
    /// <summary>
    /// Контроллер. Содержит в себе все не-UI объекты.
    /// Командный интерфейс между UI и роботом.
    /// Переводит запросы UI в поток робота.
    /// </summary>
    public class Controller : Disposable, IRobotDataUpdater, ITimeGetter {
        #region fields/properties
        static Controller _instance;
        public static Controller Instance {get {return _instance;}}

        readonly Logger _log = new Logger("Controller");

        public Scheduler Scheduler {get;}

        public TransactionListener TransactionListener {get; private set;}

        readonly RobotData _robotData;
        public RobotData RobotData {get {return _robotData;}}

        Connector _connector;
        public Connector Connector {get {return _connector; }}
        /// <summary>Пользовательский интерфейс подписывается на события адаптера через данный объект для получения событий сразу по потоке GUI.</summary>
        public Connector.IConnectorSubscriber ConnectorGUISubscriber {get; private set;}
        readonly Robot _robot;
        public Robot Robot => _robot;

        public RobotLogger RobotLogger {get; private set;}

        public TimeKeeper TimeKeeper {get; private set;}

        HandlerThread RobotThread {get; set;}

        readonly Action<Action> _callback;

        readonly ConfigProvider _configProvider;
        public ConfigProvider ConfigProvider {get {return _configProvider;}}

        EmailSender _emailSender;

        public RobotHeart Heart {get;} = new RobotHeart();

        #endregion

        #region init/deinit

        public Controller(Action<Action> callback) {
            _callback = callback;
            _instance = this;

            _configProvider = new ConfigProvider(this);

            _connector = new Connector(this, () => RobotThread);
            ConnectorGUISubscriber = new Connector.Subscriber(_connector, callback);

            _robotData = new RobotData(this);

            SubscribeConnectorEvents();

            TimeKeeper = new TimeKeeper(_connector);
            TimeKeeper.TimeUpdated += () => _timeCorrected.SafeInvoke();

            NLogLogger.NLogLogger.SetTimeGetter(this);

            RobotThread = CreateRobotThread();

            Scheduler = new Scheduler(this);

            TransactionListener = new TransactionListener(this);

            RobotLogger = new RobotLogger(this);
            RobotLogger.Start();

            _robot = new Robot(this, () => RobotThread);
            SubscribeRobotEvents();

            RobotLogger.CfgGeneral.Log(ConfigProvider.General.Effective);
            //RobotLogger.Portfolio.LogPorfolio(RobotData.ActivePortfolio.NativePortfolio);
            //Trader.Positions.Where(p => p.CurrentValue != 0).ForEach(p => RobotLogger.Positions.LogPosition(p));

            CreateEmailSender(ConfigProvider.General.Effective.NotifyEmail);

            ConfigProvider.General.EffectiveConfigChanged += (general, names) => {
                RobotLogger.CfgGeneral.Log(general.Effective);
                CreateEmailSender(ConfigProvider.General.Effective.NotifyEmail);
            };

            ConnectorGUISubscriber.MarketDepthChanged += (connector, depth) => GUIMarketDepthChanged?.Invoke(connector, depth);

            OptionSeriesInfo.AnySeriesAtmStrikeChanged += (series, oldAtmCall, oldAtmPut) => {
                if(oldAtmCall != null && oldAtmPut != null)
                    _emailSender.Do(sender => sender.SendAtmChange(series, oldAtmCall, oldAtmPut));
            };
        }

        protected override void DisposeManaged() {
            if(IsDisposed) { _log.Dbg.AddWarningLog("повторный вызов Dispose для {0}", GetType().Name); return; }

            var disposed = false;

            _robot.Deinitialized += () => {
                if(disposed) {
                    _log.Dbg.AddErrorLog("Controller.DisposeManaged: already disposed");
                    return;
                }

                _log.Dbg.AddInfoLog("Disposing controller...");

                Action dispose = () => {
                    disposed = true;
                    Heart.Dispose();
                    UnsubscribeRobotEvents();
                    DisposeConnector();
                    RobotData.Dispose();
                    RobotLogger.Dispose();
                    _emailSender.Do(sender => sender.Dispose());
                    base.DisposeManaged();
                };

                if(RobotThread.IsAlive) {
                    RobotThread.ExecuteAsync(() => {
                        dispose();

                        RobotThread.DelayedAction(() => {
                            _log.Dbg.AddInfoLog("disposing of Robot thread...");
                            RobotThread.Dispose();
                        }, TimeSpan.FromSeconds(1));
                    });
                } else {
                    _log.Dbg.AddWarningLog("dispose: Controller thread is dead. disposing from current thread...");
                    dispose();
                }
            };

            _robot.Dispose();
        }

        public void UpdateData() {
            Scheduler.UpdateData();
            RobotData.UpdateData();
            ConfigProvider.UpdateData();
        }

        void DisposeConnector() {
            if(Connector == null) { _log.Dbg.AddInfoLog("DisposeConnector: connector is already null"); return; }

            if(RobotData.ConnectionState != ConnectionState.Disconnected)
                Disconnect();

            UnsubscribeConnectorEvents();
            Connector.Dispose();
            _connector = null;

            _configProvider.Dispose();
        }

        #region subscription

        bool _connectorSubscribed, _robotSubscribed;

        void SubscribeConnectorEvents() {
            if(_connectorSubscribed) { _log.Dbg.AddWarningLog("subscribe connector: already subscribed"); return; }
            _connectorSubscribed = true;

            ConnectorGUISubscriber.Error += ConnectorOnError;
        }

        void UnsubscribeConnectorEvents() {
            if(!_connectorSubscribed) { _log.Dbg.AddWarningLog("subscribe connector: not subscribed"); return; }
            _connectorSubscribed = false;

            ConnectorGUISubscriber.Error -= ConnectorOnError;
        }

        void SubscribeRobotEvents() {
            if(_robotSubscribed) { _log.Dbg.AddWarningLog("subscribe Robot: already subscribed"); return; }
            _robotSubscribed = true;
            
//            _robot.Error                 += RobotOnError;
        }

        void UnsubscribeRobotEvents() {
            if(!_robotSubscribed) { _log.Dbg.AddWarningLog("unsubscribe Robot: not subscribed"); return; }
            _robotSubscribed = false;
            
//            _robot.Error                 -= RobotOnError;
        }

        #endregion

        void OnRobotThreadCrash(object thread, Exception e) {
            _log.AddErrorLog("Необработанное исключение в потоке робота. Поток будет заменен новым.\n{0}", e);
            RobotThread.Dispose();
            RobotThread = CreateRobotThread(Thread.CurrentThread.ManagedThreadId);

            if(!RobotData.IsRobotStopped) {
                _log.AddErrorLog("Робот будет остановлен из-за ошибки в потоке робота.");
                //Stop(); // todo 
            }
        }

        RobotHandlerThread CreateRobotThread(int prevId = 0) {
            var t = new RobotHandlerThread("robot_thread") {
                OnCrashHandler = OnRobotThreadCrash, PropagateExceptions = false
            };
            t.Start();
            t.Post(() => _log.Dbg.AddInfoLog("Created Robot thread (id={0}).{1}", Thread.CurrentThread.ManagedThreadId, prevId==0?"":" Previous thread ({0}) crashed.".Put(prevId)));

            Heart.SetThreadToCheck("robotThread", action => t.ExecuteForceAsync(action));

            return t;
        }

        void CreateEmailSender(string address) {
            address = address.With(a => a.Trim());
            if(address.IsEmpty()) {
                _emailSender.Do(sender => sender.Dispose());
                _emailSender = null;
                return;
            }

            if(_emailSender != null && _emailSender.Address == address)
                return;

            try {
                _emailSender.Do(sender => sender.Dispose());
                _emailSender = new EmailSender(this, address);
                _log.Dbg.AddInfoLog("Created email sender: {0}", address);
            } catch(Exception e) {
                _log.AddErrorLog("Ошибка создания модуля отсылки email: {0}", e);
            }
        }

        #endregion

        #region commands interface

        /// <summary>
        /// Подключиться к плазе.
        /// </summary>
        public void Connect() {
            _log.Dbg.AddInfoLog("Connect()");
            RobotThread.ExecuteAsync(() => Connector.Connect());
        }

        /// <summary>
        /// Отключиться от плазы.
        /// </summary>
        public void Disconnect() {
            _log.Dbg.AddInfoLog("Disconnect()");

            RobotThread.ExecuteAsync(() => {
                if(Connector == null) {
                    _log.Dbg.AddWarningLog("Disconnect: connector is null");
                    return;
                }

                var allStopped = Robot.AllStopped;
                _log.Dbg.AddDebugLog($"Disconnect: AllStopped = {allStopped}");

                if(allStopped) {
                    Connector.Disconnect();
                } else {
                    Action<int> action = null;

                    action = numStra => {
                        if(!Robot.AllStopped) return;

                        Robot.RobotStateChanged -= action;
                        _log.Dbg.AddDebugLog("Everything is stopped. Proceeding with disconnect...");
                        Connector.Disconnect();
                    };

                    Robot.RobotStateChanged += action;
                    Robot.StopEverything();
                }
            });
        }

        /// <summary>
        /// Обновить стакан по заданному инструменту.
        /// </summary>
        /// <param name="secId"></param>
        public void UpdateMarketDepth(string secId) {
            RobotThread.ExecuteAsync(() => Connector.UpdateMarketDepth(secId));
        }

        /// <summary>
        /// Отменить все заявки.
        /// </summary>
        public void CancelOrders() {
            _log.Dbg.AddInfoLog("CancelOrders()");
            RobotThread.ExecuteAsync(() => _robot.CancelOrders());
        }

        /// <summary>
        /// Закрыть все позиции.
        /// </summary>
        public void ClosePositions() {
            _log.Dbg.AddInfoLog("ClosePositions()");
            RobotThread.ExecuteAsync(() => _robot.ClosePositions());
        }

        public void SendEmail(string subject, string body = null, bool separateMessage = false) {
            _log.Dbg.AddDebugLog("SendEmail({0})", subject);
            var sender = _emailSender;

            if(sender == null)
                _log.Dbg.AddErrorLog("Unable to send email. sender==null. (body={0})", body);
            else
                sender.SendEmail(subject, body, separateMessage);
        }

        #endregion

        #region events

        public event Action<Controller, string, Exception> Error;

        public event Action<Connector, MarketDepth> GUIMarketDepthChanged;

        #endregion

        #region event handlers

        void ConnectorOnError(Connector c, string message, Exception exception) {
            _callback(() => Error.SafeInvoke(this, message, exception));
        }

        #endregion

        #region ITimeGetter

        DateTime ITimeGetter.Now {get {return _connector.GetMarketTime();}}

        Action _timeCorrected;

        event Action ITimeGetter.TimeCorrected {
            add {_timeCorrected += value;}
            remove {_timeCorrected -= value;}
        }

        #endregion
    }

    /// <summary>
    /// Интерфейс, поддерживающий обновление данных робота RobotData.
    /// UpdateData() вызывается по таймеру на всех объектах IRobotDataUpdater чтобы пользовательский интерфейс отображал актуальную информацию.
    /// </summary>
    public interface IRobotDataUpdater {
        void UpdateData();
    }

    public interface IInitializable {
        void Init();
    }
}
