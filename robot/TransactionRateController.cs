using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AsyncHandler;
using Ecng.Common;
using OptionBot.Config;
using StockSharp.BusinessEntities;
using StockSharp.Plaza;

namespace OptionBot.robot {
    public class TransactionRateController : Disposable {
        const int NumReserveTransactions = 2; // 1(futures hedge) + 1(reserved for group cancel)
        static readonly TimeSpan BlockTransactionsTime = TimeSpan.FromSeconds(1);
        static readonly TimeSpan _oneSecond = TimeSpan.FromSeconds(1);

        readonly Logger _log = new Logger("TranRateCtl");

        readonly object _stateLock = new object();
        readonly PlazaTraderEx _trader;
        readonly Func<HandlerThread> _robotThreadGetter;
        readonly List<Tuple<DateTime, TransactionType>> _lastPeriodTransactions = new List<Tuple<DateTime, TransactionType>>();
        readonly List<Action> _restoreActions = new List<Action>();
        DateTime _plazaPenaltyExpirationTime, _lastTransactionsUpdateTime, _mmOnlyEndsAt;
        Portfolio _portfolio;
        HTCancellationToken _token;
        int _numMMStrategies;
        bool _settingsUpdated;
        int _transactionsLimit, _transactionsNewOrderLimit;
        double _mmOnlyDelay;

        Controller Controller => _trader.Controller;
        Robot Robot => Controller.Robot;
        HandlerThread RobotThread => _robotThreadGetter();
        IConfigGeneral CfgGeneral => Controller.ConfigProvider.General.Effective;
        TransactionControllerState State {get; set;}
        public int LastSecondTransactionsCached => SteadyClock.Now - _lastTransactionsUpdateTime > _oneSecond ? 0 : _lastPeriodTransactions.Count;
        public bool IsLimitExceeded => State == TransactionControllerState.LimitExceeded;
        public bool IsNewOrderLimitExceeded => IsLimitExceeded || State == TransactionControllerState.NewOrderLimitExceeded;

        public event Action<TransactionControllerState> StateChanged;

        public TransactionRateController(PlazaTraderEx trader, Func<HandlerThread> robotThreadGetter) {
            _trader = trader;
            _robotThreadGetter = robotThreadGetter;
            State = TransactionControllerState.NormalOperation;
            Controller.ConfigProvider.General.EffectiveConfigChanged += (general, strings) => _settingsUpdated = true;
            _settingsUpdated = true;
            TryUpdateSettings();
        }

        protected override void DisposeManaged() {
            _log.Dbg.AddDebugLog("disposing");
            Util.CancelDelayedAction(ref _token);

            base.DisposeManaged();
        }

        void EnsurePortfolio() {
            var pname = CfgGeneral.Portfolio;
            if(_portfolio != null && _portfolio.Name == pname)
                return;

            if(string.IsNullOrEmpty(pname)) {
                _log.Dbg.AddErrorLog("portfolio name in config is not set");
                return;
            }

            _portfolio = _trader.Portfolios.FirstOrDefault(p => p.Name == pname);
            if(_portfolio == null)
                _log.Dbg.AddWarningLog($"Unable to find portfolio '{pname}'");
        }

        void TryUpdateSettings() {
            if(!_settingsUpdated)
                return;

            _settingsUpdated = false;

            var cfg = CfgGeneral;
            _transactionsLimit = cfg.PlazaTransactionsLimit;
            _transactionsNewOrderLimit = (int)Math.Round(_transactionsLimit * cfg.PlazaTransactionsRateNewOrderFraction);
            _mmOnlyDelay = cfg.PlazaTransactionsMMOnlyDelay;

            _log.Dbg.AddDebugLog($"Limits: max={_transactionsLimit}, newOrder={_transactionsNewOrderLimit}, MM delay={_mmOnlyDelay:0.###} sec");
        }

        public bool CanTrade(StrategyType? stype = null) {
            switch(State) {
                case TransactionControllerState.NewOrderLimitExceeded:
                    var mmOnlyPending = _numMMStrategies > 0 && _mmOnlyEndsAt >= SteadyClock.Now;
                    return stype == null || stype == StrategyType.MM || !mmOnlyPending;
                case TransactionControllerState.MMOnly:
                    return stype == null || stype == StrategyType.MM;
                case TransactionControllerState.LimitExceeded:
                    return false;
            }

            return true;
        }

        public void RegisterStrategy(IOptionStrategy strategy) {
            if(strategy.StrategyType == StrategyType.MM && Interlocked.Increment(ref _numMMStrategies) == 1) {
                RobotThread.ExecuteAsync(() => {
                    var now = SteadyClock.Now;
                    if(State == TransactionControllerState.NormalOperation || (State == TransactionControllerState.NewOrderLimitExceeded && _mmOnlyEndsAt <= now)) {
                        _mmOnlyEndsAt = now + TimeSpan.FromSeconds(_mmOnlyDelay);
                        ChangeState(TransactionControllerState.MMOnly);
                    }
                });
            }
        }

        public void DeregisterStrategy(IOptionStrategy strategy) {
            if(strategy.StrategyType != StrategyType.MM)
                return;

            Interlocked.Decrement(ref _numMMStrategies);

            RobotThread.ExecuteAsync(() => {
                if(_numMMStrategies > 0 || State != TransactionControllerState.MMOnly)
                    return;

                TryRestore();
            });
        }

        int UpdateLastPeriodTransactions(DateTime removeBefore) {
            return _lastPeriodTransactions.RemoveAll(dt => dt.Item1 < removeBefore);
        }

        /// <summary>
        /// Попытаться зарезервировать транзакцию.
        /// </summary>
        /// <param name="ttype">Тип добавляемой транзакции.</param>
        /// <param name="useReserve">Использовать ли полную доступную частоту транзакций (по умолчанию используется частота-2).</param>
        /// <param name="onRestoreAction">Если попытка TryAddTransaction неудачна, то действие onRestoreAction будет выполнено при переходе из LimitExceeded в другое.</param>
        /// <returns>True, если транзакция разрешена.</returns>
        public bool TryAddTransaction(TransactionType ttype, bool useReserve = false, Action onRestoreAction = null) {
            TryUpdateSettings();
            var plazaPenaltyLeft = 0d;
            TransactionControllerState? nextState = null;
            var success = false;
            var minLeft = useReserve ? 0 : NumReserveTransactions;
            var now = SteadyClock.Now;
            var oldTime = now - _oneSecond;
            int removed, curNumTransactions;

            removed = curNumTransactions = 0;
            try {
                lock(_stateLock) {
                    try {
                        if(State == TransactionControllerState.LimitExceeded && !useReserve)
                            return false;

                        removed = UpdateLastPeriodTransactions(oldTime);
                        curNumTransactions = _lastPeriodTransactions.Count;

                        _lastTransactionsUpdateTime = now;

                        if(_transactionsLimit - (curNumTransactions + 1) < minLeft) {
                            if(State != TransactionControllerState.LimitExceeded && ttype != TransactionType.NewOrder)
                                nextState = TransactionControllerState.LimitExceeded;
                            return false;
                        }

                        if(ttype == TransactionType.NewOrder && _lastPeriodTransactions.Count(t => t.Item2 == TransactionType.NewOrder) >= _transactionsNewOrderLimit) {
                            if(State != TransactionControllerState.LimitExceeded && State != TransactionControllerState.NewOrderLimitExceeded)
                                nextState = TransactionControllerState.NewOrderLimitExceeded;
                            return false;
                        }

                        plazaPenaltyLeft = Math.Max(0, (_plazaPenaltyExpirationTime - now).TotalMilliseconds);
                        if(plazaPenaltyLeft > 0)
                            return false;

                        success = true;
                        _lastPeriodTransactions.Add(Tuple.Create(now, ttype));

                        return true;
                    } finally {
                        if(!success) {
                            if(onRestoreAction != null)
                                _restoreActions.Add(onRestoreAction);

                            _log.Dbg.AddDebugLog($"TryAddTransaction({ttype},{useReserve}): failed ({State}, plazaPenaltyLeft={plazaPenaltyLeft:0.###}), {removed} removed, {curNumTransactions} left.");

                            if (nextState != null)
                                ChangeState(nextState.Value);
                        }
                    }
                }
            } finally {
                if(success)
                    _log.Dbg.AddDebugLog($"TryAddTransaction({ttype},{useReserve}): ok ({State}), {removed} removed, {curNumTransactions} left.");
            }
        }

        void HandleTransactionsLimitExceeded() {
            var now = SteadyClock.Now;

            lock(_stateLock) {
                var countNew = _lastPeriodTransactions.Count(t => t.Item2 == TransactionType.NewOrder);

                EnsurePortfolio();
                TryUpdateSettings();

                var firstMs = _lastPeriodTransactions.FirstOrDefault().Return(t => (t.Item1 - now).TotalMilliseconds, 0);
                var lastMs = _lastPeriodTransactions.LastOrDefault().Return(t => (t.Item1 - now).TotalMilliseconds, 0);
                var oneSec = _lastPeriodTransactions.Count(t => t.Item1 >= now - _oneSecond);
                _log.Dbg.AddWarningLog($"Transaction limit exceeded. {_lastPeriodTransactions.Count} trans. 1sec={oneSec} first={firstMs:F0}ms. last={lastMs:F0}ms, newOrder={countNew}");

                if(_portfolio != null)
                    _trader.CancelAllOrders(_portfolio);
            }
        }

        public void HandlePlazaTransactionLimitExceeded(OrderFail fail) {
            var plazaError = fail.Error as PlazaException;
            var now = SteadyClock.Now;
            _log.Dbg.AddWarningLog($"Plaza transaction limit exceeded. tranId={fail.Order?.TransactionId}, penalty_remain={plazaError?.PenaltyRemain}; {_lastPeriodTransactions.Count} transactions in buffer");

            if(plazaError?.PenaltyRemain > 0) {
                _plazaPenaltyExpirationTime = now + TimeSpan.FromMilliseconds(plazaError.PenaltyRemain);
            } else {
                _plazaPenaltyExpirationTime = now + BlockTransactionsTime;
            }

            lock(_stateLock) {
                if(State != TransactionControllerState.LimitExceeded) {
                    ChangeState(TransactionControllerState.LimitExceeded);
                } else {
                    ScheduleRestore(_plazaPenaltyExpirationTime - now);
                }
            }
        }

        void OnEnterState(TransactionControllerState oldState) {
            lock(_stateLock) {
                switch(State) {
                    case TransactionControllerState.NormalOperation:
                        _mmOnlyEndsAt = DateTime.MinValue;
                        break;
                    case TransactionControllerState.NewOrderLimitExceeded:
                        ScheduleRestore(BlockTransactionsTime);
                        break;
                    case TransactionControllerState.LimitExceeded:
                        HandleTransactionsLimitExceeded();
                        var now = SteadyClock.Now;
                        var blockTime = _plazaPenaltyExpirationTime > now ? _plazaPenaltyExpirationTime - now : BlockTransactionsTime;
                        ScheduleRestore(blockTime);
                        break;
                    case TransactionControllerState.MMOnly:
                        TryUpdateSettings();
                        var sleep = _mmOnlyEndsAt - SteadyClock.Now;
                        if(sleep <= TimeSpan.Zero) {
                            _log.Dbg.AddWarningLog("negative sleep time in MMOnly state. changing back to normal.");
                            ChangeState(TransactionControllerState.NormalOperation);
                        } else {
                            ScheduleRestore(sleep);
                        }
                        break;
                }

                if(oldState == TransactionControllerState.LimitExceeded) {
                    try {
                        _restoreActions.ForEach(a => a());
                    } finally {
                        _restoreActions.Clear();
                    }
                }
            }
        }

        void TryRestore() {
            var now = SteadyClock.Now;
            Util.CancelDelayedAction(ref _token);

            lock(_stateLock) {
                var s = State;
                _log.Dbg.AddInfoLog($"TryRestore: state={s}");
                if(s == TransactionControllerState.NormalOperation)
                    return;

                var sleep = _plazaPenaltyExpirationTime - now;
                if(sleep > TimeSpan.Zero) {
                    ScheduleRestore(sleep);
                    return;
                }

                switch(s) {
                    case TransactionControllerState.LimitExceeded:
                        if(_numMMStrategies > 0) {
                            _mmOnlyEndsAt = SteadyClock.Now + TimeSpan.FromSeconds(_mmOnlyDelay);
                            ChangeState(TransactionControllerState.MMOnly);
                        } else {
                            ChangeState(TransactionControllerState.NormalOperation);
                        }
                        break;
                    
                    case TransactionControllerState.NewOrderLimitExceeded:
                        ChangeState(_numMMStrategies > 0 && _mmOnlyEndsAt > SteadyClock.Now ? TransactionControllerState.MMOnly : TransactionControllerState.NormalOperation);
                        break;

                    case TransactionControllerState.MMOnly:
                        sleep = _mmOnlyEndsAt - SteadyClock.Now;
                        if(sleep <= TimeSpan.Zero) {
                            ChangeState(TransactionControllerState.NormalOperation);
                        } else {
                            ScheduleRestore(sleep);
                        }
                        break;

                    default:
                        _log.Dbg.AddErrorLog($"unhandled state={s}");
                        break;
                }
            }
        }

        void ScheduleRestore(TimeSpan sleep) {
            if(sleep <= TimeSpan.Zero) {
                _log.Dbg.AddWarningLog($"ScheduleRestore: wrong delay {sleep:c}, scheduling 1ms");
                sleep = TimeSpan.FromMilliseconds(1);
            }

            Util.CancelDelayedAction(ref _token);
            _log.Dbg.AddDebugLog($"TryRestore in {sleep.TotalMilliseconds:0.###} ms");
            _token = RobotThread.DelayedAction(TryRestore, sleep);
        }

        void ChangeState(TransactionControllerState newState) {
            lock(_stateLock) {
                var oldState = State;
                if(oldState == newState) {
                    _log.Dbg.AddWarningLog($"ChangeState: already in {newState}");
                    return;
                }

                _log.AddInfoLog($"state: {oldState} ==> {newState}");

                State = newState;
                Util.CancelDelayedAction(ref _token);
                OnEnterState(oldState);

                if(State != newState) {
                    _log.Dbg.AddWarningLog("state changed during OnEnterState()");
                    return; // no 
                }

                StateChanged?.Invoke(State);
            }
        }
    }
}
