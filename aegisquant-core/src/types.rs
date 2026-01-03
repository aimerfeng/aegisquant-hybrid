//! Core FFI-compatible data structures for AegisQuant-Hybrid.
//!
//! All structs use `#[repr(C)]` to ensure memory layout compatibility
//! with C# `StructLayout.Sequential`.

/// Tick data representing a single market data point.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Tick {
    /// Unix timestamp in nanoseconds
    pub timestamp: i64,
    /// Price (f64 for performance)
    pub price: f64,
    /// Volume
    pub volume: f64,
}

impl Default for Tick {
    fn default() -> Self {
        Self {
            timestamp: 0,
            price: 0.0,
            volume: 0.0,
        }
    }
}

/// Order request structure for submitting orders.
/// 
/// # FFI Safety
/// - `symbol` is a fixed-size array (null-terminated UTF-8)
/// - `direction`: 1 = Buy, -1 = Sell
/// - `order_type`: 0 = Market, 1 = Limit
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct OrderRequest {
    /// Symbol as fixed-size byte array (null-terminated)
    pub symbol: [u8; 16],
    /// Order quantity
    pub quantity: f64,
    /// Direction: 1 = Buy, -1 = Sell
    pub direction: i32,
    /// Order type: 0 = Market, 1 = Limit
    pub order_type: i32,
    /// Limit price (ignored for Market orders)
    pub limit_price: f64,
}

impl Default for OrderRequest {
    fn default() -> Self {
        Self {
            symbol: [0u8; 16],
            quantity: 0.0,
            direction: 0,
            order_type: 0,
            limit_price: 0.0,
        }
    }
}

impl OrderRequest {
    /// Create a new OrderRequest with the given symbol.
    pub fn with_symbol(symbol: &str) -> Self {
        let mut req = Self::default();
        let bytes = symbol.as_bytes();
        let len = bytes.len().min(15); // Leave room for null terminator
        req.symbol[..len].copy_from_slice(&bytes[..len]);
        req
    }

    /// Get the symbol as a string slice.
    pub fn symbol_str(&self) -> &str {
        let end = self.symbol.iter().position(|&b| b == 0).unwrap_or(16);
        std::str::from_utf8(&self.symbol[..end]).unwrap_or("")
    }
}


/// Position structure representing a held position.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Position {
    /// Symbol as fixed-size byte array (null-terminated)
    pub symbol: [u8; 16],
    /// Position quantity (positive = long, negative = short)
    pub quantity: f64,
    /// Average entry price
    pub average_price: f64,
    /// Unrealized profit/loss
    pub unrealized_pnl: f64,
    /// Realized profit/loss
    pub realized_pnl: f64,
}

impl Default for Position {
    fn default() -> Self {
        Self {
            symbol: [0u8; 16],
            quantity: 0.0,
            average_price: 0.0,
            unrealized_pnl: 0.0,
            realized_pnl: 0.0,
        }
    }
}

impl Position {
    /// Create a new Position with the given symbol.
    pub fn with_symbol(symbol: &str) -> Self {
        let mut pos = Self::default();
        let bytes = symbol.as_bytes();
        let len = bytes.len().min(15);
        pos.symbol[..len].copy_from_slice(&bytes[..len]);
        pos
    }

    /// Get the symbol as a string slice.
    pub fn symbol_str(&self) -> &str {
        let end = self.symbol.iter().position(|&b| b == 0).unwrap_or(16);
        std::str::from_utf8(&self.symbol[..end]).unwrap_or("")
    }
}

/// Account status structure.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
/// Note: Internal calculations use Decimal for precision, exported as f64.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct AccountStatus {
    /// Account balance
    pub balance: f64,
    /// Net equity = balance + unrealized_pnl
    pub equity: f64,
    /// Available funds for trading
    pub available: f64,
    /// Number of open positions
    pub position_count: i32,
    /// Total profit/loss
    pub total_pnl: f64,
}

impl Default for AccountStatus {
    fn default() -> Self {
        Self {
            balance: 0.0,
            equity: 0.0,
            available: 0.0,
            position_count: 0,
            total_pnl: 0.0,
        }
    }
}

/// Strategy parameters for the dual moving average strategy.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct StrategyParams {
    /// Short moving average period
    pub short_ma_period: i32,
    /// Long moving average period
    pub long_ma_period: i32,
    /// Position size per trade
    pub position_size: f64,
    /// Stop loss percentage (e.g., 0.02 = 2%)
    pub stop_loss_pct: f64,
    /// Take profit percentage (e.g., 0.05 = 5%)
    pub take_profit_pct: f64,
    /// Number of bars to warm up before generating signals
    pub warmup_bars: i32,
}

impl Default for StrategyParams {
    fn default() -> Self {
        Self {
            short_ma_period: 5,
            long_ma_period: 20,
            position_size: 100.0,
            stop_loss_pct: 0.02,
            take_profit_pct: 0.05,
            warmup_bars: 0,
        }
    }
}


/// Risk configuration parameters.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct RiskConfig {
    /// Maximum orders per second
    pub max_order_rate: i32,
    /// Maximum position size
    pub max_position_size: f64,
    /// Maximum single order value
    pub max_order_value: f64,
    /// Maximum drawdown percentage (e.g., 0.1 = 10%)
    pub max_drawdown_pct: f64,
}

impl Default for RiskConfig {
    fn default() -> Self {
        Self {
            max_order_rate: 10,
            max_position_size: 1000.0,
            max_order_value: 100000.0,
            max_drawdown_pct: 0.1,
        }
    }
}

/// Data quality report from data cleansing.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DataQualityReport {
    /// Total number of ticks processed
    pub total_ticks: i64,
    /// Number of valid ticks
    pub valid_ticks: i64,
    /// Number of invalid ticks (price <= 0 or volume < 0)
    pub invalid_ticks: i64,
    /// Number of anomaly ticks (price jumps > 10%)
    pub anomaly_ticks: i64,
    /// First timestamp in the dataset
    pub first_timestamp: i64,
    /// Last timestamp in the dataset
    pub last_timestamp: i64,
}

/// Backtest result structure.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct BacktestResult {
    /// Final equity value
    pub final_equity: f64,
    /// Total return percentage
    pub total_return_pct: f64,
    /// Maximum drawdown percentage
    pub max_drawdown_pct: f64,
    /// Sharpe ratio
    pub sharpe_ratio: f64,
    /// Total number of trades
    pub total_trades: i32,
    /// Number of winning trades
    pub winning_trades: i32,
    /// Number of losing trades
    pub losing_trades: i32,
    /// Actual start bar (after warmup period)
    pub actual_start_bar: i32,
    /// First trade timestamp (0 if no trades)
    pub first_trade_timestamp: i64,
}

impl Default for BacktestResult {
    fn default() -> Self {
        Self {
            final_equity: 0.0,
            total_return_pct: 0.0,
            max_drawdown_pct: 0.0,
            sharpe_ratio: 0.0,
            total_trades: 0,
            winning_trades: 0,
            losing_trades: 0,
            actual_start_bar: 0,
            first_trade_timestamp: 0,
        }
    }
}

// Direction constants
pub const DIRECTION_BUY: i32 = 1;
pub const DIRECTION_SELL: i32 = -1;

// Order type constants
pub const ORDER_TYPE_MARKET: i32 = 0;
pub const ORDER_TYPE_LIMIT: i32 = 1;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_tick_default() {
        let tick = Tick::default();
        assert_eq!(tick.timestamp, 0);
        assert_eq!(tick.price, 0.0);
        assert_eq!(tick.volume, 0.0);
    }

    #[test]
    fn test_order_request_symbol() {
        let order = OrderRequest::with_symbol("BTCUSDT");
        assert_eq!(order.symbol_str(), "BTCUSDT");
    }

    #[test]
    fn test_position_symbol() {
        let pos = Position::with_symbol("ETHUSDT");
        assert_eq!(pos.symbol_str(), "ETHUSDT");
    }

    #[test]
    fn test_struct_sizes() {
        // Verify struct sizes for FFI compatibility
        assert_eq!(std::mem::size_of::<Tick>(), 24); // i64 + f64 + f64
        assert_eq!(std::mem::size_of::<AccountStatus>(), 40); // 4*f64 + i32 + padding
    }
}
