namespace OptionBot.robot {
    /// <summary>
    /// Базовая стратегия, от которой наследуют модуль хеджирования и основной робот.
    /// Включает в себя их общую функциональность.
    /// </summary>
    public abstract class BaseRobotStrategy : BaseStrategy {
        protected readonly StrategyWrapper _wrapper;

        protected BaseRobotStrategy(StrategyWrapper wrapper, string name = "") : base(wrapper.Robot, wrapper.SecurityInfo, name) {
            _wrapper = wrapper;
        }

        protected override void OnStarting() {
        }

        protected abstract void RobotOnCanTradeStateChanged();

        protected override void OnSubscribe() {
            Robot.CanTradeStateChanged      += RobotOnCanTradeStateChanged;
        }

        protected override void OnUnsubscribe() {
            Robot.CanTradeStateChanged      -= RobotOnCanTradeStateChanged;
        }
    }
}
