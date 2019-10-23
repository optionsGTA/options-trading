using System;
using System.Runtime.CompilerServices;

namespace OptionBot.robot {
    /// <summary>
    /// Класс, позволяющий подписываться на события через метод-посредник.
    /// </summary>
    public abstract class ProxySubscriber {
        readonly Action<Action> _proxy;
        readonly ConditionalWeakTable<object, Ecng.Common.WeakReference<object>> _subscribers = new ConditionalWeakTable<object, Ecng.Common.WeakReference<object>>();

        protected ProxySubscriber(Action<Action> proxy) {
            _proxy = proxy;
        }
    
        protected Action<T> Proxy<T>(Action<T> action) {
            var wref = _subscribers.TryGetValue(action);
            if(wref == null) {
                wref = new Ecng.Common.WeakReference<object>(new Action<T>(x => _proxy(() => action(x))));
                _subscribers.Add(action, wref);
            }
            return wref.Target as Action<T>;
        }

        protected Action<T1,T2> Proxy<T1,T2>(Action<T1,T2> action) {
            var wref = _subscribers.TryGetValue(action);
            if(wref == null) {
                wref = new Ecng.Common.WeakReference<object>(new Action<T1,T2>((x1,x2) => _proxy(() => action(x1,x2))));
                _subscribers.Add(action, wref);
            }
            return wref.Target as Action<T1,T2>;
        }

        protected Action<T1,T2,T3> Proxy<T1,T2,T3>(Action<T1,T2,T3> action) {
            var wref = _subscribers.TryGetValue(action);
            if(wref == null) {
                wref = new Ecng.Common.WeakReference<object>(new Action<T1,T2,T3>((x1,x2,x3) => _proxy(() => action(x1,x2,x3))));
                _subscribers.Add(action, wref);
            }
            return wref.Target as Action<T1,T2,T3>;
        }

        protected Action<T1,T2,T3,T4> Proxy<T1,T2,T3,T4>(Action<T1,T2,T3,T4> action) {
            var wref = _subscribers.TryGetValue(action);
            if(wref == null) {
                wref = new Ecng.Common.WeakReference<object>(new Action<T1,T2,T3,T4>((x1,x2,x3,x4) => _proxy(() => action(x1,x2,x3,x4))));
                _subscribers.Add(action, wref);
            }
            return wref.Target as Action<T1,T2,T3,T4>;
        }

        protected Action<T1,T2,T3,T4,T5> Proxy<T1,T2,T3,T4,T5>(Action<T1,T2,T3,T4,T5> action) {
            var wref = _subscribers.TryGetValue(action);
            if(wref == null) {
                wref = new Ecng.Common.WeakReference<object>(new Action<T1,T2,T3,T4,T5>((x1,x2,x3,x4,x5) => _proxy(() => action(x1,x2,x3,x4,x5))));
                _subscribers.Add(action, wref);
            }
            return wref.Target as Action<T1,T2,T3,T4,T5>;
        }
    }
}
