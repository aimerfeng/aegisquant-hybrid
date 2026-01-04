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

// ============================================================================
// Hybrid Backtest Mode FFI Types
// ============================================================================

/// Execution event types for hybrid backtest mode.
/// 
/// # FFI Safety
/// These constants are used with ExecutionEvent.event_type field.
pub const EVENT_TYPE_TRADE: i32 = 0;
pub const EVENT_TYPE_ORDER_REJECTED: i32 = 1;
pub const EVENT_TYPE_STOP_TRIGGERED: i32 = 2;
pub const EVENT_TYPE_TAKE_PROFIT_TRIGGERED: i32 = 3;

/// Signal types for external strategy integration.
/// 
/// # FFI Safety
/// These constants are used with place_order signal parameter.
pub const SIGNAL_NONE: i32 = 0;
pub const SIGNAL_BUY: i32 = 1;
pub const SIGNAL_SELL: i32 = 2;

/// Execution event structure for hybrid backtest mode.
/// 
/// Returned by process_tick_with_result to notify C# of trades,
/// order rejections, and stop/take-profit triggers.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
/// Uses "Caller Allocates" pattern - C# provides buffer, Rust writes events.
/// 
/// # Memory Model
/// - C# allocates ExecutionEvent[] buffer
/// - Rust writes events into the buffer
/// - C# reads events and reuses buffer for next call
/// - NO cross-language memory allocation/deallocation
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct ExecutionEvent {
    /// Event type: 0=Trade, 1=OrderRejected, 2=StopTriggered, 3=TakeProfitTriggered
    pub event_type: i32,
    /// Unix timestamp in nanoseconds when event occurred
    pub timestamp: i64,
    /// Execution price
    pub price: f64,
    /// Execution quantity
    pub quantity: f64,
    /// Side: 1=Buy, -1=Sell
    pub side: i32,
    /// Order ID (for tracking)
    pub order_id: i64,
    /// Realized PnL from this execution (0 if opening position)
    pub realized_pnl: f64,
}

/// Order result structure returned by place_order FFI.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct OrderResult {
    /// Whether the order was accepted (1) or rejected (0)
    pub accepted: i32,
    /// Order ID assigned by the engine (0 if rejected)
    pub order_id: i64,
    /// Fill price (0 if not filled yet or rejected)
    pub fill_price: f64,
    /// Fill quantity (0 if not filled yet or rejected)
    pub fill_quantity: f64,
    /// Rejection reason code (0 if accepted)
    /// 1=InsufficientCapital, 2=RiskLimitExceeded, 3=InvalidPrice, 4=InvalidQuantity
    pub rejection_code: i32,
}

/// Order rejection reason codes.
pub const REJECTION_NONE: i32 = 0;
pub const REJECTION_INSUFFICIENT_CAPITAL: i32 = 1;
pub const REJECTION_RISK_LIMIT_EXCEEDED: i32 = 2;
pub const REJECTION_INVALID_PRICE: i32 = 3;
pub const REJECTION_INVALID_QUANTITY: i32 = 4;
pub const REJECTION_POSITION_LIMIT: i32 = 5;
pub const REJECTION_THROTTLE_EXCEEDED: i32 = 6;

/// CSV column mapping configuration for flexible data import.
/// 
/// Allows C# to specify column names and date format for CSV files
/// with non-standard column names.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
/// String fields are fixed-size byte arrays (null-terminated UTF-8).
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CsvMapping {
    /// Column name for timestamp (e.g., "Date", "Time", "Timestamp")
    pub time_column: [u8; 32],
    /// Column name for price (e.g., "Close", "Price", "Last")
    pub price_column: [u8; 32],
    /// Column name for volume (e.g., "Volume", "Vol", "Qty")
    pub volume_column: [u8; 32],
    /// Column name for open price (optional, empty if not used)
    pub open_column: [u8; 32],
    /// Column name for high price (optional, empty if not used)
    pub high_column: [u8; 32],
    /// Column name for low price (optional, empty if not used)
    pub low_column: [u8; 32],
    /// Date format string (e.g., "yyyy-MM-dd", "unix", "auto")
    pub date_format: [u8; 32],
    /// Whether to skip the first row (header row)
    pub skip_header: i32,
}

impl Default for CsvMapping {
    fn default() -> Self {
        let mut mapping = Self {
            time_column: [0u8; 32],
            price_column: [0u8; 32],
            volume_column: [0u8; 32],
            open_column: [0u8; 32],
            high_column: [0u8; 32],
            low_column: [0u8; 32],
            date_format: [0u8; 32],
            skip_header: 1,
        };
        // Set default column names
        mapping.set_time_column("timestamp");
        mapping.set_price_column("price");
        mapping.set_volume_column("volume");
        mapping.set_date_format("unix");
        mapping
    }
}

impl CsvMapping {
    /// Set the time column name.
    pub fn set_time_column(&mut self, name: &str) {
        let bytes = name.as_bytes();
        let len = bytes.len().min(31);
        self.time_column[..len].copy_from_slice(&bytes[..len]);
        self.time_column[len] = 0;
    }

    /// Set the price column name.
    pub fn set_price_column(&mut self, name: &str) {
        let bytes = name.as_bytes();
        let len = bytes.len().min(31);
        self.price_column[..len].copy_from_slice(&bytes[..len]);
        self.price_column[len] = 0;
    }

    /// Set the volume column name.
    pub fn set_volume_column(&mut self, name: &str) {
        let bytes = name.as_bytes();
        let len = bytes.len().min(31);
        self.volume_column[..len].copy_from_slice(&bytes[..len]);
        self.volume_column[len] = 0;
    }

    /// Set the date format string.
    pub fn set_date_format(&mut self, format: &str) {
        let bytes = format.as_bytes();
        let len = bytes.len().min(31);
        self.date_format[..len].copy_from_slice(&bytes[..len]);
        self.date_format[len] = 0;
    }

    /// Get the time column name as a string slice.
    pub fn time_column_str(&self) -> &str {
        let end = self.time_column.iter().position(|&b| b == 0).unwrap_or(32);
        std::str::from_utf8(&self.time_column[..end]).unwrap_or("")
    }

    /// Get the price column name as a string slice.
    pub fn price_column_str(&self) -> &str {
        let end = self.price_column.iter().position(|&b| b == 0).unwrap_or(32);
        std::str::from_utf8(&self.price_column[..end]).unwrap_or("")
    }

    /// Get the volume column name as a string slice.
    pub fn volume_column_str(&self) -> &str {
        let end = self.volume_column.iter().position(|&b| b == 0).unwrap_or(32);
        std::str::from_utf8(&self.volume_column[..end]).unwrap_or("")
    }

    /// Get the date format as a string slice.
    pub fn date_format_str(&self) -> &str {
        let end = self.date_format.iter().position(|&b| b == 0).unwrap_or(32);
        std::str::from_utf8(&self.date_format[..end]).unwrap_or("")
    }

    /// Get the open column name as a string slice.
    pub fn open_column_str(&self) -> &str {
        let end = self.open_column.iter().position(|&b| b == 0).unwrap_or(32);
        std::str::from_utf8(&self.open_column[..end]).unwrap_or("")
    }

    /// Get the high column name as a string slice.
    pub fn high_column_str(&self) -> &str {
        let end = self.high_column.iter().position(|&b| b == 0).unwrap_or(32);
        std::str::from_utf8(&self.high_column[..end]).unwrap_or("")
    }

    /// Get the low column name as a string slice.
    pub fn low_column_str(&self) -> &str {
        let end = self.low_column.iter().position(|&b| b == 0).unwrap_or(32);
        std::str::from_utf8(&self.low_column[..end]).unwrap_or("")
    }
}

/// OHLC (Open-High-Low-Close) bar data for chart display.
/// 
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct OhlcBar {
    /// Unix timestamp in nanoseconds (bar open time)
    pub timestamp: i64,
    /// Open price
    pub open: f64,
    /// High price
    pub high: f64,
    /// Low price
    pub low: f64,
    /// Close price
    pub close: f64,
    /// Volume
    pub volume: f64,
}

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

    // ========================================================================
    // Hybrid Backtest Mode FFI Types Tests
    // ========================================================================

    #[test]
    fn test_execution_event_default() {
        let event = ExecutionEvent::default();
        assert_eq!(event.event_type, 0);
        assert_eq!(event.timestamp, 0);
        assert_eq!(event.price, 0.0);
        assert_eq!(event.quantity, 0.0);
        assert_eq!(event.side, 0);
        assert_eq!(event.order_id, 0);
        assert_eq!(event.realized_pnl, 0.0);
    }

    #[test]
    fn test_execution_event_size() {
        // ExecutionEvent: i32 + i64 + f64 + f64 + i32 + i64 + f64 = 4 + 8 + 8 + 8 + 4 + 8 + 8 = 48
        // With padding for alignment
        let size = std::mem::size_of::<ExecutionEvent>();
        assert!(size >= 48, "ExecutionEvent size should be at least 48 bytes, got {}", size);
    }

    #[test]
    fn test_order_result_default() {
        let result = OrderResult::default();
        assert_eq!(result.accepted, 0);
        assert_eq!(result.order_id, 0);
        assert_eq!(result.fill_price, 0.0);
        assert_eq!(result.fill_quantity, 0.0);
        assert_eq!(result.rejection_code, 0);
    }

    #[test]
    fn test_order_result_size() {
        // OrderResult: i32 + i64 + f64 + f64 + i32 = 4 + 8 + 8 + 8 + 4 = 32
        // With padding for alignment
        let size = std::mem::size_of::<OrderResult>();
        assert!(size >= 32, "OrderResult size should be at least 32 bytes, got {}", size);
    }

    #[test]
    fn test_csv_mapping_default() {
        let mapping = CsvMapping::default();
        assert_eq!(mapping.time_column_str(), "timestamp");
        assert_eq!(mapping.price_column_str(), "price");
        assert_eq!(mapping.volume_column_str(), "volume");
        assert_eq!(mapping.date_format_str(), "unix");
        assert_eq!(mapping.skip_header, 1);
    }

    #[test]
    fn test_csv_mapping_set_columns() {
        let mut mapping = CsvMapping::default();
        mapping.set_time_column("Date");
        mapping.set_price_column("Close");
        mapping.set_volume_column("Vol");
        mapping.set_date_format("yyyy-MM-dd");

        assert_eq!(mapping.time_column_str(), "Date");
        assert_eq!(mapping.price_column_str(), "Close");
        assert_eq!(mapping.volume_column_str(), "Vol");
        assert_eq!(mapping.date_format_str(), "yyyy-MM-dd");
    }

    #[test]
    fn test_csv_mapping_size() {
        // CsvMapping: 7 * [u8; 32] + i32 = 7 * 32 + 4 = 228
        let size = std::mem::size_of::<CsvMapping>();
        assert_eq!(size, 228, "CsvMapping size should be 228 bytes, got {}", size);
    }

    #[test]
    fn test_ohlc_bar_default() {
        let bar = OhlcBar::default();
        assert_eq!(bar.timestamp, 0);
        assert_eq!(bar.open, 0.0);
        assert_eq!(bar.high, 0.0);
        assert_eq!(bar.low, 0.0);
        assert_eq!(bar.close, 0.0);
        assert_eq!(bar.volume, 0.0);
    }

    #[test]
    fn test_ohlc_bar_size() {
        // OhlcBar: i64 + 5*f64 = 8 + 40 = 48
        let size = std::mem::size_of::<OhlcBar>();
        assert_eq!(size, 48, "OhlcBar size should be 48 bytes, got {}", size);
    }

    #[test]
    fn test_event_type_constants() {
        assert_eq!(EVENT_TYPE_TRADE, 0);
        assert_eq!(EVENT_TYPE_ORDER_REJECTED, 1);
        assert_eq!(EVENT_TYPE_STOP_TRIGGERED, 2);
        assert_eq!(EVENT_TYPE_TAKE_PROFIT_TRIGGERED, 3);
    }

    #[test]
    fn test_signal_constants() {
        assert_eq!(SIGNAL_NONE, 0);
        assert_eq!(SIGNAL_BUY, 1);
        assert_eq!(SIGNAL_SELL, 2);
    }

    #[test]
    fn test_rejection_constants() {
        assert_eq!(REJECTION_NONE, 0);
        assert_eq!(REJECTION_INSUFFICIENT_CAPITAL, 1);
        assert_eq!(REJECTION_RISK_LIMIT_EXCEEDED, 2);
        assert_eq!(REJECTION_INVALID_PRICE, 3);
        assert_eq!(REJECTION_INVALID_QUANTITY, 4);
        assert_eq!(REJECTION_POSITION_LIMIT, 5);
        assert_eq!(REJECTION_THROTTLE_EXCEEDED, 6);
    }
}
