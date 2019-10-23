using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using MoreLinq;
using OptionBot.Xaml;

namespace OptionBot.Config {
    [Serializable]
    [DataContract]
    public class ConfigUI : ViewModelBase, ConfigProvider.ISaveableConfiguration {
        static readonly Logger _log = new Logger();

        [DataMember] bool _showWindowHeaders = true;
        [DataMember] bool _showToolbars = true;
        [DataMember] Dictionary<string, DataGridSettings> _dataGrids = new Dictionary<string, DataGridSettings>();
        [DataMember] HashSet<string> _marketDepths = new HashSet<string>();

        public bool ShowWindowHeaders {get {return _showWindowHeaders;} set {SetField(ref _showWindowHeaders, value);}}
        public bool ShowToolbars {get {return _showToolbars;} set {SetField(ref _showToolbars, value);}}

        public ConfigUI() {
            PropertyChanged += (sender, args) => ConfigurationChanged?.Invoke(this);
        }

        public void AddDepths(IEnumerable<string> secIds) {
            var added = secIds.Aggregate(false, (current, id) => current | _marketDepths.Add(id));

            if(added)
                RaisePropertyChanged(nameof(_marketDepths));
        }

        public void RemoveDepth(string secId) {
            _marketDepths.Remove(secId);
        }

        public bool HasMarketDepth(string secId) {
            return _marketDepths.Contains(secId);
        }

        public bool HasDataGridSettingsFor(string gridName) {
            return _dataGrids.ContainsKey(gridName);
        }

        [field:NonSerialized] public event Action<ConfigProvider.ISaveableConfiguration> ConfigurationChanged;

        public DataGridSettings this[string gridName] {
            get {
                DataGridSettings gs;
                if(!_dataGrids.TryGetValue(gridName, out gs)) {
                    _dataGrids.Add(gridName, gs = new DataGridSettings(gridName));
                    gs.PropertyChanged += DataGridSettingsOnPropertyChanged;
                }

                return gs;
            }
        }

        void DataGridSettingsOnPropertyChanged(object sender, PropertyChangedEventArgs args) {
            RaisePropertyChanged(args.PropertyName);
        }

        public void SaveEffectiveConfig() {
            _log.Dbg.AddInfoLog("Saving UI config...");
            this.SaveToXml(ConfigProvider.FName(ConfigProvider.FilenameUISettings));
        }

        protected override void OnDeserialized() {
            base.OnDeserialized();

            if(_marketDepths == null)
                _marketDepths = new HashSet<string>();

            if(_dataGrids == null)
                _dataGrids = new Dictionary<string, DataGridSettings>();

            PropertyChanged += (sender, args) => ConfigurationChanged?.Invoke(this);

            _dataGrids.Values.ForEach(d => {
                d.PropertyChanged += DataGridSettingsOnPropertyChanged;
            });
        }
    }

    [Serializable]
    [DataContract]
    public class DataGridSettings : ViewModelBase {
        [DataMember] readonly string _name;
        [DataMember] readonly Dictionary<string, ColumnSettings> _columns = new Dictionary<string, ColumnSettings>();

        public IEnumerable<ColumnSettings> Columns => _columns.Values;

        public DataGridSettings(string name) {
            _name = name;
        }

        public ColumnSettings this[string columnName] {
            get {
                ColumnSettings col;
                if(!_columns.TryGetValue(columnName, out col)) {
                    _columns.Add(columnName, col = new ColumnSettings(columnName));
                    col.PropertyChanged += ColumnOnPropertyChanged;
                }

                return col;
            }
        }

        void ColumnOnPropertyChanged(object sender, PropertyChangedEventArgs args) {
            RaisePropertyChanged(args.PropertyName);
        }

        protected override void OnDeserialized() {
            base.OnDeserialized();
            _columns.Values.ForEach(c => c.PropertyChanged += ColumnOnPropertyChanged);
        }

        public void SaveSettings(MyDataGrid grid) {
            _columns.Clear();

            foreach(var c in grid.Columns) {
                var s = this[c.Header.ToString()];

                s.Order = c.DisplayIndex;
                s.IsVisible = c.Visibility == Visibility.Visible;
                s.Width = c.ActualWidth;
            }
        }
    }

    [Serializable]
    [DataContract]
    public class ColumnSettings : ViewModelBase {
        [DataMember] readonly string _name;
        [DataMember] bool _isVisible = true;
        [DataMember] double _width = 70;
        [DataMember] int _order;

        public ColumnSettings(string name) { _name = name; }

        public string Name => _name;
        public bool IsVisible {get {return _isVisible;} set {SetField(ref _isVisible, value);}}
        public double Width {get {return _width;} set {SetField(ref _width, value);}}
        public int Order {get {return _order;} set {SetField(ref _order, value);}}
    }
}
