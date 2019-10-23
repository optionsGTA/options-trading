using System;
using System.Collections.Generic;
using System.Linq;
using Ecng.Common;
using StockSharp.Algo;
using StockSharp.Algo.Strategies;
using StockSharp.BusinessEntities;

namespace OptionBot.robot {
    // strategy is used to track real position for each security
    public sealed class SecurityMainStrategy : BaseStrategy {
        ICancellationToken _positionTimer;
        static readonly TimeSpan _positionUpdateWaitTime = TimeSpan.FromSeconds(5);

        Position _securityPosition;

        public event Action<SecurityMainStrategy> PositionError;

        public SecurityMainStrategy(Robot robot, SecurityInfo secInfo) : base(robot, secInfo, "SecMain") { }

        protected override void OnStarting() {
            SyncPosition();
        }

        protected override void OnStarted2() {
        }

        protected override void OnStopping() {
            Util.CancelDelayedAction(ref _positionTimer);
            base.OnStopping();
        }

        protected override void OnStop(bool force) {
        }

        void ConnectorOnPositionsChanged(IEnumerable<Position> positions) {
            var pos = ((Position[])positions)[0];
            if(pos.Security != Security) return;

            _log.Dbg.AddInfoLog("Position changed(connector): {0}", pos.CurrentValue);

            OnPositionChanged(Position, pos.CurrentValue);
        }

        void StrategyOnPositionChanged() {
            _log.Dbg.AddInfoLog("Position changed(strategy): {0}", Position);

            if(_securityPosition == null)
                _securityPosition = PlazaTrader.Positions.FirstOrDefault(p => p.Security.Id == Security.Id);

            var connectorPos = _securityPosition.Return(p => p.CurrentValue, 0);

            OnPositionChanged(Position, connectorPos);
        }

        void OnPositionChanged(decimal strategyPosition, decimal connectorPosition) {
            Util.CancelDelayedAction(ref _positionTimer);
            if(strategyPosition != connectorPosition)
                _positionTimer = SecProcessor.DelayedPost(() => {
                    _log.Dbg.AddErrorLog("PositionError: real={0}, calculated={1}", 
                        PlazaTrader.Positions.FirstOrDefault(p => p.Security.Id == Security.Id).Return(p => p.CurrentValue, 0),
                        Position);
                    PositionError.SafeInvoke(this);
                }, _positionUpdateWaitTime, "pos error handler");
        }

        protected override void OnSubscribe() {
            PlazaTrader.PositionsChanged += ConnectorOnPositionsChanged;
            PositionChanged += StrategyOnPositionChanged;
        }

        protected override void OnUnsubscribe() {
            PlazaTrader.PositionsChanged -= ConnectorOnPositionsChanged;
            PositionChanged -= StrategyOnPositionChanged;
        }

        public void SyncPosition() {
            SecProcessor.Post(() => {
                _securityPosition = PlazaTrader.Positions.FirstOrDefault(p => p.Security.Id == Security.Id);
                var pos = _securityPosition.Return(p => p.CurrentValue, 0);
                Position = pos;
                _log.Dbg.AddWarningLog("SyncPosition: Position={0}", pos);
            }, "sync_pos");
        }

        public void StartChild(Strategy strategy) {
            SecProcessor.Post(() => ChildStrategies.Add(strategy), "start child strategy");
        }

        public void ClosePosition() {
            if(!IsInSecurityThread) {
                SecProcessor.Post(ClosePosition, "close pos");
                return;
            }

            if(ProcessState != ProcessStates.Started)
                return;

            var curval = Position;
            if(curval == 0) return;

            try {
                var order = CreateMarketOrder(CfgGeneral, Portfolio, Security, -curval);
                _log.AddInfoLog("закрытие позиции ({0})...", curval);

                (new IMarketRule[] {order.WhenMatched(), order.WhenRegisterFailed(), order.WhenCanceled()})
                    .Or()
                    .Do(arg => {
                        if(order.IsMatched()) {
                            _log.AddInfoLog("заявка на закрытие позиции исполнена.");
                        } else {
                            var fail = arg as OrderFail;
                            _log.AddErrorLog("заявка на закрытие позиции не исполнена (остаток {0}) {1}", order.Balance, fail != null ? fail.Error : null);
                        }
                    })
                    .Once()
                    .Apply();

                RegisterOrder(order);
            } catch(Exception e) {
                _log.AddErrorLog("Не удалось послать рыночную заявку: {0}", e);
            }
        }
    }
}
