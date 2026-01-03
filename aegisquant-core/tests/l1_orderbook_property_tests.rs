//! Property-based tests for L1 Order Book matching correctness.
//!
//! Feature: aegisquant-optimizations, Property 5: L1 OrderBook Matching Correctness
//! Validates: Requirements 5.2, 5.3, 5.4
//!
//! Tests that:
//! - If order quantity <= total orderbook quantity, order should be fully filled
//! - If order quantity > total orderbook quantity, order should be partially filled
//! - Average fill price should equal volume-weighted average price (with slippage)

use proptest::prelude::*;
use aegisquant_core::orderbook::{OrderBookLevel, OrderBookSnapshot};
use aegisquant_core::l1_gateway::{L1SimulatedGateway, SlippageModel};
use aegisquant_core::types::{OrderRequest, DIRECTION_BUY, DIRECTION_SELL};

/// Generate a random order book level with valid values.
fn arb_orderbook_level(min_price: f64, max_price: f64) -> impl Strategy<Value = OrderBookLevel> {
    (min_price..max_price, 10.0..1000.0, 1i32..100)
        .prop_map(|(price, quantity, order_count)| {
            OrderBookLevel::new(price, quantity, order_count)
        })
}

/// Generate a random order book with sorted bid/ask levels.
fn arb_orderbook() -> impl Strategy<Value = OrderBookSnapshot> {
    let bids_strategy = prop::collection::vec(arb_orderbook_level(90.0, 99.0), 3..=5)
        .prop_map(|mut levels| {
            levels.sort_by(|a, b| b.price.partial_cmp(&a.price).unwrap());
            levels
        });
    
    let asks_strategy = prop::collection::vec(arb_orderbook_level(101.0, 110.0), 3..=5)
        .prop_map(|mut levels| {
            levels.sort_by(|a, b| a.price.partial_cmp(&b.price).unwrap());
            levels
        });
    
    (bids_strategy, asks_strategy).prop_map(|(bids, asks)| {
        OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 0)
    })
}

/// Generate a random order quantity.
fn arb_order_quantity() -> impl Strategy<Value = f64> {
    10.0..500.0
}

/// Calculate expected fill result independently.
fn calculate_expected_fill(
    orderbook: &OrderBookSnapshot,
    quantity: f64,
    direction: i32,
    fill_ratio: f64,
    slippage_model: &SlippageModel,
) -> (f64, f64, f64) {
    let levels = if direction == DIRECTION_BUY {
        &orderbook.asks[..orderbook.ask_count as usize]
    } else {
        &orderbook.bids[..orderbook.bid_count as usize]
    };
    
    let mut remaining = quantity;
    let mut total_cost = 0.0;
    let mut filled = 0.0;
    
    for level in levels {
        if remaining <= 0.0 || level.quantity <= 0.0 {
            break;
        }
        
        let available = level.quantity * fill_ratio;
        let fill_qty = remaining.min(available);
        
        let slippage = slippage_model.calculate(fill_qty);
        let fill_price = if direction == DIRECTION_BUY {
            level.price * (1.0 + slippage)
        } else {
            level.price * (1.0 - slippage)
        };
        
        total_cost += fill_price * fill_qty;
        filled += fill_qty;
        remaining -= fill_qty;
    }
    
    let avg_price = if filled > 0.0 { total_cost / filled } else { 0.0 };
    
    (filled, remaining, avg_price)
}

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]
    
    /// Property 5: L1 OrderBook Matching - Buy orders
    #[test]
    fn prop_l1_orderbook_buy_matching(
        orderbook in arb_orderbook(),
        quantity in arb_order_quantity(),
    ) {
        let slippage_model = SlippageModel::new(0.0001, 0.00001, 0.01);
        let fill_ratio = 0.5;
        
        let mut gateway = L1SimulatedGateway::new(1_000_000.0, slippage_model, 0.0);
        gateway.set_fill_ratio(fill_ratio);
        gateway.update_orderbook(orderbook);
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_BUY;
        
        let result = gateway.execute_order(&order);
        
        let (expected_filled, expected_unfilled, expected_avg_price) = 
            calculate_expected_fill(&orderbook, quantity, DIRECTION_BUY, fill_ratio, &slippage_model);
        
        prop_assert!(
            (result.filled_quantity - expected_filled).abs() < 0.0001,
            "Filled quantity mismatch: got {}, expected {}",
            result.filled_quantity, expected_filled
        );
        
        prop_assert!(
            (result.unfilled - expected_unfilled).abs() < 0.0001,
            "Unfilled quantity mismatch: got {}, expected {}",
            result.unfilled, expected_unfilled
        );
        
        if result.filled_quantity > 0.0 {
            prop_assert!(
                (result.average_price - expected_avg_price).abs() < 0.01,
                "Average price mismatch: got {}, expected {}",
                result.average_price, expected_avg_price
            );
        }
        
        prop_assert!(
            (result.filled_quantity + result.unfilled - quantity).abs() < 0.0001,
            "Quantity not conserved: filled {} + unfilled {} != order {}",
            result.filled_quantity, result.unfilled, quantity
        );
    }
    
    /// Property 5: L1 OrderBook Matching - Sell orders
    #[test]
    fn prop_l1_orderbook_sell_matching(
        orderbook in arb_orderbook(),
        quantity in arb_order_quantity(),
    ) {
        let slippage_model = SlippageModel::new(0.0001, 0.00001, 0.01);
        let fill_ratio = 0.5;
        
        let mut gateway = L1SimulatedGateway::new(1_000_000.0, slippage_model, 0.0);
        gateway.set_fill_ratio(fill_ratio);
        gateway.update_orderbook(orderbook);
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = quantity;
        order.direction = DIRECTION_SELL;
        
        let result = gateway.execute_order(&order);
        
        let (expected_filled, expected_unfilled, expected_avg_price) = 
            calculate_expected_fill(&orderbook, quantity, DIRECTION_SELL, fill_ratio, &slippage_model);
        
        prop_assert!(
            (result.filled_quantity - expected_filled).abs() < 0.0001,
            "Filled quantity mismatch: got {}, expected {}",
            result.filled_quantity, expected_filled
        );
        
        prop_assert!(
            (result.unfilled - expected_unfilled).abs() < 0.0001,
            "Unfilled quantity mismatch: got {}, expected {}",
            result.unfilled, expected_unfilled
        );
        
        if result.filled_quantity > 0.0 {
            prop_assert!(
                (result.average_price - expected_avg_price).abs() < 0.01,
                "Average price mismatch: got {}, expected {}",
                result.average_price, expected_avg_price
            );
        }
        
        prop_assert!(
            (result.filled_quantity + result.unfilled - quantity).abs() < 0.0001,
            "Quantity not conserved: filled {} + unfilled {} != order {}",
            result.filled_quantity, result.unfilled, quantity
        );
    }
    
    /// Property 5: Partial fill when order exceeds available liquidity
    #[test]
    fn prop_partial_fill_when_exceeds_liquidity(
        orderbook in arb_orderbook(),
    ) {
        let slippage_model = SlippageModel::default();
        let fill_ratio = 0.5;
        
        let total_ask_liquidity: f64 = orderbook.asks[..orderbook.ask_count as usize]
            .iter()
            .map(|l| l.quantity * fill_ratio)
            .sum();
        
        let order_quantity = total_ask_liquidity * 2.0;
        
        let mut gateway = L1SimulatedGateway::new(10_000_000.0, slippage_model, 0.0);
        gateway.set_fill_ratio(fill_ratio);
        gateway.update_orderbook(orderbook);
        
        let mut order = OrderRequest::with_symbol("TEST");
        order.quantity = order_quantity;
        order.direction = DIRECTION_BUY;
        
        let result = gateway.execute_order(&order);
        
        prop_assert!(
            result.unfilled > 0.0,
            "Expected partial fill but got full fill"
        );
        
        prop_assert!(
            (result.filled_quantity - total_ask_liquidity).abs() < 0.01,
            "Filled {} should be close to available liquidity {}",
            result.filled_quantity, total_ask_liquidity
        );
    }
    
    /// Property 5: Slippage increases with order size
    #[test]
    fn prop_slippage_increases_with_size(
        orderbook in arb_orderbook(),
    ) {
        let slippage_model = SlippageModel::new(0.001, 0.0001, 0.1);
        let fill_ratio = 1.0;
        
        let mut gateway = L1SimulatedGateway::new(10_000_000.0, slippage_model, 0.0);
        gateway.set_fill_ratio(fill_ratio);
        gateway.update_orderbook(orderbook);
        
        let mut small_order = OrderRequest::with_symbol("TEST");
        small_order.quantity = 10.0;
        small_order.direction = DIRECTION_BUY;
        let small_result = gateway.execute_order(&small_order);
        
        let mut large_order = OrderRequest::with_symbol("TEST");
        large_order.quantity = 100.0;
        large_order.direction = DIRECTION_BUY;
        let large_result = gateway.execute_order(&large_order);
        
        if small_result.fills.len() == 1 && large_result.fills.len() == 1 {
            prop_assert!(
                large_result.fills[0].price >= small_result.fills[0].price,
                "Large order should have higher fill price due to slippage"
            );
        }
    }
}
