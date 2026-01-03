# Custom Strategy Template
# Use this as a starting point for your own Python strategies

from aegisquant import Strategy, Signal, Context

class CustomStrategy(Strategy):
    """
    Custom Strategy Template
    
    This is a template for creating your own trading strategies.
    Modify the on_tick method to implement your trading logic.
    """
    
    def __init__(self):
        super().__init__()
        self.name = "Custom Strategy"
        self.description = "A custom trading strategy template"
        
        # Define your parameters here
        self.params = {
            "rsi_period": 14,
            "ma_short": 5,
            "ma_long": 20,
            "rsi_oversold": 30,
            "rsi_overbought": 70
        }
        
        # Initialize any state variables
        self.prev_short_ma = None
        self.prev_long_ma = None
    
    def on_tick(self, ctx: Context) -> Signal:
        """
        Process each tick and generate trading signals.
        
        Available in ctx:
        - ctx.price: Current price
        - ctx.volume: Current volume
        - ctx.timestamp: Current timestamp
        - ctx.position: Current position info
        - ctx.account: Account status
        - ctx.indicators: Technical indicator service
        
        Available indicators:
        - ctx.indicators.SMA(period)
        - ctx.indicators.EMA(period)
        - ctx.indicators.RSI(period)
        - ctx.indicators.MACD(fast, slow, signal)
        - ctx.indicators.BollingerBands(period, stdDev)
        - ctx.indicators.ATR(period)
        - ctx.indicators.Stochastic(kPeriod, dPeriod)
        
        Args:
            ctx: Strategy context with price data and indicators
            
        Returns:
            Signal.BUY, Signal.SELL, or Signal.NONE
        """
        # Get indicator values
        rsi = ctx.indicators.RSI(self.params["rsi_period"])
        short_ma = ctx.indicators.SMA(self.params["ma_short"])
        long_ma = ctx.indicators.SMA(self.params["ma_long"])
        
        # Check if we have enough data
        if rsi is None or short_ma is None or long_ma is None:
            return Signal.NONE
        
        # Example strategy logic:
        # Buy when RSI is oversold AND short MA crosses above long MA
        # Sell when RSI is overbought AND short MA crosses below long MA
        
        signal = Signal.NONE
        
        if self.prev_short_ma is not None and self.prev_long_ma is not None:
            # Check for golden cross with RSI confirmation
            if (self.prev_short_ma <= self.prev_long_ma and 
                short_ma > long_ma and 
                rsi < self.params["rsi_oversold"]):
                signal = Signal.BUY
            
            # Check for death cross with RSI confirmation
            elif (self.prev_short_ma >= self.prev_long_ma and 
                  short_ma < long_ma and 
                  rsi > self.params["rsi_overbought"]):
                signal = Signal.SELL
        
        # Update state
        self.prev_short_ma = short_ma
        self.prev_long_ma = long_ma
        
        return signal
    
    def on_reset(self):
        """Reset strategy state when backtest restarts."""
        self.prev_short_ma = None
        self.prev_long_ma = None
