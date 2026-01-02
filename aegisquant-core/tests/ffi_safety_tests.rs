//! Property-based tests for FFI safety.
//!
//! Feature: aegisquant-hybrid, Property 2: FFI Safety - Error Codes Instead of Panics
//! Validates: Requirements 2.5, 2.6, 8.1, 8.2, 8.3

use proptest::prelude::*;
use aegisquant_core::ffi::*;
use aegisquant_core::types::*;

proptest! {
    #![proptest_config(ProptestConfig::with_cases(100))]

    /// Property 2: Null engine pointer returns error code, not crash
    #[test]
    fn null_engine_returns_error_code_process_tick(
        timestamp in any::<i64>(),
        price in 0.01f64..1_000_000.0,
        volume in 0.0f64..1_000_000.0
    ) {
        let tick = Tick { timestamp, price, volume };
        
        unsafe {
            let result = process_tick(std::ptr::null_mut(), &tick);
            prop_assert_eq!(result, ERR_NULL_POINTER);
        }
    }

    /// Property 2: Null tick pointer returns error code, not crash
    #[test]
    fn null_tick_returns_error_code(
        _dummy in any::<i32>()  // Just to make it a property test
    ) {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null());
            
            let result = process_tick(engine, std::ptr::null());
            prop_assert_eq!(result, ERR_NULL_POINTER);
            
            free_engine(engine);
        }
    }

    /// Property 2: Invalid price (<=0) returns error code
    #[test]
    fn invalid_price_returns_error_code(
        timestamp in any::<i64>(),
        price in prop_oneof![
            Just(0.0f64),
            Just(-0.01f64),
            -1_000_000.0f64..0.0
        ],
        volume in 0.0f64..1_000_000.0
    ) {
        let tick = Tick { timestamp, price, volume };
        
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null());
            
            let result = process_tick(engine, &tick);
            prop_assert_eq!(result, ERR_INVALID_DATA);
            
            free_engine(engine);
        }
    }

    /// Property 2: Invalid volume (<0) returns error code
    #[test]
    fn invalid_volume_returns_error_code(
        timestamp in any::<i64>(),
        price in 0.01f64..1_000_000.0,
        volume in -1_000_000.0f64..-0.01
    ) {
        let tick = Tick { timestamp, price, volume };
        
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null());
            
            let result = process_tick(engine, &tick);
            prop_assert_eq!(result, ERR_INVALID_DATA);
            
            free_engine(engine);
        }
    }

    /// Property 2: Valid tick returns success
    #[test]
    fn valid_tick_returns_success(
        timestamp in any::<i64>(),
        price in 0.01f64..1_000_000.0,
        volume in 0.0f64..1_000_000.0
    ) {
        let tick = Tick { timestamp, price, volume };
        
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null());
            
            let result = process_tick(engine, &tick);
            prop_assert_eq!(result, ERR_SUCCESS);
            
            free_engine(engine);
        }
    }

    /// Property 2: Null pointers in get_account_status return error code
    #[test]
    fn get_account_status_null_pointers(
        _dummy in any::<i32>()
    ) {
        unsafe {
            // Null engine
            let mut status = AccountStatus::default();
            let result = get_account_status(std::ptr::null_mut(), &mut status);
            prop_assert_eq!(result, ERR_NULL_POINTER);
            
            // Null status
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null());
            
            let result = get_account_status(engine, std::ptr::null_mut());
            prop_assert_eq!(result, ERR_NULL_POINTER);
            
            free_engine(engine);
        }
    }

    /// Property 2: get_account_status with valid pointers returns success
    #[test]
    fn get_account_status_valid_returns_success(
        _dummy in any::<i32>()
    ) {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null());
            
            let mut status = AccountStatus::default();
            let result = get_account_status(engine, &mut status);
            prop_assert_eq!(result, ERR_SUCCESS);
            prop_assert!(status.balance > 0.0);
            
            free_engine(engine);
        }
    }

    /// Property 2: load_data_from_file with null pointers returns error code
    #[test]
    fn load_data_null_pointers(
        _dummy in any::<i32>()
    ) {
        unsafe {
            let mut report = DataQualityReport::default();
            let path = b"test.csv\0".as_ptr() as *const i8;
            
            // Null engine
            let result = load_data_from_file(std::ptr::null_mut(), path, &mut report);
            prop_assert_eq!(result, ERR_NULL_POINTER);
            
            // Null path
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            let result = load_data_from_file(engine, std::ptr::null(), &mut report);
            prop_assert_eq!(result, ERR_NULL_POINTER);
            
            // Null report
            let result = load_data_from_file(engine, path, std::ptr::null_mut());
            prop_assert_eq!(result, ERR_NULL_POINTER);
            
            free_engine(engine);
        }
    }

    /// Property 2: run_backtest with null engine returns error code
    #[test]
    fn run_backtest_null_engine(
        _dummy in any::<i32>()
    ) {
        unsafe {
            let result = run_backtest(std::ptr::null_mut());
            prop_assert_eq!(result, ERR_NULL_POINTER);
        }
    }

    /// Property 2: Engine initialization with various params never crashes
    #[test]
    fn init_engine_never_crashes(
        short_ma in 1i32..100,
        long_ma in 10i32..500,
        position_size in 0.01f64..100_000.0,
        stop_loss in 0.001f64..0.5,
        take_profit in 0.001f64..1.0,
        max_order_rate in 1i32..100,
        max_position in 0.01f64..1_000_000.0,
        max_order_value in 0.01f64..10_000_000.0,
        max_drawdown in 0.01f64..1.0
    ) {
        let params = StrategyParams {
            short_ma_period: short_ma,
            long_ma_period: long_ma,
            position_size,
            stop_loss_pct: stop_loss,
            take_profit_pct: take_profit,
        };
        
        let risk = RiskConfig {
            max_order_rate,
            max_position_size: max_position,
            max_order_value,
            max_drawdown_pct: max_drawdown,
        };
        
        unsafe {
            let engine = init_engine(&params, &risk);
            prop_assert!(!engine.is_null());
            
            // Verify params were stored correctly
            let engine_ref = &*engine;
            prop_assert_eq!(engine_ref.params.short_ma_period, short_ma);
            prop_assert_eq!(engine_ref.params.long_ma_period, long_ma);
            
            free_engine(engine);
        }
    }

    /// Property 2: Multiple init/free cycles don't cause issues
    #[test]
    fn multiple_init_free_cycles(
        cycles in 1usize..10
    ) {
        for _ in 0..cycles {
            unsafe {
                let engine = init_engine(std::ptr::null(), std::ptr::null());
                prop_assert!(!engine.is_null());
                free_engine(engine);
            }
        }
    }
}
