using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OptionBot.Config;
using OptionBot.Xaml;

namespace OptionBot {
    // часть VMRobot, ответственная за работу с конфигурацией (загрузка, сохранение, начальная обработка изменения параметров пользователем)
    public partial class VMRobot {
        IConfigPair _selectedConfigObject;
        public IConfigPair SelectedConfigObject {get {return _selectedConfigObject;} set {SetField(ref _selectedConfigObject, value);}}

        bool _inGroupEditMode;
        public bool InGroupEditMode {get {return _inGroupEditMode;} set {SetField(ref _inGroupEditMode, value); CommandManager.InvalidateRequerySuggested();}}

        void GeneralOnCanUpdateConfig(IConfigGeneral configGeneral, CanUpdateEventArgs args) {
            if(args.Names.Contains(Util.PropertyName(() => CfgGeneral.Portfolio)) && !RobotData.IsRobotStopped)
                args.Errors.Add("Нельзя поменять портфель во время работы робота");
            CommandManager.InvalidateRequerySuggested();
        }

        void GeneralOnEffectiveConfigChanged(ICfgPairGeneral cfgPairGeneral, string[] strings) {
            if(strings.Contains(Util.PropertyName(() => CfgGeneral.Portfolio))) {
                _log.AddInfoLog("portfolio changed: {0}", CfgGeneral.Portfolio);

                RobotData.SetActivePortfolio(CfgGeneral.Portfolio);
            }
            CommandManager.InvalidateRequerySuggested();
        }

        #region commands

        ICommand _cmdApplyCfgChange, _cmdUndoCfgChange;

        public ICommand CommandApplyConfigChange {get {
            return _cmdApplyCfgChange ?? (_cmdApplyCfgChange = new RelayCommand(o => OnApplyCfgChange((ObjectParamsControl)o), 
                                                                                o => InGroupEditMode || 
                                                                                      !(((ObjectParamsControl)o).SelectedObject).Return(so => so.IsEffectiveConfigUpToDate, true)));
        }}

        public ICommand CommandUndoConfigChange {get {
            return _cmdUndoCfgChange ?? (_cmdUndoCfgChange = new RelayCommand(o => OnUndoCfgChange((IConfigPair)o), o => !((IConfigPair)o).Return(so => so.IsEffectiveConfigUpToDate, true)));
        }}

        #region implementation

        // пользователь поменял параметры и нажал кнопку "применить"
        void OnApplyCfgChange(ObjectParamsControl ctl) {
            var pair = ctl.SelectedObject;

            if(InGroupEditMode) {
                var names = ctl.GetGroupChangeNames();
                if(!names.Any())
                    return;

                if(pair is ICfgPairStrategy) {
                    var cfgStraChanges = ((ICfgPairStrategy)pair).UI;
                    MainWindow.Instance._wndStrategies.Activate();
                    MainWindow.Instance._controlStrategies.TryToApplyToSelected(cfgStraChanges, names);
                } else if(pair is ICfgPairVP) {
                    var cfgVPChanges = ((ICfgPairVP)pair).UI;
                    MainWindow.Instance._wndValParams.Activate();
                    MainWindow.Instance._controlValuationParams.TryToApplyToSelected(cfgVPChanges, names);
                }
                return;
            }

            if(pair.IsEffectiveConfigUpToDate) {
                _log.Dbg.AddWarningLog("OnApplyCfgChange: settings are already equal");
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            var errors = new List<string>();
            pair.TryToApplyChanges(errors);

            if(errors.Any()) {
                ShowError("Ошибки в конфигурации:\n" + string.Join("\n", errors.ToArray()));
                return;
            }

            if(!pair.IsEffectiveConfigUpToDate)
                _log.Dbg.AddWarningLog("OnApplyCfgChange({0}): settings are NOT equal after copy", pair.ConfigName);
        }

        // при запущенном роботе пользователь поменял параметры и нажал кнопку "отменить редактирование"
        void OnUndoCfgChange(IConfigPair pair) {
            if(pair.IsEffectiveConfigUpToDate) {
                _log.Dbg.AddWarningLog("OnUndoCfgChange: settings are equal");
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            pair.UndoUIChanges();

            CommandManager.InvalidateRequerySuggested();

            if(!pair.IsEffectiveConfigUpToDate)
                _log.Dbg.AddWarningLog("OnUndoCfgChange: settings are NOT equal after copy");
        }

        public void ShowError(string msg, string caption = "Ошибка") {
            _log.Dbg.AddErrorLog("showerror: {0}", msg);
            MessageBox.Show(msg, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion
        #endregion
    }
}
