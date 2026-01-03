//! Property-based tests for multi-engine thread safety.
//!
//! Feature: aegisquant-hybrid, Property 7: Multi-Engine Thread Safety
//! Validates: Requirements 12.2, 12.8
//!
//! For any set of N independent engine instances running in parallel (via Rayon),
//! each engine should produce the same results as if run sequentially,
//! with no data races or corrupted state.

use proptest::prelude::*;
use rayon::prelude::*;

use aegisquant_core::engine::BacktestEngine;
use aegisquant_core::optimizer::{Optimizer, ParameterRange};
use aegisquant_core::types::{RiskConfig, StrategyParams, Tick};

/// Generate valid tick data for testing.
fn generate_test_ticks(count: usize, seed: u64) -> Vec<Tick> {
    let mut ticks = Vec::with_capacity(count);
    let mut price = 100.0;
    
    // Use seed for deterministic generation
    let mut rng_state = seed;
    
    for i in 0..count {
        // Simple LCG for deterministic pseudo-random
        rng_state = rng_state.wrapping_mul(6364136223846793005).wrapping_add(1);
        let random = ((rng_state >> 33) as f64) / (u32::MAX as f64);
        
        // Random walk with bounded changes
        let change = (random - 0.5) * 2.0; // -1.0 to 1.0
        price = (price + change).clamp(50.0, 200.0);
        
        ticks.push(Tick {
            timestamp: i as i64,
            price,
            volume: 1000.0,
        });
    }
    
    ticks
}

/// Run a single backtest and return the result.
fn run_single_backtest(
    params: &StrategyParams,
    risk_config: &RiskConfig,
    ticks: &[Tick],
    initial_balance: f64,
) -> Option<(f64, f64, i32)> {
    let timestamps: Vec<i64> = ticks.iter().map(|t| t.timestamp).collect();
    let prices: Vec<f64> = ticks.iter().map(|t| t.price).collect();
    let volumes: Vec<f64> = ticks.iter().map(|t| t.volume).collect();
    
    let mut engine = BacktestEngine::new(*params, *risk_config)
        .with_initial_balance(initial_balance);
    
    if engine.load_data_from_vectors(timestamps, prices, volumes).is_err() {
        return None;
    }
    
    engine.run().ok().map(|r| (r.final_equity, r.total_return_pct, r.total_trades))
}

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]

    /// Property 7.1: Parallel execution produces same results as serial execution.
    ///
    /// For any set of parameters, running N engines in parallel should produce
    /// the same results as running them sequentially.
    #[test]
    fn parallel_vs_serial_consistency(
        seed in 1u64..10000,
        tick_count in 50usize..200,
        short_ma in 3i32..8,
        long_ma in 10i32..20,
    ) {
        prop_assume!(short_ma < long_ma);
        
        let ticks = generate_test_ticks(tick_count, seed);
        let params = StrategyParams {
            short_ma_period: short_ma,
            long_ma_period: long_ma,
            position_size: 10.0,
            stop_loss_pct: 0.02,
            take_profit_pct: 0.05,
            warmup_bars: 0,
        };
        let risk_config = RiskConfig::default();
        let initial_balance = 100_000.0;
        
        // Run serially
        let serial_result = run_single_backtest(&params, &risk_config, &ticks, initial_balance);
        
        // Run in parallel (same params, should get same result)
        let parallel_results: Vec<_> = (0..4)
            .into_par_iter()
            .map(|_| run_single_backtest(&params, &risk_config, &ticks, initial_balance))
            .collect();
        
        // All parallel results should match serial result
        if let Some((serial_equity, serial_return, serial_trades)) = serial_result {
            for (par_equity, par_return, par_trades) in parallel_results.into_iter().flatten() {
                // Equity should match within floating point tolerance
                prop_assert!(
                    (serial_equity - par_equity).abs() < 0.01,
                    "Equity mismatch: serial={}, parallel={}",
                    serial_equity, par_equity
                );
                prop_assert!(
                    (serial_return - par_return).abs() < 0.01,
                    "Return mismatch: serial={}, parallel={}",
                    serial_return, par_return
                );
                prop_assert_eq!(
                    serial_trades, par_trades,
                    "Trade count mismatch: serial={}, parallel={}",
                    serial_trades, par_trades
                );
            }
        }
    }

    /// Property 7.2: Independent engines with different parameters produce independent results.
    ///
    /// Running multiple engines with different parameters in parallel should not
    /// cause any cross-contamination of state.
    #[test]
    fn independent_engines_no_state_contamination(
        seed in 1u64..10000,
        tick_count in 50usize..150,
    ) {
        let ticks = generate_test_ticks(tick_count, seed);
        let risk_config = RiskConfig::default();
        let initial_balance = 100_000.0;
        
        // Create different parameter sets
        let param_sets: Vec<StrategyParams> = vec![
            StrategyParams {
                short_ma_period: 3,
                long_ma_period: 10,
                position_size: 10.0,
                ..Default::default()
            },
            StrategyParams {
                short_ma_period: 5,
                long_ma_period: 15,
                position_size: 20.0,
                ..Default::default()
            },
            StrategyParams {
                short_ma_period: 7,
                long_ma_period: 20,
                position_size: 15.0,
                ..Default::default()
            },
        ];
        
        // Run each parameter set serially first
        let serial_results: Vec<_> = param_sets
            .iter()
            .map(|p| run_single_backtest(p, &risk_config, &ticks, initial_balance))
            .collect();
        
        // Run all in parallel
        let parallel_results: Vec<_> = param_sets
            .par_iter()
            .map(|p| run_single_backtest(p, &risk_config, &ticks, initial_balance))
            .collect();
        
        // Results should match
        for (i, (serial, parallel)) in serial_results.iter().zip(parallel_results.iter()).enumerate() {
            match (serial, parallel) {
                (Some((s_eq, s_ret, s_trades)), Some((p_eq, p_ret, p_trades))) => {
                    prop_assert!(
                        (s_eq - p_eq).abs() < 0.01,
                        "Param set {} equity mismatch: serial={}, parallel={}",
                        i, s_eq, p_eq
                    );
                    prop_assert!(
                        (s_ret - p_ret).abs() < 0.01,
                        "Param set {} return mismatch: serial={}, parallel={}",
                        i, s_ret, p_ret
                    );
                    prop_assert_eq!(
                        s_trades, p_trades,
                        "Param set {} trade count mismatch",
                        i
                    );
                }
                (None, None) => {} // Both failed, ok
                _ => {
                    prop_assert!(false, "Param set {} result mismatch: one succeeded, one failed", i);
                }
            }
        }
    }

    /// Property 7.3: Optimizer parallel sweep produces consistent results.
    ///
    /// Running the optimizer's parameter sweep multiple times should produce
    /// the same results (order may differ due to parallelism).
    #[test]
    fn optimizer_sweep_consistency(
        seed in 1u64..10000,
        tick_count in 80usize..150,
    ) {
        let ticks = generate_test_ticks(tick_count, seed);
        let range = ParameterRange {
            short_ma_range: (3, 5, 1),
            long_ma_range: (8, 12, 2),
            position_size_range: None,
        };
        
        // Run optimizer twice
        let mut optimizer1 = Optimizer::default().with_initial_balance(100_000.0);
        let results1 = optimizer1.run_parameter_sweep(&ticks, &range);
        
        let mut optimizer2 = Optimizer::default().with_initial_balance(100_000.0);
        let results2 = optimizer2.run_parameter_sweep(&ticks, &range);
        
        // Should have same number of results
        prop_assert_eq!(
            results1.len(), results2.len(),
            "Result count mismatch: {} vs {}",
            results1.len(), results2.len()
        );
        
        // Each result in results1 should have a matching result in results2
        for r1 in &results1 {
            let matching = results2.iter().find(|r2| {
                r2.params.short_ma_period == r1.params.short_ma_period
                    && r2.params.long_ma_period == r1.params.long_ma_period
            });
            
            prop_assert!(
                matching.is_some(),
                "No matching result for params ({}, {})",
                r1.params.short_ma_period, r1.params.long_ma_period
            );
            
            if let Some(r2) = matching {
                prop_assert!(
                    (r1.result.final_equity - r2.result.final_equity).abs() < 0.01,
                    "Equity mismatch for params ({}, {}): {} vs {}",
                    r1.params.short_ma_period, r1.params.long_ma_period,
                    r1.result.final_equity, r2.result.final_equity
                );
            }
        }
    }

    /// Property 7.4: High concurrency stress test.
    ///
    /// Running many engines concurrently should not cause crashes or data corruption.
    #[test]
    fn high_concurrency_stress(
        seed in 1u64..10000,
        tick_count in 50usize..100,
        engine_count in 8usize..16,
    ) {
        let ticks = generate_test_ticks(tick_count, seed);
        let risk_config = RiskConfig::default();
        let initial_balance = 100_000.0;
        
        // Create many different parameter sets
        let param_sets: Vec<StrategyParams> = (0..engine_count)
            .map(|i| StrategyParams {
                short_ma_period: 3 + (i % 5) as i32,
                long_ma_period: 10 + (i % 10) as i32,
                position_size: 10.0 + (i as f64),
                ..Default::default()
            })
            .filter(|p| p.short_ma_period < p.long_ma_period)
            .collect();
        
        // Run all in parallel
        let results: Vec<_> = param_sets
            .par_iter()
            .map(|p| run_single_backtest(p, &risk_config, &ticks, initial_balance))
            .collect();
        
        // All results should be valid (not corrupted)
        for (i, result) in results.iter().enumerate() {
            if let Some((equity, return_pct, trades)) = result {
                // Equity should be positive and reasonable
                prop_assert!(
                    *equity > 0.0 && *equity < 1_000_000_000.0,
                    "Engine {} has invalid equity: {}",
                    i, equity
                );
                // Return should be reasonable (not NaN or Inf)
                prop_assert!(
                    return_pct.is_finite(),
                    "Engine {} has invalid return: {}",
                    i, return_pct
                );
                // Trade count should be non-negative
                prop_assert!(
                    *trades >= 0,
                    "Engine {} has invalid trade count: {}",
                    i, trades
                );
            }
        }
    }
}

#[cfg(test)]
mod determinism_tests {
    use super::*;

    /// Test that the same seed produces the same tick data.
    #[test]
    fn test_tick_generation_determinism() {
        let ticks1 = generate_test_ticks(100, 12345);
        let ticks2 = generate_test_ticks(100, 12345);
        
        assert_eq!(ticks1.len(), ticks2.len());
        for (t1, t2) in ticks1.iter().zip(ticks2.iter()) {
            assert_eq!(t1.timestamp, t2.timestamp);
            assert!((t1.price - t2.price).abs() < f64::EPSILON);
            assert!((t1.volume - t2.volume).abs() < f64::EPSILON);
        }
    }

    /// Test that different seeds produce different tick data.
    #[test]
    fn test_tick_generation_variation() {
        let ticks1 = generate_test_ticks(100, 12345);
        let ticks2 = generate_test_ticks(100, 54321);
        
        // At least some prices should differ
        let different_count = ticks1
            .iter()
            .zip(ticks2.iter())
            .filter(|(t1, t2)| (t1.price - t2.price).abs() > 0.01)
            .count();
        
        assert!(different_count > 50, "Seeds should produce different data");
    }
}
