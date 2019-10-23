using StockSharp.Algo;
using StockSharp.BusinessEntities;

namespace OptionBot.robot {
    class EntityFactoryEx : EntityFactory {
        public override Portfolio CreatePortfolio(string name) {
            return new PortfolioEx { Name = name };
        }

        public override MyTrade CreateMyTrade(Order order, Trade trade) {
            return new MyTradeEx {
                Order = order,
                Trade = trade
            };
        }
    }
}
