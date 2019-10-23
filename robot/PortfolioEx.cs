using StockSharp.BusinessEntities;

namespace OptionBot.robot {
    /// <summary>
    /// Портфель с расширенной информацией.
    /// </summary>
    public class PortfolioEx : Portfolio {
        decimal _blockedMoney, _freeMoney;

        /// <summary>Заблокированные средства.</summary>
        public decimal BlockedMoney {
            get { return _blockedMoney; }
            set {
                if(_blockedMoney == value) return;

                _blockedMoney = value;
                NotifyChanged("BlockedMoney");
            }
        }

        /// <summary>Свободные средства.</summary>
        public decimal FreeMoney {
            get { return _freeMoney; }
            set {
                if(_freeMoney == value) return;

                _freeMoney = value;
                NotifyChanged("FreeMoney");
            }
        }
    }
}
