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
        }
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

/// Log callback function type.
pub type LogCallback = extern "C" fn(level: i32, message: *const c_char);

/// Global log callback (thread-safe placeholder).
static mut LOG_CALLBACK: Option<LogCallback> = None;

/// Set log callback function.
///
/// # Safety
/// - `callback` must be a valid function pointer or null to disable logging
///
/// # Returns
/// - ERR_SUCCESS always
#[no_mangle]
pub unsafe extern "C" fn set_log_callback(callback: LogCallback) -> i32 {
    let result = catch_unwind(|| {
        // SAFETY: This is a simple assignment
        LOG_CALLBACK = Some(callback);
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
}
