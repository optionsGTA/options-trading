using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Ecng.Common;
using OptionBot.Config;
using OptionBot.robot;
using OptionBot.Xaml;
using StockSharp.Messages;

namespace OptionBot {
    // часть VMRobot, отвечающая за обработку команд пользователя, не относящихся к конфигурации
    public partial class VMRobot {
        ICommand _cmdConnectDisconnect, _cmdStopAllStrategies, _cmdCancelAllOrders, _cmdCloseAllPositions;
        ICommand _cmdAddFuture, _cmdDeleteFuture;
        ICommand _cmdShowParameters, _cmdShowMarketDepth, _cmdHideMarketDepth, _cmdSyncDepthToSecurity;
        ICommand _cmdResetOptions, _cmdResetCurveParams;
        ICommand _cmdSaveDefaultParams;
        ICommand _cmdSortMarketDepths, _cmdSortMMInfos;
        ICommand _cmdForceRecalcATM;
        ICommand _cmdSelectVPForOption;

        // plaza подключение/отключение
        public ICommand CommandConnectDisconnect {get {
            return _cmdConnectDisconnect ?? (_cmdConnectDisconnect = new RelayCommand(o => OnCmdConnectDisconnect(), 
                        o => RobotData.ConnectionState == ConnectionState.Disconnected || RobotData.IsRobotStopped));
                        // условие в этой и других командах определяет, когда эта команда активна (разрешена к исполнению)
        }}

        public ICommand CommandStopAllStrategies {get {
            return _cmdStopAllStrategies ?? (_cmdStopAllStrategies = new RelayCommand(o => OnCmdStopAllStrategies(), o => true));
        }}

        // запуск/остановка робота
//        public ICommand CommandStartStop {get {
//            return _cmdStartStop ?? (_cmdStartStop = new RelayCommand(o => OnCmdStartStop(), o => 
//                !Settings.With(s => s.Portfolio).IsEmpty() &&
//                (RobotData.RobotState != RobotState.Inactive || (RobotData.ConnectionState != ConnectionState.Disconnected && RobotData.ConnectionState != ConnectionState.Disconnecting))));
//        }}

        public ICommand CommandAddFuture {get {
            return _cmdAddFuture ?? (_cmdAddFuture = new RelayCommand(o => OnCmdAddFuture((string)o), 
                        o => RobotData.ConnectionState == ConnectionState.Disconnected && RobotData.IsRobotStopped));
        }}

        public ICommand CommandDeleteFuture {get {
            return _cmdDeleteFuture ?? (_cmdDeleteFuture = new RelayCommand(o => OnCmdDeleteFuture(o as FuturesInfo), 
                        o => RobotData.ConnectionState == ConnectionState.Disconnected && RobotData.IsRobotStopped));
        }}

        public ICommand CommandShowParameters {get {
            return _cmdShowParameters ?? (_cmdShowParameters = new RelayCommand(OnCmdShowParameters));
        }}

        public ICommand CommandSelectVPForOption {get {
            return _cmdSelectVPForOption ?? (_cmdSelectVPForOption = new RelayCommand(o => OnCmdSelectVPForOption(o as OptionInfo)));
        }}

        public ICommand CommandShowMarketDepth {get {
            return _cmdShowMarketDepth ?? (_cmdShowMarketDepth = new RelayCommand(o => OnCmdShowMarketDepth(o as ICollection)));
        }}

        public ICommand CommandForceRecalcATM {get {
            return _cmdForceRecalcATM ?? (_cmdForceRecalcATM = new RelayCommand(o => OnCmdForceRecalcATM(o as ICollection)));
        }}

        public ICommand CommandResetOptions {get {
            return _cmdResetOptions ?? (_cmdResetOptions = new RelayCommand(o => OnCmdResetOptions(o as ICollection)));
        }}

        public ICommand CommandResetCurveParams {get {
            return _cmdResetCurveParams ?? (_cmdResetCurveParams = new RelayCommand(o => OnCmdResetCurveParams(o as SeriesWrapper)));
        }}

        public ICommand CommandHideMarketDepth {get {
            return _cmdHideMarketDepth ?? (_cmdHideMarketDepth = new RelayCommand(o => OnCmdHideMarketDepth(o as SecurityInfo)));
        }}

        public ICommand CommandSyncDepthToSecurity {get {
            return _cmdSyncDepthToSecurity ?? (_cmdSyncDepthToSecurity = new RelayCommand(o => OnCmdSyncDepthToSecurity(o as SecurityInfo)));
        }}

        public ICommand CommandSortMarketDepths {get {
            return _cmdSortMarketDepths ?? (_cmdSortMarketDepths = new RelayCommand(o => OnCmdSortMarketDepths()));
        }}

        public ICommand CommandSortMMInfos {get {
            return _cmdSortMMInfos ?? (_cmdSortMMInfos = new RelayCommand(o => OnCmdSortMMInfos()));
        }}

        // отмена заявок
        public ICommand CommandCancelAllOrders {get {
            return _cmdCancelAllOrders ?? (_cmdCancelAllOrders = new RelayCommand(o => OnCmdCancelOrders(), o => 
                                           RobotData.ConnectionState == ConnectionState.Connected && RobotData.IsRobotStopped));
        }}

        // закрытие позиций
        public ICommand CommandCloseAllPositions {get {
            return _cmdCloseAllPositions ?? (_cmdCloseAllPositions = new RelayCommand(o => OnCmdClosePositions(), o => 
                                             RobotData.ConnectionState == ConnectionState.Connected && RobotData.IsRobotStopped));
        }}

        public ICommand CommandSaveDefaultParams {get {
            return _cmdSaveDefaultParams ?? (_cmdSaveDefaultParams = new RelayCommand(OnCmdSaveDefaultParams));
        }}

        // пользователь нажал кнопку подключения/отключения
        void OnCmdConnectDisconnect() {
            if(RobotData.ConnectionState == ConnectionState.Disconnected)
                _controller.Connect();
            else
                _controller.Disconnect();
        }

        static readonly Regex _futCodeRegex = new Regex(@"^[a-z0-9]{1,7}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        void OnCmdAddFuture(string code) {
            code = code.Trim();
            _log.AddInfoLog("AddFuture({0})", code);

            if(!RobotData.IsDisconnected) {
                _log.AddErrorLog("Фьючерсы можно добавлять только в отключенном состоянии.");
                return;
            }

            if(!_futCodeRegex.IsMatch(code) || code.Length < 2) {
                _log.AddErrorLog("Код должен быть строкой до 7 символов, состоящей из англ букв и цифр.");
                return;
            }

            if(RobotData.AllFutures.Any(f => f.Code.CompareIgnoreCase(code))) {
                _log.AddErrorLog("Фьючерс с кодом {0} уже есть в списке.", code);
                return;
            }

            try {
                RobotData.GetFuture(code);
            } catch(Exception e) {
                _log.AddErrorLog("Ошибка добавления фьючерса: {0}", e);
            }
        }

        void OnCmdDeleteFuture(FuturesInfo fut) {
            if(fut == null) { _log.AddErrorLog("Не выбран фьючерс для удаления"); return; }
            _log.AddInfoLog("DeleteFuture({0})", fut.Code);

            if(!RobotData.IsDisconnected) {
                _log.AddErrorLog("Фьючерсы можно добавлять только в отключенном состоянии.");
                return;
            }

            if(MessageBox.Show(MainWindow, "Операция также удалит все связанные с {0} опционы и стратегии. Продолжить?".Put(fut.Code), "Удаление {0}".Put(fut.Code), 
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
                return;
            }

            RobotData.RemoveFuture(fut);
        }

        void OnCmdShowParameters(object obj) {
            if(obj is FuturesInfo) {
                SelectedConfigObject = (obj as FuturesInfo).Config;
                MainWindow.ShowObjectParams();
            } else if(obj is VPWrapper) {
                SelectedConfigObject = ((VPWrapper)obj).Config;
                MainWindow.ShowObjectParams();
            } else if(obj is VMStrategy) {
                SelectedConfigObject = ((VMStrategy)obj).Config;
                MainWindow.ShowObjectParams();
            } else if(obj is SeriesWrapper) {
                SelectedConfigObject = ((SeriesWrapper)obj).Config;
                MainWindow.ShowObjectParams();
            } else if(obj is IConfigPair) {
                SelectedConfigObject = (IConfigPair)obj;
                MainWindow.ShowObjectParams();
            }
        }

        void OnCmdSaveDefaultParams(object obj) {
            var errors = new List<string>();

            if(obj is FuturesInfo) {
                var toSavePair = ((FuturesInfo)obj).Config;

                var defaultPair = ConfigProvider.DefaultFuture;
                defaultPair.UI.CopyFrom((ConfigFuture)toSavePair.Effective);
                defaultPair.TryToApplyChanges(errors);
                if(errors.Any()) {
                    _log.AddErrorLog("Невозможно сохранить параметры фьючерса по умолчанию. Ошибки: {0}", string.Join(",", errors));
                    defaultPair.UndoUIChanges();
                } else {
                    _log.AddInfoLog("Сохранены параметры по фьючерса умолчанию");
                }
            } else if(obj is VPWrapper) {
                var toSavePair = ((VPWrapper)obj).Config;

                var defaultPair = ConfigProvider.DefaultValuationParams;
                defaultPair.UI.CopyFrom((ConfigValuationParameters)toSavePair.Effective);
                defaultPair.TryToApplyChanges(errors);
                if(errors.Any()) {
                    _log.AddErrorLog("Невозможно сохранить параметры по умолчанию. Ошибки: {0}", string.Join(",", errors));
                    defaultPair.UndoUIChanges();
                } else {
                    _log.AddInfoLog("Сохранены параметры по умолчанию valuation_params");
                }
            } else if(obj is VMStrategy) {
                var toSavePair = ((VMStrategy)obj).Config;

                var stype = toSavePair.Effective.StrategyType;
                var defaultPair = ConfigProvider.DefaultConfigByStrategyType(stype);

                defaultPair.UI.CopyFrom((ConfigStrategy)toSavePair.Effective);
                defaultPair.TryToApplyChanges(errors);
                if(errors.Any()) {
                    _log.AddErrorLog("Невозможно сохранить параметры по умолчанию стратегии {0}. Ошибки: {1}", stype, string.Join(",", errors));
                    defaultPair.UndoUIChanges();
                } else {
                    _log.AddInfoLog("Сохранены параметры по умолчанию стратегии {0}", stype);
                }
            }
        }

        void OnCmdShowMarketDepth(ICollection items) {
            if(items == null || items.Count == 0) return;

            foreach(var secInfo in items.Cast<SecurityInfo>().Where(si => si != null))
                MainWindow._controlMarketDepths.AddSecurity(secInfo);

            MainWindow._wndMarketDepths.Activate();
            MainWindow._controlMarketDepths.TrySelectSecurity(items.Cast<SecurityInfo>().First());
        }

        void OnCmdForceRecalcATM(ICollection items) {
            if(items == null || items.Count == 0) return;

            foreach(var fut in items.Cast<FuturesInfo>().Where(fi => fi != null))
                fut.ForceRecalcATM();
        }

        void OnCmdResetOptions(ICollection items) {
            if(items == null || items.Count == 0) return;

            _log.AddInfoLog($"Сброс модели у {items.Count} опционов...");

            foreach(var option in items.Cast<OptionInfo>().Where(oi => oi != null))
                option.ResetModel("user cmd");

            Controller.Robot.ForceRecalculate();
        }

        void OnCmdResetCurveParams(SeriesWrapper sw) {
            var series = sw?.Series;
            if(series == null) return;

            _log.AddInfoLog($"Сброс параметров кв у серии '{series.SeriesId.StrFutDate}'...");

            Controller.Robot.CurveManager.ManualResetSeries(series);
        }

        void OnCmdHideMarketDepth(SecurityInfo secInfo) {
            if(secInfo == null) return;

            MainWindow._controlMarketDepths.RemoveSecurity(secInfo);
        }

        void OnCmdSelectVPForOption(OptionInfo option) {
            var cfgVP = option.With(o => o.CfgValuationParamsPair);
            if(cfgVP == null)
                return;

            MainWindow._controlValuationParams.SetActive(cfgVP);
            SelectedConfigObject = cfgVP;
            MainWindow.ShowObjectParams();
        }

        void OnCmdSyncDepthToSecurity(SecurityInfo secInfo) {
            if(secInfo == null)
                return;

            switch(secInfo.Type) {
                case SecurityTypes.Future:
                    if(MainWindow._controlFutures.TrySelectSecurity(secInfo))
                        MainWindow._wndFutures.Activate();
                    break;
                case SecurityTypes.Option:
                    if(MainWindow._controlOptions.TrySelectSecurity(secInfo))
                        MainWindow._wndOptions.Activate();
                    break;
            }
        }

        void OnCmdSortMarketDepths() {
            MainWindow._controlMarketDepths.SortMarketDepths();
        }

        void OnCmdSortMMInfos() {
            MainWindow._controlMMInfos.SortMMInfos();
        }

        void OnCmdStopAllStrategies() {
            _log.AddInfoLog("Остановка всех стратегий. {0} активных.", RobotData.AllStrategies.Count(vms => vms.IsActive));

            _controller.Robot.StopEverything(true);
        }

        // пользователь нажал кнопку отмены ордеров
        void OnCmdCancelOrders() {
            if(!RobotData.IsRobotStopped) {
                _log.AddErrorLog("Нельзя отменять заявки пока есть запущенные стратегии");
                return;
            }

            _controller.CancelOrders();
        }

        // пользователь нажал кнопку закрытия позиций
        void OnCmdClosePositions() {
            if(!RobotData.IsRobotStopped) {
                _log.AddErrorLog("Нельзя закрывать позиции пока есть запущенные стратегии");
                return;
            }

            _controller.ClosePositions();
        }
    }
}
