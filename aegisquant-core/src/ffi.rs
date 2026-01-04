//! FFI (Foreign Function Interface) layer for C# interop.
//!
//! All functions use `extern "C"` ABI and `#[no_mangle]` for C# P/Invoke compatibility.
//! Error handling uses return codes instead of panics to ensure FFI safety.

use std::ffi::c_char;
use std::panic::catch_unwind;

use crate::types::*;

// ============================================================================
// Error Codes
// ============================================================================

/// Operation completed successfully
pub const ERR_SUCCESS: i32 = 0;
/// Null pointer was passed to function
pub const ERR_NULL_POINTER: i32 = -1;
/// Invalid parameter value
pub const ERR_INVALID_PARAM: i32 = -2;
/// Engine not initialized
pub const ERR_ENGINE_NOT_INIT: i32 = -3;
/// Order rejected by risk manager
pub const ERR_RISK_REJECTED: i32 = -4;
/// Failed to load data file
pub const ERR_DATA_LOAD_FAILED: i32 = -5;
/// Invalid data (e.g., negative price)
pub const ERR_INVALID_DATA: i32 = -6;
/// Insufficient capital for order
pub const ERR_INSUFFICIENT_CAPITAL: i32 = -7;
/// Order rate throttle exceeded
pub const ERR_THROTTLE_EXCEEDED: i32 = -8;
/// Position limit exceeded
pub const ERR_POSITION_LIMIT: i32 = -9;
/// File not found
pub const ERR_FILE_NOT_FOUND: i32 = -10;
/// Internal panic (should not happen)
pub const ERR_INTERNAL_PANIC: i32 = -99;

// ============================================================================
// Engine Handle (Placeholder - will be implemented in Phase 2)
// ============================================================================

/// Opaque engine handle for FFI.
/// This is a placeholder that will be replaced with BacktestEngine in Phase 2.
pub struct EngineHandle {
    pub params: StrategyParams,
    pub risk_config: RiskConfig,
    pub account: AccountStatus,
    pub initialized: bool,
    /// Event queue for execution events (hybrid backtest mode)
    pub event_queue: Vec<ExecutionEvent>,
    /// Current position quantity (positive = long, negative = short)
    pub position_quantity: f64,
    /// Average entry price for current position
    pub entry_price: f64,
    /// Next order ID
    pub next_order_id: i64,
    /// Current timestamp
    pub current_timestamp: i64,
    /// Tick count processed
    pub tick_count: i64,
    /// Loaded tick data for OHLC aggregation
    pub ticks: Vec<Tick>,
    /// Pre-computed OHLC bars (1-minute)
    pub ohlc_bars: Vec<OhlcBar>,
    /// Current tick index for fast-forward/replay
    pub current_tick_index: i64,
}

impl EngineHandle {
    fn new(params: StrategyParams, risk_config: RiskConfig) -> Self {
        Self {
            params,
            risk_config,
            account: AccountStatus {
                balance: 100_000.0,
                equity: 100_000.0,
                available: 100_000.0,
                position_count: 0,
                total_pnl: 0.0,
            },
            initialized: true,
            event_queue: Vec::with_capacity(16),
            position_quantity: 0.0,
            entry_price: 0.0,
            next_order_id: 1,
            current_timestamp: 0,
            tick_count: 0,
            ticks: Vec::new(),
            ohlc_bars: Vec::new(),
            current_tick_index: 0,
        }
    }

    /// Process a tick and check for stop-loss/take-profit triggers.
    /// Returns the number of events generated.
    fn process_tick_internal(&mut self, tick: &Tick) -> usize {
        self.current_timestamp = tick.timestamp;
        self.tick_count += 1;
        
        // Clear previous events
        self.event_queue.clear();
        
        // Check stop-loss and take-profit if we have a position
        if self.position_quantity.abs() > 0.0001 {
            let pnl_pct = if self.position_quantity > 0.0 {
                // Long position
                (tick.price - self.entry_price) / self.entry_price
            } else {
                // Short position
                (self.entry_price - tick.price) / self.entry_price
            };
            
            // Check stop-loss
            if pnl_pct <= -self.params.stop_loss_pct {
                let realized_pnl = self.position_quantity * (tick.price - self.entry_price);
                let event = ExecutionEvent {
                    event_type: EVENT_TYPE_STOP_TRIGGERED,
                    timestamp: tick.timestamp,
                    price: tick.price,
                    quantity: self.position_quantity.abs(),
                    side: if self.position_quantity > 0.0 { DIRECTION_SELL } else { DIRECTION_BUY },
                    order_id: self.next_order_id,
                    realized_pnl,
                };
                self.next_order_id += 1;
                self.event_queue.push(event);
                
                // Update account
                self.account.balance += realized_pnl;
                self.account.total_pnl += realized_pnl;
                self.position_quantity = 0.0;
                self.entry_price = 0.0;
                self.account.position_count = 0;
            }
            // Check take-profit
            else if pnl_pct >= self.params.take_profit_pct {
                let realized_pnl = self.position_quantity * (tick.price - self.entry_price);
                let event = ExecutionEvent {
                    event_type: EVENT_TYPE_TAKE_PROFIT_TRIGGERED,
                    timestamp: tick.timestamp,
                    price: tick.price,
                    quantity: self.position_quantity.abs(),
                    side: if self.position_quantity > 0.0 { DIRECTION_SELL } else { DIRECTION_BUY },
                    order_id: self.next_order_id,
                    realized_pnl,
                };
                self.next_order_id += 1;
                self.event_queue.push(event);
                
                // Update account
                self.account.balance += realized_pnl;
                self.account.total_pnl += realized_pnl;
                self.position_quantity = 0.0;
                self.entry_price = 0.0;
                self.account.position_count = 0;
            }
        }
        
        // Update equity based on current position
        if self.position_quantity.abs() > 0.0001 {
            let unrealized_pnl = self.position_quantity * (tick.price - self.entry_price);
            self.account.equity = self.account.balance + unrealized_pnl;
        } else {
            self.account.equity = self.account.balance;
        }
        
        self.event_queue.len()
    }
}

// ============================================================================
// FFI Functions
// ============================================================================

/// Initialize a new backtest engine.
///
/// # Safety
/// - `params` must be a valid pointer to StrategyParams or null (uses defaults)
/// - `risk_config` must be a valid pointer to RiskConfig or null (uses defaults)
/// - Caller must call `free_engine` to release the returned pointer
///
/// # Returns
/// - Valid engine pointer on success
/// - Null pointer on failure
#[no_mangle]
pub unsafe extern "C" fn init_engine(
    params: *const StrategyParams,
    risk_config: *const RiskConfig,
) -> *mut EngineHandle {
    let result = catch_unwind(|| {
        let strategy_params = if params.is_null() {
            StrategyParams::default()
        } else {
            // SAFETY: Caller guarantees params is valid
            *params
        };

        let risk_cfg = if risk_config.is_null() {
            RiskConfig::default()
        } else {
            // SAFETY: Caller guarantees risk_config is valid
            *risk_config
        };

        let engine = Box::new(EngineHandle::new(strategy_params, risk_cfg));
        Box::into_raw(engine)
    });

    match result {
        Ok(ptr) => ptr,
        Err(_) => std::ptr::null_mut(),
    }
}

/// Free engine resources.
///
/// # Safety
/// - `engine` must be a valid pointer returned by `init_engine`
/// - Must only be called once per engine
/// - After calling, the engine pointer is invalid
#[no_mangle]
pub unsafe extern "C" fn free_engine(engine: *mut EngineHandle) {
    if engine.is_null() {
        return;
    }

    let _ = catch_unwind(|| {
        // SAFETY: Caller guarantees engine is valid and this is called only once
        let _ = Box::from_raw(engine);
    });
}

/// Process a single tick.
///
/// # Safety
/// - `engine` must be a valid engine pointer from `init_engine`
/// - `tick` must be a valid pointer to Tick data
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if engine or tick is null
/// - ERR_INVALID_DATA if tick data is invalid
#[no_mangle]
pub unsafe extern "C" fn process_tick(
    engine: *mut EngineHandle,
    tick: *const Tick,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if tick.is_null() {
        return ERR_NULL_POINTER;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let _engine = &mut *engine;
        let tick_data = &*tick;

        // Validate tick data
        if tick_data.price <= 0.0 {
            return ERR_INVALID_DATA;
        }
        if tick_data.volume < 0.0 {
            return ERR_INVALID_DATA;
        }

        // TODO: Implement actual tick processing in Phase 2
        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Process a single tick and return execution events to caller-provided buffer.
///
/// This is the primary FFI function for hybrid backtest mode. It processes
/// a tick through the engine and returns any execution events (trades,
/// stop-loss triggers, take-profit triggers) to the caller's pre-allocated buffer.
///
/// # Safety
/// - `engine` must be a valid engine pointer from `init_engine`
/// - `tick` must be a valid pointer to Tick data
/// - `out_events_buffer` must be a valid pointer to an array of at least `buffer_capacity` ExecutionEvents
/// - `out_event_count` must be a valid pointer to write the actual event count
///
/// # Memory Model (Caller Allocates Pattern)
/// - C# allocates the buffer (e.g., ExecutionEvent[16])
/// - Rust writes events into the buffer
/// - C# reads events and reuses the buffer for next call
/// - NO cross-language memory allocation/deallocation
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if any pointer is null
/// - ERR_INVALID_DATA if tick data is invalid
/// - ERR_INVALID_PARAM if buffer_capacity <= 0
#[no_mangle]
pub unsafe extern "C" fn process_tick_with_result(
    engine: *mut EngineHandle,
    tick: *const Tick,
    out_events_buffer: *mut ExecutionEvent,
    buffer_capacity: i32,
    out_event_count: *mut i32,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if tick.is_null() {
        return ERR_NULL_POINTER;
    }
    if out_events_buffer.is_null() {
        return ERR_NULL_POINTER;
    }
    if out_event_count.is_null() {
        return ERR_NULL_POINTER;
    }
    if buffer_capacity <= 0 {
        return ERR_INVALID_PARAM;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let engine_ref = &mut *engine;
        let tick_data = &*tick;

        // Validate tick data
        if tick_data.price <= 0.0 {
            *out_event_count = 0;
            return ERR_INVALID_DATA;
        }
        if tick_data.volume < 0.0 {
            *out_event_count = 0;
            return ERR_INVALID_DATA;
        }

        // Process the tick and get events
        let event_count = engine_ref.process_tick_internal(tick_data);
        
        // Copy events to caller's buffer
        let copy_count = event_count.min(buffer_capacity as usize);
        for i in 0..copy_count {
            *out_events_buffer.add(i) = engine_ref.event_queue[i];
        }
        
        *out_event_count = copy_count as i32;
        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Place an order based on external strategy signal.
///
/// This FFI function allows C# to submit buy/sell orders from external strategies
/// (Python/JSON) to the Rust engine for execution.
///
/// # Safety
/// - `engine` must be a valid engine pointer from `init_engine`
/// - `result` must be a valid pointer to write OrderResult
///
/// # Arguments
/// - `signal`: Order signal (SIGNAL_BUY=1, SIGNAL_SELL=2)
/// - `price`: Current market price for execution
/// - `quantity`: Order quantity (if 0, uses params.position_size)
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if engine or result is null
/// - ERR_INVALID_PARAM if signal is invalid
#[no_mangle]
pub unsafe extern "C" fn place_order(
    engine: *mut EngineHandle,
    signal: i32,
    price: f64,
    quantity: f64,
    result: *mut OrderResult,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if result.is_null() {
        return ERR_NULL_POINTER;
    }

    let ffi_result = catch_unwind(|| {
        // SAFETY: Validated above
        let engine_ref = &mut *engine;
        let result_ref = &mut *result;

        // Initialize result
        *result_ref = OrderResult::default();

        // Validate signal
        if signal != SIGNAL_BUY && signal != SIGNAL_SELL {
            result_ref.rejection_code = REJECTION_INVALID_QUANTITY;
            return ERR_INVALID_PARAM;
        }

        // Validate price
        if price <= 0.0 {
            result_ref.rejection_code = REJECTION_INVALID_PRICE;
            return ERR_INVALID_PARAM;
        }

        // Determine quantity
        let order_quantity = if quantity > 0.0 {
            quantity
        } else {
            engine_ref.params.position_size
        };

        if order_quantity <= 0.0 {
            result_ref.rejection_code = REJECTION_INVALID_QUANTITY;
            return ERR_INVALID_PARAM;
        }

        // Check risk limits
        let order_value = order_quantity * price;
        if order_value > engine_ref.risk_config.max_order_value {
            result_ref.rejection_code = REJECTION_RISK_LIMIT_EXCEEDED;
            return ERR_SUCCESS; // Return success but order rejected
        }

        // Check position limit
        let new_position = if signal == SIGNAL_BUY {
            engine_ref.position_quantity + order_quantity
        } else {
            engine_ref.position_quantity - order_quantity
        };
        if new_position.abs() > engine_ref.risk_config.max_position_size {
            result_ref.rejection_code = REJECTION_POSITION_LIMIT;
            return ERR_SUCCESS; // Return success but order rejected
        }

        // Check available capital for buy orders
        if signal == SIGNAL_BUY {
            if order_value > engine_ref.account.available {
                result_ref.rejection_code = REJECTION_INSUFFICIENT_CAPITAL;
                return ERR_SUCCESS; // Return success but order rejected
            }
        }

        // Execute the order
        let order_id = engine_ref.next_order_id;
        engine_ref.next_order_id += 1;

        // Calculate realized PnL if closing position
        let mut realized_pnl = 0.0;
        if signal == SIGNAL_BUY && engine_ref.position_quantity < 0.0 {
            // Closing short position
            let close_qty = order_quantity.min(engine_ref.position_quantity.abs());
            realized_pnl = close_qty * (engine_ref.entry_price - price);
        } else if signal == SIGNAL_SELL && engine_ref.position_quantity > 0.0 {
            // Closing long position
            let close_qty = order_quantity.min(engine_ref.position_quantity);
            realized_pnl = close_qty * (price - engine_ref.entry_price);
        }

        // Update position
        if signal == SIGNAL_BUY {
            if engine_ref.position_quantity <= 0.0 {
                // Opening or adding to long position
                engine_ref.entry_price = price;
            }
            engine_ref.position_quantity += order_quantity;
        } else {
            if engine_ref.position_quantity >= 0.0 {
                // Opening or adding to short position
                engine_ref.entry_price = price;
            }
            engine_ref.position_quantity -= order_quantity;
        }

        // Update account
        engine_ref.account.balance += realized_pnl;
        engine_ref.account.total_pnl += realized_pnl;
        engine_ref.account.position_count = if engine_ref.position_quantity.abs() > 0.0001 { 1 } else { 0 };
        
        // Update available capital
        if signal == SIGNAL_BUY {
            engine_ref.account.available -= order_value;
        } else {
            engine_ref.account.available += order_value;
        }

        // Add execution event to queue
        let event = ExecutionEvent {
            event_type: EVENT_TYPE_TRADE,
            timestamp: engine_ref.current_timestamp,
            price,
            quantity: order_quantity,
            side: if signal == SIGNAL_BUY { DIRECTION_BUY } else { DIRECTION_SELL },
            order_id,
            realized_pnl,
        };
        engine_ref.event_queue.push(event);

        // Fill result
        result_ref.accepted = 1;
        result_ref.order_id = order_id;
        result_ref.fill_price = price;
        result_ref.fill_quantity = order_quantity;
        result_ref.rejection_code = REJECTION_NONE;

        ERR_SUCCESS
    });

    match ffi_result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Callback function type for receiving OHLC data.
/// 
/// # Arguments
/// - `bars`: Pointer to array of OhlcBar structs
/// - `count`: Number of bars in the array
pub type OhlcCallback = extern "C" fn(bars: *const OhlcBar, count: i32);

/// Get OHLC data for a specified timeframe.
///
/// This function aggregates tick data into OHLC bars and returns them
/// via a callback function. The callback is invoked once with all bars.
///
/// # Safety
/// - `engine` must be a valid engine pointer from `init_engine`
/// - `timeframe` must be a valid null-terminated UTF-8 string
/// - `callback` must be a valid function pointer
///
/// # Arguments
/// - `timeframe`: Timeframe string (e.g., "1m", "5m", "15m", "1h", "1d")
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if engine or timeframe is null
/// - ERR_INVALID_PARAM if timeframe is invalid
#[no_mangle]
pub unsafe extern "C" fn get_ohlc_data(
    engine: *mut EngineHandle,
    timeframe: *const c_char,
    callback: OhlcCallback,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if timeframe.is_null() {
        return ERR_NULL_POINTER;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let engine_ref = &mut *engine;

        // Convert C string to Rust string
        let tf_cstr = std::ffi::CStr::from_ptr(timeframe);
        let tf_str = match tf_cstr.to_str() {
            Ok(s) => s,
            Err(_) => return ERR_INVALID_PARAM,
        };

        // Parse timeframe to minutes
        let period_minutes = match parse_timeframe(tf_str) {
            Some(m) => m,
            None => return ERR_INVALID_PARAM,
        };

        // If no ticks loaded, return empty
        if engine_ref.ticks.is_empty() {
            callback(std::ptr::null(), 0);
            return ERR_SUCCESS;
        }

        // Aggregate ticks into OHLC bars
        let bars = aggregate_ticks_to_ohlc(&engine_ref.ticks, period_minutes);
        
        // Store bars in engine for potential reuse
        engine_ref.ohlc_bars = bars;

        // Call callback with the bars
        callback(
            engine_ref.ohlc_bars.as_ptr(),
            engine_ref.ohlc_bars.len() as i32,
        );

        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Parse timeframe string to minutes.
fn parse_timeframe(tf: &str) -> Option<i64> {
    let tf = tf.to_lowercase();
    if tf.ends_with('m') {
        tf[..tf.len()-1].parse::<i64>().ok()
    } else if tf.ends_with('h') {
        tf[..tf.len()-1].parse::<i64>().ok().map(|h| h * 60)
    } else if tf.ends_with('d') {
        tf[..tf.len()-1].parse::<i64>().ok().map(|d| d * 1440)
    } else {
        // Try parsing as minutes directly
        tf.parse::<i64>().ok()
    }
}

/// Aggregate ticks into OHLC bars.
/// 
/// Uses time-window based aggregation to handle market gaps correctly.
fn aggregate_ticks_to_ohlc(ticks: &[Tick], period_minutes: i64) -> Vec<OhlcBar> {
    if ticks.is_empty() {
        return Vec::new();
    }

    let period_nanos = period_minutes * 60 * 1_000_000_000i64;
    let mut bars = Vec::new();
    let mut current_bar: Option<OhlcBar> = None;
    let mut period_end: i64 = 0;

    for tick in ticks {
        // Calculate the period this tick belongs to (align to period boundary)
        let period_start = (tick.timestamp / period_nanos) * period_nanos;
        let calculated_end = period_start + period_nanos;

        if current_bar.is_none() || calculated_end > period_end {
            // Start new bar
            if let Some(bar) = current_bar.take() {
                bars.push(bar);
            }
            period_end = calculated_end;
            current_bar = Some(OhlcBar {
                timestamp: period_start,
                open: tick.price,
                high: tick.price,
                low: tick.price,
                close: tick.price,
                volume: tick.volume,
            });
        } else {
            // Update current bar
            if let Some(ref mut bar) = current_bar {
                bar.high = bar.high.max(tick.price);
                bar.low = bar.low.min(tick.price);
                bar.close = tick.price;
                bar.volume += tick.volume;
            }
        }
    }

    // Don't forget the last bar
    if let Some(bar) = current_bar {
        bars.push(bar);
    }

    bars
}

/// Load CSV file with custom column mapping.
///
/// This function loads a CSV file using Polars with custom column name mapping
/// and date format conversion. It supports various column naming conventions
/// and date formats commonly used in financial data.
///
/// # Safety
/// - `engine` must be a valid engine pointer from `init_engine`
/// - `file_path` must be a valid null-terminated UTF-8 string
/// - `mapping` must be a valid pointer to CsvMapping struct
/// - `report` must be a valid pointer to write DataQualityReport
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if any pointer is null
/// - ERR_FILE_NOT_FOUND if file doesn't exist
/// - ERR_DATA_LOAD_FAILED if loading fails
#[no_mangle]
pub unsafe extern "C" fn load_csv_with_mapping(
    engine: *mut EngineHandle,
    file_path: *const c_char,
    mapping: *const CsvMapping,
    report: *mut DataQualityReport,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if file_path.is_null() {
        return ERR_NULL_POINTER;
    }
    if mapping.is_null() {
        return ERR_NULL_POINTER;
    }
    if report.is_null() {
        return ERR_NULL_POINTER;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let engine_ref = &mut *engine;
        let mapping_ref = &*mapping;
        let report_ref = &mut *report;

        // Convert C string to Rust string
        let path_cstr = std::ffi::CStr::from_ptr(file_path);
        let path_str = match path_cstr.to_str() {
            Ok(s) => s,
            Err(_) => return ERR_INVALID_PARAM,
        };

        // Check file exists
        let path = std::path::Path::new(path_str);
        if !path.exists() {
            return ERR_FILE_NOT_FOUND;
        }

        // Get column names from mapping
        let time_col = mapping_ref.time_column_str();
        let price_col = mapping_ref.price_column_str();
        let volume_col = mapping_ref.volume_column_str();
        let date_format = mapping_ref.date_format_str();

        // Load CSV with Polars
        match load_csv_with_mapping_internal(path_str, time_col, price_col, volume_col, date_format) {
            Ok((ticks, quality_report)) => {
                engine_ref.ticks = ticks;
                *report_ref = quality_report;
                ERR_SUCCESS
            }
            Err(_) => ERR_DATA_LOAD_FAILED,
        }
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Internal function to load CSV with column mapping using Polars.
fn load_csv_with_mapping_internal(
    file_path: &str,
    time_col: &str,
    price_col: &str,
    volume_col: &str,
    date_format: &str,
) -> Result<(Vec<Tick>, DataQualityReport), String> {
    use polars::prelude::*;

    // Read CSV file
    let df = CsvReadOptions::default()
        .with_has_header(true)
        .try_into_reader_with_file_path(Some(file_path.into()))
        .map_err(|e| format!("Failed to create CSV reader: {}", e))?
        .finish()
        .map_err(|e| format!("Failed to read CSV: {}", e))?;

    if df.height() == 0 {
        return Err("Empty CSV file".to_string());
    }

    // Check if required columns exist
    let columns: Vec<String> = df.get_column_names().iter().map(|s| s.to_string()).collect();
    if !columns.iter().any(|c| c == time_col) {
        return Err(format!("Time column '{}' not found", time_col));
    }
    if !columns.iter().any(|c| c == price_col) {
        return Err(format!("Price column '{}' not found", price_col));
    }
    if !columns.iter().any(|c| c == volume_col) {
        return Err(format!("Volume column '{}' not found", volume_col));
    }

    // Extract and convert columns
    let timestamps = extract_timestamps(&df, time_col, date_format)?;
    let prices = extract_f64_column(&df, price_col)?;
    let volumes = extract_f64_column(&df, volume_col)?;

    // Build ticks with validation
    let total_ticks = timestamps.len() as i64;
    let mut valid_ticks = Vec::with_capacity(timestamps.len());
    let mut invalid_count = 0i64;
    let mut anomaly_count = 0i64;
    let mut prev_timestamp: Option<i64> = None;
    let mut prev_price: Option<f64> = None;
    let price_jump_threshold = 0.10; // 10%

    let first_timestamp = timestamps.first().copied().unwrap_or(0);
    let last_timestamp = timestamps.last().copied().unwrap_or(0);

    for ((&timestamp, &price), &volume) in timestamps.iter()
        .zip(prices.iter())
        .zip(volumes.iter())
    {
        // Validate price > 0 and is finite
        if price <= 0.0 || !price.is_finite() {
            invalid_count += 1;
            continue;
        }

        // Validate volume >= 0 and is finite
        if volume < 0.0 || !volume.is_finite() {
            invalid_count += 1;
            continue;
        }

        // Check timestamp order
        if let Some(prev_ts) = prev_timestamp {
            if timestamp <= prev_ts {
                invalid_count += 1;
                continue;
            }
        }

        // Check price jump anomaly
        if let Some(prev_p) = prev_price {
            if prev_p > 0.0 {
                let change_pct = ((price - prev_p) / prev_p).abs();
                if change_pct > price_jump_threshold {
                    anomaly_count += 1;
                }
            }
        }

        valid_ticks.push(Tick {
            timestamp,
            price,
            volume,
        });

        prev_timestamp = Some(timestamp);
        prev_price = Some(price);
    }

    let report = DataQualityReport {
        total_ticks,
        valid_ticks: valid_ticks.len() as i64,
        invalid_ticks: invalid_count,
        anomaly_ticks: anomaly_count,
        first_timestamp,
        last_timestamp,
    };

    Ok((valid_ticks, report))
}

/// Extract timestamps from DataFrame with date format conversion.
fn extract_timestamps(df: &polars::prelude::DataFrame, col_name: &str, date_format: &str) -> Result<Vec<i64>, String> {
    use polars::prelude::*;

    let series = df.column(col_name)
        .map_err(|_| format!("Column '{}' not found", col_name))?;

    // Try to get as i64 directly (Unix timestamp)
    if let Ok(chunked) = series.i64() {
        let timestamps: Vec<i64> = chunked.into_iter()
            .map(|opt| {
                let ts = opt.unwrap_or(0);
                // Convert to nanoseconds if needed (detect if seconds or milliseconds)
                if ts < 1_000_000_000_000i64 {
                    // Likely seconds, convert to nanoseconds
                    ts * 1_000_000_000
                } else if ts < 1_000_000_000_000_000i64 {
                    // Likely milliseconds, convert to nanoseconds
                    ts * 1_000_000
                } else {
                    // Already nanoseconds or microseconds
                    ts
                }
            })
            .collect();
        return Ok(timestamps);
    }

    // Try to parse as string date
    if let Ok(chunked) = series.str() {
        let timestamps: Vec<i64> = chunked.into_iter()
            .map(|opt| {
                let s = opt.unwrap_or("");
                parse_date_string(s, date_format)
            })
            .collect();
        return Ok(timestamps);
    }

    // Try to cast to i64
    let casted = series.cast(&DataType::Int64)
        .map_err(|_| format!("Cannot convert column '{}' to timestamp", col_name))?;
    
    let chunked = casted.i64()
        .map_err(|_| format!("Cannot convert column '{}' to i64", col_name))?;
    
    let timestamps: Vec<i64> = chunked.into_iter()
        .map(|opt| {
            let ts = opt.unwrap_or(0);
            if ts < 1_000_000_000_000i64 {
                ts * 1_000_000_000
            } else if ts < 1_000_000_000_000_000i64 {
                ts * 1_000_000
            } else {
                ts
            }
        })
        .collect();
    
    Ok(timestamps)
}

/// Parse date string to Unix timestamp in nanoseconds.
fn parse_date_string(s: &str, format: &str) -> i64 {
    // Handle Unix timestamp
    if format == "unix" || format == "auto" {
        if let Ok(ts) = s.parse::<i64>() {
            if ts < 1_000_000_000_000i64 {
                return ts * 1_000_000_000;
            } else if ts < 1_000_000_000_000_000i64 {
                return ts * 1_000_000;
            }
            return ts;
        }
    }

    // Try common date formats
    let formats: Vec<String> = if format == "auto" {
        vec![
            "%Y-%m-%d %H:%M:%S".to_string(),
            "%Y-%m-%d".to_string(),
            "%Y/%m/%d %H:%M:%S".to_string(),
            "%Y/%m/%d".to_string(),
            "%m/%d/%Y %H:%M:%S".to_string(),
            "%m/%d/%Y".to_string(),
            "%d-%m-%Y %H:%M:%S".to_string(),
            "%d-%m-%Y".to_string(),
        ]
    } else {
        // Convert common format strings to chrono format
        let chrono_format = format
            .replace("yyyy", "%Y")
            .replace("MM", "%m")
            .replace("dd", "%d")
            .replace("HH", "%H")
            .replace("mm", "%M")
            .replace("ss", "%S");
        vec![chrono_format]
    };

    for fmt in &formats {
        if let Ok(dt) = chrono::NaiveDateTime::parse_from_str(s, fmt) {
            return dt.and_utc().timestamp_nanos_opt().unwrap_or(0);
        }
        // Try date only
        if let Ok(d) = chrono::NaiveDate::parse_from_str(s, fmt) {
            return d.and_hms_opt(0, 0, 0)
                .map(|dt| dt.and_utc().timestamp_nanos_opt().unwrap_or(0))
                .unwrap_or(0);
        }
    }

    0 // Return 0 if parsing fails
}

/// Extract f64 column from DataFrame.
fn extract_f64_column(df: &polars::prelude::DataFrame, col_name: &str) -> Result<Vec<f64>, String> {
    use polars::prelude::*;

    let series = df.column(col_name)
        .map_err(|_| format!("Column '{}' not found", col_name))?;

    // Try to get as f64 directly
    if let Ok(chunked) = series.f64() {
        return Ok(chunked.into_iter()
            .map(|opt| opt.unwrap_or(f64::NAN))
            .collect());
    }

    // Try to cast to f64
    let casted = series.cast(&DataType::Float64)
        .map_err(|_| format!("Cannot convert column '{}' to f64", col_name))?;
    
    let chunked = casted.f64()
        .map_err(|_| format!("Cannot convert column '{}' to f64", col_name))?;
    
    Ok(chunked.into_iter()
        .map(|opt| opt.unwrap_or(f64::NAN))
        .collect())
}

/// Fast-forward to a specific tick index without invoking callbacks.
///
/// This function allows efficient seeking in replay mode by skipping
/// tick-by-tick processing and directly jumping to a target position.
/// It updates the engine's internal state to reflect the position at
/// the target index.
///
/// # Safety
/// - `engine` must be a valid engine pointer from `init_engine`
/// - `out_timestamp` must be a valid pointer to write the timestamp at target index
///
/// # Arguments
/// - `target_index`: The tick index to fast-forward to (0-based)
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if engine or out_timestamp is null
/// - ERR_INVALID_PARAM if target_index is out of bounds
/// - ERR_ENGINE_NOT_INIT if no data is loaded
#[no_mangle]
pub unsafe extern "C" fn fast_forward_to(
    engine: *mut EngineHandle,
    target_index: i64,
    out_timestamp: *mut i64,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if out_timestamp.is_null() {
        return ERR_NULL_POINTER;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let engine_ref = &mut *engine;

        // Check if data is loaded
        if engine_ref.ticks.is_empty() {
            return ERR_ENGINE_NOT_INIT;
        }

        // Validate target index
        if target_index < 0 || target_index >= engine_ref.ticks.len() as i64 {
            return ERR_INVALID_PARAM;
        }

        // Update current tick index
        engine_ref.current_tick_index = target_index;
        
        // Update current timestamp
        let tick = &engine_ref.ticks[target_index as usize];
        engine_ref.current_timestamp = tick.timestamp;
        
        // Write timestamp to output
        *out_timestamp = tick.timestamp;

        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Get the current tick index in the loaded data.
///
/// # Safety
/// - `engine` must be a valid engine pointer from `init_engine`
/// - `out_index` must be a valid pointer to write the current index
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if engine or out_index is null
#[no_mangle]
pub unsafe extern "C" fn get_current_tick_index(
    engine: *mut EngineHandle,
    out_index: *mut i64,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if out_index.is_null() {
        return ERR_NULL_POINTER;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let engine_ref = &*engine;
        *out_index = engine_ref.current_tick_index;
        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Get the total number of ticks loaded.
///
/// # Safety
/// - `engine` must be a valid engine pointer from `init_engine`
/// - `out_count` must be a valid pointer to write the tick count
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if engine or out_count is null
#[no_mangle]
pub unsafe extern "C" fn get_tick_count(
    engine: *mut EngineHandle,
    out_count: *mut i64,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if out_count.is_null() {
        return ERR_NULL_POINTER;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let engine_ref = &*engine;
        *out_count = engine_ref.ticks.len() as i64;
        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Get current account status.
///
/// # Safety
/// - `engine` must be a valid engine pointer from `init_engine`
/// - `status` must be a valid pointer to write AccountStatus
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if engine or status is null
#[no_mangle]
pub unsafe extern "C" fn get_account_status(
    engine: *mut EngineHandle,
    status: *mut AccountStatus,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if status.is_null() {
        return ERR_NULL_POINTER;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let engine_ref = &*engine;
        let status_ref = &mut *status;

        *status_ref = engine_ref.account;
        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Load data from file (placeholder).
///
/// # Safety
/// - `engine` must be a valid engine pointer
/// - `file_path` must be a valid null-terminated UTF-8 string
/// - `report` must be a valid pointer to write DataQualityReport
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if any pointer is null
/// - ERR_FILE_NOT_FOUND if file doesn't exist
#[no_mangle]
pub unsafe extern "C" fn load_data_from_file(
    engine: *mut EngineHandle,
    file_path: *const c_char,
    report: *mut DataQualityReport,
) -> i32 {
    // Validate pointers
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }
    if file_path.is_null() {
        return ERR_NULL_POINTER;
    }
    if report.is_null() {
        return ERR_NULL_POINTER;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let _engine_ref = &mut *engine;
        let report_ref = &mut *report;

        // Convert C string to Rust string
        let path_cstr = std::ffi::CStr::from_ptr(file_path);
        let _path = match path_cstr.to_str() {
            Ok(s) => s,
            Err(_) => return ERR_INVALID_PARAM,
        };

        // TODO: Implement actual data loading with Polars in Phase 2
        // For now, return a placeholder report
        *report_ref = DataQualityReport::default();
        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Run complete backtest (placeholder).
///
/// # Safety
/// - `engine` must be a valid engine pointer
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if engine is null
#[no_mangle]
pub unsafe extern "C" fn run_backtest(engine: *mut EngineHandle) -> i32 {
    if engine.is_null() {
        return ERR_NULL_POINTER;
    }

    let result = catch_unwind(|| {
        // SAFETY: Validated above
        let _engine_ref = &mut *engine;

        // TODO: Implement actual backtest execution in Phase 2
        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Log callback function type for FFI.
pub type FfiLogCallback = extern "C" fn(level: i32, message: *const c_char);

/// Set the global log callback for FFI.
///
/// # Safety
/// - `callback` must be a valid function pointer
/// - The callback must remain valid for the lifetime of the program
/// - The callback must be thread-safe
///
/// # Returns
/// - ERR_SUCCESS on success
#[no_mangle]
pub unsafe extern "C" fn set_log_callback(
    callback: FfiLogCallback,
) -> i32 {
    let result = catch_unwind(|| {
        crate::logger::set_log_callback(callback);
        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Clear the global log callback.
///
/// After calling this, log messages will be silently ignored.
/// Call this before disposing the engine to ensure no callbacks are invoked
/// after the delegate has been garbage collected.
///
/// # Returns
/// - ERR_SUCCESS on success
#[no_mangle]
pub extern "C" fn clear_log_callback() -> i32 {
    let result = catch_unwind(|| {
        crate::logger::clear_log_callback();
        ERR_SUCCESS
    });

    match result {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_init_and_free_engine() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            assert!(!engine.is_null());
            free_engine(engine);
        }
    }

    #[test]
    fn test_init_with_params() {
        unsafe {
            let params = StrategyParams {
                short_ma_period: 10,
                long_ma_period: 30,
                position_size: 200.0,
                stop_loss_pct: 0.03,
                take_profit_pct: 0.06,
                warmup_bars: 0,
            };
            let risk = RiskConfig::default();

            let engine = init_engine(&params, &risk);
            assert!(!engine.is_null());

            let engine_ref = &*engine;
            assert_eq!(engine_ref.params.short_ma_period, 10);
            assert_eq!(engine_ref.params.long_ma_period, 30);

            free_engine(engine);
        }
    }

    #[test]
    fn test_process_tick_null_engine() {
        unsafe {
            let tick = Tick::default();
            let result = process_tick(std::ptr::null_mut(), &tick);
            assert_eq!(result, ERR_NULL_POINTER);
        }
    }

    #[test]
    fn test_process_tick_null_tick() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let result = process_tick(engine, std::ptr::null());
            assert_eq!(result, ERR_NULL_POINTER);
            free_engine(engine);
        }
    }

    #[test]
    fn test_process_tick_invalid_price() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let tick = Tick {
                timestamp: 1000,
                price: -100.0, // Invalid
                volume: 100.0,
            };
            let result = process_tick(engine, &tick);
            assert_eq!(result, ERR_INVALID_DATA);
            free_engine(engine);
        }
    }

    #[test]
    fn test_process_tick_invalid_volume() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let tick = Tick {
                timestamp: 1000,
                price: 100.0,
                volume: -1.0, // Invalid
            };
            let result = process_tick(engine, &tick);
            assert_eq!(result, ERR_INVALID_DATA);
            free_engine(engine);
        }
    }

    #[test]
    fn test_get_account_status() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut status = AccountStatus::default();

            let result = get_account_status(engine, &mut status);
            assert_eq!(result, ERR_SUCCESS);
            assert_eq!(status.balance, 100_000.0);

            free_engine(engine);
        }
    }

    #[test]
    fn test_get_account_status_null_pointers() {
        unsafe {
            let mut status = AccountStatus::default();

            // Null engine
            let result = get_account_status(std::ptr::null_mut(), &mut status);
            assert_eq!(result, ERR_NULL_POINTER);

            // Null status
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let result = get_account_status(engine, std::ptr::null_mut());
            assert_eq!(result, ERR_NULL_POINTER);
            free_engine(engine);
        }
    }

    // ========================================================================
    // process_tick_with_result Tests
    // ========================================================================

    #[test]
    fn test_process_tick_with_result_basic() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let tick = Tick {
                timestamp: 1000,
                price: 100.0,
                volume: 1000.0,
            };
            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );

            assert_eq!(result, ERR_SUCCESS);
            assert_eq!(event_count, 0); // No position, no events

            free_engine(engine);
        }
    }

    #[test]
    fn test_process_tick_with_result_null_pointers() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let tick = Tick {
                timestamp: 1000,
                price: 100.0,
                volume: 1000.0,
            };
            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            // Null engine
            let result = process_tick_with_result(
                std::ptr::null_mut(),
                &tick,
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );
            assert_eq!(result, ERR_NULL_POINTER);

            // Null tick
            let result = process_tick_with_result(
                engine,
                std::ptr::null(),
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );
            assert_eq!(result, ERR_NULL_POINTER);

            // Null buffer
            let result = process_tick_with_result(
                engine,
                &tick,
                std::ptr::null_mut(),
                16,
                &mut event_count,
            );
            assert_eq!(result, ERR_NULL_POINTER);

            // Null event count
            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                16,
                std::ptr::null_mut(),
            );
            assert_eq!(result, ERR_NULL_POINTER);

            free_engine(engine);
        }
    }

    #[test]
    fn test_process_tick_with_result_invalid_capacity() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let tick = Tick {
                timestamp: 1000,
                price: 100.0,
                volume: 1000.0,
            };
            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                0, // Invalid capacity
                &mut event_count,
            );
            assert_eq!(result, ERR_INVALID_PARAM);

            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                -1, // Invalid capacity
                &mut event_count,
            );
            assert_eq!(result, ERR_INVALID_PARAM);

            free_engine(engine);
        }
    }

    #[test]
    fn test_process_tick_with_result_invalid_data() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            // Invalid price
            let tick = Tick {
                timestamp: 1000,
                price: -100.0,
                volume: 1000.0,
            };
            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );
            assert_eq!(result, ERR_INVALID_DATA);
            assert_eq!(event_count, 0);

            // Invalid volume
            let tick = Tick {
                timestamp: 1000,
                price: 100.0,
                volume: -1.0,
            };
            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );
            assert_eq!(result, ERR_INVALID_DATA);
            assert_eq!(event_count, 0);

            free_engine(engine);
        }
    }

    #[test]
    fn test_process_tick_with_result_stop_loss_trigger() {
        unsafe {
            // Create engine with 2% stop loss
            let params = StrategyParams {
                stop_loss_pct: 0.02,
                take_profit_pct: 0.10,
                ..Default::default()
            };
            let engine = init_engine(&params, std::ptr::null());
            let engine_ref = &mut *engine;

            // Simulate a long position at price 100
            engine_ref.position_quantity = 10.0;
            engine_ref.entry_price = 100.0;
            engine_ref.account.position_count = 1;

            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            // Price drops 3% - should trigger stop loss
            let tick = Tick {
                timestamp: 1000,
                price: 97.0, // 3% drop
                volume: 1000.0,
            };

            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );

            assert_eq!(result, ERR_SUCCESS);
            assert_eq!(event_count, 1);
            assert_eq!(events[0].event_type, EVENT_TYPE_STOP_TRIGGERED);
            assert_eq!(events[0].side, DIRECTION_SELL); // Closing long position
            assert!((events[0].price - 97.0).abs() < 0.001);

            // Position should be closed
            assert!((engine_ref.position_quantity).abs() < 0.001);

            free_engine(engine);
        }
    }

    #[test]
    fn test_process_tick_with_result_take_profit_trigger() {
        unsafe {
            // Create engine with 5% take profit
            let params = StrategyParams {
                stop_loss_pct: 0.02,
                take_profit_pct: 0.05,
                ..Default::default()
            };
            let engine = init_engine(&params, std::ptr::null());
            let engine_ref = &mut *engine;

            // Simulate a long position at price 100
            engine_ref.position_quantity = 10.0;
            engine_ref.entry_price = 100.0;
            engine_ref.account.position_count = 1;

            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            // Price rises 6% - should trigger take profit
            let tick = Tick {
                timestamp: 1000,
                price: 106.0, // 6% rise
                volume: 1000.0,
            };

            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );

            assert_eq!(result, ERR_SUCCESS);
            assert_eq!(event_count, 1);
            assert_eq!(events[0].event_type, EVENT_TYPE_TAKE_PROFIT_TRIGGERED);
            assert_eq!(events[0].side, DIRECTION_SELL); // Closing long position
            assert!((events[0].price - 106.0).abs() < 0.001);
            assert!(events[0].realized_pnl > 0.0); // Profitable trade

            // Position should be closed
            assert!((engine_ref.position_quantity).abs() < 0.001);

            free_engine(engine);
        }
    }

    #[test]
    fn test_process_tick_with_result_tick_count() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let engine_ref = &mut *engine;
            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            // Process multiple ticks
            for i in 1..=5 {
                let tick = Tick {
                    timestamp: i * 1000,
                    price: 100.0 + i as f64,
                    volume: 1000.0,
                };
                process_tick_with_result(
                    engine,
                    &tick,
                    events.as_mut_ptr(),
                    16,
                    &mut event_count,
                );
            }

            assert_eq!(engine_ref.tick_count, 5);

            free_engine(engine);
        }
    }

    // ========================================================================
    // place_order Tests
    // ========================================================================

    #[test]
    fn test_place_order_buy() {
        unsafe {
            let params = StrategyParams {
                position_size: 10.0,
                ..Default::default()
            };
            let engine = init_engine(&params, std::ptr::null());
            let mut result = OrderResult::default();

            let ret = place_order(engine, SIGNAL_BUY, 100.0, 0.0, &mut result);

            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(result.accepted, 1);
            assert!(result.order_id > 0);
            assert!((result.fill_price - 100.0).abs() < 0.001);
            assert!((result.fill_quantity - 10.0).abs() < 0.001);
            assert_eq!(result.rejection_code, REJECTION_NONE);

            // Check position was updated
            let engine_ref = &*engine;
            assert!((engine_ref.position_quantity - 10.0).abs() < 0.001);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_sell() {
        unsafe {
            let params = StrategyParams {
                position_size: 10.0,
                ..Default::default()
            };
            let engine = init_engine(&params, std::ptr::null());
            let mut result = OrderResult::default();

            let ret = place_order(engine, SIGNAL_SELL, 100.0, 0.0, &mut result);

            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(result.accepted, 1);
            assert!(result.order_id > 0);
            assert!((result.fill_price - 100.0).abs() < 0.001);
            assert!((result.fill_quantity - 10.0).abs() < 0.001);

            // Check position was updated (short position)
            let engine_ref = &*engine;
            assert!((engine_ref.position_quantity - (-10.0)).abs() < 0.001);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_with_custom_quantity() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut result = OrderResult::default();

            let ret = place_order(engine, SIGNAL_BUY, 100.0, 25.0, &mut result);

            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(result.accepted, 1);
            assert!((result.fill_quantity - 25.0).abs() < 0.001);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_null_pointers() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut result = OrderResult::default();

            // Null engine
            let ret = place_order(std::ptr::null_mut(), SIGNAL_BUY, 100.0, 10.0, &mut result);
            assert_eq!(ret, ERR_NULL_POINTER);

            // Null result
            let ret = place_order(engine, SIGNAL_BUY, 100.0, 10.0, std::ptr::null_mut());
            assert_eq!(ret, ERR_NULL_POINTER);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_invalid_signal() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut result = OrderResult::default();

            // Invalid signal (0 = SIGNAL_NONE)
            let ret = place_order(engine, SIGNAL_NONE, 100.0, 10.0, &mut result);
            assert_eq!(ret, ERR_INVALID_PARAM);

            // Invalid signal (3)
            let ret = place_order(engine, 3, 100.0, 10.0, &mut result);
            assert_eq!(ret, ERR_INVALID_PARAM);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_invalid_price() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut result = OrderResult::default();

            let ret = place_order(engine, SIGNAL_BUY, -100.0, 10.0, &mut result);
            assert_eq!(ret, ERR_INVALID_PARAM);
            assert_eq!(result.rejection_code, REJECTION_INVALID_PRICE);

            let ret = place_order(engine, SIGNAL_BUY, 0.0, 10.0, &mut result);
            assert_eq!(ret, ERR_INVALID_PARAM);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_risk_limit_exceeded() {
        unsafe {
            let risk = RiskConfig {
                max_order_value: 1000.0, // Very low limit
                ..Default::default()
            };
            let engine = init_engine(std::ptr::null(), &risk);
            let mut result = OrderResult::default();

            // Order value = 100 * 100 = 10000 > 1000
            let ret = place_order(engine, SIGNAL_BUY, 100.0, 100.0, &mut result);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(result.accepted, 0);
            assert_eq!(result.rejection_code, REJECTION_RISK_LIMIT_EXCEEDED);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_position_limit_exceeded() {
        unsafe {
            let risk = RiskConfig {
                max_position_size: 50.0, // Low position limit
                ..Default::default()
            };
            let engine = init_engine(std::ptr::null(), &risk);
            let mut result = OrderResult::default();

            // Try to buy 100 units, exceeds 50 limit
            let ret = place_order(engine, SIGNAL_BUY, 100.0, 100.0, &mut result);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(result.accepted, 0);
            assert_eq!(result.rejection_code, REJECTION_POSITION_LIMIT);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_insufficient_capital() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let engine_ref = &mut *engine;
            engine_ref.account.available = 100.0; // Very low available capital

            let mut result = OrderResult::default();

            // Order value = 100 * 10 = 1000 > 100 available
            let ret = place_order(engine, SIGNAL_BUY, 100.0, 10.0, &mut result);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(result.accepted, 0);
            assert_eq!(result.rejection_code, REJECTION_INSUFFICIENT_CAPITAL);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_close_position_pnl() {
        unsafe {
            let params = StrategyParams {
                position_size: 10.0,
                ..Default::default()
            };
            let engine = init_engine(&params, std::ptr::null());
            let engine_ref = &mut *engine;

            // Simulate existing long position at price 100
            engine_ref.position_quantity = 10.0;
            engine_ref.entry_price = 100.0;
            let initial_balance = engine_ref.account.balance;

            let mut result = OrderResult::default();

            // Sell at 110 (10% profit)
            let ret = place_order(engine, SIGNAL_SELL, 110.0, 10.0, &mut result);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(result.accepted, 1);

            // Check realized PnL: 10 * (110 - 100) = 100
            let engine_ref = &*engine;
            assert!((engine_ref.account.balance - (initial_balance + 100.0)).abs() < 0.001);

            free_engine(engine);
        }
    }

    #[test]
    fn test_place_order_generates_event() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut result = OrderResult::default();

            let ret = place_order(engine, SIGNAL_BUY, 100.0, 10.0, &mut result);
            assert_eq!(ret, ERR_SUCCESS);

            // Check event was generated
            let engine_ref = &*engine;
            assert_eq!(engine_ref.event_queue.len(), 1);
            assert_eq!(engine_ref.event_queue[0].event_type, EVENT_TYPE_TRADE);
            assert_eq!(engine_ref.event_queue[0].side, DIRECTION_BUY);

            free_engine(engine);
        }
    }

    // ========================================================================
    // get_ohlc_data Tests
    // ========================================================================

    #[test]
    fn test_parse_timeframe() {
        assert_eq!(parse_timeframe("1m"), Some(1));
        assert_eq!(parse_timeframe("5m"), Some(5));
        assert_eq!(parse_timeframe("15m"), Some(15));
        assert_eq!(parse_timeframe("30m"), Some(30));
        assert_eq!(parse_timeframe("1h"), Some(60));
        assert_eq!(parse_timeframe("4h"), Some(240));
        assert_eq!(parse_timeframe("1d"), Some(1440));
        assert_eq!(parse_timeframe("1M"), Some(1)); // Case insensitive
        assert_eq!(parse_timeframe("1H"), Some(60));
        assert_eq!(parse_timeframe("invalid"), None);
    }

    #[test]
    fn test_aggregate_ticks_to_ohlc_empty() {
        let ticks: Vec<Tick> = vec![];
        let bars = aggregate_ticks_to_ohlc(&ticks, 1);
        assert!(bars.is_empty());
    }

    #[test]
    fn test_aggregate_ticks_to_ohlc_single_tick() {
        let ticks = vec![
            Tick { timestamp: 60_000_000_000, price: 100.0, volume: 1000.0 },
        ];
        let bars = aggregate_ticks_to_ohlc(&ticks, 1);
        
        assert_eq!(bars.len(), 1);
        assert!((bars[0].open - 100.0).abs() < 0.001);
        assert!((bars[0].high - 100.0).abs() < 0.001);
        assert!((bars[0].low - 100.0).abs() < 0.001);
        assert!((bars[0].close - 100.0).abs() < 0.001);
        assert!((bars[0].volume - 1000.0).abs() < 0.001);
    }

    #[test]
    fn test_aggregate_ticks_to_ohlc_multiple_ticks_same_bar() {
        // All ticks within the same 1-minute bar
        let minute_nanos = 60_000_000_000i64;
        let ticks = vec![
            Tick { timestamp: minute_nanos, price: 100.0, volume: 100.0 },
            Tick { timestamp: minute_nanos + 10_000_000_000, price: 105.0, volume: 200.0 },
            Tick { timestamp: minute_nanos + 20_000_000_000, price: 95.0, volume: 150.0 },
            Tick { timestamp: minute_nanos + 30_000_000_000, price: 102.0, volume: 250.0 },
        ];
        let bars = aggregate_ticks_to_ohlc(&ticks, 1);
        
        assert_eq!(bars.len(), 1);
        assert!((bars[0].open - 100.0).abs() < 0.001);
        assert!((bars[0].high - 105.0).abs() < 0.001);
        assert!((bars[0].low - 95.0).abs() < 0.001);
        assert!((bars[0].close - 102.0).abs() < 0.001);
        assert!((bars[0].volume - 700.0).abs() < 0.001); // 100 + 200 + 150 + 250
    }

    #[test]
    fn test_aggregate_ticks_to_ohlc_multiple_bars() {
        let minute_nanos = 60_000_000_000i64;
        let ticks = vec![
            // First minute
            Tick { timestamp: minute_nanos, price: 100.0, volume: 100.0 },
            Tick { timestamp: minute_nanos + 30_000_000_000, price: 105.0, volume: 200.0 },
            // Second minute
            Tick { timestamp: 2 * minute_nanos, price: 110.0, volume: 150.0 },
            Tick { timestamp: 2 * minute_nanos + 30_000_000_000, price: 108.0, volume: 250.0 },
        ];
        let bars = aggregate_ticks_to_ohlc(&ticks, 1);
        
        assert_eq!(bars.len(), 2);
        
        // First bar
        assert!((bars[0].open - 100.0).abs() < 0.001);
        assert!((bars[0].high - 105.0).abs() < 0.001);
        assert!((bars[0].close - 105.0).abs() < 0.001);
        
        // Second bar
        assert!((bars[1].open - 110.0).abs() < 0.001);
        assert!((bars[1].high - 110.0).abs() < 0.001);
        assert!((bars[1].close - 108.0).abs() < 0.001);
    }

    #[test]
    fn test_aggregate_ticks_to_ohlc_5min_bars() {
        let minute_nanos = 60_000_000_000i64;
        let ticks = vec![
            // First 5-minute bar (minutes 0-4)
            Tick { timestamp: 0, price: 100.0, volume: 100.0 },
            Tick { timestamp: 2 * minute_nanos, price: 105.0, volume: 200.0 },
            Tick { timestamp: 4 * minute_nanos, price: 102.0, volume: 150.0 },
            // Second 5-minute bar (minutes 5-9)
            Tick { timestamp: 5 * minute_nanos, price: 110.0, volume: 300.0 },
            Tick { timestamp: 7 * minute_nanos, price: 115.0, volume: 400.0 },
        ];
        let bars = aggregate_ticks_to_ohlc(&ticks, 5);
        
        assert_eq!(bars.len(), 2);
        
        // First 5-min bar
        assert!((bars[0].open - 100.0).abs() < 0.001);
        assert!((bars[0].high - 105.0).abs() < 0.001);
        assert!((bars[0].low - 100.0).abs() < 0.001);
        assert!((bars[0].close - 102.0).abs() < 0.001);
        
        // Second 5-min bar
        assert!((bars[1].open - 110.0).abs() < 0.001);
        assert!((bars[1].high - 115.0).abs() < 0.001);
    }

    // Static variable to capture callback results
    static mut CALLBACK_BARS: Vec<OhlcBar> = Vec::new();

    extern "C" fn test_ohlc_callback(bars: *const OhlcBar, count: i32) {
        unsafe {
            CALLBACK_BARS.clear();
            if !bars.is_null() && count > 0 {
                for i in 0..count as usize {
                    CALLBACK_BARS.push(*bars.add(i));
                }
            }
        }
    }

    #[test]
    fn test_get_ohlc_data_empty() {
        unsafe {
            CALLBACK_BARS.clear();
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let timeframe = std::ffi::CString::new("1m").unwrap();

            let ret = get_ohlc_data(engine, timeframe.as_ptr(), test_ohlc_callback);
            
            assert_eq!(ret, ERR_SUCCESS);
            assert!(CALLBACK_BARS.is_empty());

            free_engine(engine);
        }
    }

    #[test]
    fn test_get_ohlc_data_with_ticks() {
        unsafe {
            CALLBACK_BARS.clear();
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let engine_ref = &mut *engine;

            // Add some ticks
            let minute_nanos = 60_000_000_000i64;
            engine_ref.ticks = vec![
                Tick { timestamp: minute_nanos, price: 100.0, volume: 100.0 },
                Tick { timestamp: minute_nanos + 30_000_000_000, price: 105.0, volume: 200.0 },
                Tick { timestamp: 2 * minute_nanos, price: 110.0, volume: 150.0 },
            ];

            let timeframe = std::ffi::CString::new("1m").unwrap();
            let ret = get_ohlc_data(engine, timeframe.as_ptr(), test_ohlc_callback);
            
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(CALLBACK_BARS.len(), 2);

            free_engine(engine);
        }
    }

    #[test]
    fn test_get_ohlc_data_null_pointers() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let timeframe = std::ffi::CString::new("1m").unwrap();

            // Null engine
            let ret = get_ohlc_data(std::ptr::null_mut(), timeframe.as_ptr(), test_ohlc_callback);
            assert_eq!(ret, ERR_NULL_POINTER);

            // Null timeframe
            let ret = get_ohlc_data(engine, std::ptr::null(), test_ohlc_callback);
            assert_eq!(ret, ERR_NULL_POINTER);

            free_engine(engine);
        }
    }

    #[test]
    fn test_get_ohlc_data_invalid_timeframe() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let timeframe = std::ffi::CString::new("invalid").unwrap();

            let ret = get_ohlc_data(engine, timeframe.as_ptr(), test_ohlc_callback);
            assert_eq!(ret, ERR_INVALID_PARAM);

            free_engine(engine);
        }
    }

    // ========================================================================
    // load_csv_with_mapping Tests
    // ========================================================================

    #[test]
    fn test_parse_date_string_unix() {
        // Unix timestamp in seconds
        let ts = parse_date_string("1704067200", "unix");
        assert!(ts > 0);
        
        // Unix timestamp in milliseconds
        let ts = parse_date_string("1704067200000", "unix");
        assert!(ts > 0);
    }

    #[test]
    fn test_parse_date_string_formats() {
        // ISO format
        let ts = parse_date_string("2024-01-01 00:00:00", "auto");
        assert!(ts > 0);
        
        // Date only
        let ts = parse_date_string("2024-01-01", "auto");
        assert!(ts > 0);
        
        // Slash format
        let ts = parse_date_string("2024/01/01", "auto");
        assert!(ts > 0);
    }

    #[test]
    fn test_load_csv_with_mapping_null_pointers() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let file_path = std::ffi::CString::new("test.csv").unwrap();
            let mapping = CsvMapping::default();
            let mut report = DataQualityReport::default();

            // Null engine
            let ret = load_csv_with_mapping(
                std::ptr::null_mut(),
                file_path.as_ptr(),
                &mapping,
                &mut report,
            );
            assert_eq!(ret, ERR_NULL_POINTER);

            // Null file path
            let ret = load_csv_with_mapping(
                engine,
                std::ptr::null(),
                &mapping,
                &mut report,
            );
            assert_eq!(ret, ERR_NULL_POINTER);

            // Null mapping
            let ret = load_csv_with_mapping(
                engine,
                file_path.as_ptr(),
                std::ptr::null(),
                &mut report,
            );
            assert_eq!(ret, ERR_NULL_POINTER);

            // Null report
            let ret = load_csv_with_mapping(
                engine,
                file_path.as_ptr(),
                &mapping,
                std::ptr::null_mut(),
            );
            assert_eq!(ret, ERR_NULL_POINTER);

            free_engine(engine);
        }
    }

    #[test]
    fn test_load_csv_with_mapping_file_not_found() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let file_path = std::ffi::CString::new("nonexistent_file.csv").unwrap();
            let mapping = CsvMapping::default();
            let mut report = DataQualityReport::default();

            let ret = load_csv_with_mapping(
                engine,
                file_path.as_ptr(),
                &mapping,
                &mut report,
            );
            assert_eq!(ret, ERR_FILE_NOT_FOUND);

            free_engine(engine);
        }
    }

    #[test]
    fn test_load_csv_with_mapping_real_file() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            
            // Try to load the test data file if it exists
            let file_path = std::ffi::CString::new("../test_data/ticks_clean.csv").unwrap();
            let mapping = CsvMapping::default();
            let mut report = DataQualityReport::default();

            let ret = load_csv_with_mapping(
                engine,
                file_path.as_ptr(),
                &mapping,
                &mut report,
            );

            // If file exists, check it loaded correctly
            if ret == ERR_SUCCESS {
                assert!(report.total_ticks > 0);
                assert!(report.valid_ticks > 0);
                
                let engine_ref = &*engine;
                assert!(!engine_ref.ticks.is_empty());
            }
            // If file doesn't exist, that's okay for this test

            free_engine(engine);
        }
    }

    // ========================================================================
    // fast_forward_to Tests
    // ========================================================================

    #[test]
    fn test_fast_forward_to_basic() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let engine_ref = &mut *engine;

            // Add some ticks
            engine_ref.ticks = vec![
                Tick { timestamp: 1000, price: 100.0, volume: 100.0 },
                Tick { timestamp: 2000, price: 101.0, volume: 200.0 },
                Tick { timestamp: 3000, price: 102.0, volume: 300.0 },
                Tick { timestamp: 4000, price: 103.0, volume: 400.0 },
                Tick { timestamp: 5000, price: 104.0, volume: 500.0 },
            ];

            let mut timestamp: i64 = 0;

            // Fast forward to index 2
            let ret = fast_forward_to(engine, 2, &mut timestamp);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(timestamp, 3000);
            assert_eq!(engine_ref.current_tick_index, 2);
            assert_eq!(engine_ref.current_timestamp, 3000);

            // Fast forward to index 4
            let ret = fast_forward_to(engine, 4, &mut timestamp);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(timestamp, 5000);
            assert_eq!(engine_ref.current_tick_index, 4);

            // Fast forward back to index 0
            let ret = fast_forward_to(engine, 0, &mut timestamp);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(timestamp, 1000);
            assert_eq!(engine_ref.current_tick_index, 0);

            free_engine(engine);
        }
    }

    #[test]
    fn test_fast_forward_to_null_pointers() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let engine_ref = &mut *engine;
            engine_ref.ticks = vec![
                Tick { timestamp: 1000, price: 100.0, volume: 100.0 },
            ];

            let mut timestamp: i64 = 0;

            // Null engine
            let ret = fast_forward_to(std::ptr::null_mut(), 0, &mut timestamp);
            assert_eq!(ret, ERR_NULL_POINTER);

            // Null timestamp
            let ret = fast_forward_to(engine, 0, std::ptr::null_mut());
            assert_eq!(ret, ERR_NULL_POINTER);

            free_engine(engine);
        }
    }

    #[test]
    fn test_fast_forward_to_no_data() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut timestamp: i64 = 0;

            // No ticks loaded
            let ret = fast_forward_to(engine, 0, &mut timestamp);
            assert_eq!(ret, ERR_ENGINE_NOT_INIT);

            free_engine(engine);
        }
    }

    #[test]
    fn test_fast_forward_to_invalid_index() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let engine_ref = &mut *engine;
            engine_ref.ticks = vec![
                Tick { timestamp: 1000, price: 100.0, volume: 100.0 },
                Tick { timestamp: 2000, price: 101.0, volume: 200.0 },
            ];

            let mut timestamp: i64 = 0;

            // Negative index
            let ret = fast_forward_to(engine, -1, &mut timestamp);
            assert_eq!(ret, ERR_INVALID_PARAM);

            // Index out of bounds
            let ret = fast_forward_to(engine, 2, &mut timestamp);
            assert_eq!(ret, ERR_INVALID_PARAM);

            let ret = fast_forward_to(engine, 100, &mut timestamp);
            assert_eq!(ret, ERR_INVALID_PARAM);

            free_engine(engine);
        }
    }

    #[test]
    fn test_get_current_tick_index() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let engine_ref = &mut *engine;
            engine_ref.ticks = vec![
                Tick { timestamp: 1000, price: 100.0, volume: 100.0 },
                Tick { timestamp: 2000, price: 101.0, volume: 200.0 },
            ];

            let mut index: i64 = 0;
            let mut timestamp: i64 = 0;

            // Initial index should be 0
            let ret = get_current_tick_index(engine, &mut index);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(index, 0);

            // Fast forward and check index
            fast_forward_to(engine, 1, &mut timestamp);
            let ret = get_current_tick_index(engine, &mut index);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(index, 1);

            free_engine(engine);
        }
    }

    #[test]
    fn test_get_current_tick_index_null_pointers() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut index: i64 = 0;

            // Null engine
            let ret = get_current_tick_index(std::ptr::null_mut(), &mut index);
            assert_eq!(ret, ERR_NULL_POINTER);

            // Null index
            let ret = get_current_tick_index(engine, std::ptr::null_mut());
            assert_eq!(ret, ERR_NULL_POINTER);

            free_engine(engine);
        }
    }

    #[test]
    fn test_get_tick_count() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let engine_ref = &mut *engine;

            let mut count: i64 = 0;

            // Empty ticks
            let ret = get_tick_count(engine, &mut count);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(count, 0);

            // Add some ticks
            engine_ref.ticks = vec![
                Tick { timestamp: 1000, price: 100.0, volume: 100.0 },
                Tick { timestamp: 2000, price: 101.0, volume: 200.0 },
                Tick { timestamp: 3000, price: 102.0, volume: 300.0 },
            ];

            let ret = get_tick_count(engine, &mut count);
            assert_eq!(ret, ERR_SUCCESS);
            assert_eq!(count, 3);

            free_engine(engine);
        }
    }

    #[test]
    fn test_get_tick_count_null_pointers() {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let mut count: i64 = 0;

            // Null engine
            let ret = get_tick_count(std::ptr::null_mut(), &mut count);
            assert_eq!(ret, ERR_NULL_POINTER);

            // Null count
            let ret = get_tick_count(engine, std::ptr::null_mut());
            assert_eq!(ret, ERR_NULL_POINTER);

            free_engine(engine);
        }
    }
}
