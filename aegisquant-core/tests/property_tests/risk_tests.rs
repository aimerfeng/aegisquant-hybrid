//! Property-based tests for Risk Manager order validation.
//!
//! Feature: aegisquant-hybrid, Property 4: Risk Manager Order Validation
//! Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5

use proptest::prelude::*;
use aegisquant_core::types::*;
use aegisquant_core::risk::{RiskManager, RiskError};

/// Generate a random symbol as [u8; 16]
fn arb_symbol() -> impl Strategy<Value = [u8; 16]> {
    proptest::collection::vec(b'A'..=b'Z', 1..=15)
        .prop_map(|chars| {
            let mut symbol = [0u8; 16];
            for (i, &c) in chars.iter().enumerate() {
                symbol[i] = c;
            }
            symbol
        })
}

/// Generate a valid OrderRequest
fn arb_order_request() -> impl Strategy<Value = OrderRequest> {
    (
        arb_symbol(),
        0.01f64..10_000.0,  // quantity
        prop_oneof![Just(1i32), Just(-1i32)],  // direction
        0i32..=1,  // order_type
        0.01f64..1_000_000.0,  // limit_price
    ).prop_map(|(symbol, quantity, direction, order_type, limit_price)| {
        OrderRequest {
            symbol,
            quantity,
            direction,
            order_type,
            limit_price,
        }
    })
}

/// Generate a valid AccountStatus
fn arb_account_status() -> impl Strategy<Value = AccountStatus> {
    (
        0.0f64..10_000_000.0,  // balance
        0.0f64..10_000_000.0,  // equity
        0.0f64..10_000_000.0,  // available
        0i32..100,  // position_count
        -1_000_000.0f64..1_000_000.0,  // total_pnl
    ).prop_map(|(balance, equity, available, position_count, total_pnl)| {
        AccountStatus {
            balance,
            equity,
            available,
            position_count,
            total_pnl,
        }
    })
}

/// Generate a valid RiskConfig
fn arb_risk_config() -> impl Strategy<Value = RiskConfig> {
    (
        1i32..100,  // max_order_rate
        100.0f64..1_000_000.0,  // max_position_size
        1000.0f64..10_000_000.0,  // max_order_value
        0.01f64..0.5,  // max_drawdown_pct
    ).prop_map(|(max_order_rate, max_position_size, max_order_value, max_drawdown_pct)| {
        RiskConfig {
            max_order_rate,
            max_position_size,
            max_order_value,
            max_drawdown_pct,
        }
    })
}

proptest! {
    #![proptest_config(ProptestConfig::with_cases(100))]

    /// Property 4: Capital check rejects orders when available_balance < order_value
    /// For any order where quantity * price > available, the order should be rejected
    /// with InsufficientCapital error.
    #[test]
    fn capital_check_rejects_when_insufficient(
        quantity in 100.0f64..10_000.0,
        price in 100.0f64..1_000.0,
        available_ratio in 0.01f64..0.99,  // available is less than order value
    ) {
        let order_value = quantity * price;
        let available = order_value * available_ratio;  // Always less than order_value
        
        let config = RiskConfig {
            max_order_rate: 100,
            max_position_size: 1_000_000.0,
            max_order_value: 100_000_000.0,  // Very high to not interfere
            max_drawdown_pct: 1.0,
        };
        
        let rm = RiskManager::new(config);
        let account = AccountStatus {
            balance: available,
            equity: available,
            available,
            position_count: 0,
            total_pnl: 0.0,
        };
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        
        let result = rm.check_capital(&order, &account, price);
        
        prop_assert!(
            matches!(result, Err(RiskError::InsufficientCapital { .. })),
            "Expected InsufficientCapital error for order_value={}, available={}",
            order_value, available
        );
    }

    /// Property 4: Capital check passes when available_balance >= order_value
    /// For any order where quantity * price <= available, the order should pass.
    #[test]
    fn capital_check_passes_when_sufficient(
        quantity in 1.0f64..100.0,
        price in 1.0f64..100.0,
        extra_ratio in 1.01f64..10.0,  // available is more than order value
    ) {
        let order_value = quantity * price;
        let available = order_value * extra_ratio;  // Always more than order_value
        
        let config = RiskConfig {
            max_order_rate: 100,
            max_position_size: 1_000_000.0,
            max_order_value: 100_000_000.0,  // Very high to not interfere
            max_drawdown_pct: 1.0,
        };
        
        let rm = RiskManager::new(config);
        let account = AccountStatus {
            balance: available,
            equity: available,
            available,
            position_count: 0,
            total_pnl: 0.0,
        };
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        
        let result = rm.check_capital(&order, &account, price);
        
        prop_assert!(
            result.is_ok(),
            "Expected Ok for order_value={}, available={}",
            order_value, available
        );
    }

    /// Property 4: Throttle check rejects when orders_per_second >= max_rate
    /// For any sequence of orders exceeding max_order_rate, excess orders should be rejected.
    #[test]
    fn throttle_check_rejects_excess_orders(
        max_rate in 1i32..10,
    ) {
        let config = RiskConfig {
            max_order_rate: max_rate,
            max_position_size: 1_000_000.0,
            max_order_value: 100_000_000.0,
            max_drawdown_pct: 1.0,
        };
        
        let mut rm = RiskManager::new(config);
        
        // Fill up the throttle window
        for _ in 0..max_rate {
            let _ = rm.check_throttle();
        }
        
        // Next order should fail
        let result = rm.check_throttle();
        
        prop_assert!(
            matches!(result, Err(RiskError::ThrottleExceeded { .. })),
            "Expected ThrottleExceeded error after {} orders with max_rate={}",
            max_rate, max_rate
        );
    }

    /// Property 4: Throttle check passes when orders_per_second < max_rate
    /// For any number of orders less than max_order_rate, all should pass.
    #[test]
    fn throttle_check_passes_under_limit(
        max_rate in 5i32..20,
        order_count in 1usize..5,
    ) {
        let config = RiskConfig {
            max_order_rate: max_rate,
            max_position_size: 1_000_000.0,
            max_order_value: 100_000_000.0,
            max_drawdown_pct: 1.0,
        };
        
        let mut rm = RiskManager::new(config);
        
        // Submit fewer orders than the limit
        let actual_count = order_count.min((max_rate - 1) as usize);
        for i in 0..actual_count {
            let result = rm.check_throttle();
            prop_assert!(
                result.is_ok(),
                "Expected Ok for order {} with max_rate={}",
                i, max_rate
            );
        }
    }

    /// Property 4: Position limit check rejects when total position > max_position_size
    /// For any order that would cause total position to exceed limit, it should be rejected.
    #[test]
    fn position_limit_rejects_excess(
        max_position in 100.0f64..1000.0,
        order_quantity in 200.0f64..500.0,
    ) {
        // Ensure order_quantity > max_position to trigger rejection
        let actual_max = max_position.min(order_quantity - 1.0);
        
        let config = RiskConfig {
            max_order_rate: 100,
            max_position_size: actual_max,
            max_order_value: 100_000_000.0,
            max_drawdown_pct: 1.0,
        };
        
        let rm = RiskManager::new(config);
        let account = AccountStatus {
            balance: 1_000_000.0,
            equity: 1_000_000.0,
            available: 1_000_000.0,
            position_count: 0,
            total_pnl: 0.0,
        };
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = order_quantity;
        order.direction = DIRECTION_BUY;
        
        let result = rm.check_position_limit(&order, &account);
        
        prop_assert!(
            matches!(result, Err(RiskError::PositionLimitExceeded { .. })),
            "Expected PositionLimitExceeded for order_quantity={}, max_position={}",
            order_quantity, actual_max
        );
    }

    /// Property 4: Position limit check passes when total position <= max_position_size
    #[test]
    fn position_limit_passes_under_limit(
        order_quantity in 1.0f64..100.0,
        extra_capacity in 1.0f64..1000.0,
    ) {
        let max_position = order_quantity + extra_capacity;
        
        let config = RiskConfig {
            max_order_rate: 100,
            max_position_size: max_position,
            max_order_value: 100_000_000.0,
            max_drawdown_pct: 1.0,
        };
        
        let rm = RiskManager::new(config);
        let account = AccountStatus {
            balance: 1_000_000.0,
            equity: 1_000_000.0,
            available: 1_000_000.0,
            position_count: 0,
            total_pnl: 0.0,
        };
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = order_quantity;
        order.direction = DIRECTION_BUY;
        
        let result = rm.check_position_limit(&order, &account);
        
        prop_assert!(
            result.is_ok(),
            "Expected Ok for order_quantity={}, max_position={}",
            order_quantity, max_position
        );
    }

    /// Property 4: Drawdown check rejects when current drawdown > max_drawdown_pct
    #[test]
    fn drawdown_check_rejects_excess(
        peak_equity in 10000.0f64..100000.0,
        max_drawdown_pct in 0.05f64..0.2,
        actual_drawdown_pct in 0.25f64..0.5,  // Always exceeds max
    ) {
        let config = RiskConfig {
            max_order_rate: 100,
            max_position_size: 1_000_000.0,
            max_order_value: 100_000_000.0,
            max_drawdown_pct,
        };
        
        let mut rm = RiskManager::new(config);
        rm.initialize(peak_equity);
        
        let current_equity = peak_equity * (1.0 - actual_drawdown_pct);
        let account = AccountStatus {
            balance: current_equity,
            equity: current_equity,
            available: current_equity,
            position_count: 0,
            total_pnl: current_equity - peak_equity,
        };
        
        let result = rm.check_drawdown(&account);
        
        prop_assert!(
            matches!(result, Err(RiskError::MaxDrawdownExceeded { .. })),
            "Expected MaxDrawdownExceeded for drawdown={}%, max={}%",
            actual_drawdown_pct * 100.0, max_drawdown_pct * 100.0
        );
    }

    /// Property 4: Drawdown check passes when current drawdown <= max_drawdown_pct
    #[test]
    fn drawdown_check_passes_under_limit(
        peak_equity in 10000.0f64..100000.0,
        max_drawdown_pct in 0.1f64..0.5,
        drawdown_ratio in 0.0f64..0.99,  // Ratio of max drawdown to use
    ) {
        let actual_drawdown_pct = max_drawdown_pct * drawdown_ratio;
        
        let config = RiskConfig {
            max_order_rate: 100,
            max_position_size: 1_000_000.0,
            max_order_value: 100_000_000.0,
            max_drawdown_pct,
        };
        
        let mut rm = RiskManager::new(config);
        rm.initialize(peak_equity);
        
        let current_equity = peak_equity * (1.0 - actual_drawdown_pct);
        let account = AccountStatus {
            balance: current_equity,
            equity: current_equity,
            available: current_equity,
            position_count: 0,
            total_pnl: current_equity - peak_equity,
        };
        
        let result = rm.check_drawdown(&account);
        
        prop_assert!(
            result.is_ok(),
            "Expected Ok for drawdown={}%, max={}%",
            actual_drawdown_pct * 100.0, max_drawdown_pct * 100.0
        );
    }

    /// Property 4: Full risk check integrates all individual checks
    /// An order that passes all individual checks should pass the full check.
    #[test]
    fn full_check_integrates_all_checks(
        quantity in 1.0f64..10.0,
        price in 10.0f64..100.0,
    ) {
        let order_value = quantity * price;
        let available = order_value * 10.0;  // Plenty of capital
        
        let config = RiskConfig {
            max_order_rate: 100,
            max_position_size: 10000.0,
            max_order_value: 100_000_000.0,
            max_drawdown_pct: 0.5,
        };
        
        let mut rm = RiskManager::new(config);
        rm.initialize(available);
        
        let account = AccountStatus {
            balance: available,
            equity: available,
            available,
            position_count: 0,
            total_pnl: 0.0,
        };
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        
        let result = rm.check(&order, &account, price);
        
        prop_assert!(
            result.is_ok(),
            "Expected Ok for valid order with quantity={}, price={}, available={}",
            quantity, price, available
        );
    }
}
