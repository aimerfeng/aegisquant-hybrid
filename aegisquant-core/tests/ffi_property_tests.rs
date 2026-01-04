//! FFI Property Tests for Hybrid Backtest Mode
//!
//! These tests validate the FFI layer behavior using property-based testing.
//! 
//! Properties tested:
//! - Property 4: ProcessTick Invocation - Each tick is processed correctly
//! - Property 6: ExecutionResult Contains Events - Events are properly generated

use proptest::prelude::*;
use aegisquant_core::ffi::*;
use aegisquant_core::types::*;

// ============================================================================
// Test Strategies (Generators)
// ============================================================================

/// Generate valid tick data
fn valid_tick_strategy() -> impl Strategy<Value = Tick> {
    (
        1i64..=1_000_000_000_000i64,  // timestamp
        1.0f64..=10000.0f64,           // price (positive)
        0.0f64..=1_000_000.0f64,       // volume (non-negative)
    ).prop_map(|(timestamp, price, volume)| Tick {
        timestamp,
        price,
        volume,
    })
}

/// Generate a sequence of valid ticks with increasing timestamps
fn valid_tick_sequence_strategy(count: usize) -> impl Strategy<Value = Vec<Tick>> {
    proptest::collection::vec(
        (
            1.0f64..=10000.0f64,           // price
            0.0f64..=1_000_000.0f64,       // volume
        ),
        count..=count
    ).prop_map(|price_volumes| {
        price_volumes.into_iter()
            .enumerate()
            .map(|(i, (price, volume))| Tick {
                timestamp: (i as i64 + 1) * 1_000_000_000, // 1 second apart
                price,
                volume,
            })
            .collect()
    })
}

/// Generate strategy parameters
fn strategy_params_strategy() -> impl Strategy<Value = StrategyParams> {
    (
        1i32..=50i32,                    // short_ma_period
        51i32..=200i32,                  // long_ma_period
        1.0f64..=1000.0f64,              // position_size
        0.001f64..=0.10f64,              // stop_loss_pct
        0.01f64..=0.20f64,               // take_profit_pct
    ).prop_map(|(short_ma, long_ma, pos_size, sl, tp)| StrategyParams {
        short_ma_period: short_ma,
        long_ma_period: long_ma,
        position_size: pos_size,
        stop_loss_pct: sl,
        take_profit_pct: tp,
        warmup_bars: 0,
    })
}

// ============================================================================
// Property 4: ProcessTick Invocation
// ============================================================================

proptest! {
    #![proptest_config(ProptestConfig::with_cases(100))]

    /// Property 4: ProcessTick Invocation
    /// 
    /// For any valid tick sequence, process_tick_with_result should:
    /// 1. Return ERR_SUCCESS for each valid tick
    /// 2. Increment tick_count for each processed tick
    /// 3. Update current_timestamp to the tick's timestamp
    #[test]
    fn property_process_tick_invocation(
        ticks in valid_tick_sequence_strategy(10)
    ) {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null(), "Engine should be initialized");

            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            for (i, tick) in ticks.iter().enumerate() {
                let result = process_tick_with_result(
                    engine,
                    tick,
                    events.as_mut_ptr(),
                    16,
                    &mut event_count,
                );

                // Property: Each valid tick should be processed successfully
                prop_assert_eq!(
                    result, 
                    ERR_SUCCESS, 
                    "process_tick_with_result should return ERR_SUCCESS for valid tick"
                );

                // Property: tick_count should be incremented
                let engine_ref = &*engine;
                prop_assert_eq!(
                    engine_ref.tick_count,
                    (i + 1) as i64,
                    "tick_count should equal number of processed ticks"
                );

                // Property: current_timestamp should be updated
                prop_assert_eq!(
                    engine_ref.current_timestamp,
                    tick.timestamp,
                    "current_timestamp should match the last processed tick"
                );
            }

            free_engine(engine);
        }
    }

    /// Property: Invalid ticks should be rejected
    #[test]
    fn property_invalid_tick_rejection(
        timestamp in 1i64..=1_000_000_000_000i64,
        volume in 0.0f64..=1_000_000.0f64,
    ) {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null());

            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            // Test with invalid price (negative)
            let invalid_tick = Tick {
                timestamp,
                price: -100.0,
                volume,
            };

            let result = process_tick_with_result(
                engine,
                &invalid_tick,
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );

            // Property: Invalid ticks should return ERR_INVALID_DATA
            prop_assert_eq!(
                result,
                ERR_INVALID_DATA,
                "Invalid tick should be rejected"
            );

            // Property: event_count should be 0 for invalid tick
            prop_assert_eq!(
                event_count,
                0,
                "No events should be generated for invalid tick"
            );

            free_engine(engine);
        }
    }
}

// ============================================================================
// Property 6: ExecutionResult Contains Events
// ============================================================================

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]

    /// Property 6: ExecutionResult Contains Events
    /// 
    /// When a stop-loss or take-profit is triggered:
    /// 1. An ExecutionEvent should be generated
    /// 2. The event should have correct event_type
    /// 3. The event should have correct price and quantity
    /// 4. The position should be closed
    #[test]
    fn property_stop_loss_generates_event(
        entry_price in 100.0f64..=1000.0f64,
        position_size in 1.0f64..=100.0f64,
        stop_loss_pct in 0.01f64..=0.05f64,
    ) {
        unsafe {
            let params = StrategyParams {
                stop_loss_pct,
                take_profit_pct: 0.20, // High take profit to avoid triggering
                position_size,
                ..Default::default()
            };
            let engine = init_engine(&params, std::ptr::null());
            prop_assert!(!engine.is_null());

            let engine_ref = &mut *engine;

            // Setup a long position
            engine_ref.position_quantity = position_size;
            engine_ref.entry_price = entry_price;
            engine_ref.account.position_count = 1;

            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            // Create a tick that triggers stop loss (price drops more than stop_loss_pct)
            let trigger_price = entry_price * (1.0 - stop_loss_pct - 0.01);
            let tick = Tick {
                timestamp: 1000,
                price: trigger_price,
                volume: 1000.0,
            };

            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );

            prop_assert_eq!(result, ERR_SUCCESS);

            // Property: Stop loss should generate exactly one event
            prop_assert_eq!(
                event_count,
                1,
                "Stop loss should generate exactly one event"
            );

            // Property: Event type should be STOP_TRIGGERED
            prop_assert_eq!(
                events[0].event_type,
                EVENT_TYPE_STOP_TRIGGERED,
                "Event type should be STOP_TRIGGERED"
            );

            // Property: Event price should match trigger price
            prop_assert!(
                (events[0].price - trigger_price).abs() < 0.001,
                "Event price should match trigger price"
            );

            // Property: Event quantity should match position size
            prop_assert!(
                (events[0].quantity - position_size).abs() < 0.001,
                "Event quantity should match position size"
            );

            // Property: Position should be closed
            let engine_ref = &*engine;
            prop_assert!(
                engine_ref.position_quantity.abs() < 0.001,
                "Position should be closed after stop loss"
            );

            free_engine(engine);
        }
    }

    /// Property: Take profit generates correct event
    #[test]
    fn property_take_profit_generates_event(
        entry_price in 100.0f64..=1000.0f64,
        position_size in 1.0f64..=100.0f64,
        take_profit_pct in 0.02f64..=0.10f64,
    ) {
        unsafe {
            let params = StrategyParams {
                stop_loss_pct: 0.50, // High stop loss to avoid triggering
                take_profit_pct,
                position_size,
                ..Default::default()
            };
            let engine = init_engine(&params, std::ptr::null());
            prop_assert!(!engine.is_null());

            let engine_ref = &mut *engine;

            // Setup a long position
            engine_ref.position_quantity = position_size;
            engine_ref.entry_price = entry_price;
            engine_ref.account.position_count = 1;

            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            // Create a tick that triggers take profit (price rises more than take_profit_pct)
            let trigger_price = entry_price * (1.0 + take_profit_pct + 0.01);
            let tick = Tick {
                timestamp: 1000,
                price: trigger_price,
                volume: 1000.0,
            };

            let result = process_tick_with_result(
                engine,
                &tick,
                events.as_mut_ptr(),
                16,
                &mut event_count,
            );

            prop_assert_eq!(result, ERR_SUCCESS);

            // Property: Take profit should generate exactly one event
            prop_assert_eq!(
                event_count,
                1,
                "Take profit should generate exactly one event"
            );

            // Property: Event type should be TAKE_PROFIT_TRIGGERED
            prop_assert_eq!(
                events[0].event_type,
                EVENT_TYPE_TAKE_PROFIT_TRIGGERED,
                "Event type should be TAKE_PROFIT_TRIGGERED"
            );

            // Property: Realized PnL should be positive
            prop_assert!(
                events[0].realized_pnl > 0.0,
                "Realized PnL should be positive for take profit"
            );

            // Property: Position should be closed
            let engine_ref = &*engine;
            prop_assert!(
                engine_ref.position_quantity.abs() < 0.001,
                "Position should be closed after take profit"
            );

            free_engine(engine);
        }
    }

    /// Property: Place order generates trade event
    #[test]
    fn property_place_order_generates_event(
        price in 10.0f64..=1000.0f64,
        quantity in 1.0f64..=50.0f64,
        is_buy in proptest::bool::ANY,
    ) {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null());

            let mut order_result = OrderResult::default();
            let signal = if is_buy { SIGNAL_BUY } else { SIGNAL_SELL };

            let ret = place_order(engine, signal, price, quantity, &mut order_result);

            prop_assert_eq!(ret, ERR_SUCCESS);

            // Property: Order should be accepted
            prop_assert_eq!(
                order_result.accepted,
                1,
                "Order should be accepted"
            );

            // Property: Fill price should match order price
            prop_assert!(
                (order_result.fill_price - price).abs() < 0.001,
                "Fill price should match order price"
            );

            // Property: Fill quantity should match order quantity
            prop_assert!(
                (order_result.fill_quantity - quantity).abs() < 0.001,
                "Fill quantity should match order quantity"
            );

            // Property: Event queue should contain trade event
            let engine_ref = &*engine;
            prop_assert_eq!(
                engine_ref.event_queue.len(),
                1,
                "Event queue should contain one trade event"
            );

            prop_assert_eq!(
                engine_ref.event_queue[0].event_type,
                EVENT_TYPE_TRADE,
                "Event type should be TRADE"
            );

            let expected_side = if is_buy { DIRECTION_BUY } else { DIRECTION_SELL };
            prop_assert_eq!(
                engine_ref.event_queue[0].side,
                expected_side,
                "Event side should match order direction"
            );

            free_engine(engine);
        }
    }

    /// Property: No events when no position and no triggers
    #[test]
    fn property_no_events_without_position(
        ticks in valid_tick_sequence_strategy(5)
    ) {
        unsafe {
            let engine = init_engine(std::ptr::null(), std::ptr::null());
            prop_assert!(!engine.is_null());

            let mut events = [ExecutionEvent::default(); 16];
            let mut event_count: i32 = 0;

            for tick in &ticks {
                let result = process_tick_with_result(
                    engine,
                    tick,
                    events.as_mut_ptr(),
                    16,
                    &mut event_count,
                );

                prop_assert_eq!(result, ERR_SUCCESS);

                // Property: No events should be generated without a position
                prop_assert_eq!(
                    event_count,
                    0,
                    "No events should be generated without a position"
                );
            }

            free_engine(engine);
        }
    }
}
