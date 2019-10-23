using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using ActiproSoftware.Windows.Themes;
using Ecng.Common;
using OptionBot.robot;
using OptionBot.Xaml;

namespace OptionBot {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        readonly Mutex _mutex;
        readonly string _filename;

        public App() {
            using(var p = Process.GetCurrentProcess()) {
                p.PriorityClass = ProcessPriorityClass.High;
                _filename = p.MainModule.FileName;
                _mutex = new Mutex(false, "OptionBot-" + _filename.Replace(Path.DirectorySeparatorChar, '|'));
            }

            AppDomain.CurrentDomain.ProcessExit += (sender, args) => {
                _mutex.Dispose();
                MMTimer.DisposeMMTimer(true);
            };
        }

        void Application_Startup(object sender, StartupEventArgs startup) {
            if(!CheckRunningInstance(startup))
                return;

            ThemeManager.BeginUpdate();
            try {
                ThemesMetroThemeCatalogRegistrar.Register();
                ThemeManager.AreNativeThemesEnabled = true;
                ThemeManager.RegisterThemeCatalog(new TintedThemeCatalog("CustomTheme", ThemeName.MetroLight.ToString(), Colors.SteelBlue));
                ThemeManager.CurrentTheme = "CustomTheme";
            } finally {
                ThemeManager.EndUpdate();
            }

            var path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if(!string.IsNullOrEmpty(path)) System.IO.Directory.SetCurrentDirectory(path);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

#if !DEBUG
            var login = new LoginWindow();
            login.ShowDialog();

            if(!login.DialogResult.HasValue || !login.DialogResult.Value)
                Shutdown();
#endif
        }

        bool CheckRunningInstance(StartupEventArgs startup) {
            const int waitSec = 3;
            Action<int> showErrShutdown = num => {
                MessageBox.Show(
                    "{0}: Экземпляр робота из данной папки уже запущен ({1})\nЧтобы запустить еще один экземпляр, скопируйте все файлы робота в новую папку.".Put(num, _filename),
                    "Робот", MessageBoxButton.OK, MessageBoxImage.Error);
                startup.GetType().GetProperty("PerformDefaultAction", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                    .Do(p => p.SetValue(startup, false, BindingFlags.SetProperty, null, null, null));
                Shutdown(1);
            };

            try {
                if(!_mutex.WaitOne(TimeSpan.FromSeconds(waitSec))) {
                    showErrShutdown(1);
                    return false;
                }
            } catch(AbandonedMutexException) {
                try {
                    if(!_mutex.WaitOne(TimeSpan.FromSeconds(waitSec))) {
                        showErrShutdown(2);
                        return false;
                    }
                } catch(AbandonedMutexException) {
                    showErrShutdown(3);
                    return false;
                }
            }

            return true;
        }
    }
}
