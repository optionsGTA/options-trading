using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using ActiproSoftware.Windows.Controls.Docking.Serialization;
using ActiproSoftware.Windows.Controls.Ribbon;
using Ecng.Interop;
using OptionBot.Config;
using OptionBot.robot;
using NLogLogger;

namespace OptionBot.Xaml {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : RibbonWindow {
        public static Dispatcher GlobalDispatcher {get; private set;}

        static readonly LoggerRoot _log = new LoggerRoot("main");
        public VMRobot VMRobot {get {return DataContext as VMRobot;}}

        static MainWindow _instance;
        public static MainWindow Instance {get {return _instance;}}

        bool _initialized;

#if DEBUG
        const string _buildConfiguration = "debug";
#else
        const string _buildConfiguration = "release";
#endif

        public MainWindow() {
            _instance = this;

            _log.RegisterListener(new NLogListener());

            GlobalDispatcher = Dispatcher;
            Dispatcher.UnhandledException += DispatcherOnUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;

            var name = Assembly.GetExecutingAssembly().GetName();
            var exefname = Process.GetCurrentProcess().MainModule.FileName;
            _log.Dbg.AddInfoLog("{0} v{1} ({2}), {3}, sha1={4}", name.Name, name.Version, _buildConfiguration, exefname, Util.GetFileSha1(exefname));
            _log.Dbg.AddInfoLog("OS: {0} ({1}), Total RAM = {2}", Environment.OSVersion, Environment.Is64BitOperatingSystem ? "64bit" : "32bit", PerformanceInfo.GetTotalRam());
            _log.Dbg.AddInfoLog("Process: {0}bit (Is64BitProcess={1})", IntPtr.Size * 8, Environment.Is64BitProcess);
            _log.Dbg.AddInfoLog("Processors: physical={0}, total cores={1}, logical={2}", PerformanceInfo.PhysicalProcessorCount, PerformanceInfo.ProcessorCoreCount, PerformanceInfo.LogicalProcessorCount);
            _log.Dbg.AddInfoLog("GC Mode: {0}, latencyMode={1}", GCSettings.IsServerGC ? "server" : "workstation", GCSettings.LatencyMode);

            var enc = Encoding.Default;
            _log.Dbg.AddInfoLog("Default encoding is {0} ({1})", enc.CodePage, enc.EncodingName);

            MMTimer.InitializeMMTimer();

            InitializeCulture();
            InitializeComponent();

            _log.RegisterListener(_logsControl.UILogger, LogTarget.UI);

            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        /// <summary>Обработчик события загрузки основного окна программы.</summary>
        void OnLoaded(object sender, RoutedEventArgs routedEventArgs) {
            if(_initialized)
                return;

            if(Properties.Settings.Default.WindowMaximized) WindowState = WindowState.Maximized;

            Application.Current.ShutdownMode = ShutdownMode.OnLastWindowClose;
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

            if(!LoadLayout()) {
                _settingsContainer.AutoHide(Dock.Left);
                _mdepthContainer.AutoHide(Dock.Right);
                _wndLogs.Activate();
            }

            _initialized = true;

            if(!_wndTradingPeriods.IsVisible)
                _wndTradingPeriods.Activate();

            if(!_wndVolumes.IsVisible)
                _wndVolumes.Activate();

            InitMenu();
        }

        /// <summary>Обработчик события закрытия основного окна программы.</summary>
        void OnClosing(object sender, CancelEventArgs cancelEventArgs) {
            if(!VMRobot.CanQuit) {
                if(MessageBox.Show(this, "Робот не остановлен. Закрыть приложение?", "Выход", MessageBoxButton.YesNo, MessageBoxImage.Hand) == MessageBoxResult.No) {
                    cancelEventArgs.Cancel = true;
                    return;
                }
            }

            _log.Dbg.AddInfoLog("***** Disposing VMRobot *****");
            VMRobot.Dispose();

            var s = Properties.Settings.Default;
            s.WindowMaximized = WindowState == WindowState.Maximized;
            if(s.WindowMaximized) {
                s.WindowLeft = RestoreBounds.Left;
                s.WindowTop = RestoreBounds.Top;
                s.WindowWidth = RestoreBounds.Width;
                s.WindowHeight = RestoreBounds.Height;
            }
            Properties.Settings.Default.Save();

            if(_initialized)
                SaveLayout();
        }

        public void ShowObjectParams() {
            _wndObjectParams.Activate();
        }

        void DispatcherOnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args) {
            _log.AddErrorLog("Dispatcher exception: {0}", args.Exception);
        }

        void TaskSchedulerOnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args) {
            var count = 0;
            args.Exception.Handle(e => {
                _log.Dbg.AddErrorLog("Task exception({0}/{1}): {2}", ++count, args.Exception.InnerExceptions.Count, e);
                return true;
            });
        }

        void InitializeCulture() {
            Thread.CurrentThread.CurrentCulture = Util.RuCulture;
            Thread.CurrentThread.CurrentUICulture = Util.RuCulture;

            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), 
                            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        }

        static readonly string LayoutPath = ConfigProvider.SettingsDirectory + Path.DirectorySeparatorChar + "layout.xml";

        void SaveLayout() {
            var layoutSerializer = new DockSiteLayoutSerializer();
            var str = layoutSerializer.SaveToString(_dockSite);
            (ConfigProvider.SettingsDirectory + Path.DirectorySeparatorChar).CreateDirIfNotExists();
            File.WriteAllText(LayoutPath, str);
        }

        bool LoadLayout() {
            if(!File.Exists(LayoutPath))
                return false;

            var layout = File.ReadAllText(LayoutPath);
            var layoutSerializer = new DockSiteLayoutSerializer();
            layoutSerializer.LoadFromString(layout, _dockSite);

            return true;
        }

        void MenuItemHeaders_OnClick(object sender, RoutedEventArgs e) {
            VMRobot.ConfigProvider.UI.ShowWindowHeaders = !VMRobot.ConfigProvider.UI.ShowWindowHeaders;
            _dockSite.ToolWindowsHaveTitleBars = VMRobot.ConfigProvider.UI.ShowWindowHeaders;
            UpdateMenu();
        }

        void MenuItemToolbars_OnClick(object sender, RoutedEventArgs e) {
            VMRobot.ConfigProvider.UI.ShowToolbars = !VMRobot.ConfigProvider.UI.ShowToolbars;
            UpdateMenu();
        }

        void InitMenu() {
            _dockSite.ToolWindowsHaveTitleBars = VMRobot.ConfigProvider.UI.ShowWindowHeaders;
            UpdateMenu();
        }

        void UpdateMenu() {
            _menuItemHeaders.Header = VMRobot.ConfigProvider.UI.ShowWindowHeaders ? "Скрыть заголовки окон" : "Показать заголовки окон";
            _menuItemToolbars.Header = VMRobot.ConfigProvider.UI.ShowToolbars ? "Скрыть панели инструментов" : "Показать панели инструментов";
        }
    }
}
