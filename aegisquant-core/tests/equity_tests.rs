//! Property-based tests for Equity Calculation Precision and Consistency.
//!
//! Feature: aegisquant-hybrid, Property 6: Equity Calculation Precision and Consistency
//! Validates: Requirements 3.5, 3.7, 4.6, 7.5

use proptest::prelude::*;
use aegisquant_core::types::*;
use aegisquant_core::gateway::{Gateway, SimulatedGateway};

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]

    /// Property 6: Equity = Balance + Unrealized PnL
    /// For any account state, equity should equal balance plus sum of unrealized PnL.
    #[test]
    fn equity_equals_balance_plus_unrealized_pnl(
        initial_balance in 100000.0f64..1000000.0,
        quantity in 1.0f64..10.0,
        entry_price in 100.0f64..500.0,
        current_price in 100.0f64..500.0,
    ) {
        // Ensure order value doesn't exceed balance
        let order_value = quantity * entry_price;
        prop_assume!(order_value < initial_balance * 0.9);
        
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, 0.0); // No slippage/commission for precision test
        
        // Buy position
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        
        gateway.submit_order(&order, entry_price).unwrap();
        
        // Update price
        gateway.update_price("TEST", current_price);
        
        // Get account status
        let status = gateway.query_account();
        
        // Calculate expected values
        let cost = quantity * entry_price;
        let expected_balance = initial_balance - cost;
        let unrealized_pnl = (current_price - entry_price) * quantity;
        let expected_equity = expected_balance + unrealized_pnl;
        
        prop_assert!(
            (status.balance - expected_balance).abs() < 0.01,
            "Balance mismatch: expected {}, got {}",
            expected_balance, status.balance
        );
        
        prop_assert!(
            (status.equity - expected_equity).abs() < 0.01,
            "Equity mismatch: expected {}, got {} (balance={}, unrealized_pnl={})",
            expected_equity, status.equity, status.balance, unrealized_pnl
        );
    }

    /// Property 6: Unrealized PnL = (Current Price - Average Price) * Quantity
    /// For any position, unrealized PnL should follow this formula.
    #[test]
    fn unrealized_pnl_formula(
        initial_balance in 100000.0f64..1000000.0,
        quantity in 1.0f64..50.0,
        entry_price in 100.0f64..500.0,
        price_change_pct in -0.2f64..0.2,
    ) {
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, 0.0);
        
        // Buy position
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        
        gateway.submit_order(&order, entry_price).unwrap();
        
        // Update to new price
        let current_price = entry_price * (1.0 + price_change_pct);
        gateway.update_price("TEST", current_price);
        
        // Query position
        let position = gateway.query_position("TEST").unwrap();
        
        // Calculate expected unrealized PnL
        let expected_pnl = (current_price - entry_price) * quantity;
        
        prop_assert!(
            (position.unrealized_pnl - expected_pnl).abs() < 0.01,
            "Unrealized PnL mismatch: expected {}, got {}",
            expected_pnl, position.unrealized_pnl
        );
    }

    /// Property 6: Total PnL = Realized PnL + Unrealized PnL
    /// For any account, total PnL should be the sum of realized and unrealized.
    #[test]
    fn total_pnl_is_sum(
        initial_balance in 100000.0f64..500000.0,
        quantity in 1.0f64..20.0,
        entry_price in 100.0f64..300.0,
        exit_price in 100.0f64..300.0,
        final_price in 100.0f64..300.0,
    ) {
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, 0.0);
        
        // Buy position
        let mut buy_order = OrderRequest::with_symbol("TEST");
        buy_order.quantity = quantity * 2.0;
        buy_order.direction = DIRECTION_BUY;
        gateway.submit_order(&buy_order, entry_price).unwrap();
        
        // Sell half (realize some PnL)
        let mut sell_order = OrderRequest::with_symbol("TEST");
        sell_order.quantity = quantity;
        sell_order.direction = DIRECTION_SELL;
        gateway.submit_order(&sell_order, exit_price).unwrap();
        
        // Update to final price
        gateway.update_price("TEST", final_price);
        
        // Get account status
        let status = gateway.query_account();
        
        // Get position for unrealized PnL
        let position = gateway.query_position("TEST");
        
        if let Some(pos) = position {
            let total_pnl = pos.realized_pnl + pos.unrealized_pnl;
            
            prop_assert!(
                (status.total_pnl - total_pnl).abs() < 0.1,
                "Total PnL mismatch: status.total_pnl={}, realized+unrealized={}",
                status.total_pnl, total_pnl
            );
        }
    }

    /// Property 6: Balance changes correctly on trades
    /// For any buy trade, balance should decrease by trade value.
    /// For any sell trade, balance should increase by trade value.
    #[test]
    fn balance_changes_on_trades(
        initial_balance in 100000.0f64..500000.0,
        quantity in 1.0f64..50.0,
        price in 100.0f64..500.0,
    ) {
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, 0.0);
        
        // Buy
        let mut buy_order = OrderRequest::with_symbol("TEST");
        buy_order.quantity = quantity;
        buy_order.direction = DIRECTION_BUY;
        gateway.submit_order(&buy_order, price).unwrap();
        
        let status_after_buy = gateway.query_account();
        let expected_after_buy = initial_balance - (quantity * price);
        
        prop_assert!(
            (status_after_buy.balance - expected_after_buy).abs() < 0.01,
            "Balance after buy: expected {}, got {}",
            expected_after_buy, status_after_buy.balance
        );
        
        // Sell
        let mut sell_order = OrderRequest::with_symbol("TEST");
        sell_order.quantity = quantity;
        sell_order.direction = DIRECTION_SELL;
        gateway.submit_order(&sell_order, price).unwrap();
        
        let status_after_sell = gateway.query_account();
        let expected_after_sell = expected_after_buy + (quantity * price);
        
        prop_assert!(
            (status_after_sell.balance - expected_after_sell).abs() < 0.01,
            "Balance after sell: expected {}, got {}",
            expected_after_sell, status_after_sell.balance
        );
    }

    /// Property 6: Position count reflects open positions
    /// Position count should equal number of symbols with non-zero quantity.
    #[test]
    fn position_count_is_accurate(
        initial_balance in 200000.0f64..500000.0,
        quantity1 in 1.0f64..20.0,
        quantity2 in 1.0f64..20.0,
        price in 100.0f64..200.0,
    ) {
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, 0.0);
        
        // Initially no positions
        let status = gateway.query_account();
        prop_assert_eq!(status.position_count, 0);
        
        // Buy first symbol
        let mut order1 = OrderRequest::with_symbol("SYM1");
        order1.quantity = quantity1;
        order1.direction = DIRECTION_BUY;
        gateway.submit_order(&order1, price).unwrap();
        
        let status = gateway.query_account();
        prop_assert_eq!(status.position_count, 1);
        
        // Buy second symbol
        let mut order2 = OrderRequest::with_symbol("SYM2");
        order2.quantity = quantity2;
        order2.direction = DIRECTION_BUY;
        gateway.submit_order(&order2, price).unwrap();
        
        let status = gateway.query_account();
        prop_assert_eq!(status.position_count, 2);
    }

    /// Property 6: Equity is non-negative for reasonable scenarios
    /// With sufficient initial balance and reasonable trades, equity should stay positive.
    #[test]
    fn equity_stays_positive(
        initial_balance in 100000.0f64..500000.0,
        quantity in 1.0f64..10.0,
        price in 100.0f64..500.0,
        price_drop_pct in 0.0f64..0.3, // Up to 30% drop
    ) {
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, 0.0);
        
        // Buy a small position relative to balance
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        gateway.submit_order(&order, price).unwrap();
        
        // Price drops
        let new_price = price * (1.0 - price_drop_pct);
        gateway.update_price("TEST", new_price);
        
        let status = gateway.query_account();
        
        // Equity should still be positive (we only bought a small amount)
        prop_assert!(
            status.equity > 0.0,
            "Equity should be positive: {} (balance={}, price_drop={}%)",
            status.equity, status.balance, price_drop_pct * 100.0
        );
    }

    /// Property 6: Commission reduces balance
    /// When commission is applied, balance should be reduced accordingly.
    #[test]
    fn commission_reduces_balance(
        initial_balance in 100000.0f64..500000.0,
        quantity in 1.0f64..50.0,
        price in 100.0f64..500.0,
        commission_rate in 0.0001f64..0.01,
    ) {
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, commission_rate);
        
        // Buy
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        gateway.submit_order(&order, price).unwrap();
        
        let status = gateway.query_account();
        let trade_value = quantity * price;
        let commission = trade_value * commission_rate;
        let expected_balance = initial_balance - trade_value - commission;
        
        prop_assert!(
            (status.balance - expected_balance).abs() < 0.01,
            "Balance with commission: expected {}, got {} (commission={})",
            expected_balance, status.balance, commission
        );
    }
}
