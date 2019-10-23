using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MoreLinq;
using OptionBot.robot;

namespace OptionBot.Config {
    public class CalculatedStrategyParams : ViewModelBase, ICalculatedConfigStrategy, IRobotDataUpdater {
        static readonly Dictionary<string, int> _propertyOrder = 
                    typeof(ICalculatedConfigStrategy).GetInterfaceProperties()
                                                     .ToDictionary(name => name, 
                                                                   name => ((MyPropertyOrderAttribute)typeof(ICalculatedConfigStrategy).GetProperty(name).GetCustomAttributes(typeof(MyPropertyOrderAttribute), false).Single()).Order);

        static readonly string[] _calculatedNames = _propertyOrder.Keys.OrderBy(name => _propertyOrder[name]).ToArray();

        static readonly Func<CalculatedStrategyParams, object>[] _getters;


        static CalculatedStrategyParams() {
            var t = typeof(CalculatedStrategyParams);
            _getters = _calculatedNames
                .Select(name => FastInvoke.BuildUntypedGetter<CalculatedStrategyParams>(t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)))
                .ToArray();
        }

        bool _recalculated;

        public int CalcMMVolume {get; set;}
        public double CalcMMMaxSpread {get; set;}
        public double SpreadsDifference {get; set;}
        public double OrderShift {get; set;}
        public bool TradingAllowedByLiquidity {get; set;}
        public double IlliquidIvBid {get; set;}
        public double IlliquidIvOffer {get; set;}
        public double IlliquidIvSpread {get; set;}
        public double IlliquidIvAverage {get; set;}
        public double OrderSpread {get; set;}
        public double MMAShiftOL {get; set;}
        public double MMAShiftOS {get; set;}
        public double ShiftOL {get; set;}
        public double ShiftOS {get; set;}
        public double ShiftCL {get; set;}
        public double ShiftCS {get; set;}
        public double ChangeNarrow {get; set;}
        public double ChangeWide {get; set;}
        public double ObligationsSpread {get; set;}
        public int ObligationsVolume {get; set;}

        public void Recalculated() {
            _recalculated = true;
        }

        public void UpdateData() {
            if(!_recalculated)
                return;

            _recalculated = false;
            _calculatedNames.ForEach(OnPropertyChanged);
        }

        public static IEnumerable<string> GetLoggerFields() {
            return _calculatedNames;
        }

        public IEnumerable<object> GetLoggerValues() {
            return _getters.Select(g => g(this));
        } 
    }
}
