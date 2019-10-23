using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using ContinuousLinq.WeakEvents;
using Ecng.Collections;
using Ecng.Common;

namespace OptionBot.robot {
    public interface IActiveObject {
        void Activate();
        void Deactivate();
        bool IsActive {get;}
    }

    /// <summary>
    /// Класс, позволяющий связать событие PropertyChanged на заданном объекте с вызовом соответствующего обработчика.
    /// </summary>
    /// <remarks>
    /// Реализует интерфейс IActiveObject, что позволяет включать/отключать вызов обработчика.
    /// Используется для объектов в составе RobotData, представляющих различные сущности рынка/робота для того, чтобы пользовательский интерфейс
    /// получал оповещения об изменении различных свойств на этих объектах для отображения изменений.
    /// </remarks>
    public class NotifyPropertyMapper : Disposable, IActiveObject {
        readonly Dictionary<string, HashSet<string>> _propertyMap = new Dictionary<string, HashSet<string>>();
        readonly Dictionary<string, List<Action<string>>> _handlersMap = new Dictionary<string, List<Action<string>>>();
        readonly object _locker = new object();
        INotifyPropertyChanged _object;
        WeakEventHandler _handler;
        public Action<string> NotifyPropertyChangedAction {get; set;}
        public bool IsActive {get {return _handler != null; }}

        public NotifyPropertyMapper(INotifyPropertyChanged obj) {
            _object = obj;
        }

        public void Activate() {
            lock(_locker) {
                if(_handler != null) return;
                _handler = WeakPropertyChangedEventHandler.Register(_object, DeregisterEvent, this, ForwarderAction);
            }

            NotifyAll();
        }

        static void DeregisterEvent(INotifyPropertyChanged obj, PropertyChangedEventHandler eh) {
            obj.PropertyChanged -= eh;
        }

        static void ForwarderAction(NotifyPropertyMapper me, object sender, PropertyChangedEventArgs args) {
            me.ObjectOnPropertyChanged(sender, args);
        }

        public void Deactivate() {
            lock(_locker) {
                if(_handler == null) return;
                _handler.Deregister();
                _handler = null;
            }
        }

        void ObjectOnPropertyChanged(object sender, PropertyChangedEventArgs args) {
            HashSet<string> hs;
            if(_propertyMap.TryGetValue(args.PropertyName, out hs)) {
                foreach(var name in hs)
                    NotifyPropertyChangedAction.SafeInvoke(name);
            }

            if(_handlersMap.Count > 0) {
                List<Action<string>> actions;
                if(_handlersMap.TryGetValue(args.PropertyName, out actions))
                    actions.ForEach(a => a(args.PropertyName));
            }
        }

        public void AddExternalProperty<T>(Expression<Func<T>> fromSelector) {
            AddExternalProperty(fromSelector, fromSelector);
        }

        public void AddPropertyChangeHandler<T>(Expression<Func<T>> nameSelector, Action<string> action) {
            var name = Util.PropertyName(nameSelector);

            var list = _handlersMap.TryGetValue(name);
            if(list == null)
                _handlersMap.Add(name, list = new List<Action<string>>());

            list.Add(action);
        }

        public void AddExternalProperty<T1, T2>(Expression<Func<T1>> fromSelector, Expression<Func<T2>> toSelector) {
            var nameFrom = Util.PropertyName(fromSelector);
            var nameTo = Util.PropertyName(toSelector);

            var hs = _propertyMap.TryGetValue(nameFrom);
            if(hs == null)
                _propertyMap.Add(nameFrom, hs = new HashSet<string>());

            hs.Add(nameTo);
        }

        /// <summary>
        /// Заменить объект, у которого слушается событие PropertyChanged на новый.
        /// </summary>
        /// <param name="newObj">Новый объект/</param>
        /// <param name="notifyAll">Оповестить ли всех подписчиков данного объекта об изменении.</param>
        public void ReplacePropertyObject(INotifyPropertyChanged newObj, bool notifyAll = true) {
            lock(_locker) {
                if(object.ReferenceEquals(_object, newObj))
                    return;

                var wasActive = IsActive;
                if(wasActive) Deactivate();
                _object = newObj;
                if(wasActive) Activate();
            }

            if(notifyAll) 
                NotifyAll();
        }

        void NotifyAll() {
            foreach(var name in _propertyMap.Values.SelectMany(hs => hs).Distinct())
                NotifyPropertyChangedAction.SafeInvoke(name);

            foreach(var kv in _handlersMap)
                kv.Value.ForEach(a => a(kv.Key));
        }

        protected override void DisposeManaged() {
            Deactivate();
            base.DisposeManaged();
        }
    }

    /// <summary>
    /// Коллекция объектов с автоматической активацией при добавлении в коллекцию, и автоматической деактивацией при удалении из коллекции.
    /// </summary>
    public sealed class ActiveObjectsObservableCollection<T> : NoResetObservableCollection<T> where T:IActiveObject {
        public ActiveObjectsObservableCollection() {
            CollectionChanged += OnCollectionChanged;
        }

        static void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args) {
            if(args.OldItems != null)
                foreach(var o in args.OldItems.Cast<IActiveObject>())
                    o.Deactivate();

            if(args.NewItems != null)
                foreach(var o in args.NewItems.Cast<IActiveObject>())
                    o.Activate();
        }
    }

    /// <summary>
    /// Объект для автоматической активации/деактивации заданных свойств.
    /// </summary>
    public class AutoPropertyActivator<T> where T:INotifyPropertyChanged,INotifyPropertyChanging {
        readonly Dictionary<string, Func<IActiveObject>>  _propertyGetters = new Dictionary<string, Func<IActiveObject>>();

        public AutoPropertyActivator(T obj) {
            obj.PropertyChanging += ObjectOnPropertyChanging;
            obj.PropertyChanged += ObjectOnPropertyChanged;
        }

        public void RegisterProperty<K>(Expression<Func<K>> nameSelector, Func<IActiveObject> getter) {
            var name = Util.PropertyName(nameSelector);
            if(_propertyGetters.ContainsKey(name)) throw new ArgumentException("duplicate property name '{0}'".Put(name));
            _propertyGetters[name] = getter;
        }


        void ObjectOnPropertyChanging(object sender, PropertyChangingEventArgs args) {
            _propertyGetters.TryGetValue(args.PropertyName).Do(getter => getter().Do(activeObject => activeObject.Deactivate()));
        }

        void ObjectOnPropertyChanged(object sender, PropertyChangedEventArgs args) {
            _propertyGetters.TryGetValue(args.PropertyName).Do(getter => getter().Do(activeObject => activeObject.Activate()));
        }
    }
}
