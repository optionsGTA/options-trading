using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using OptionBot.Config;
using OptionBot.robot;
using OptionBot.Xaml;

namespace OptionBot
{
    /// <summary>
    /// Основная View-Model, к которой привязывается пользовательский интерфейс.
    /// </summary>
    public partial class VMRobot : ViewModelBase {
        readonly Logger _log = new Logger("VMRobot");

        string _title;
        public string WindowTitle {get { return _title; } set { SetField(ref _title, value); }}

        readonly Controller _controller;
        readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
        readonly DispatcherTimer _timer;

        public Controller Controller => _controller;
        public RobotData RobotData => _controller.RobotData;

        public MainWindow MainWindow => MainWindow.Instance;
        public bool CanQuit => RobotData.IsRobotStopped;

        public ConfigProvider ConfigProvider => _controller.ConfigProvider;

        public IConfigGeneral CfgGeneral => ConfigProvider.General.Effective;

        public VMRobot() {
            _controller = new Controller(callback => RobotData.AddGuiOneTimeAction(callback));

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(333) };
            _timer.Tick += (sender, args) => UpdateData();
            _timer.Start();

            ConfigProvider.General.CanUpdateConfig += GeneralOnCanUpdateConfig;
            ConfigProvider.General.EffectiveConfigChanged += GeneralOnEffectiveConfigChanged;

            UpdateTitle();

            RobotData.NewSecurity += RobotDataOnNewSecurity;

            _controller.Heart.SetConditionToCheck("isConnected", () => RobotData.IsConnected);
            _controller.Heart.SetThreadToCheck("uiThread", action => _dispatcher.MyGuiAsync(action, true));
        }

        DateTime _lastSysinfoLogTime;
        static readonly TimeSpan _logSysInfoInterval = TimeSpan.FromSeconds(60);

        void UpdateData() {
            if(MainWindow.GlobalDispatcher == null)
                return;

            try {
                _controller.UpdateData();
            } catch(Exception e) {
                _log.Dbg.AddErrorLog("Update data exception: {0}", e);
            }

            var now = DateTime.UtcNow;
            if(now - _lastSysinfoLogTime < _logSysInfoInterval)
                return;

            _lastSysinfoLogTime = now;
            _log.Dbg.AddInfoLog("CPU: {0}, Free memory: {1}, Working set: {2}", PerformanceInfo.GetCurrentCpuUsage(), PerformanceInfo.GetAvailableRAM(), PerformanceInfo.GetWorkingSet());
        }

        protected override void DisposeManaged() {
            _timer.Stop();

            _controller.Dispose();

            base.DisposeManaged();
        }

        // обновление заголовка окна
        void UpdateTitle() {
            var verInfo = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion;
            var d = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
            var descr = d.Length==0 ? "" : ((AssemblyDescriptionAttribute)d[0]).Description;

            WindowTitle = $"{descr} v{verInfo}";
        }

        void RobotDataOnNewSecurity(SecurityInfo si) {
            try {
                if(ConfigProvider.UI.HasMarketDepth(si.Id))
                    MainWindow._controlMarketDepths.AddSecurity(si);
            } catch(Exception e) {
                _log.AddErrorLog($"Ошибка добавления сохраненного стакана. (conf,ui,si,control)=({ConfigProvider!=null},{ConfigProvider?.UI != null},{si!=null},{MainWindow._controlMarketDepths!=null}): {e}");
            }
        }
    }
}
