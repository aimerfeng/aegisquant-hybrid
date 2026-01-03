# MACD Strategy Example
# Buy when MACD crosses above signal line, sell when MACD crosses below

from aegisquant import Strategy, Signal, Context

class MACDStrategy(Strategy):
    """
    MACD (Moving Average Convergence Divergence) Strategy
    
    Generates buy signals when MACD line crosses above signal line,
    and sell signals when MACD line crosses below signal line.
    """
    
    def __init__(self):
        super().__init__()
        self.name = "MACD Strategy"
        self.description = "MACD crossover strategy"
        
        # Strategy parameters
        self.params = {
            "fast_period": 12,
            "slow_period": 26,
            "signal_period": 9
        }
        
        # State for crossover detection
        self.prev_macd = None
        self.prev_signal = None
    
    def on_tick(self, ctx: Context) -> Signal:
        """
        Process each tick and generate trading signals.
        
        Args:
            ctx: Strategy context with price data and indicators
            
        Returns:
            Signal.BUY, Signal.SELL, or Signal.NONE
        """
        # Calculate MACD
        macd, signal, histogram = ctx.indicators.MACD(
            self.params["fast_period"],
            self.params["slow_period"],
            self.params["signal_period"]
        )
        
        # Check if we have enough data
        if macd is None or signal is None:
            return Signal.NONE
        
        # Detect crossover
        result = Signal.NONE
        
        if self.prev_macd is not None and self.prev_signal is not None:
            # Golden cross: MACD crosses above signal
            if self.prev_macd <= self.prev_signal and macd > signal:
                result = Signal.BUY
            # Death cross: MACD crosses below signal
            elif self.prev_macd >= self.prev_signal and macd < signal:
                result = Signal.SELL
        
        # Update previous values
        self.prev_macd = macd
        self.prev_signal = signal
        
        return result
    
    def on_reset(self):
        """Reset strategy state."""
        self.prev_macd = None
        self.prev_signal = None
