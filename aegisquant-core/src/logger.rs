//! Structured logging module for AegisQuant-Hybrid.
//!
//! Provides structured logging with levels, correlation IDs, and FFI callback support.
//! Uses AtomicPtr for thread-safe callback management without locks.
//!
//! Requirements: 4.1, 4.2, 4.3, 4.4

use std::ffi::CString;
use std::sync::atomic::{AtomicPtr, AtomicU64, Ordering};

/// Log levels matching common logging conventions.
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub enum LogLevel {
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
}

impl LogLevel {
    /// Convert from i32 for FFI.
    pub fn from_i32(value: i32) -> Option<Self> {
        match value {
            0 => Some(LogLevel::Trace),
            1 => Some(LogLevel::Debug),
            2 => Some(LogLevel::Info),
            3 => Some(LogLevel::Warn),
            4 => Some(LogLevel::Error),
            _ => None,
        }
    }

    /// Get string representation.
    pub fn as_str(&self) -> &'static str {
        match self {
            LogLevel::Trace => "TRACE",
            LogLevel::Debug => "DEBUG",
            LogLevel::Info => "INFO",
            LogLevel::Warn => "WARN",
            LogLevel::Error => "ERROR",
        }
    }
}

/// Log callback function type for FFI.
pub type LogCallback = extern "C" fn(level: i32, message: *const std::ffi::c_char);

/// Global log callback storage using AtomicPtr for lock-free thread safety.
/// 
/// SAFETY: The pointer is either null or points to a valid function.
/// The callback function must remain valid for the lifetime of the program.
static LOG_CALLBACK: AtomicPtr<()> = AtomicPtr::new(std::ptr::null_mut());

/// Global correlation ID counter.
static CORRELATION_ID_COUNTER: AtomicU64 = AtomicU64::new(1);

/// Structured logger with correlation ID support.
#[derive(Debug)]
pub struct Logger {
    /// Minimum log level to output
    min_level: LogLevel,
    /// Current correlation ID for tracing related events
    correlation_id: u64,
    /// Logger name/context
    name: String,
}

impl Logger {
    /// Create a new logger with the given name.
    pub fn new(name: &str) -> Self {
        Self {
            min_level: LogLevel::Info,
            correlation_id: 0,
            name: name.to_string(),
        }
    }

    /// Set the minimum log level.
    pub fn with_level(mut self, level: LogLevel) -> Self {
        self.min_level = level;
        self
    }

    /// Set the correlation ID.
    pub fn with_correlation_id(mut self, id: u64) -> Self {
        self.correlation_id = id;
        self
    }

    /// Generate a new correlation ID.
    pub fn new_correlation_id() -> u64 {
        CORRELATION_ID_COUNTER.fetch_add(1, Ordering::SeqCst)
    }

    /// Set a new correlation ID and return it.
    pub fn start_correlation(&mut self) -> u64 {
        self.correlation_id = Self::new_correlation_id();
        self.correlation_id
    }

    /// Get the current correlation ID.
    pub fn correlation_id(&self) -> u64 {
        self.correlation_id
    }

    /// Log a message at the given level.
    pub fn log(&self, level: LogLevel, message: &str) {
        if level < self.min_level {
            return;
        }

        let formatted = if self.correlation_id > 0 {
            format!(
                "[{}] [{}] [cid:{}] {}",
                level.as_str(),
                self.name,
                self.correlation_id,
                message
            )
        } else {
            format!("[{}] [{}] {}", level.as_str(), self.name, message)
        };

        // Try to call the FFI callback using AtomicPtr
        let ptr = LOG_CALLBACK.load(Ordering::SeqCst);
        if !ptr.is_null() {
            // SAFETY: We only store valid function pointers in LOG_CALLBACK
            let callback: LogCallback = unsafe { std::mem::transmute(ptr) };
            if let Ok(c_string) = CString::new(formatted.clone()) {
                callback(level as i32, c_string.as_ptr());
            }
        }

        // Also print to stderr for debugging
        #[cfg(debug_assertions)]
        eprintln!("{}", formatted);
    }

    /// Log at TRACE level.
    pub fn trace(&self, message: &str) {
        self.log(LogLevel::Trace, message);
    }

    /// Log at DEBUG level.
    pub fn debug(&self, message: &str) {
        self.log(LogLevel::Debug, message);
    }

    /// Log at INFO level.
    pub fn info(&self, message: &str) {
        self.log(LogLevel::Info, message);
    }

    /// Log at WARN level.
    pub fn warn(&self, message: &str) {
        self.log(LogLevel::Warn, message);
    }

    /// Log at ERROR level.
    pub fn error(&self, message: &str) {
        self.log(LogLevel::Error, message);
    }

    // Structured logging methods for specific events

    /// Log an order creation event.
    pub fn log_order_created(&self, order_id: u64, symbol: &str, quantity: f64, direction: i32) {
        let dir_str = if direction > 0 { "BUY" } else { "SELL" };
        self.info(&format!(
            "ORDER_CREATED: id={}, symbol={}, qty={:.4}, dir={}",
            order_id, symbol, quantity, dir_str
        ));
    }

    /// Log an order fill event.
    pub fn log_order_filled(&self, order_id: u64, price: f64, commission: f64) {
        self.info(&format!(
            "ORDER_FILLED: id={}, price={:.4}, commission={:.4}",
            order_id, price, commission
        ));
    }

    /// Log an order rejection event.
    pub fn log_order_rejected(&self, order_id: u64, reason: &str) {
        self.warn(&format!("ORDER_REJECTED: id={}, reason={}", order_id, reason));
    }

    /// Log a risk check pass event.
    pub fn log_risk_check_passed(&self, order_id: u64) {
        self.debug(&format!("RISK_CHECK_PASSED: order_id={}", order_id));
    }

    /// Log a risk check fail event.
    pub fn log_risk_check_failed(&self, order_id: u64, reason: &str) {
        self.warn(&format!(
            "RISK_CHECK_FAILED: order_id={}, reason={}",
            order_id, reason
        ));
    }

    /// Log a strategy signal event.
    pub fn log_strategy_signal(&self, signal: &str, price: f64) {
        self.info(&format!("STRATEGY_SIGNAL: signal={}, price={:.4}", signal, price));
    }

    /// Log a data loading event.
    pub fn log_data_loaded(&self, tick_count: i64, valid_count: i64) {
        self.info(&format!(
            "DATA_LOADED: total={}, valid={}",
            tick_count, valid_count
        ));
    }

    /// Log a backtest start event.
    pub fn log_backtest_start(&self, symbol: &str, tick_count: usize) {
        self.info(&format!(
            "BACKTEST_START: symbol={}, ticks={}",
            symbol, tick_count
        ));
    }

    /// Log a backtest end event.
    pub fn log_backtest_end(&self, final_equity: f64, total_return_pct: f64) {
        self.info(&format!(
            "BACKTEST_END: final_equity={:.2}, return={:.2}%",
            final_equity, total_return_pct
        ));
    }
}

impl Default for Logger {
    fn default() -> Self {
        Self::new("AegisQuant")
    }
}

/// Set the global log callback for FFI using AtomicPtr.
///
/// # Safety
/// The callback must be a valid function pointer that remains valid
/// for the lifetime of the program or until clear_log_callback is called.
pub fn set_log_callback(callback: LogCallback) {
    // SAFETY: We cast the function pointer to *mut () for storage.
    // This is safe because we only read it back as LogCallback.
    LOG_CALLBACK.store(callback as *mut (), Ordering::SeqCst);
}

/// Clear the global log callback.
/// After calling this, log messages will be silently ignored (no callback invoked).
pub fn clear_log_callback() {
    LOG_CALLBACK.store(std::ptr::null_mut(), Ordering::SeqCst);
}

/// Check if a log callback is currently set.
pub fn has_log_callback() -> bool {
    !LOG_CALLBACK.load(Ordering::SeqCst).is_null()
}

/// Global log function for use without a Logger instance.
/// Useful for macros and quick logging.
pub fn log(level: LogLevel, message: &str) {
    let ptr = LOG_CALLBACK.load(Ordering::SeqCst);
    if ptr.is_null() {
        return;
    }

    // SAFETY: We only store valid function pointers in LOG_CALLBACK
    let callback: LogCallback = unsafe { std::mem::transmute(ptr) };
    if let Ok(c_string) = CString::new(message) {
        callback(level as i32, c_string.as_ptr());
    }
}

/// Log at INFO level (global function).
#[inline]
pub fn log_info(message: &str) {
    log(LogLevel::Info, message);
}

/// Log at WARN level (global function).
#[inline]
pub fn log_warn(message: &str) {
    log(LogLevel::Warn, message);
}

/// Log at ERROR level (global function).
#[inline]
pub fn log_error(message: &str) {
    log(LogLevel::Error, message);
}

/// Log at DEBUG level (global function).
#[inline]
pub fn log_debug(message: &str) {
    log(LogLevel::Debug, message);
}

/// Log at TRACE level (global function).
#[inline]
pub fn log_trace(message: &str) {
    log(LogLevel::Trace, message);
}

/// Macro for logging at INFO level with format string.
#[macro_export]
macro_rules! log_info {
    ($($arg:tt)*) => {
        $crate::logger::log($crate::logger::LogLevel::Info, &format!($($arg)*))
    };
}

/// Macro for logging at WARN level with format string.
#[macro_export]
macro_rules! log_warn {
    ($($arg:tt)*) => {
        $crate::logger::log($crate::logger::LogLevel::Warn, &format!($($arg)*))
    };
}

/// Macro for logging at ERROR level with format string.
#[macro_export]
macro_rules! log_error {
    ($($arg:tt)*) => {
        $crate::logger::log($crate::logger::LogLevel::Error, &format!($($arg)*))
    };
}

/// Macro for logging at DEBUG level with format string.
#[macro_export]
macro_rules! log_debug {
    ($($arg:tt)*) => {
        $crate::logger::log($crate::logger::LogLevel::Debug, &format!($($arg)*))
    };
}

/// Macro for logging at TRACE level with format string.
#[macro_export]
macro_rules! log_trace {
    ($($arg:tt)*) => {
        $crate::logger::log($crate::logger::LogLevel::Trace, &format!($($arg)*))
    };
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::AtomicI32;

    static TEST_CALLBACK_COUNT: AtomicI32 = AtomicI32::new(0);

    extern "C" fn test_callback(_level: i32, _message: *const std::ffi::c_char) {
        TEST_CALLBACK_COUNT.fetch_add(1, Ordering::SeqCst);
    }

    #[test]
    fn test_logger_creation() {
        let logger = Logger::new("TestLogger");
        assert_eq!(logger.name, "TestLogger");
        assert_eq!(logger.min_level, LogLevel::Info);
    }

    #[test]
    fn test_log_level_ordering() {
        assert!(LogLevel::Trace < LogLevel::Debug);
        assert!(LogLevel::Debug < LogLevel::Info);
        assert!(LogLevel::Info < LogLevel::Warn);
        assert!(LogLevel::Warn < LogLevel::Error);
    }

    #[test]
    fn test_log_level_from_i32() {
        assert_eq!(LogLevel::from_i32(0), Some(LogLevel::Trace));
        assert_eq!(LogLevel::from_i32(1), Some(LogLevel::Debug));
        assert_eq!(LogLevel::from_i32(2), Some(LogLevel::Info));
        assert_eq!(LogLevel::from_i32(3), Some(LogLevel::Warn));
        assert_eq!(LogLevel::from_i32(4), Some(LogLevel::Error));
        assert_eq!(LogLevel::from_i32(5), None);
    }

    #[test]
    fn test_correlation_id() {
        let mut logger = Logger::new("Test");
        let id1 = logger.start_correlation();
        let id2 = Logger::new_correlation_id();
        assert!(id2 > id1);
    }

    #[test]
    fn test_log_callback() {
        TEST_CALLBACK_COUNT.store(0, Ordering::SeqCst);
        set_log_callback(test_callback);

        let logger = Logger::new("Test").with_level(LogLevel::Info);
        logger.info("Test message");

        let count = TEST_CALLBACK_COUNT.load(Ordering::SeqCst);
        assert!(count >= 1, "Callback should have been called");

        clear_log_callback();
    }

    #[test]
    fn test_structured_logging() {
        let logger = Logger::new("Test").with_level(LogLevel::Debug);
        
        // These should not panic
        logger.log_order_created(1, "BTCUSDT", 1.5, 1);
        logger.log_order_filled(1, 50000.0, 5.0);
        logger.log_order_rejected(2, "Insufficient funds");
        logger.log_risk_check_passed(1);
        logger.log_risk_check_failed(2, "Position limit exceeded");
        logger.log_strategy_signal("BUY", 50000.0);
        logger.log_data_loaded(1000, 995);
        logger.log_backtest_start("BTCUSDT", 1000);
        logger.log_backtest_end(105000.0, 5.0);
    }

    #[test]
    fn test_min_level_filtering() {
        TEST_CALLBACK_COUNT.store(0, Ordering::SeqCst);
        set_log_callback(test_callback);

        let logger = Logger::new("Test").with_level(LogLevel::Warn);
        
        // These should be filtered out
        logger.trace("trace");
        logger.debug("debug");
        logger.info("info");
        
        let count_before_warn = TEST_CALLBACK_COUNT.load(Ordering::SeqCst);
        
        // These should pass through
        logger.warn("warn");
        logger.error("error");
        
        let count_after = TEST_CALLBACK_COUNT.load(Ordering::SeqCst);
        assert!(count_after > count_before_warn, "Warn and Error should pass through");

        clear_log_callback();
    }
}
