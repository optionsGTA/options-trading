using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MoreLinq;
using OptionBot.Config;
using OptionBot.robot;

namespace OptionBot.Xaml {
    /// <summary>
    /// Interaction logic for SeriesControl.xaml
    /// </summary>
    public partial class SeriesControl : UserControl {
        //readonly Logger _log = new Logger();

        public static readonly DependencyProperty SeriesListProperty = DependencyProperty.Register("SeriesList", typeof(IEnumerable<SeriesWrapper>), typeof(SeriesControl), new PropertyMetadata(null));
        public IEnumerable<SeriesWrapper> SeriesList {get {return (IEnumerable<SeriesWrapper>)GetValue(SeriesListProperty);} set {SetValue(SeriesListProperty, value);}}

        VMRobot VMRobot => (VMRobot)DataContext;
        RobotData RobotData => VMRobot.RobotData;
        ConfigProvider ConfigProvider => VMRobot.ConfigProvider;
        ICfgPairSeriesList SeriesConfig => ConfigProvider.Series;

        bool _initialized;

        public SeriesControl() {
            SeriesList = new SeriesWrapper[0];
            InitializeComponent();

            Loaded += (sender, args) => {
                if(_initialized)
                    return;

                _initialized = true;

                RobotData.AllOptionSeries.CollectionChanged += AllOptionSeriesOnCollectionChanged;
                AllOptionSeriesOnCollectionChanged(null, null);
            };
        }

        void AllOptionSeriesOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args) {
            var actualSeries = RobotData.AllOptionSeries.ToArray();
            var actualIds = actualSeries.Select(s => s.SeriesId).ToHashSet();
            var wrappers = SeriesList.ToList();
            var wrapperIds = wrappers.Select(w => w.Series.SeriesId).ToHashSet();

            var removedIds = wrapperIds.Except(actualIds).ToHashSet();
            var removedWrappers = wrappers.Where(w => removedIds.Contains(w.Series.SeriesId)).ToArray();

            foreach(var w in removedWrappers)
                if(wrappers.Remove(w))
                    w.Dispose();

            var addedIds = actualIds.Except(wrapperIds).ToArray();
            var addedSereis = addedIds.Select(sid => actualSeries.First(s => s.SeriesId == sid)).ToArray();
            
            addedSereis.ForEach(s => wrappers.Add(new SeriesWrapper(s)));

            SeriesList = new List<SeriesWrapper>(wrappers.OrderBy(w => w.Series.Future.Id).ThenBy(w => w.Series.ExpirationDate));
        }
    }

    public class SeriesWrapper : ViewModelBase {
        //static readonly Logger _log = new Logger();
        readonly RobotLogger.SeriesLogger _seriesLogger;
        public OptionSeriesInfo Series {get;}
        public ICfgPairSeries Config => Series.Config;
        public IConfigSeries Effective => Config.Effective;

        public SeriesWrapper(OptionSeriesInfo series) {
            Series = series;
            Config.CanUpdateConfig += CfgOnCanUpdateConfig;
            Config.UIEffectiveDifferent += ConfigOnUiEffectiveDifferent;
            Config.EffectiveConfigChanged += ConfigOnEffectiveConfigChanged;

            _seriesLogger = Series.Controller.RobotLogger.Series;
            _seriesLogger.LogSeries(this);
        }

        protected override void DisposeManaged() {
            Config.CanUpdateConfig -= CfgOnCanUpdateConfig;
            Config.UIEffectiveDifferent -= ConfigOnUiEffectiveDifferent;
            Config.EffectiveConfigChanged -= ConfigOnEffectiveConfigChanged;
            base.DisposeManaged();
        }

        void ConfigOnUiEffectiveDifferent(ICfgPairSeries pair) {
            Series.Controller.RobotData.Dispatcher.MyGuiAsync(() => {
                var ui = pair.UI;
                var ef = pair.Effective;
                ui.CurveHedging = ef.CurveHedging;
                ui.PreCalculationTrigger = ef.PreCalculationTrigger;
                ui.CalculateCurve = ef.CalculateCurve;
            }, true);
        }

        void ConfigOnEffectiveConfigChanged(ICfgPairSeries cfgPairSeries, string[] names) {
            OnPropertyChanged(() => Effective);
            _seriesLogger.LogSeries(this);
        }

        void CfgOnCanUpdateConfig(IConfigSeries cfgser, CanUpdateEventArgs args) {
            cfgser.VerifyConfig(args.Errors);

            if(args.Errors.Count > 0)
                return;

            if(args.ChangedOnly(nameof(cfgser.CurveHedging)) ||
               args.ChangedOnly(nameof(cfgser.CalculateCurve)) ||
               args.ChangedOnly(nameof(cfgser.PreCalculationTrigger))) {

                args.AllowInstantUpdate = true;
            }
        }
    }
}
