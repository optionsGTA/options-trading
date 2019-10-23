using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using Ecng.Common;
using MoreLinq;

namespace OptionBot {
    /// <summary>
    /// Базовый класс для реализации View-Model в модели MVVM
    /// </summary>
    [Serializable]
    [DataContract]
    public abstract class ViewModelBase : Disposable, INotifyPropertyChanged {
        static readonly Logger _log = new Logger(nameof(ViewModelBase));

        [field:NonSerialized] public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string name = null) {
            RaisePropertyChanged(name);
        }

        protected void RaisePropertyChanged<T>(Expression<Func<T>> selectorExpression) {
            RaisePropertyChanged(Util.PropertyName(selectorExpression));
        }

        protected void RaisePropertyChanged(string name) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        protected void OnPropertyChanged<T>(Expression<Func<T>> selectorExpression) {
            // ReSharper disable once ExplicitCallerInfoArgument
            OnPropertyChanged(Util.PropertyName(selectorExpression));
        }

        // установка требуемого поля в определенное значение и вызов события PropertyChanged при необходимости
        protected virtual bool SetField<T>(ref T field, T value, [CallerMemberName] string name = null, bool force=false) {
            if(!force && EqualityComparer<T>.Default.Equals(field, value)) return false;
            
            field = value;

            // ReSharper disable once ExplicitCallerInfoArgument
            OnPropertyChanged(name);

            return true;
        }

        [OnDeserializing]
        void DeserializingHandler(StreamingContext context) {
            _log.Dbg.AddDebugLog($"Deserializing: {GetType().Name}");
            OnDeserializing();
        }

        [OnDeserialized]
        void DeserializedHandler(StreamingContext context) {
            _log.Dbg.AddDebugLog($"Deserialized: {GetType().Name}");
            OnDeserialized();
        }

        protected virtual void OnDeserializing() { }
        protected virtual void OnDeserialized() { }
    }

    [Serializable]
    [DataContract]
    public abstract class SuspendableViewModelBase : ViewModelBase {
        int _suspendCount;
        HashSet<string> _changedProperties = new HashSet<string>(); 
        [Browsable(false)] public string[] ChangedViewModelProperties {get {return _changedProperties.ToArray();}} 

        // установка требуемого поля в определенное значение и вызов события PropertyChanged при необходимости
        protected override bool SetField<T>(ref T field, T value, [CallerMemberName] string name = null, bool force=false) {
            if(!force && EqualityComparer<T>.Default.Equals(field, value)) return false;
            
            field = value;

            if(_suspendCount == 0)
                OnPropertyChanged(name); // since no lock here it's possible that OnPropertyChanged will be called after SuspendNotifications
            else {
                lock(_changedProperties)
                    _changedProperties.Add(name);
            }

            return true;
        }

        protected override void OnDeserialized() {
            base.OnDeserialized();

            if(_changedProperties == null) {
                // ReSharper disable once InconsistentlySynchronizedField
                _changedProperties = new HashSet<string>();
            }
        }

        /// <summary>
        /// Приостановить вызов событий OnPropertyChanged до уничножения объекта disposable который возвращается данным методом.
        /// </summary>
        /// <returns></returns>
        public Disposable SuspendNotifications() {
            return new Suspender(this);
        }

        void FireEvents() {
            string[] names;

            lock(_changedProperties) {
                names = _changedProperties.ToArray();
                _changedProperties.Clear();
            }

            names.ForEach(OnPropertyChanged);
        }

        class Suspender : Disposable {
            readonly SuspendableViewModelBase _parent;
            bool _isDisposed;

            public Suspender(SuspendableViewModelBase parent) {
                _parent = parent;
                Interlocked.Increment(ref _parent._suspendCount);
            }

            protected override void DisposeManaged() {
                lock(this) {
                    if(_isDisposed)
                        return;
                    _isDisposed = true;
                }

                if(Interlocked.Decrement(ref _parent._suspendCount) == 0)
                    _parent.FireEvents();

                base.DisposeManaged();
            }
        }
    }

    [Serializable]
    public abstract class ViewModelBaseNotifyAction : ViewModelBase {
        protected abstract Action<string> NotifyAction {get;}

        protected override void OnPropertyChanged(string name = null) {
            NotifyAction(name);
        }
    }
}