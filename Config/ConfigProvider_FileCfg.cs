using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Ecng.Common;
using MoreLinq;

namespace OptionBot.Config {
    public partial class ConfigProvider {
        public interface ISaveableConfiguration {
            event Action<ISaveableConfiguration> ConfigurationChanged;
            void SaveEffectiveConfig();
        }

        abstract class FileConfig<T, R, I> : ViewModelBase, IConfigPair<T, R, I>, ISaveableConfiguration 
                                             where T : BaseConfig<T, R>, R
                                             where R : class, IReadOnlyConfiguration
                                             where I : class, IConfigPair<T, R, I> {
            readonly T _ui;
            T _test;
            T _effective;

            readonly string _filename;

            bool _isEffectiveConfigUpToDate, _realtimeUpdate;

            public T UI => _ui;
            public R Effective {get {return _effective;} private set {SetField(ref _effective, (T)value);}}

            public abstract string ConfigName {get; set;}

            public bool RealtimeUpdate {get {return _realtimeUpdate;} set {SetField(ref _realtimeUpdate, value);}}
            public bool IsEffectiveConfigUpToDate {get {return _isEffectiveConfigUpToDate;} private set {SetField(ref _isEffectiveConfigUpToDate, value);}}

            public event Action<R, CanUpdateEventArgs> CanUpdateConfig;
            public event Action<I, string[]> EffectiveConfigChanged;
            public event Action<I> UIEffectiveDifferent;

            protected FileConfig(T uiConfig, string filename) {
                if(!(this is I))
                    throw new InvalidOperationException("class must implement {0}".Put(typeof(I).Name));

                _filename = filename;
                _ui = uiConfig;
                _test = _ui.Clone();
                _effective = _test.Clone();

                IsEffectiveConfigUpToDate = RealtimeUpdate = true;

                _ui.PropertyChanged += UiOnPropertyChanged;
            }

            #region handle UI changes

            public void TryToApplyChanges(List<string> errors) {
                if(errors == null) throw new ArgumentNullException(nameof(errors));

                // ReSharper disable once AssignmentInConditionalExpression
                if(IsEffectiveConfigUpToDate = _ui.Equals(_effective))
                    return;

                TryToApplyChangesInternal(errors, false);
            }

            public void UndoUIChanges() {
                // ReSharper disable once AssignmentInConditionalExpression
                if(IsEffectiveConfigUpToDate = _ui.Equals(_effective))
                    return;

                _ui.CopyFrom(_effective);
                IsEffectiveConfigUpToDate = _ui.Equals(_effective);
            }

            void UiOnPropertyChanged(object sender, PropertyChangedEventArgs args) {
                try {
                    // ReSharper disable once AssignmentInConditionalExpression
                    if(IsEffectiveConfigUpToDate = _ui.Equals(_effective))
                        return;

                    TryToApplyChangesInternal(new List<string>(), !RealtimeUpdate, args.PropertyName);
                } finally {
                    if(!IsEffectiveConfigUpToDate)
                        UIEffectiveDifferent.SafeInvoke(this as I);
                }
            }

            void TryToApplyChangesInternal(List<string> errors, bool needUpdatePermission, params string[] addNames) {
                var names = new List<string>();
                string[] namesArr;

                lock(_test) {
                    _test.CopyFrom(_effective);

                    using(_test.SuspendNotifications()) {
                        _test.CopyFrom(_ui);
                        names.AddRange(_test.ChangedViewModelProperties);
                    }

                    addNames.Where(n => !names.Contains(n)).ForEach(names.Add);
                    namesArr = names.ToArray();

                    var args = new CanUpdateEventArgs(namesArr, errors, !needUpdatePermission);
                    var handler = CanUpdateConfig;
                    if(handler != null) {
                        handler(_test, args);

                        if(errors.Count != 0)
                            return;
                    }

                    if(needUpdatePermission && !args.AllowInstantUpdate)
                        return;

                    // set new effective config
                    Effective = _effective.Swap(ref _test);
                }

                IsEffectiveConfigUpToDate = _ui.Equals(_effective);

                EffectiveConfigChanged.SafeInvoke(this as I, namesArr);
                ConfigurationChanged.SafeInvoke(this);
            }

            #endregion

            #region IConfiguration

            public event Action<ISaveableConfiguration> ConfigurationChanged;

            void ISaveableConfiguration.SaveEffectiveConfig() {
                if(_filename.IsEmpty())
                    throw new InvalidOperationException("config filename is empty");
                _effective.SaveToXml(_filename, _effective.SerializerType);
            }

            public static TFCfg LoadFromFile<TFCfg>(string filename, Func<T, string, TFCfg> fileCfgCreator, Func<T> cfgCreator) where TFCfg:FileConfig<T, R, I> {
                var cfg = filename.LoadFromXml<T>() ?? cfgCreator();
                return fileCfgCreator(cfg, filename);
            }

            #endregion
        }

        abstract class FileConfigList<TFCfg, T, R, I>    : ISaveableConfiguration, IConfigPairList<T, R, I>
                                            where TFCfg  : FileConfig<T, R, I>, I
                                            where T      : BaseConfig<T, R>, R
                                            where R      : class, IReadOnlyConfiguration
                                            where I      : class, IConfigPair<T, R, I> {

            readonly string _filename;
            readonly ObservableCollection<TFCfg> _list = new NoResetObservableCollection<TFCfg>();

            public IEnumerable<I> List => _list;
            public event Action<bool> ListOrItemChanged;

            protected FileConfigList(IEnumerable<T> uiConfigs, string filename, Func<T, string, TFCfg> fileCfgCreator) {
                _filename = filename;
                _list.CollectionChanged += OnCollectionChanged;
                uiConfigs.ForEach(cfg => _list.Add(fileCfgCreator(cfg, null)));
            }

            public void Add(TFCfg item) {
                _list.Add(item);
            }

            public bool Remove(TFCfg item) {
                return _list.Remove(item);
            }

            void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args) {
                var changed = false;

                if(args.OldItems != null)
                    foreach(var o in args.OldItems.Cast<ISaveableConfiguration>()) {
                        o.ConfigurationChanged -= OnItemConfigurationChanged;
                        changed = true;
                    }

                if(args.NewItems != null)
                    foreach(var o in args.NewItems.Cast<ISaveableConfiguration>()) {
                        o.ConfigurationChanged += OnItemConfigurationChanged;
                        changed = true;
                    }

                if(changed) {
                    ConfigurationChanged.SafeInvoke(this);
                    ListOrItemChanged?.Invoke(true);
                }
            }

            void OnItemConfigurationChanged(ISaveableConfiguration obj) {
                ConfigurationChanged.SafeInvoke(this);
                ListOrItemChanged?.Invoke(false);
            }

            #region IConfiguration

            public event Action<ISaveableConfiguration> ConfigurationChanged;

            public void SaveEffectiveConfig() {
                _list.Select(cfg => cfg.Effective).Cast<T>().ToArray().SaveListToXml(_filename);
            }

            public static TFCfgList LoadFromFile<TFCfgList>(string filename, 
                                                            Func<T, string, TFCfg> fileCfgCreator,
                                                            Func<IEnumerable<T>, string, Func<T, string, TFCfg>, TFCfgList> listCreator) where TFCfgList:FileConfigList<TFCfg, T, R, I> {

                return listCreator(filename.LoadListFromXml<T>(), filename, fileCfgCreator);
            }


            #endregion
        }
    }

    public class CanUpdateEventArgs {
        readonly bool _allowInstantUpdateDefault;
        bool? _allowInstantUpdate;

        public CanUpdateEventArgs(string[] names, List<string> errors, bool allowInstantUpdateDefault) {
            Names = names;
            Errors = errors;
            _allowInstantUpdateDefault = allowInstantUpdateDefault;
        }

        public readonly string[] Names;
        public readonly List<string> Errors;

        public bool AllowInstantUpdate {
            get {return _allowInstantUpdate ?? _allowInstantUpdateDefault;}
            set {_allowInstantUpdate = _allowInstantUpdate == null ? value : _allowInstantUpdate.Value && value;}
        }

        public bool ChangedOnly(string name) {
            return Names.Length == 1 && Names[0] == name;
        }
    }
}
