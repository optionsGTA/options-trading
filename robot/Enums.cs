using System.ComponentModel;

namespace OptionBot.robot {
    /// <summary>Состояние подключения.</summary>
    public enum ConnectionState {
        [Description("Отключен")] Disconnected,
        [Description("Подключение...")] Connecting,
        [Description("Синхронизация...")] Synchronizing,
        [Description("Подключен")] Connected,
        [Description("Отключение...")] Disconnecting
    }

    /// <summary>Состояние стратегии.</summary>
    public enum StrategyState {
        [Description("неактивна")] Inactive,
        [Description("запуск...")] Starting,
        [Description("активна")] Active,
        [Description("остановка...")] Stopping,
        [Description("ошибка")] Failed
    }

    /// <summary>Текущий период на рынке.</summary>
    public enum MarketPeriodType {
        [Description("перерыв")] Pause,
        [Description("утренняя сессия")] MorningSession,
        [Description("основная сессия")] MainSession,
        [Description("вечерняя сессия")] EveningSession,
        [Description("пром. клиринг")] InterClearing
    }

    /// <summary>Тип текущего торгового периода.</summary>
    public enum TradingPeriodType {
        [Description("до клиринга")] MainBeforeClearing,
        [Description("после клиринга")] MainAfterClearing,
        [Description("вечер")] Evening
    }

    /// <summary>Текущий период робота.</summary>
    public enum RobotPeriodType {
        [Description("перерыв")] Pause,
        [Description("дельта-хедж")] DeltaHedge,
        [Description("торги")] Active,
    }

    /// <summary>Состояние контроллера транзакций.</summary>
    public enum TransactionControllerState {
        NormalOperation,
        NewOrderLimitExceeded,
        LimitExceeded,
        MMOnly
    }

    public enum TransactionType {
        NewOrder,
        MoveOrder,
        MoverOrderPair,
        CancelOrder,
        GroupCancel
    }

    public enum StrategyRegime {OpenAndClose, CloseOnly}
    public enum StrategyOrderDirection {BuyOnly, SellOnly, BuyAndSell}

    // order is important (used in OptionMainStrategy). do not change it.
    // numeric values are also important (used in RobotLogger and in OptionModel.Data)
    public enum StrategyType {VegaHedge, GammaHedge, Regular, MM}

    /// <summary>Признак денежности опциона.</summary>
    public enum StrikeMoneyness {
        DeepInTheMoney,
        DeepOutOfMoney,
        AtTheMoney
    }

    /// <summary>Режим расчета греков по опциону.</summary>
    public enum GreeksRegime {
        Liquid,
        Illiquid
    }

    public enum RecalcReason {
         FutureChanged,
         OptionChanged,
         FutureSettingsUpdated,
         GeneralSettingsUpdated,
         VPSettingsUpdated,
         CanTradeStateChanged,
         ATMStrikeChanged,
         FutureRecalculatedOnPositionChange,
         OrderOrPositionUpdate,
         RealtimeMode,
         CanCalculateMode,
         ForcedRecalculate,
         TranRateControllerStateChanged,
         MoneyErrorDelay,
         OnStart,
         Periodic,
    }

    public enum CurveTypeParam {
        Ini,
        Bid,
        Offer
    }

    public enum CurveTypeModel {
        Linear,
        Parabola,
        Cube
    }

    public enum CurveConfigType {
        Curve,
        PreCurve
    }

    public enum CurveModelStatus {
        Reset,
        Illiquid,
        BadStats,
        Valuation,
    }

    public enum ValuationStatus {
        None,
        Manual,
        Aggressive,
        Conservative,
        QuoteVolume,
        QuoteVolumeBid,
        QuoteVolumeOffer,
        DealVolume,
        Time,
    }
}