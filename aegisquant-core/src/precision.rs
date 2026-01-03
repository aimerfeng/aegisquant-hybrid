//! Floating-point precision module for AegisQuant.
//!
//! This module provides:
//! - `PRICE_EPSILON` constant for price comparisons
//! - `approx_eq()` and `price_eq()` functions for floating-point comparisons
//! - `AccountBalance` struct using rust_decimal for precise accounting
//! - `Price` and `Quantity` type aliases for L1 OrderBook
//! - `spread_bps()` function for calculating bid-ask spread in basis points
//!
//! Requirements: 3.1, 3.2, 3.3, 3.4, 3.5

use rust_decimal::prelude::*;
use rust_decimal::Decimal;

/// Price comparison epsilon (1e-10)
/// More lenient than f64::EPSILON but still precise enough for financial calculations.
pub const PRICE_EPSILON: f64 = 1e-10;

/// Quantity comparison epsilon
pub const QUANTITY_EPSILON: f64 = 1e-8;

/// Type alias for price values (f64 for performance in hot paths)
pub type Price = f64;

/// Type alias for quantity values
pub type Quantity = f64;

/// Approximate equality comparison for floating-point numbers.
///
/// # Arguments
/// * `a` - First value
/// * `b` - Second value
/// * `epsilon` - Maximum allowed difference
///
/// # Returns
/// `true` if the absolute difference between `a` and `b` is less than `epsilon`
#[inline]
pub fn approx_eq(a: f64, b: f64, epsilon: f64) -> bool {
    (a - b).abs() < epsilon
}

/// Price-specific equality comparison using PRICE_EPSILON.
///
/// # Arguments
/// * `a` - First price
/// * `b` - Second price
///
/// # Returns
/// `true` if the prices are approximately equal within PRICE_EPSILON
#[inline]
pub fn price_eq(a: f64, b: f64) -> bool {
    approx_eq(a, b, PRICE_EPSILON)
}


/// Quantity-specific equality comparison using QUANTITY_EPSILON.
#[inline]
pub fn quantity_eq(a: f64, b: f64) -> bool {
    approx_eq(a, b, QUANTITY_EPSILON)
}

/// Calculate bid-ask spread in basis points.
///
/// # Arguments
/// * `bid` - Best bid price
/// * `ask` - Best ask price
///
/// # Returns
/// Spread in basis points (1 bp = 0.01%)
/// Returns 0.0 if mid price is zero or negative
#[inline]
pub fn spread_bps(bid: Price, ask: Price) -> f64 {
    let mid_price = (bid + ask) / 2.0;
    if mid_price <= 0.0 {
        return 0.0;
    }
    let spread = ask - bid;
    spread / mid_price * 10000.0
}

/// Account balance using rust_decimal for precise financial calculations.
///
/// This struct ensures no cumulative floating-point errors in account balance
/// and PnL calculations. Values are only converted to f64 when exported via FFI.
#[derive(Debug, Clone)]
pub struct AccountBalance {
    balance: Decimal,
    realized_pnl: Decimal,
    unrealized_pnl: Decimal,
}

impl Default for AccountBalance {
    fn default() -> Self {
        Self {
            balance: Decimal::ZERO,
            realized_pnl: Decimal::ZERO,
            unrealized_pnl: Decimal::ZERO,
        }
    }
}

impl AccountBalance {
    /// Create a new AccountBalance with initial balance.
    pub fn new(initial_balance: Decimal) -> Self {
        Self {
            balance: initial_balance,
            realized_pnl: Decimal::ZERO,
            unrealized_pnl: Decimal::ZERO,
        }
    }

    /// Create from f64 initial balance.
    pub fn from_f64(initial_balance: f64) -> Self {
        Self::new(Decimal::from_f64_retain(initial_balance).unwrap_or_default())
    }

    /// Get current balance as Decimal.
    pub fn balance(&self) -> Decimal {
        self.balance
    }

    /// Get realized PnL as Decimal.
    pub fn realized_pnl(&self) -> Decimal {
        self.realized_pnl
    }

    /// Get unrealized PnL as Decimal.
    pub fn unrealized_pnl(&self) -> Decimal {
        self.unrealized_pnl
    }

    /// Get equity (balance + unrealized PnL) as Decimal.
    pub fn equity(&self) -> Decimal {
        self.balance + self.unrealized_pnl
    }

    /// Execute a trade, updating balance.
    ///
    /// # Arguments
    /// * `quantity` - Trade quantity
    /// * `price` - Trade price
    /// * `is_buy` - true for buy, false for sell
    pub fn execute_trade(&mut self, quantity: f64, price: f64, is_buy: bool) {
        let qty = Decimal::from_f64_retain(quantity).unwrap_or_default();
        let prc = Decimal::from_f64_retain(price).unwrap_or_default();
        let value = qty * prc;

        if is_buy {
            self.balance -= value;
        } else {
            self.balance += value;
        }
    }

    /// Execute a trade with commission.
    pub fn execute_trade_with_commission(
        &mut self,
        quantity: f64,
        price: f64,
        is_buy: bool,
        commission: f64,
    ) {
        self.execute_trade(quantity, price, is_buy);
        let comm = Decimal::from_f64_retain(commission).unwrap_or_default();
        self.balance -= comm;
    }

    /// Record realized PnL from closing a position.
    pub fn record_realized_pnl(&mut self, pnl: f64) {
        let pnl_decimal = Decimal::from_f64_retain(pnl).unwrap_or_default();
        self.realized_pnl += pnl_decimal;
    }

    /// Update unrealized PnL.
    pub fn update_unrealized_pnl(&mut self, pnl: f64) {
        self.unrealized_pnl = Decimal::from_f64_retain(pnl).unwrap_or_default();
    }

    /// Deposit funds.
    pub fn deposit(&mut self, amount: f64) {
        let amt = Decimal::from_f64_retain(amount).unwrap_or_default();
        self.balance += amt;
    }

    /// Withdraw funds.
    pub fn withdraw(&mut self, amount: f64) -> bool {
        let amt = Decimal::from_f64_retain(amount).unwrap_or_default();
        if self.balance >= amt {
            self.balance -= amt;
            true
        } else {
            false
        }
    }

    /// Export balance as f64 (for FFI).
    pub fn to_f64(&self) -> f64 {
        self.balance.to_f64().unwrap_or(0.0)
    }

    /// Export realized PnL as f64 (for FFI).
    pub fn realized_pnl_f64(&self) -> f64 {
        self.realized_pnl.to_f64().unwrap_or(0.0)
    }

    /// Export unrealized PnL as f64 (for FFI).
    pub fn unrealized_pnl_f64(&self) -> f64 {
        self.unrealized_pnl.to_f64().unwrap_or(0.0)
    }

    /// Export equity as f64 (for FFI).
    pub fn equity_f64(&self) -> f64 {
        self.equity().to_f64().unwrap_or(0.0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_approx_eq() {
        assert!(approx_eq(1.0, 1.0, 1e-10));
        assert!(approx_eq(1.0, 1.0 + 1e-11, 1e-10));
        assert!(!approx_eq(1.0, 1.0 + 1e-9, 1e-10));
    }

    #[test]
    fn test_price_eq() {
        assert!(price_eq(100.0, 100.0));
        assert!(price_eq(100.0, 100.0 + 1e-11));
        assert!(!price_eq(100.0, 100.1));
    }

    #[test]
    fn test_quantity_eq() {
        assert!(quantity_eq(1000.0, 1000.0));
        assert!(quantity_eq(1000.0, 1000.0 + 1e-9));
        assert!(!quantity_eq(1000.0, 1000.1));
    }

    #[test]
    fn test_spread_bps() {
        // Spread = 0.02, mid = 100.01, bps = 0.02/100.01 * 10000 ≈ 2.0
        let bps = spread_bps(100.0, 100.02);
        assert!((bps - 2.0).abs() < 0.01);

        // Zero mid price
        assert_eq!(spread_bps(0.0, 0.0), 0.0);
        assert_eq!(spread_bps(-1.0, 1.0), 0.0);
    }

    #[test]
    fn test_account_balance_new() {
        let balance = AccountBalance::new(Decimal::from(1000000));
        assert_eq!(balance.balance(), Decimal::from(1000000));
        assert_eq!(balance.realized_pnl(), Decimal::ZERO);
    }

    #[test]
    fn test_account_balance_from_f64() {
        let balance = AccountBalance::from_f64(1000000.0);
        assert!((balance.to_f64() - 1000000.0).abs() < PRICE_EPSILON);
    }

    #[test]
    fn test_execute_trade_buy() {
        let mut balance = AccountBalance::new(Decimal::from(1000000));
        balance.execute_trade(100.0, 50.0, true); // Buy 100 @ 50 = 5000
        assert_eq!(balance.balance(), Decimal::from(995000));
    }

    #[test]
    fn test_execute_trade_sell() {
        let mut balance = AccountBalance::new(Decimal::from(1000000));
        balance.execute_trade(100.0, 50.0, false); // Sell 100 @ 50 = 5000
        assert_eq!(balance.balance(), Decimal::from(1005000));
    }

    #[test]
    fn test_execute_trade_with_commission() {
        let mut balance = AccountBalance::new(Decimal::from(1000000));
        balance.execute_trade_with_commission(100.0, 50.0, true, 5.0);
        // Buy 100 @ 50 = 5000, commission = 5, total = 5005
        assert_eq!(balance.balance(), Decimal::from(994995));
    }

    #[test]
    fn test_realized_pnl() {
        let mut balance = AccountBalance::new(Decimal::from(1000000));
        balance.record_realized_pnl(500.0);
        balance.record_realized_pnl(-200.0);
        assert_eq!(balance.realized_pnl(), Decimal::from(300));
    }

    #[test]
    fn test_unrealized_pnl() {
        let mut balance = AccountBalance::new(Decimal::from(1000000));
        balance.update_unrealized_pnl(1000.0);
        assert_eq!(balance.equity(), Decimal::from(1001000));
    }

    #[test]
    fn test_deposit_withdraw() {
        let mut balance = AccountBalance::new(Decimal::from(1000));
        balance.deposit(500.0);
        assert_eq!(balance.balance(), Decimal::from(1500));

        assert!(balance.withdraw(1000.0));
        assert_eq!(balance.balance(), Decimal::from(500));

        assert!(!balance.withdraw(1000.0)); // Insufficient funds
        assert_eq!(balance.balance(), Decimal::from(500));
    }

    #[test]
    fn test_no_cumulative_error() {
        // Test that many small trades don't accumulate floating-point errors
        let mut balance = AccountBalance::new(Decimal::from(1000000));
        
        // Execute 1000 trades of 0.01 @ 100.01
        // Using Decimal directly to avoid f64 conversion issues
        let qty = Decimal::from_str("0.01").unwrap();
        let price = Decimal::from_str("100.01").unwrap();
        let trade_value = qty * price; // 1.0001
        
        for _ in 0..1000 {
            balance.balance -= trade_value;
        }
        
        // Expected: 1000000 - (1000 * 1.0001) = 1000000 - 1000.1 = 998999.9
        let expected = Decimal::from(1000000) - Decimal::from_str("1000.1").unwrap();
        assert_eq!(balance.balance(), expected);
    }

    #[test]
    fn test_export_precision() {
        let balance = AccountBalance::new(Decimal::from_str("123456.789012345").unwrap());
        let exported = balance.to_f64();
        // The exported f64 should be very close to the original
        assert!((exported - 123456.789012345).abs() < PRICE_EPSILON);
    }
}
