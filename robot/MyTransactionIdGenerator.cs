using System;
using Ecng.Common;

namespace OptionBot.robot {
    class MyTransactionIdGenerator : IncrementalIdGenerator {
        static readonly DateTime _startDate = new DateTime(2015, 4, 24);

        const int AverageTransactionsPerSecond = 200; // 200 = ~124 days collision interval for int (max=2^31)

        public MyTransactionIdGenerator() {
            var now = DateTime.Now;
            if(now <= _startDate) throw new InvalidOperationException("wrong time");

            Current = (long)((now - _startDate).Duration().TotalMilliseconds / (1000d / AverageTransactionsPerSecond));
        }

        public override long GetNextId() { return (base.GetNextId() % int.MaxValue) + 1; }
    }
}
