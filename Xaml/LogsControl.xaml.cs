using System.Windows.Controls;
using StockSharp.Xaml;

namespace OptionBot.Xaml {
    /// <summary>
    /// Interaction logic for LogsControl.xaml
    /// </summary>
    public partial class LogsControl : UserControl {
        public LogsControl() {
            InitializeComponent();
        }

        public LogControl UILogger {get {return _logControl;}}
    }
}
