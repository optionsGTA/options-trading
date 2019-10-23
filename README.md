# options
HFT robot for options trading with risk control and hedging.

Short description:
The bot operates with four types of option strategies: regular, market maker and two hedging.
Within each strategy, pricing parameters are set with reference to the relative displacement relative to the central strategy.
Operator order:
- definition of constants
- selection of underlying assets
- definition of parameters for underlying assets
- determination of options pricing options
- determination of options for optional strategies
The order of the bot:
- definition of traded tickers (rarely)
- calculation of the total price
- calculation of risk control parameters
- calculation of the number of applications
- pricing for bids
- decision making on sending an application
- control of civil defense
- transaction analysis
- logging

1. Exchange data:
- the best quotes for futures through the order book
- glasses of option quotes
- active applications
- open positions

2. The choice of tools for trading.
The underlying assets for option series (futures) are manually selected from the list.
Options for market valuation are manually selected from the list. The bot shortens the selected list of options in accordance with the specified evaluation parameters.
The list of options for trading is selected from the list of options for evaluating the market in accordance with active strategies.
Base asset quotes, market valuation options and active strategies.
Evaluation parameters and strategy parameters are set in the central part of each option series.

Unique parameters of market assessment and strategies:
- type of strategy
- optional series
- offset relative to the central strike
- type of option

The set of parameters may differ depending on the type of option strategy.

3. Simplified algorithm for working with applications.
Applications for the purchase and sale of options.
When market prices change, orders are moved by the team.
An independent futures hedging module.
In some specially described cases, active applications are withdrawn.

At the same time, up to eight active statements can be made.

Submission of applications may be limited.

Sending applications (regardless of directions)
- capital limit for trading
- open position limit
- volatility spread
- the number of applications per day
- the number of applications per second
- trading period

4. General requirements for the robot.
- the best working time of the robot should not exceed 1 ms
- the ability to change the parameters of the Strategy during the operation of the robot
- log files are added daily
- interface for interacting with the bot using client-server technologies
- sending messages by e-mail upon the unexpected termination of the bot
- launch on historical data
