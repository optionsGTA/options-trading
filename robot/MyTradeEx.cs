using System.ComponentModel;
using System.Runtime.CompilerServices;
using StockSharp.BusinessEntities;

namespace OptionBot.robot {
    public class MyTradeEx : MyTrade, INotifyPropertyChanged {
        double? _tradeIv;

        public double? TradeIv { get { return _tradeIv; }
            set {
                if(_tradeIv == value) return;

                _tradeIv = value;
                OnPropertyChanged();
            }
        }

        public decimal CurVarMargin {get; set;}
        public PricePair CurFutQuote {get; set;}
        public int CurPosition {get; set;}
        public double CurIvBid {get; set;}
        public double CurIvOffer {get; set;}
        public double CurMarketIvBid {get; set;}
        public double CurMarketIvOffer {get; set;}
        public decimal CurVegaPortfolio {get; set;}

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
