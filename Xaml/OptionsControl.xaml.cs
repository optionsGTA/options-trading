using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ActiproSoftware.Windows;
using Ecng.Common;
using MoreLinq;
using OptionBot.Config;
using OptionBot.robot;
using StockSharp.Messages;

namespace OptionBot.Xaml {
    /// <summary>
    /// Interaction logic for OptionsControl.xaml
    /// </summary>
    public partial class OptionsControl : UserControl {
        readonly Logger _log = new Logger();

        public static readonly DependencyProperty FilteredOptionsProperty = DependencyProperty.Register("FilteredOptions", typeof(ObservableCollection<OptionInfo>), typeof(OptionsControl), new PropertyMetadata(default(ObservableCollection<OptionInfo>)));
        public ObservableCollection<OptionInfo> FilteredOptions {get {return (ObservableCollection<OptionInfo>)GetValue(FilteredOptionsProperty);} set {SetValue(FilteredOptionsProperty, value);}}

        readonly DeferredUIAction _filterAction;

        bool _handlingIsActiveClick, _processingIsActiveSelection;
        readonly DeferredUIAction _cancelHandlingIsActiveAction;

        bool _initialized;

        VMRobot VMRobot {get {return (VMRobot)DataContext;}}
        RobotData RobotData {get {return VMRobot.RobotData;}}
        ConfigProvider ConfigProvider {get {return VMRobot.ConfigProvider;}}

        public OptionsControl() {
            _filterAction = new DeferredUIAction(Dispatcher, ApplyFilter, TimeSpan.FromMilliseconds(250));
            FilteredOptions = new ObservableCollection<OptionInfo>();

            InitializeComponent();

            _cancelHandlingIsActiveAction = new DeferredUIAction(Dispatcher, () => _handlingIsActiveClick = false, TimeSpan.FromSeconds(2));

            OptionInfo.IsActiveChangedByUser += OptionInfoOnIsActiveChangedByUser;
        }

        public bool TrySelectSecurity(SecurityInfo secInfo) {
            if(secInfo.Type != SecurityTypes.Option) { _log.Dbg.AddErrorLog("wrong type: {0} - {1}", secInfo.Id, secInfo.Type); return false; }

            if(!FilteredOptions.Contains(secInfo)) {
                _log.AddWarningLog("Инструмент {0} не выбран текущим фильтром.", secInfo.Code);
                return false;
            }

            _datagrid.SelectedItem = secInfo;
            _datagrid.ScrollIntoView(secInfo);
            return true;
        }

        void _tbFilter_OnKeyDown(object sender, KeyEventArgs e) {
            if(e.Key == Key.Escape)
                _tbFilter.Text = string.Empty;
        }

        void _tbFilter_OnTextChanged(object sender, StringPropertyChangedRoutedEventArgs e) {
            _filterAction.DeferredExecute();
        }

        void AllOptionsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs) {
            _filterAction.DeferredExecute();
        }

        void AllOptionSeriesOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            var series = _comboSeries.Items.Cast<ComboBoxItem>().Where(i => i.Tag != null)
                                     .Select(i => i.Tag).Cast<OptionSeriesInfo>().Where(os => os != null).ToArray();
            var seriesIds = series.Select(os => os.SeriesId.Id).ToHashSet();
            var allSeries = RobotData.AllOptionSeries.ToArray();
            var allSeriesIds = allSeries.Select(os => os.SeriesId.Id).ToHashSet();

            allSeriesIds.ExceptWith(seriesIds);

            if(allSeriesIds.Count == 0)
                return;

            allSeries = allSeries.Where(s => allSeriesIds.Contains(s.SeriesId.Id)).ToArray();

            allSeries.ForEach(ser => _comboSeries.Items.Add(new ComboBoxItem {Content = ser.SeriesId.StrFutSerCodeShortDate, Tag = ser}));

            _filterAction.DeferredExecute();
        }

        void _datagrid_OnLoaded(object sender, RoutedEventArgs e) {
            if(_initialized)
                return;

            _initialized = true;
            RobotData.AllOptions.CollectionChanged += AllOptionsOnCollectionChanged;
            RobotData.AllOptionSeries.CollectionChanged += AllOptionSeriesOnCollectionChanged;
            ConfigProvider.ConfigSecuritySelection.EffectiveConfigChanged += (cfg, names) => ApplyFilter();
        }

        void SelectedSeries_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            ApplyFilter();
        }

        void SelectedOptTypes_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            ApplyFilter();
        }

        void ApplyFilter() {
            var filter = _tbFilter.Text.Trim();

            //var selected = _datagrid.SelectedItem as OptionInfo;
            if(VMRobot == null || _comboOptType == null || _comboSeries == null || RobotData == null)
                return;

            var optTypeTag = ((ComboBoxItem)_comboOptType.SelectedItem).Tag as string;

            Func<OptionInfo, bool> checkOptionAvailability;
            if(optTypeTag == "calc")
                checkOptionAvailability = o => o.Strike.IsStrikeCalculated;
            else if(optTypeTag == "calc_and_active")
                checkOptionAvailability = o => o.IsSelectedForTrading;
            else
                checkOptionAvailability = o => true;

            var selectedSeries = ((ComboBoxItem)_comboSeries.SelectedItem).With(i => i.Tag as OptionSeriesInfo);

            var options = RobotData.AllOptions.Where(o =>
                (filter.IsEmpty() || Contains(filter, o.Code, o.Future.Code, o.Series.SeriesId.ToString(), o.Strike.Strike.ToString(CultureInfo.InvariantCulture), o.OptionType.ToString())) &&
                (selectedSeries == null || selectedSeries.SeriesId == o.Series.SeriesId) &&
                checkOptionAvailability(o)).ToHashSet();

            if(FilteredOptions.Count == options.Count && FilteredOptions.All(options.Contains))
                return;

            FilteredOptions = new ObservableCollection<OptionInfo>(options.OrderBy(o => o.Future.Code).ThenBy(o => o.Series.SeriesId.StrSerCodeShortDate).ThenBy(o => o.Strike.Strike).ThenBy(o => o.OptionType));

//            if(selected != null && FilteredOptions.Contains(selected)) {
//                _datagrid.SelectedItem = selected;
//                _datagrid.ScrollIntoView(selected);
//            }
        }

        static bool Contains(string what, params string[] whereArray) {
            what = what.ToLower(CultureInfo.CurrentCulture);
            return whereArray.Any(s => s.ToLower(CultureInfo.CurrentCulture).Contains(what));
        }

        void OptionInfoOnIsActiveChangedByUser(OptionInfo option) {
            //_log.AddInfoLog($"isactive({option.Id}) = {option.IsOptionActive}, handling={_handlingIsActiveClick}");

            if(!_handlingIsActiveClick || _processingIsActiveSelection)
                return;

            try {
                _cancelHandlingIsActiveAction.Cancel();
                _processingIsActiveSelection = true;
                var isActive = option.IsOptionActive;
                var selected = _datagrid.SelectedItems.Cast<OptionInfo>().Where(o => o != null && o != option).ToArray();
                selected.ForEach(o => o.ConfigProvider.ConfigSecuritySelection.UI.SetActive(o, isActive));
                //_log.AddInfoLog($"selecting({option.IsOptionActive}) following options: {selected.Select(o => o.Code).Join(",")}");
            } finally {
                _processingIsActiveSelection = false;
            }
        }

        void IsActiveCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            //_log.AddInfoLog("ldown");
            _handlingIsActiveClick = true;
            _cancelHandlingIsActiveAction.DeferredExecute();
        }
    }
}
