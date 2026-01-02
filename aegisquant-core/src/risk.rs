//! Risk Management module for AegisQuant-Hybrid.
//!
//! Implements pre-trade risk checks including:
//! - Capital adequacy check
//! - Order rate throttling
//! - Position limit enforcement
//! - Maximum drawdown protection

use std::collections::VecDeque;
use std::time::Instant;
use thiserror::Error;

use crate::types::{AccountStatus, OrderRequest, RiskConfig};

/// Risk check error types with specific rejection reasons.
#[derive(Debug, Error, Clone, PartialEq)]
pub enum RiskError {
    #[error("Insufficient capital: required {required:.2}, available {available:.2}")]
    InsufficientCapital { required: f64, available: f64 },

    #[error("Order rate exceeded: {current} orders/sec, max {max}")]
    ThrottleExceeded { current: i32, max: i32 },

    #[error("Position limit exceeded: current {current:.2} + order {order:.2} > max {max:.2}")]
    PositionLimitExceeded { current: f64, order: f64, max: f64 },

    #[error("Max drawdown exceeded: current {current:.2}% > max {max:.2}%")]
    MaxDrawdownExceeded { current: f64, max: f64 },
}

impl RiskError {
    /// Convert error to FFI error code.
    pub fn to_error_code(&self) -> i32 {
        match self {
            RiskError::InsufficientCapital { .. } => crate::ffi::ERR_INSUFFICIENT_CAPITAL,
            RiskError::ThrottleExceeded { .. } => crate::ffi::ERR_THROTTLE_EXCEEDED,
            RiskError::PositionLimitExceeded { .. } => crate::ffi::ERR_POSITION_LIMIT,
            RiskError::MaxDrawdownExceeded { .. } => crate::ffi::ERR_RISK_REJECTED,
        }
    }
}

/// Risk Manager for pre-trade risk validation.
///
/// Performs multiple risk checks before allowing order execution:
/// 1. Capital check - ensures sufficient funds
/// 2. Throttle check - rate limits orders per second
/// 3. Position limit check - prevents over-concentration
/// 4. Drawdown check - stops trading on excessive losses
#[derive(Debug)]
pub struct RiskManager {
    /// Risk configuration parameters
    config: RiskConfig,
    /// Timestamps of recent orders for throttle calculation
    order_timestamps: VecDeque<Instant>,
    /// Peak equity value for drawdown calculation
    peak_equity: f64,
    /// Initial equity for drawdown calculation
    initial_equity: f64,
}

impl RiskManager {
    /// Create a new RiskManager with the given configuration.
    pub fn new(config: RiskConfig) -> Self {
        Self {
            config,
            order_timestamps: VecDeque::with_capacity(config.max_order_rate as usize + 1),
            peak_equity: 0.0,
            initial_equity: 0.0,
        }
    }

    /// Initialize the risk manager with starting equity.
    pub fn initialize(&mut self, initial_equity: f64) {
        self.initial_equity = initial_equity;
        self.peak_equity = initial_equity;
    }

    /// Update peak equity for drawdown tracking.
    pub fn update_equity(&mut self, current_equity: f64) {
        if current_equity > self.peak_equity {
            self.peak_equity = current_equity;
        }
    }

    /// Perform all risk checks on an order.
    ///
    /// # Arguments
    /// * `order` - The order request to validate
    /// * `account` - Current account status
    /// * `current_price` - Current market price for order value calculation
    ///
    /// # Returns
    /// * `Ok(())` if all checks pass
    /// * `Err(RiskError)` with specific rejection reason
    pub fn check(
        &mut self,
        order: &OrderRequest,
        account: &AccountStatus,
        current_price: f64,
    ) -> Result<(), RiskError> {
        self.check_capital(order, account, current_price)?;
        self.check_throttle()?;
        self.check_position_limit(order, account)?;
        self.check_drawdown(account)?;
        Ok(())
    }

    /// Check if account has sufficient capital for the order.
    ///
    /// Calculates order value as: quantity * price
    /// Rejects if order_value > available_balance
    pub fn check_capital(
        &self,
        order: &OrderRequest,
        account: &AccountStatus,
        current_price: f64,
    ) -> Result<(), RiskError> {
        let order_value = order.quantity.abs() * current_price;

        if order_value > account.available {
            return Err(RiskError::InsufficientCapital {
                required: order_value,
                available: account.available,
            });
        }

        // Also check against max_order_value config
        if order_value > self.config.max_order_value {
            return Err(RiskError::InsufficientCapital {
                required: order_value,
                available: self.config.max_order_value,
            });
        }

        Ok(())
    }

    /// Check order rate throttling.
    ///
    /// Uses a sliding window of 1 second to count recent orders.
    /// Rejects if orders in the last second >= max_order_rate.
    pub fn check_throttle(&mut self) -> Result<(), RiskError> {
        let now = Instant::now();
        let one_second_ago = now - std::time::Duration::from_secs(1);

        // Remove timestamps older than 1 second
        while let Some(&front) = self.order_timestamps.front() {
            if front < one_second_ago {
                self.order_timestamps.pop_front();
            } else {
                break;
            }
        }

        let current_rate = self.order_timestamps.len() as i32;

        if current_rate >= self.config.max_order_rate {
            return Err(RiskError::ThrottleExceeded {
                current: current_rate,
                max: self.config.max_order_rate,
            });
        }

        // Record this order timestamp
        self.order_timestamps.push_back(now);

        Ok(())
    }

    /// Check position limit.
    ///
    /// Calculates total position after order execution.
    /// Rejects if total position > max_position_size.
    pub fn check_position_limit(
        &self,
        order: &OrderRequest,
        account: &AccountStatus,
    ) -> Result<(), RiskError> {
        // For simplicity, we use position_count as a proxy for current position size
        // In a real implementation, we'd track actual position quantities per symbol
        let current_position = account.position_count as f64 * 100.0; // Assume 100 units per position
        let order_quantity = order.quantity.abs();

        // Check if adding this order would exceed position limit
        let total_position = current_position + order_quantity;

        if total_position > self.config.max_position_size {
            return Err(RiskError::PositionLimitExceeded {
                current: current_position,
                order: order_quantity,
                max: self.config.max_position_size,
            });
        }

        Ok(())
    }

    /// Check maximum drawdown.
    ///
    /// Calculates current drawdown from peak equity.
    /// Rejects if drawdown > max_drawdown_pct.
    pub fn check_drawdown(&self, account: &AccountStatus) -> Result<(), RiskError> {
        if self.peak_equity <= 0.0 {
            return Ok(()); // Not initialized yet
        }

        let drawdown = (self.peak_equity - account.equity) / self.peak_equity;

        if drawdown > self.config.max_drawdown_pct {
            return Err(RiskError::MaxDrawdownExceeded {
                current: drawdown * 100.0,
                max: self.config.max_drawdown_pct * 100.0,
            });
        }

        Ok(())
    }

    /// Get the current configuration.
    pub fn config(&self) -> &RiskConfig {
        &self.config
    }

    /// Get the current peak equity.
    pub fn peak_equity(&self) -> f64 {
        self.peak_equity
    }

    /// Get the number of orders in the current throttle window.
    pub fn current_order_rate(&self) -> i32 {
        self.order_timestamps.len() as i32
    }

    /// Clear throttle history (useful for testing).
    pub fn clear_throttle_history(&mut self) {
        self.order_timestamps.clear();
    }
}

impl Default for RiskManager {
    fn default() -> Self {
        Self::new(RiskConfig::default())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn create_test_account(available: f64, equity: f64) -> AccountStatus {
        AccountStatus {
            balance: available,
            equity,
            available,
            position_count: 0,
            total_pnl: 0.0,
        }
    }

    fn create_test_order(quantity: f64) -> OrderRequest {
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = quantity;
        order.direction = crate::types::DIRECTION_BUY;
        order
    }

    #[test]
    fn test_risk_manager_creation() {
        let config = RiskConfig::default();
        let rm = RiskManager::new(config);
        assert_eq!(rm.config().max_order_rate, 10);
    }

    #[test]
    fn test_capital_check_pass() {
        let rm = RiskManager::new(RiskConfig::default());
        let account = create_test_account(10000.0, 10000.0);
        let order = create_test_order(10.0);

        let result = rm.check_capital(&order, &account, 100.0);
        assert!(result.is_ok());
    }

    #[test]
    fn test_capital_check_fail() {
        let rm = RiskManager::new(RiskConfig::default());
        let account = create_test_account(100.0, 100.0);
        let order = create_test_order(10.0);

        let result = rm.check_capital(&order, &account, 100.0);
        assert!(matches!(result, Err(RiskError::InsufficientCapital { .. })));
    }

    #[test]
    fn test_throttle_check_pass() {
        let mut rm = RiskManager::new(RiskConfig {
            max_order_rate: 10,
            ..Default::default()
        });

        // Should pass for first few orders
        for _ in 0..5 {
            assert!(rm.check_throttle().is_ok());
        }
    }

    #[test]
    fn test_throttle_check_fail() {
        let mut rm = RiskManager::new(RiskConfig {
            max_order_rate: 3,
            ..Default::default()
        });

        // Fill up the throttle window
        for _ in 0..3 {
            let _ = rm.check_throttle();
        }

        // Next order should fail
        let result = rm.check_throttle();
        assert!(matches!(result, Err(RiskError::ThrottleExceeded { .. })));
    }

    #[test]
    fn test_position_limit_pass() {
        let rm = RiskManager::new(RiskConfig {
            max_position_size: 1000.0,
            ..Default::default()
        });
        let account = create_test_account(10000.0, 10000.0);
        let order = create_test_order(100.0);

        let result = rm.check_position_limit(&order, &account);
        assert!(result.is_ok());
    }

    #[test]
    fn test_position_limit_fail() {
        let rm = RiskManager::new(RiskConfig {
            max_position_size: 50.0,
            ..Default::default()
        });
        let account = create_test_account(10000.0, 10000.0);
        let order = create_test_order(100.0);

        let result = rm.check_position_limit(&order, &account);
        assert!(matches!(result, Err(RiskError::PositionLimitExceeded { .. })));
    }

    #[test]
    fn test_drawdown_check_pass() {
        let mut rm = RiskManager::new(RiskConfig {
            max_drawdown_pct: 0.1, // 10%
            ..Default::default()
        });
        rm.initialize(10000.0);

        let account = create_test_account(9500.0, 9500.0); // 5% drawdown
        let result = rm.check_drawdown(&account);
        assert!(result.is_ok());
    }

    #[test]
    fn test_drawdown_check_fail() {
        let mut rm = RiskManager::new(RiskConfig {
            max_drawdown_pct: 0.1, // 10%
            ..Default::default()
        });
        rm.initialize(10000.0);

        let account = create_test_account(8000.0, 8000.0); // 20% drawdown
        let result = rm.check_drawdown(&account);
        assert!(matches!(result, Err(RiskError::MaxDrawdownExceeded { .. })));
    }

    #[test]
    fn test_full_check_pass() {
        let mut rm = RiskManager::new(RiskConfig {
            max_order_rate: 10,
            max_position_size: 1000.0,
            max_order_value: 100000.0,
            max_drawdown_pct: 0.1,
        });
        rm.initialize(10000.0);

        let account = create_test_account(10000.0, 10000.0);
        let order = create_test_order(10.0);

        let result = rm.check(&order, &account, 100.0);
        assert!(result.is_ok());
    }

    #[test]
    fn test_error_codes() {
        assert_eq!(
            RiskError::InsufficientCapital {
                required: 100.0,
                available: 50.0
            }
            .to_error_code(),
            crate::ffi::ERR_INSUFFICIENT_CAPITAL
        );

        assert_eq!(
            RiskError::ThrottleExceeded { current: 11, max: 10 }.to_error_code(),
            crate::ffi::ERR_THROTTLE_EXCEEDED
        );

        assert_eq!(
            RiskError::PositionLimitExceeded {
                current: 900.0,
                order: 200.0,
                max: 1000.0
            }
            .to_error_code(),
            crate::ffi::ERR_POSITION_LIMIT
        );

        assert_eq!(
            RiskError::MaxDrawdownExceeded {
                current: 15.0,
                max: 10.0
            }
            .to_error_code(),
            crate::ffi::ERR_RISK_REJECTED
        );
    }
}
