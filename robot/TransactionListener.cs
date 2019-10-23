using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Ecng.Common;

namespace OptionBot.robot
{
    /// <summary>Класс, обеспечивающий подсчет количества транзакций.</summary>
    public class TransactionListener {
        //readonly Logger _log = new Logger();

        readonly Dictionary<ValTuple<long, long>, int> _transactions = new Dictionary<ValTuple<long, long>, int>(); 

        readonly Controller _controller;

        int _currentSessionId;

        RobotData RobotData => _controller.RobotData;

        public int NumTransactions {get; private set;}

        public event Action NumTransactionsChanged;

        public TransactionListener(Controller controller) {
            _controller = controller;

            controller.ConnectorGUISubscriber.NewTransaction += OnNewTransaction;
            RobotData.PropertyChanged += RobotDataOnPropertyChanged;
        }

        void RobotDataOnPropertyChanged(object sender, PropertyChangedEventArgs args) {
            if(args.PropertyName != Util.PropertyName(() => RobotData.CurrentSessionId))
                return;

            _currentSessionId = RobotData.CurrentSessionId;
            NumTransactions = _transactions.Values.Count(sid => sid == _currentSessionId);

            NumTransactionsChanged.SafeInvoke();
        }

        void OnNewTransaction(Connector connector, PlazaTransactionInfo tInfo) {
            if(_transactions.ContainsKey(tInfo.Id))
                return;

            _transactions[tInfo.Id] = tInfo.SessionId;

            if(tInfo.SessionId == _currentSessionId) {
                ++NumTransactions;
                NumTransactionsChanged.SafeInvoke();
            }
        }
    }
}
