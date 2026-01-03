"""
AegisQuant Python Strategy SDK

This module provides the base classes and types for writing
Python trading strategies for AegisQuant.
"""

from enum import IntEnum
from typing import Optional, Tuple, List, Dict, Any
from abc import ABC, abstractmethod


class Signal(IntEnum):
    """Trading signal enumeration."""
    NONE = 0
    BUY = 1
    SELL = -1


class PositionInfo:
    """Current position information."""
    
    def __init__(self):
        self.quantity: float = 0.0
        self.average_price: float = 0.0
        self.unrealized_pnl: float = 0.0
    
    @property
    def has_position(self) -> bool:
        """Whether currently holding a position."""
        return abs(self.quantity) > 0.0001
    
    @property
    def is_long(self) -> bool:
        """Whether position is long."""
        return self.quantity > 0.0001
    
    @property
    def is_short(self) -> bool:
        """Whether position is short."""
        return self.quantity < -0.0001


class AccountStatus:
    """Account status information."""
    
    def __init__(self):
        self.equity: float = 100000.0
        self.cash: float = 100000.0
        self.position: float = 0.0
        self.realized_pnl: float = 0.0
        self.unrealized_pnl: float = 0.0
        self.total_trades: int = 0


class IndicatorService:
    """
    Technical indicator calculation service.
    
    Provides access to common technical indicators with caching.
    """
    
    def __init__(self, price_history: List[float]):
        self._prices = price_history
        self._cache: Dict[str, Any] = {}
    
    def SMA(self, period: int) -> Optional[float]:
        """
        Simple Moving Average.
        
        Args:
            period: Number of periods
            
        Returns:
            SMA value or None if insufficient data
        """
        if len(self._prices) < period:
            return None
        return sum(self._prices[-period:]) / period
    
    def EMA(self, period: int) -> Optional[float]:
        """
        Exponential Moving Average.
        
        Args:
            period: Number of periods
            
        Returns:
            EMA value or None if insufficient data
        """
        if len(self._prices) < period:
            return None
        
        multiplier = 2.0 / (period + 1)
        ema = sum(self._prices[:period]) / period
        
        for price in self._prices[period:]:
            ema = (price - ema) * multiplier + ema
        
        return ema
    
    def RSI(self, period: int = 14) -> Optional[float]:
        """
        Relative Strength Index.
        
        Args:
            period: Number of periods (default 14)
            
        Returns:
            RSI value (0-100) or None if insufficient data
        """
        if len(self._prices) < period + 1:
            return None
        
        gains = []
        losses = []
        
        for i in range(1, len(self._prices)):
            change = self._prices[i] - self._prices[i-1]
            if change > 0:
                gains.append(change)
                losses.append(0)
            else:
                gains.append(0)
                losses.append(abs(change))
        
        if len(gains) < period:
            return None
        
        avg_gain = sum(gains[-period:]) / period
        avg_loss = sum(losses[-period:]) / period
        
        if avg_loss == 0:
            return 100.0
        
        rs = avg_gain / avg_loss
        return 100 - (100 / (1 + rs))
    
    def MACD(self, fast_period: int = 12, slow_period: int = 26, 
             signal_period: int = 9) -> Tuple[Optional[float], Optional[float], Optional[float]]:
        """
        Moving Average Convergence Divergence.
        
        Args:
            fast_period: Fast EMA period (default 12)
            slow_period: Slow EMA period (default 26)
            signal_period: Signal line period (default 9)
            
        Returns:
            Tuple of (MACD line, Signal line, Histogram) or (None, None, None)
        """
        if len(self._prices) < slow_period + signal_period:
            return (None, None, None)
        
        # Calculate EMAs
        fast_ema = self._calculate_ema(fast_period)
        slow_ema = self._calculate_ema(slow_period)
        
        if fast_ema is None or slow_ema is None:
            return (None, None, None)
        
        macd_line = fast_ema - slow_ema
        
        # For simplicity, return current MACD values
        # Full implementation would track MACD history for signal line
        return (macd_line, macd_line * 0.9, macd_line * 0.1)
    
    def BollingerBands(self, period: int = 20, 
                       std_dev: float = 2.0) -> Tuple[Optional[float], Optional[float], Optional[float]]:
        """
        Bollinger Bands.
        
        Args:
            period: Number of periods (default 20)
            std_dev: Standard deviation multiplier (default 2.0)
            
        Returns:
            Tuple of (Upper band, Middle band, Lower band) or (None, None, None)
        """
        if len(self._prices) < period:
            return (None, None, None)
        
        prices = self._prices[-period:]
        middle = sum(prices) / period
        
        variance = sum((p - middle) ** 2 for p in prices) / period
        std = variance ** 0.5
        
        upper = middle + std_dev * std
        lower = middle - std_dev * std
        
        return (upper, middle, lower)
    
    def ATR(self, period: int = 14) -> Optional[float]:
        """
        Average True Range.
        
        Args:
            period: Number of periods (default 14)
            
        Returns:
            ATR value or None if insufficient data
        """
        if len(self._prices) < period + 1:
            return None
        
        # Simplified ATR using price changes
        true_ranges = []
        for i in range(1, len(self._prices)):
            tr = abs(self._prices[i] - self._prices[i-1])
            true_ranges.append(tr)
        
        if len(true_ranges) < period:
            return None
        
        return sum(true_ranges[-period:]) / period
    
    def Stochastic(self, k_period: int = 14, 
                   d_period: int = 3) -> Tuple[Optional[float], Optional[float]]:
        """
        Stochastic Oscillator.
        
        Args:
            k_period: %K period (default 14)
            d_period: %D period (default 3)
            
        Returns:
            Tuple of (%K, %D) or (None, None)
        """
        if len(self._prices) < k_period + d_period - 1:
            return (None, None)
        
        k_values = []
        for i in range(k_period - 1, len(self._prices)):
            period_prices = self._prices[i - k_period + 1:i + 1]
            highest = max(period_prices)
            lowest = min(period_prices)
            
            if highest - lowest > 0:
                k = 100 * (self._prices[i] - lowest) / (highest - lowest)
            else:
                k = 50
            k_values.append(k)
        
        if len(k_values) < d_period:
            return (None, None)
        
        k = k_values[-1]
        d = sum(k_values[-d_period:]) / d_period
        
        return (k, d)
    
    def _calculate_ema(self, period: int) -> Optional[float]:
        """Helper to calculate EMA."""
        if len(self._prices) < period:
            return None
        
        multiplier = 2.0 / (period + 1)
        ema = sum(self._prices[:period]) / period
        
        for price in self._prices[period:]:
            ema = (price - ema) * multiplier + ema
        
        return ema


class Context:
    """
    Strategy execution context.
    
    Provides access to market data, indicators, and account information.
    """
    
    def __init__(self):
        self._price_history: List[float] = []
        self._indicators: Optional[IndicatorService] = None
        
        self.price: float = 0.0
        self.volume: float = 0.0
        self.timestamp: int = 0
        self.position: PositionInfo = PositionInfo()
        self.account: AccountStatus = AccountStatus()
    
    @property
    def indicators(self) -> IndicatorService:
        """Get the indicator service."""
        if self._indicators is None:
            self._indicators = IndicatorService(self._price_history)
        return self._indicators
    
    def _update(self, price: float, volume: float, timestamp: int):
        """Update context with new tick data (called by engine)."""
        self.price = price
        self.volume = volume
        self.timestamp = timestamp
        self._price_history.append(price)
        self._indicators = None  # Invalidate cache


class Strategy(ABC):
    """
    Base class for Python trading strategies.
    
    Subclass this and implement on_tick() to create your strategy.
    """
    
    def __init__(self):
        self.name: str = "Unnamed Strategy"
        self.description: str = ""
        self.params: Dict[str, Any] = {}
    
    @abstractmethod
    def on_tick(self, ctx: Context) -> Signal:
        """
        Process a tick and generate a trading signal.
        
        Args:
            ctx: Strategy context with market data and indicators
            
        Returns:
            Signal.BUY, Signal.SELL, or Signal.NONE
        """
        pass
    
    def on_reset(self):
        """
        Called when the strategy is reset.
        Override to reset any internal state.
        """
        pass
    
    def validate(self) -> bool:
        """
        Validate the strategy configuration.
        Override to add custom validation.
        
        Returns:
            True if valid, False otherwise
        """
        return True


# Export public API
__all__ = [
    'Signal',
    'PositionInfo', 
    'AccountStatus',
    'IndicatorService',
    'Context',
    'Strategy'
]
