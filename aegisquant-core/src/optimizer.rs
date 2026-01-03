//! Parameter optimization module using Rayon for parallel execution.
//!
//! Provides parameter sweep functionality to find optimal strategy parameters
//! by running multiple backtests in parallel.

use rayon::prelude::*;
use std::sync::atomic::{AtomicUsize, Ordering};

use crate::engine::BacktestEngine;
use crate::types::{BacktestResult, RiskConfig, StrategyParams, Tick};

/// Result of a single parameter combination test.
#[derive(Debug, Clone)]
pub struct OptimizationResult {
    /// The parameters used
    pub params: StrategyParams,
    /// The backtest result
    pub result: BacktestResult,
}

/// Parameter range for optimization.
#[derive(Debug, Clone)]
pub struct ParameterRange {
    /// Short MA period range (start, end, step)
    pub short_ma_range: (i32, i32, i32),
    /// Long MA period range (start, end, step)
    pub long_ma_range: (i32, i32, i32),
    /// Position size range (start, end, step)
    pub position_size_range: Option<(f64, f64, f64)>,
}

impl Default for ParameterRange {
    fn default() -> Self {
        Self {
            short_ma_range: (3, 10, 1),
            long_ma_range: (10, 30, 2),
            position_size_range: None,
        }
    }
}

/// Parameter optimizer using Rayon for parallel execution.
#[derive(Debug)]
pub struct Optimizer {
    /// Risk configuration (shared across all runs)
    risk_config: RiskConfig,
    /// Initial balance for backtests
    initial_balance: f64,
    /// Trading symbol
    symbol: String,
    /// Progress counter
    progress: AtomicUsize,
    /// Total combinations to test
    total_combinations: usize,
}

impl Optimizer {
    /// Create a new optimizer with the given risk configuration.
    pub fn new(risk_config: RiskConfig) -> Self {
        Self {
            risk_config,
            initial_balance: 100_000.0,
            symbol: "BTCUSDT".to_string(),
            progress: AtomicUsize::new(0),
            total_combinations: 0,
        }
    }

    /// Set the initial balance for backtests.
    pub fn with_initial_balance(mut self, balance: f64) -> Self {
        self.initial_balance = balance;
        self
    }

    /// Set the trading symbol.
    pub fn with_symbol(mut self, symbol: &str) -> Self {
        self.symbol = symbol.to_string();
        self
    }

    /// Generate all parameter combinations from a range.
    pub fn generate_combinations(&self, range: &ParameterRange) -> Vec<StrategyParams> {
        let mut combinations = Vec::new();

        let (short_start, short_end, short_step) = range.short_ma_range;
        let (long_start, long_end, long_step) = range.long_ma_range;

        let mut short_ma = short_start;
        while short_ma <= short_end {
            let mut long_ma = long_start;
            while long_ma <= long_end {
                // Ensure short_ma < long_ma
                if short_ma < long_ma {
                    let position_sizes = if let Some((ps_start, ps_end, ps_step)) = range.position_size_range {
                        let mut sizes = Vec::new();
                        let mut ps = ps_start;
                        while ps <= ps_end {
                            sizes.push(ps);
                            ps += ps_step;
                        }
                        sizes
                    } else {
                        vec![100.0] // Default position size
                    };

                    for &position_size in &position_sizes {
                        combinations.push(StrategyParams {
                            short_ma_period: short_ma,
                            long_ma_period: long_ma,
                            position_size,
                            stop_loss_pct: 0.02,
                            take_profit_pct: 0.05,
                            warmup_bars: 0,
                        });
                    }
                }
                long_ma += long_step;
            }
            short_ma += short_step;
        }

        combinations
    }

    /// Run parameter sweep with the given tick data.
    ///
    /// Uses Rayon to parallelize across CPU cores.
    pub fn run_parameter_sweep(
        &mut self,
        ticks: &[Tick],
        range: &ParameterRange,
    ) -> Vec<OptimizationResult> {
        let combinations = self.generate_combinations(range);
        self.total_combinations = combinations.len();
        self.progress.store(0, Ordering::SeqCst);

        // Convert ticks to vectors for engine loading
        let timestamps: Vec<i64> = ticks.iter().map(|t| t.timestamp).collect();
        let prices: Vec<f64> = ticks.iter().map(|t| t.price).collect();
        let volumes: Vec<f64> = ticks.iter().map(|t| t.volume).collect();

        // Run backtests in parallel
        let results: Vec<OptimizationResult> = combinations
            .par_iter()
            .filter_map(|params| {
                let result = self.run_single_backtest(
                    params,
                    &timestamps,
                    &prices,
                    &volumes,
                );
                
                // Update progress
                self.progress.fetch_add(1, Ordering::SeqCst);
                
                result.map(|r| OptimizationResult {
                    params: *params,
                    result: r,
                })
            })
            .collect();

        results
    }

    /// Run a single backtest with the given parameters.
    fn run_single_backtest(
        &self,
        params: &StrategyParams,
        timestamps: &[i64],
        prices: &[f64],
        volumes: &[f64],
    ) -> Option<BacktestResult> {
        let mut engine = BacktestEngine::new(*params, self.risk_config)
            .with_initial_balance(self.initial_balance)
            .with_symbol(&self.symbol);

        // Load data
        if engine
            .load_data_from_vectors(
                timestamps.to_vec(),
                prices.to_vec(),
                volumes.to_vec(),
            )
            .is_err()
        {
            return None;
        }

        // Run backtest
        engine.run().ok()
    }

    /// Get current progress (completed / total).
    pub fn progress(&self) -> (usize, usize) {
        (self.progress.load(Ordering::SeqCst), self.total_combinations)
    }

    /// Sort results by a metric (e.g., Sharpe ratio, total return).
    pub fn sort_by_sharpe(results: &mut [OptimizationResult]) {
        results.sort_by(|a, b| {
            b.result
                .sharpe_ratio
                .partial_cmp(&a.result.sharpe_ratio)
                .unwrap_or(std::cmp::Ordering::Equal)
        });
    }

    /// Sort results by total return.
    pub fn sort_by_return(results: &mut [OptimizationResult]) {
        results.sort_by(|a, b| {
            b.result
                .total_return_pct
                .partial_cmp(&a.result.total_return_pct)
                .unwrap_or(std::cmp::Ordering::Equal)
        });
    }

    /// Get the best result by Sharpe ratio.
    pub fn best_by_sharpe(results: &[OptimizationResult]) -> Option<&OptimizationResult> {
        results
            .iter()
            .max_by(|a, b| {
                a.result
                    .sharpe_ratio
                    .partial_cmp(&b.result.sharpe_ratio)
                    .unwrap_or(std::cmp::Ordering::Equal)
            })
    }

    /// Get the best result by total return.
    pub fn best_by_return(results: &[OptimizationResult]) -> Option<&OptimizationResult> {
        results
            .iter()
            .max_by(|a, b| {
                a.result
                    .total_return_pct
                    .partial_cmp(&b.result.total_return_pct)
                    .unwrap_or(std::cmp::Ordering::Equal)
            })
    }
}

impl Default for Optimizer {
    fn default() -> Self {
        Self::new(RiskConfig::default())
    }
}

/// Run parameter sweep from FFI.
///
/// This function is designed to be called from C# via FFI.
pub fn run_parameter_sweep_ffi(
    params_list: &[StrategyParams],
    risk_config: &RiskConfig,
    ticks: &[Tick],
    initial_balance: f64,
) -> Vec<BacktestResult> {
    let timestamps: Vec<i64> = ticks.iter().map(|t| t.timestamp).collect();
    let prices: Vec<f64> = ticks.iter().map(|t| t.price).collect();
    let volumes: Vec<f64> = ticks.iter().map(|t| t.volume).collect();

    params_list
        .par_iter()
        .filter_map(|params| {
            let mut engine = BacktestEngine::new(*params, *risk_config)
                .with_initial_balance(initial_balance);

            if engine
                .load_data_from_vectors(
                    timestamps.clone(),
                    prices.clone(),
                    volumes.clone(),
                )
                .is_err()
            {
                return None;
            }

            engine.run().ok()
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn create_test_ticks() -> Vec<Tick> {
        (0..100)
            .map(|i| {
                let base = 100.0;
                let cycle = (i as f64 / 20.0 * std::f64::consts::PI).sin() * 10.0;
                Tick {
                    timestamp: i,
                    price: base + cycle,
                    volume: 1000.0,
                }
            })
            .collect()
    }

    #[test]
    fn test_optimizer_creation() {
        let optimizer = Optimizer::default();
        assert_eq!(optimizer.initial_balance, 100_000.0);
    }

    #[test]
    fn test_generate_combinations() {
        let optimizer = Optimizer::default();
        let range = ParameterRange {
            short_ma_range: (3, 5, 1),
            long_ma_range: (6, 8, 1),
            position_size_range: None,
        };

        let combinations = optimizer.generate_combinations(&range);
        
        // Should have combinations where short < long
        assert!(!combinations.is_empty());
        for combo in &combinations {
            assert!(combo.short_ma_period < combo.long_ma_period);
        }
    }

    #[test]
    fn test_parameter_sweep() {
        let mut optimizer = Optimizer::default()
            .with_initial_balance(100_000.0);

        let ticks = create_test_ticks();
        let range = ParameterRange {
            short_ma_range: (3, 5, 1),
            long_ma_range: (8, 10, 1),
            position_size_range: None,
        };

        let results = optimizer.run_parameter_sweep(&ticks, &range);
        
        assert!(!results.is_empty());
        for result in &results {
            assert!(result.result.final_equity > 0.0);
        }
    }

    #[test]
    fn test_sort_by_sharpe() {
        let mut results = vec![
            OptimizationResult {
                params: StrategyParams::default(),
                result: BacktestResult {
                    sharpe_ratio: 1.0,
                    ..Default::default()
                },
            },
            OptimizationResult {
                params: StrategyParams::default(),
                result: BacktestResult {
                    sharpe_ratio: 2.0,
                    ..Default::default()
                },
            },
        ];

        Optimizer::sort_by_sharpe(&mut results);
        assert!(results[0].result.sharpe_ratio > results[1].result.sharpe_ratio);
    }

    #[test]
    fn test_best_by_return() {
        let results = vec![
            OptimizationResult {
                params: StrategyParams::default(),
                result: BacktestResult {
                    total_return_pct: 5.0,
                    ..Default::default()
                },
            },
            OptimizationResult {
                params: StrategyParams::default(),
                result: BacktestResult {
                    total_return_pct: 10.0,
                    ..Default::default()
                },
            },
        ];

        let best = Optimizer::best_by_return(&results);
        assert!(best.is_some());
        assert_eq!(best.unwrap().result.total_return_pct, 10.0);
    }

    #[test]
    fn test_parallel_execution_consistency() {
        // Run the same optimization twice and verify results are consistent
        let ticks = create_test_ticks();
        let range = ParameterRange {
            short_ma_range: (3, 4, 1),
            long_ma_range: (6, 7, 1),
            position_size_range: None,
        };

        let mut optimizer1 = Optimizer::default();
        let results1 = optimizer1.run_parameter_sweep(&ticks, &range);

        let mut optimizer2 = Optimizer::default();
        let results2 = optimizer2.run_parameter_sweep(&ticks, &range);

        assert_eq!(results1.len(), results2.len());
        
        // Results should be the same (order might differ due to parallelism)
        for r1 in &results1 {
            let matching = results2.iter().find(|r2| {
                r2.params.short_ma_period == r1.params.short_ma_period
                    && r2.params.long_ma_period == r1.params.long_ma_period
            });
            assert!(matching.is_some());
            let r2 = matching.unwrap();
            assert!((r1.result.final_equity - r2.result.final_equity).abs() < 0.01);
        }
    }
}
