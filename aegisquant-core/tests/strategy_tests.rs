//! Property-based tests for Dual Moving Average Strategy Signal Correctness.
//!
//! Feature: aegisquant-hybrid, Property 3: Dual Moving Average Strategy Signal Correctness
//! Validates: Requirements 3.3, 3.4

use proptest::prelude::*;
use aegisquant_core::types::{StrategyParams, Tick};
use aegisquant_core::strategy::{DualMAStrategy, Signal, Strategy};

/// Create a tick with the given price
fn create_tick(price: f64) -> Tick {
    Tick {
        timestamp: 0,
        price,
        volume: 1000.0,
    }
}

/// Calculate simple moving average from a slice of prices
fn calculate_ma(prices: &[f64], period: usize) -> Option<f64> {
    if prices.len() < period {
        return None;
    }
    let sum: f64 = prices[prices.len() - period..].iter().sum();
    Some(sum / period as f64)
}

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]

    /// Property 3: Buy signal only on golden cross
    /// For any price sequence, a buy signal should only occur when
    /// short MA crosses above long MA (golden cross).
    #[test]
    fn buy_signal_only_on_golden_cross(
        short_period in 2i32..5,
        long_period in 5i32..10,
        price_count in 15usize..30,
        base_price in 100.0f64..1000.0,
        volatility in 0.01f64..0.1,
    ) {
        prop_assume!(short_period < long_period);
        
        let params = StrategyParams {
            short_ma_period: short_period,
            long_ma_period: long_period,
            position_size: 100.0,
            ..Default::default()
        };
        let mut strategy = DualMAStrategy::new(params);
        
        // Generate random price sequence
        let mut prices = Vec::with_capacity(price_count);
        let mut current_price = base_price;
        for i in 0..price_count {
            // Create some trend changes to potentially trigger crossovers
            let trend = if i < price_count / 3 {
                -volatility // Downtrend
            } else if i < 2 * price_count / 3 {
                volatility // Uptrend
            } else {
                -volatility / 2.0 // Slight downtrend
            };
            current_price *= 1.0 + trend;
            prices.push(current_price);
        }
        
        // Track signals and verify they occur at crossovers
        let mut all_prices = Vec::new();
        
        for &price in &prices {
            all_prices.push(price);
            let signal = strategy.on_tick(&create_tick(price));
            
            if signal == Signal::Buy {
                // Verify this is a golden cross
                let short_ma = calculate_ma(&all_prices, short_period as usize);
                let long_ma = calculate_ma(&all_prices, long_period as usize);
                
                if let (Some(short), Some(long)) = (short_ma, long_ma) {
                    // At a buy signal, short MA should be above long MA
                    prop_assert!(
                        short > long,
                        "Buy signal should occur when short MA ({}) > long MA ({})",
                        short, long
                    );
                }
            }
        }
    }

    /// Property 3: Sell signal only on death cross
    /// For any price sequence, a sell signal should only occur when
    /// short MA crosses below long MA (death cross).
    #[test]
    fn sell_signal_only_on_death_cross(
        short_period in 2i32..5,
        long_period in 5i32..10,
        price_count in 15usize..30,
        base_price in 100.0f64..1000.0,
        volatility in 0.01f64..0.1,
    ) {
        prop_assume!(short_period < long_period);
        
        let params = StrategyParams {
            short_ma_period: short_period,
            long_ma_period: long_period,
            position_size: 100.0,
            ..Default::default()
        };
        let mut strategy = DualMAStrategy::new(params);
        
        // Generate random price sequence with trend changes
        let mut prices = Vec::with_capacity(price_count);
        let mut current_price = base_price;
        for i in 0..price_count {
            let trend = if i < price_count / 3 {
                volatility // Uptrend
            } else if i < 2 * price_count / 3 {
                -volatility // Downtrend
            } else {
                volatility / 2.0 // Slight uptrend
            };
            current_price *= 1.0 + trend;
            prices.push(current_price);
        }
        
        let mut all_prices = Vec::new();
        
        for &price in &prices {
            all_prices.push(price);
            let signal = strategy.on_tick(&create_tick(price));
            
            if signal == Signal::Sell {
                // Verify this is a death cross
                let short_ma = calculate_ma(&all_prices, short_period as usize);
                let long_ma = calculate_ma(&all_prices, long_period as usize);
                
                if let (Some(short), Some(long)) = (short_ma, long_ma) {
                    // At a sell signal, short MA should be below long MA
                    prop_assert!(
                        short < long,
                        "Sell signal should occur when short MA ({}) < long MA ({})",
                        short, long
                    );
                }
            }
        }
    }

    /// Property 3: No signal without sufficient data
    /// For any price sequence shorter than long_ma_period, no signal should be generated.
    #[test]
    fn no_signal_without_sufficient_data(
        short_period in 2i32..5,
        long_period in 5i32..15,
        base_price in 100.0f64..1000.0,
    ) {
        prop_assume!(short_period < long_period);
        
        let params = StrategyParams {
            short_ma_period: short_period,
            long_ma_period: long_period,
            position_size: 100.0,
            ..Default::default()
        };
        let mut strategy = DualMAStrategy::new(params);
        
        // Process fewer ticks than long_ma_period
        for i in 0..(long_period - 1) as usize {
            let price = base_price + i as f64;
            let signal = strategy.on_tick(&create_tick(price));
            
            prop_assert_eq!(
                signal, Signal::None,
                "Should not generate signal with only {} ticks (need {} for long MA)",
                i + 1, long_period
            );
        }
    }

    /// Property 3: Signals alternate between buy and sell
    /// After a buy signal, the next signal should be sell (and vice versa).
    #[test]
    fn signals_alternate(
        short_period in 2i32..4,
        long_period in 4i32..8,
        cycle_count in 2usize..5,
        base_price in 100.0f64..500.0,
    ) {
        prop_assume!(short_period < long_period);
        
        let params = StrategyParams {
            short_ma_period: short_period,
            long_ma_period: long_period,
            position_size: 100.0,
            ..Default::default()
        };
        let mut strategy = DualMAStrategy::new(params);
        
        // Generate price sequence with multiple cycles
        let mut prices = Vec::new();
        let cycle_length = (long_period * 3) as usize;
        
        for _cycle in 0..cycle_count {
            for i in 0..cycle_length {
                let phase = (i as f64 / cycle_length as f64) * std::f64::consts::PI * 2.0;
                let price = base_price * (1.0 + 0.2 * phase.sin());
                prices.push(price);
            }
        }
        
        let mut signals: Vec<Signal> = Vec::new();
        
        for &price in &prices {
            let signal = strategy.on_tick(&create_tick(price));
            if signal != Signal::None {
                signals.push(signal);
            }
        }
        
        // Check that signals alternate
        for i in 1..signals.len() {
            prop_assert_ne!(
                signals[i], signals[i - 1],
                "Consecutive signals should alternate: {:?} followed by {:?}",
                signals[i - 1], signals[i]
            );
        }
    }

    /// Property 3: MA values are correctly calculated
    /// For any price sequence, the calculated MAs should match manual calculation.
    #[test]
    fn ma_values_are_correct(
        short_period in 2i32..5,
        long_period in 5i32..10,
        price_count in 10usize..20,
        base_price in 100.0f64..500.0,
    ) {
        prop_assume!(short_period < long_period);
        prop_assume!(price_count >= long_period as usize);
        
        let params = StrategyParams {
            short_ma_period: short_period,
            long_ma_period: long_period,
            position_size: 100.0,
            ..Default::default()
        };
        let mut strategy = DualMAStrategy::new(params);
        
        // Generate prices
        let prices: Vec<f64> = (0..price_count)
            .map(|i| base_price + (i as f64) * 2.0)
            .collect();
        
        // Process all prices
        for &price in &prices {
            strategy.on_tick(&create_tick(price));
        }
        
        // Calculate expected MAs
        let expected_short_ma = calculate_ma(&prices, short_period as usize).unwrap();
        let expected_long_ma = calculate_ma(&prices, long_period as usize).unwrap();
        
        // Get actual MAs from strategy
        let actual_short_ma = strategy.current_short_ma().unwrap();
        let actual_long_ma = strategy.current_long_ma().unwrap();
        
        prop_assert!(
            (actual_short_ma - expected_short_ma).abs() < 0.001,
            "Short MA mismatch: expected {}, got {}",
            expected_short_ma, actual_short_ma
        );
        
        prop_assert!(
            (actual_long_ma - expected_long_ma).abs() < 0.001,
            "Long MA mismatch: expected {}, got {}",
            expected_long_ma, actual_long_ma
        );
    }

    /// Property 3: Strategy reset clears all state
    /// After reset, the strategy should behave as if newly created.
    #[test]
    fn reset_clears_state(
        short_period in 2i32..5,
        long_period in 5i32..10,
        price_count in 10usize..20,
        base_price in 100.0f64..500.0,
    ) {
        prop_assume!(short_period < long_period);
        
        let params = StrategyParams {
            short_ma_period: short_period,
            long_ma_period: long_period,
            position_size: 100.0,
            ..Default::default()
        };
        let mut strategy = DualMAStrategy::new(params);
        
        // Process some prices
        for i in 0..price_count {
            strategy.on_tick(&create_tick(base_price + i as f64));
        }
        
        // Reset
        strategy.reset();
        
        // Verify state is cleared
        prop_assert_eq!(strategy.price_count(), 0);
        prop_assert!(strategy.prev_short_ma().is_none());
        prop_assert!(strategy.prev_long_ma().is_none());
        prop_assert!(strategy.current_short_ma().is_none());
        prop_assert!(strategy.current_long_ma().is_none());
    }
}
