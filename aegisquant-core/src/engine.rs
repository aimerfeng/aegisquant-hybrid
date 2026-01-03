//! Backtest Engine module.
//!
//! The core engine that orchestrates backtesting by integrating
//! data loading, strategy execution, risk management, and order execution.

use rust_decimal::prelude::*;
use rust_decimal::Decimal;
use std::path::Path;

use crate::data_loader::DataLoader;
use crate::error::{EngineError, EngineResult};
use crate::gateway::{Gateway, SimulatedGateway};
use crate::risk::RiskManager;
use crate::strategy::{DualMAStrategy, Signal, Strategy};
use crate::types::{
    AccountStatus, BacktestResult, DataQualityReport, RiskConfig, StrategyParams, Tick,
};

/// Backtest Engine for running strategy simulations.
///
/// Integrates all components:
/// - Data loading and cleansing
/// - Strategy signal generation
/// - Risk management
/// - Order execution via Gateway
/// - Account and equity tracking
#[derive(Debug)]
pub struct BacktestEngine {
    /// Strategy parameters (kept for potential future use in parameter reporting)
    #[allow(dead_code)]
    params: StrategyParams,
    /// Risk configuration (kept for potential future use in config reporting)
    #[allow(dead_code)]
    risk_config: RiskConfig,
    /// Account balance (using Decimal for precision)
    balance: Decimal,
    /// Initial balance for PnL calculation
    initial_balance: Decimal,
    /// Strategy instance
    strategy: DualMAStrategy,
    /// Risk manager
    risk_manager: RiskManager,
    /// Gateway for order execution
    gateway: SimulatedGateway,
    /// Loaded tick data
    ticks: Vec<Tick>,
    /// Data quality report
    data_report: Option<DataQualityReport>,
    /// Current tick index
    current_index: usize,
    /// Equity curve (for tracking performance)
    equity_curve: Vec<f64>,
    /// Peak equity for drawdown calculation
    peak_equity: Decimal,
    /// Trading symbol
    symbol: String,
    /// Is engine initialized
    initialized: bool,
    /// Total trades executed
    total_trades: i32,
    /// Winning trades
    winning_trades: i32,
    /// Losing trades
    losing_trades: i32,
}

impl BacktestEngine {
    /// Create a new BacktestEngine with the given parameters.
    pub fn new(params: StrategyParams, risk_config: RiskConfig) -> Self {
        let initial_balance = Decimal::from(100_000);
        
        Self {
            params,
            risk_config,
            balance: initial_balance,
            initial_balance,
            strategy: DualMAStrategy::new(params),
            risk_manager: RiskManager::new(risk_config),
            gateway: SimulatedGateway::new(100_000.0, 0.001, 0.0001),
            ticks: Vec::new(),
            data_report: None,
            current_index: 0,
            equity_curve: Vec::new(),
            peak_equity: initial_balance,
            symbol: "BTCUSDT".to_string(),
            initialized: true,
            total_trades: 0,
            winning_trades: 0,
            losing_trades: 0,
        }
    }

    /// Create engine with custom initial balance.
    pub fn with_initial_balance(mut self, balance: f64) -> Self {
        self.balance = Decimal::from_f64(balance).unwrap_or(Decimal::from(100_000));
        self.initial_balance = self.balance;
        self.gateway = SimulatedGateway::new(balance, 0.001, 0.0001);
        self
    }

    /// Set the trading symbol.
    pub fn with_symbol(mut self, symbol: &str) -> Self {
        self.symbol = symbol.to_string();
        self
    }

    /// Load data from a file.
    pub fn load_data<P: AsRef<Path>>(&mut self, path: P) -> EngineResult<DataQualityReport> {
        let loader = DataLoader::new();
        let result = loader.load_from_file(path)?;
        
        self.ticks = result.ticks;
        self.data_report = Some(result.report);
        self.current_index = 0;
        
        // Initialize risk manager with starting equity
        self.risk_manager.initialize(self.balance.to_f64().unwrap_or(100_000.0));
        
        Ok(result.report)
    }

    /// Load data from vectors (for testing).
    pub fn load_data_from_vectors(
        &mut self,
        timestamps: Vec<i64>,
        prices: Vec<f64>,
        volumes: Vec<f64>,
    ) -> EngineResult<DataQualityReport> {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes)?;
        
        self.ticks = result.ticks;
        self.data_report = Some(result.report);
        self.current_index = 0;
        
        // Initialize risk manager
        self.risk_manager.initialize(self.balance.to_f64().unwrap_or(100_000.0));
        
        Ok(result.report)
    }

    /// Process a single tick.
    pub fn process_tick(&mut self, tick: &Tick) -> EngineResult<Option<Signal>> {
        if !self.initialized {
            return Err(EngineError::EngineNotInitialized);
        }

        // Update gateway price
        self.gateway.update_price(&self.symbol, tick.price);
        self.gateway.set_timestamp(tick.timestamp);

        // Get strategy signal
        let signal = self.strategy.on_tick(tick);

        // If signal, try to execute order
        if signal != Signal::None {
            if let Some(order) = self.strategy.generate_order(signal, &self.symbol, tick.price) {
                // Get current account status for risk check
                let account = self.get_account_status();

                // Risk check
                match self.risk_manager.check(&order, &account, tick.price) {
                    Ok(()) => {
                        // Execute order through gateway
                        match self.gateway.submit_order(&order, tick.price) {
                            Ok(_order_id) => {
                                self.total_trades += 1;
                                
                                // Check fills for PnL tracking
                                let fills = self.gateway.get_fills();
                                for fill in fills {
                                    // Simple win/loss tracking based on direction
                                    // In a real system, this would track actual PnL
                                    if fill.direction == crate::types::DIRECTION_SELL {
                                        // Closing a position - check if profitable
                                        if let Some(pos) = self.gateway.query_position(&self.symbol) {
                                            if pos.realized_pnl > 0.0 {
                                                self.winning_trades += 1;
                                            } else if pos.realized_pnl < 0.0 {
                                                self.losing_trades += 1;
                                            }
                                        }
                                    }
                                }
                            }
                            Err(_) => {
                                // Order rejected by gateway
                            }
                        }
                    }
                    Err(_) => {
                        // Order rejected by risk manager
                    }
                }
            }
        }

        // Update equity curve
        let account = self.gateway.query_account();
        self.equity_curve.push(account.equity);

        // Update balance from gateway
        self.balance = Decimal::from_f64(account.balance).unwrap_or(self.balance);

        // Update peak equity for drawdown
        let equity_decimal = Decimal::from_f64(account.equity).unwrap_or(self.balance);
        if equity_decimal > self.peak_equity {
            self.peak_equity = equity_decimal;
        }

        // Update risk manager equity
        self.risk_manager.update_equity(account.equity);

        Ok(Some(signal))
    }

    /// Run the complete backtest.
    pub fn run(&mut self) -> EngineResult<BacktestResult> {
        if self.ticks.is_empty() {
            return Err(EngineError::validation("No data loaded"));
        }

        // Reset state
        self.current_index = 0;
        self.equity_curve.clear();
        self.strategy.reset();
        self.total_trades = 0;
        self.winning_trades = 0;
        self.losing_trades = 0;

        // Process all ticks
        let ticks = self.ticks.clone();
        for tick in &ticks {
            self.process_tick(tick)?;
            self.current_index += 1;
        }

        // Calculate results
        let final_account = self.gateway.query_account();
        let final_equity = final_account.equity;
        let initial = self.initial_balance.to_f64().unwrap_or(100_000.0);
        let total_return_pct = (final_equity - initial) / initial * 100.0;

        // Calculate max drawdown
        let max_drawdown_pct = self.calculate_max_drawdown();

        // Calculate Sharpe ratio (simplified)
        let sharpe_ratio = self.calculate_sharpe_ratio();

        Ok(BacktestResult {
            final_equity,
            total_return_pct,
            max_drawdown_pct,
            sharpe_ratio,
            total_trades: self.total_trades,
            winning_trades: self.winning_trades,
            losing_trades: self.losing_trades,
            actual_start_bar: 0, // TODO: Integrate with WarmupManager
            first_trade_timestamp: 0, // TODO: Track first trade timestamp
        })
    }

    /// Get current account status.
    pub fn get_account_status(&self) -> AccountStatus {
        self.gateway.query_account()
    }

    /// Get the equity curve.
    pub fn equity_curve(&self) -> &[f64] {
        &self.equity_curve
    }

    /// Get the data quality report.
    pub fn data_report(&self) -> Option<&DataQualityReport> {
        self.data_report.as_ref()
    }

    /// Get current tick index.
    pub fn current_index(&self) -> usize {
        self.current_index
    }

    /// Get total tick count.
    pub fn tick_count(&self) -> usize {
        self.ticks.len()
    }

    /// Calculate maximum drawdown from equity curve.
    fn calculate_max_drawdown(&self) -> f64 {
        if self.equity_curve.is_empty() {
            return 0.0;
        }

        let mut peak = self.equity_curve[0];
        let mut max_drawdown = 0.0;

        for &equity in &self.equity_curve {
            if equity > peak {
                peak = equity;
            }
            let drawdown = (peak - equity) / peak;
            if drawdown > max_drawdown {
                max_drawdown = drawdown;
            }
        }

        max_drawdown * 100.0 // Return as percentage
    }

    /// Calculate Sharpe ratio (simplified version).
    fn calculate_sharpe_ratio(&self) -> f64 {
        if self.equity_curve.len() < 2 {
            return 0.0;
        }

        // Calculate returns
        let mut returns = Vec::with_capacity(self.equity_curve.len() - 1);
        for i in 1..self.equity_curve.len() {
            let ret = (self.equity_curve[i] - self.equity_curve[i - 1]) / self.equity_curve[i - 1];
            returns.push(ret);
        }

        if returns.is_empty() {
            return 0.0;
        }

        // Calculate mean return
        let mean_return: f64 = returns.iter().sum::<f64>() / returns.len() as f64;

        // Calculate standard deviation
        let variance: f64 = returns
            .iter()
            .map(|r| (r - mean_return).powi(2))
            .sum::<f64>()
            / returns.len() as f64;
        let std_dev = variance.sqrt();

        if std_dev == 0.0 {
            return 0.0;
        }

        // Annualized Sharpe (assuming daily data, 252 trading days)
        // Simplified: just return mean/std for now
        mean_return / std_dev * (252.0_f64).sqrt()
    }
}

impl Default for BacktestEngine {
    fn default() -> Self {
        Self::new(StrategyParams::default(), RiskConfig::default())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn create_test_data() -> (Vec<i64>, Vec<f64>, Vec<f64>) {
        let timestamps: Vec<i64> = (0..100).collect();
        let prices: Vec<f64> = (0..100)
            .map(|i| {
                // Create a price pattern that will trigger crossovers
                let base = 100.0;
                let cycle = (i as f64 / 20.0 * std::f64::consts::PI).sin() * 10.0;
                base + cycle
            })
            .collect();
        let volumes: Vec<f64> = vec![1000.0; 100];
        (timestamps, prices, volumes)
    }

    #[test]
    fn test_engine_creation() {
        let engine = BacktestEngine::default();
        assert!(engine.initialized);
    }

    #[test]
    fn test_load_data() {
        let mut engine = BacktestEngine::default();
        let (timestamps, prices, volumes) = create_test_data();
        
        let result = engine.load_data_from_vectors(timestamps, prices, volumes);
        assert!(result.is_ok());
        assert_eq!(engine.tick_count(), 100);
    }

    #[test]
    fn test_run_backtest() {
        let params = StrategyParams {
            short_ma_period: 5,
            long_ma_period: 10,
            position_size: 10.0,
            ..Default::default()
        };
        let mut engine = BacktestEngine::new(params, RiskConfig::default())
            .with_initial_balance(100_000.0);
        
        let (timestamps, prices, volumes) = create_test_data();
        engine.load_data_from_vectors(timestamps, prices, volumes).unwrap();
        
        let result = engine.run();
        assert!(result.is_ok());
        
        let result = result.unwrap();
        assert!(result.final_equity > 0.0);
    }

    #[test]
    fn test_equity_curve_tracking() {
        let mut engine = BacktestEngine::default();
        let (timestamps, prices, volumes) = create_test_data();
        engine.load_data_from_vectors(timestamps, prices, volumes).unwrap();
        
        engine.run().unwrap();
        
        assert!(!engine.equity_curve().is_empty());
        assert_eq!(engine.equity_curve().len(), 100);
    }

    #[test]
    fn test_no_data_error() {
        let mut engine = BacktestEngine::default();
        let result = engine.run();
        assert!(matches!(result, Err(EngineError::ValidationError(_))));
    }

    #[test]
    fn test_account_status() {
        let engine = BacktestEngine::default()
            .with_initial_balance(50_000.0);
        
        let status = engine.get_account_status();
        assert!((status.balance - 50_000.0).abs() < 0.01);
    }

    #[test]
    fn test_max_drawdown_calculation() {
        let engine = BacktestEngine {
            equity_curve: vec![100.0, 110.0, 105.0, 95.0, 100.0],
            ..Default::default()
        };
        
        let max_dd = engine.calculate_max_drawdown();
        // Max drawdown from 110 to 95 = 13.6%
        assert!((max_dd - 13.636).abs() < 0.1);
    }
}
