//! Property-based tests for Gateway order routing.
//!
//! Feature: aegisquant-hybrid, Property 8: Order Routing Through Gateway
//! Validates: Requirements 14.4

use proptest::prelude::*;
use aegisquant_core::types::*;
use aegisquant_core::gateway::{Gateway, SimulatedGateway};

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]

    /// Property 8: All orders submitted through Gateway produce fills
    /// For any valid order submitted through Gateway::submit_order, a corresponding
    /// fill should be recorded with matching order details.
    #[test]
    fn orders_produce_fills_through_gateway(
        quantity in 0.1f64..5.0,
        price in 100.0f64..1000.0,
        direction in prop_oneof![Just(DIRECTION_BUY), Just(DIRECTION_SELL)],
    ) {
        let initial_balance = 1_000_000.0; // Large balance to avoid insufficient funds
        let mut gateway = SimulatedGateway::new(initial_balance, 0.001, 0.0001);
        
        // For sell orders, first establish a long position
        if direction == DIRECTION_SELL {
            let mut buy_order = OrderRequest::with_symbol("TEST");
            buy_order.quantity = quantity * 2.0; // Buy more than we'll sell
            buy_order.direction = DIRECTION_BUY;
            gateway.submit_order(&buy_order, price).unwrap();
            gateway.get_fills(); // Clear the buy fill
        }
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = direction;
        
        let order_id = gateway.submit_order(&order, price);
        prop_assert!(order_id.is_ok(), "Order submission should succeed");
        
        let fills = gateway.get_fills();
        prop_assert_eq!(fills.len(), 1, "Should have exactly one fill");
        
        let fill = &fills[0];
        prop_assert_eq!(fill.order_id, order_id.unwrap(), "Fill order_id should match");
        prop_assert!((fill.quantity - quantity).abs() < 0.0001, "Fill quantity should match order");
        prop_assert_eq!(fill.direction, direction, "Fill direction should match order");
    }

    /// Property 8: Gateway maintains order sequence
    /// For any sequence of orders, fills should be produced in the same order.
    #[test]
    fn gateway_maintains_order_sequence(
        order_count in 1usize..5,
        base_quantity in 0.1f64..1.0,
        price in 100.0f64..500.0,
    ) {
        let initial_balance = 1_000_000.0;
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, 0.0);
        
        let mut order_ids = Vec::new();
        
        // Submit multiple buy orders
        for i in 0..order_count {
            let mut order = OrderRequest::with_symbol("TEST");
            order.quantity = base_quantity + (i as f64 * 0.1);
            order.direction = DIRECTION_BUY;
            
            let order_id = gateway.submit_order(&order, price).unwrap();
            order_ids.push(order_id);
        }
        
        let fills = gateway.get_fills();
        prop_assert_eq!(fills.len(), order_count, "Should have fill for each order");
        
        // Verify order IDs are sequential
        for (i, fill) in fills.iter().enumerate() {
            prop_assert_eq!(fill.order_id, order_ids[i], "Fill {} should have correct order_id", i);
        }
    }

    /// Property 8: Gateway applies slippage consistently
    /// For any order, the fill price should include slippage in the correct direction.
    #[test]
    fn gateway_applies_slippage_correctly(
        quantity in 0.1f64..5.0,
        price in 100.0f64..1000.0,
        slippage in 0.001f64..0.05,
        direction in prop_oneof![Just(DIRECTION_BUY), Just(DIRECTION_SELL)],
    ) {
        let initial_balance = 1_000_000.0;
        let mut gateway = SimulatedGateway::new(initial_balance, slippage, 0.0);
        
        // For sell orders, first establish a position
        if direction == DIRECTION_SELL {
            let mut buy_order = OrderRequest::with_symbol("TEST");
            buy_order.quantity = quantity * 2.0;
            buy_order.direction = DIRECTION_BUY;
            gateway.submit_order(&buy_order, price).unwrap();
            gateway.get_fills();
        }
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = direction;
        
        gateway.submit_order(&order, price).unwrap();
        let fills = gateway.get_fills();
        let fill = &fills[0];
        
        let expected_slippage = price * slippage;
        if direction == DIRECTION_BUY {
            // Buy should have higher fill price
            let expected_price = price + expected_slippage;
            prop_assert!(
                (fill.price - expected_price).abs() < 0.01,
                "Buy fill price {} should be {} (base {} + slippage {})",
                fill.price, expected_price, price, expected_slippage
            );
        } else {
            // Sell should have lower fill price
            let expected_price = price - expected_slippage;
            prop_assert!(
                (fill.price - expected_price).abs() < 0.01,
                "Sell fill price {} should be {} (base {} - slippage {})",
                fill.price, expected_price, price, expected_slippage
            );
        }
    }

    /// Property 8: Gateway applies commission correctly
    /// For any order, the fill should include commission based on trade value.
    #[test]
    fn gateway_applies_commission_correctly(
        quantity in 0.1f64..5.0,
        price in 100.0f64..1000.0,
        commission_rate in 0.0001f64..0.01,
    ) {
        let initial_balance = 1_000_000.0;
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, commission_rate);
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        
        gateway.submit_order(&order, price).unwrap();
        let fills = gateway.get_fills();
        let fill = &fills[0];
        
        let trade_value = quantity * price;
        let expected_commission = trade_value * commission_rate;
        
        prop_assert!(
            (fill.commission - expected_commission).abs() < 0.01,
            "Commission {} should be {} (trade_value {} * rate {})",
            fill.commission, expected_commission, trade_value, commission_rate
        );
    }

    /// Property 8: Gateway updates account state after order
    /// For any order routed through Gateway, account state should reflect the trade.
    #[test]
    fn gateway_updates_account_state(
        quantity in 0.1f64..5.0,
        price in 100.0f64..500.0,
    ) {
        let initial_balance = 100_000.0;
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, 0.0);
        
        let status_before = gateway.query_account();
        prop_assert_eq!(status_before.balance, initial_balance);
        prop_assert_eq!(status_before.position_count, 0);
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        
        gateway.submit_order(&order, price).unwrap();
        
        let status_after = gateway.query_account();
        let expected_balance = initial_balance - (quantity * price);
        
        prop_assert!(
            (status_after.balance - expected_balance).abs() < 0.01,
            "Balance {} should be {} after buying {} at {}",
            status_after.balance, expected_balance, quantity, price
        );
        prop_assert_eq!(status_after.position_count, 1, "Should have 1 position after buy");
    }

    /// Property 8: Gateway position query reflects submitted orders
    /// For any order routed through Gateway, position query should return correct data.
    #[test]
    fn gateway_position_reflects_orders(
        quantity in 0.1f64..5.0,
        price in 100.0f64..500.0,
    ) {
        let initial_balance = 100_000.0;
        let mut gateway = SimulatedGateway::new(initial_balance, 0.0, 0.0);
        
        // No position initially
        let position_before = gateway.query_position("TEST");
        prop_assert!(position_before.is_none(), "Should have no position initially");
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        
        gateway.submit_order(&order, price).unwrap();
        
        let position_after = gateway.query_position("TEST");
        prop_assert!(position_after.is_some(), "Should have position after buy");
        
        let pos = position_after.unwrap();
        prop_assert!(
            (pos.quantity - quantity).abs() < 0.0001,
            "Position quantity {} should be {}",
            pos.quantity, quantity
        );
        prop_assert!(
            (pos.average_price - price).abs() < 0.01,
            "Position average_price {} should be {}",
            pos.average_price, price
        );
    }
}
