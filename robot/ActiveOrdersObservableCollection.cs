using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace OptionBot.robot {
    public class ActiveOrdersObservableCollection : NoResetObservableCollection<OrderInfo> {
        readonly ObservableCollection<OrderInfo> _parent;
        readonly HashSet<long> _ids = new HashSet<long>();

        public ActiveOrdersObservableCollection(ObservableCollection<OrderInfo> allOrders) {
            _parent = allOrders;
            _parent.CollectionChanged += ParentOnCollectionChanged;

            OnParentReset();
        }

        void ParentOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args) {
            if(args.Action == NotifyCollectionChangedAction.Reset) {
                OnParentReset();
                return;
            }

            if(args.OldItems != null)
                foreach(var o in args.OldItems.Cast<OrderInfo>().Where(o => _ids.Contains(o.TransactionId)))
                    Remove(o);

            if(args.NewItems != null)
                foreach(var o in args.NewItems.Cast<OrderInfo>().Where(o => !o.State.IsFinalState()))
                    Add(o);
        }

        void OnParentReset() {
            Clear();
            foreach(var o in _parent.Where(oi => !oi.State.IsFinalState()))
                Add(o);
        }

        protected override void InsertItem(int index, OrderInfo item) {
            base.InsertItem(index, item);
            item.PropertyChanged += OrderInfoOnPropertyChanged;
            _ids.Add(item.TransactionId);
        }

        protected override void RemoveItem(int index) {
            var item = this[index];
            base.RemoveItem(index);
            item.PropertyChanged -= OrderInfoOnPropertyChanged;
            _ids.Remove(item.TransactionId);
        }

        void OrderInfoOnPropertyChanged(object sender, PropertyChangedEventArgs args) {
            if(args.PropertyName != nameof(OrderInfo.State))
                return;

            var o = (OrderInfo)sender;
            if(o.State.IsFinalState())
                Remove(o);
        }
    }
}
