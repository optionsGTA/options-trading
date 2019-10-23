using System.Collections.Generic;
using Ecng.Collections;
using MoreLinq;
using StockSharp.BusinessEntities;
using StockSharp.Plaza;

namespace OptionBot.robot {
    /// <summary>
    /// Расширение адаптера плазы для получения расширенной информации по портфелю, не включаемой в портфель StockSharp.
    /// </summary>
    class PlazaPortfolioListener : PlazaExtension {
        readonly Dictionary<string, PortfolioEx> _portfolios = new Dictionary<string, PortfolioEx>(); 
        readonly Dictionary<string, PortfolioTableRecord> _records = new Dictionary<string, PortfolioTableRecord>(); 

        public PlazaPortfolioListener(PlazaTraderEx trader) : base(trader) {
            var portfolioColumns = _trader.TableRegistry.ColumnRegistry.Portfolios;

            var list = new[] {
                portfolioColumns.ClientCode,
                portfolioColumns.MoneyBlocked,
                portfolioColumns.MoneyFree
            };

            RegisterTableHandler(_trader.TableRegistry.Portfolios, list, OnPortfoliosRecordInserted);

            _trader.NewPortfolios += TraderOnNewPortfolios;
        }

        void TraderOnNewPortfolios(IEnumerable<Portfolio> portfolios) {
            portfolios.ForEach(p => {
                _portfolios[p.Name] = (PortfolioEx)p;
                UpdatePortfolio(p.Name);
            });
        }

        void OnPortfoliosRecordInserted(PlazaRecord r) {
            var columns = _trader.TableRegistry.ColumnRegistry.Portfolios;

            var record = new PortfolioTableRecord {
                Name = r.GetString(columns.ClientCode),
                BlockedMoney = r.GetDecimal(columns.MoneyBlocked),
                FreeMoney = r.GetDecimal(columns.MoneyFree)
            };

            _records[record.Name] = record;
            UpdatePortfolio(record.Name);
        }

        void UpdatePortfolio(string name) {
            var p = _portfolios.TryGetValue(name);
            var r = _records.TryGetValue(name);

            if(p != null && r != null) {
                p.BlockedMoney = r.BlockedMoney;
                p.FreeMoney = r.FreeMoney;
            }
        }

        class PortfolioTableRecord {
            public string Name { get; set; }
            public decimal BlockedMoney { get; set; }
            public decimal FreeMoney { get; set; }
        }
    }
}
