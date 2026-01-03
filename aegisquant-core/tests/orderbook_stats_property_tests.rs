//! Property-based tests for orderbook statistics.
//!
//! **Feature: aegisquant-optimizations, Property 9: 盘口统计正确性**
//! **Validates: Requirements 20.4**
//!
//! Tests that OrderBookSnapshot.get_stats() produces results consistent with
//! independent calculations of the same statistics.

use proptest::prelude::*;

use aegisquant_core::orderbook::{OrderBookLevel, OrderBookSnapshot, MAX_LEVELS};
use aegisquant_core::precision::PRICE_EPSILON;

/// Generate a valid orderbook with proper bid/ask spread.
fn orderbook_strategy() -> impl Strategy<Value = OrderBookSnapshot> {
    (
        1usize..=MAX_LEVELS,  // bid_count
        1usize..=MAX_LEVELS,  // ask_count
        50.0f64..100.0,       // base_bid_price
        0.1f64..5.0,          // spread
        50.0f64..150.0,       // last_price
        0i64..1_000_000_000,  // timestamp
    )
        .prop_flat_map(|(bid_count, ask_count, base_bid, spread, last_price, timestamp)| {
            let base_ask = base_bid + spread;
            
            // Generate bids below base_bid
            let bids = prop::collection::vec(
                (0.0f64..10.0, 1.0f64..10000.0, 1i32..100),
                bid_count
            ).prop_map(move |levels| {
                levels.iter().enumerate().map(|(i, (offset, qty, orders))| {
                    OrderBookLevel::new(base_bid - offset - (i as f64 * 0.1), *qty, *orders)
                }).collect::<Vec<_>>()
            });
            
            // Generate asks above base_ask
            let asks = prop::collection::vec(
                (0.0f64..10.0, 1.0f64..10000.0, 1i32..100),
                ask_count
            ).prop_map(move |levels| {
                levels.iter().enumerate().map(|(i, (offset, qty, orders))| {
                    OrderBookLevel::new(base_ask + offset + (i as f64 * 0.1), *qty, *orders)
                }).collect::<Vec<_>>()
            });
            
            (bids, asks, Just(last_price), Just(timestamp))
        })
        .prop_map(|(bids, asks, last_price, timestamp)| {
            OrderBookSnapshot::with_levels(&bids, &asks, last_price, timestamp)
        })
}

/// Independent calculation of total bid volume.
fn independent_total_bid_volume(snapshot: &OrderBookSnapshot) -> f64 {
    snapshot.bids[..snapshot.bid_count as usize]
        .iter()
        .map(|l| l.quantity)
        .sum()
}

/// Independent calculation of total ask volume.
fn independent_total_ask_volume(snapshot: &OrderBookSnapshot) -> f64 {
    snapshot.asks[..snapshot.ask_count as usize]
        .iter()
        .map(|l| l.quantity)
        .sum()
}

/// Independent calculation of spread.
fn independent_spread(snapshot: &OrderBookSnapshot) -> f64 {
    if snapshot.bid_count > 0 && snapshot.ask_count > 0 {
        snapshot.asks[0].price - snapshot.bids[0].price
    } else {
        0.0
    }
}

/// Independent calculation of bid/ask ratio.
fn independent_bid_ask_ratio(snapshot: &OrderBookSnapshot) -> f64 {
    let total_bid = independent_total_bid_volume(snapshot);
    let total_ask = independent_total_ask_volume(snapshot);
    if total_ask > 0.0 {
        total_bid / total_ask
    } else {
        0.0
    }
}

/// Independent calculation of total bid orders.
fn independent_total_bid_orders(snapshot: &OrderBookSnapshot) -> i32 {
    snapshot.bids[..snapshot.bid_count as usize]
        .iter()
        .map(|l| l.order_count)
        .sum()
}

/// Independent calculation of total ask orders.
fn independent_total_ask_orders(snapshot: &OrderBookSnapshot) -> i32 {
    snapshot.asks[..snapshot.ask_count as usize]
        .iter()
        .map(|l| l.order_count)
        .sum()
}

proptest! {
    #![proptest_config(ProptestConfig::with_cases(100))]

    /// Property 9: total_bid_volume equals sum of bid quantities.
    ///
    /// **Feature: aegisquant-optimizations, Property 9: 盘口统计正确性**
    /// **Validates: Requirements 20.4**
    ///
    /// For any OrderBookSnapshot, total_bid_volume should equal
    /// the sum of all bid level quantities.
    #[test]
    fn prop_total_bid_volume_equals_sum(
        snapshot in orderbook_strategy()
    ) {
        let stats = snapshot.get_stats();
        let expected = independent_total_bid_volume(&snapshot);
        
        let diff = (stats.total_bid_volume - expected).abs();
        prop_assert!(
            diff < PRICE_EPSILON,
            "total_bid_volume mismatch: got={}, expected={}, diff={}",
            stats.total_bid_volume, expected, diff
        );
    }

    /// Property 9: total_ask_volume equals sum of ask quantities.
    ///
    /// **Feature: aegisquant-optimizations, Property 9: 盘口统计正确性**
    /// **Validates: Requirements 20.4**
    ///
    /// For any OrderBookSnapshot, total_ask_volume should equal
    /// the sum of all ask level quantities.
    #[test]
    fn prop_total_ask_volume_equals_sum(
        snapshot in orderbook_strategy()
    ) {
        let stats = snapshot.get_stats();
        let expected = independent_total_ask_volume(&snapshot);
        
        let diff = (stats.total_ask_volume - expected).abs();
        prop_assert!(
            diff < PRICE_EPSILON,
            "total_ask_volume mismatch: got={}, expected={}, diff={}",
            stats.total_ask_volume, expected, diff
        );
    }

    /// Property 9: spread equals asks[0].price - bids[0].price.
    ///
    /// **Feature: aegisquant-optimizations, Property 9: 盘口统计正确性**
    /// **Validates: Requirements 20.4**
    ///
    /// For any OrderBookSnapshot with both bids and asks,
    /// spread should equal best_ask - best_bid.
    #[test]
    fn prop_spread_equals_best_ask_minus_best_bid(
        snapshot in orderbook_strategy()
    ) {
        let stats = snapshot.get_stats();
        let expected = independent_spread(&snapshot);
        
        let diff = (stats.spread - expected).abs();
        prop_assert!(
            diff < PRICE_EPSILON,
            "spread mismatch: got={}, expected={}, diff={}",
            stats.spread, expected, diff
        );
    }

    /// Property 9: bid_ask_ratio equals total_bid_volume / total_ask_volume.
    ///
    /// **Feature: aegisquant-optimizations, Property 9: 盘口统计正确性**
    /// **Validates: Requirements 20.4**
    ///
    /// For any OrderBookSnapshot, bid_ask_ratio should equal
    /// total_bid_volume divided by total_ask_volume.
    #[test]
    fn prop_bid_ask_ratio_equals_volume_ratio(
        snapshot in orderbook_strategy()
    ) {
        let stats = snapshot.get_stats();
        let expected = independent_bid_ask_ratio(&snapshot);
        
        let diff = (stats.bid_ask_ratio - expected).abs();
        prop_assert!(
            diff < 0.0001,
            "bid_ask_ratio mismatch: got={}, expected={}, diff={}",
            stats.bid_ask_ratio, expected, diff
        );
    }

    /// Property 9: total_bid_orders equals sum of bid order counts.
    ///
    /// **Feature: aegisquant-optimizations, Property 9: 盘口统计正确性**
    /// **Validates: Requirements 20.4**
    ///
    /// For any OrderBookSnapshot, total_bid_orders should equal
    /// the sum of all bid level order counts.
    #[test]
    fn prop_total_bid_orders_equals_sum(
        snapshot in orderbook_strategy()
    ) {
        let stats = snapshot.get_stats();
        let expected = independent_total_bid_orders(&snapshot);
        
        prop_assert_eq!(
            stats.total_bid_orders, expected,
            "total_bid_orders mismatch: got={}, expected={}",
            stats.total_bid_orders, expected
        );
    }

    /// Property 9: total_ask_orders equals sum of ask order counts.
    ///
    /// **Feature: aegisquant-optimizations, Property 9: 盘口统计正确性**
    /// **Validates: Requirements 20.4**
    ///
    /// For any OrderBookSnapshot, total_ask_orders should equal
    /// the sum of all ask level order counts.
    #[test]
    fn prop_total_ask_orders_equals_sum(
        snapshot in orderbook_strategy()
    ) {
        let stats = snapshot.get_stats();
        let expected = independent_total_ask_orders(&snapshot);
        
        prop_assert_eq!(
            stats.total_ask_orders, expected,
            "total_ask_orders mismatch: got={}, expected={}",
            stats.total_ask_orders, expected
        );
    }

    /// Property 9: bid_levels and ask_levels match snapshot counts.
    ///
    /// **Feature: aegisquant-optimizations, Property 9: 盘口统计正确性**
    /// **Validates: Requirements 20.4**
    ///
    /// For any OrderBookSnapshot, the stats should correctly
    /// report the number of bid and ask levels.
    #[test]
    fn prop_level_counts_match(
        snapshot in orderbook_strategy()
    ) {
        let stats = snapshot.get_stats();
        
        prop_assert_eq!(
            stats.bid_levels, snapshot.bid_count,
            "bid_levels mismatch: got={}, expected={}",
            stats.bid_levels, snapshot.bid_count
        );
        prop_assert_eq!(
            stats.ask_levels, snapshot.ask_count,
            "ask_levels mismatch: got={}, expected={}",
            stats.ask_levels, snapshot.ask_count
        );
    }

    /// Property 9: All stats values are finite and non-negative.
    ///
    /// **Feature: aegisquant-optimizations, Property 9: 盘口统计正确性**
    /// **Validates: Requirements 20.4**
    ///
    /// For any OrderBookSnapshot, all statistics should be
    /// finite numbers and volumes/counts should be non-negative.
    #[test]
    fn prop_stats_values_valid(
        snapshot in orderbook_strategy()
    ) {
        let stats = snapshot.get_stats();
        
        // All f64 values should be finite
        prop_assert!(stats.total_bid_volume.is_finite(), "total_bid_volume is not finite");
        prop_assert!(stats.total_ask_volume.is_finite(), "total_ask_volume is not finite");
        prop_assert!(stats.spread.is_finite(), "spread is not finite");
        prop_assert!(stats.spread_bps.is_finite(), "spread_bps is not finite");
        prop_assert!(stats.bid_ask_ratio.is_finite(), "bid_ask_ratio is not finite");
        
        // Volumes should be non-negative
        prop_assert!(stats.total_bid_volume >= 0.0, "total_bid_volume is negative");
        prop_assert!(stats.total_ask_volume >= 0.0, "total_ask_volume is negative");
        
        // Order counts should be non-negative
        prop_assert!(stats.total_bid_orders >= 0, "total_bid_orders is negative");
        prop_assert!(stats.total_ask_orders >= 0, "total_ask_orders is negative");
        prop_assert!(stats.bid_levels >= 0, "bid_levels is negative");
        prop_assert!(stats.ask_levels >= 0, "ask_levels is negative");
    }
}

#[cfg(test)]
mod unit_tests {
    use super::*;

    #[test]
    fn test_empty_orderbook_stats() {
        let snapshot = OrderBookSnapshot::default();
        let stats = snapshot.get_stats();
        
        assert_eq!(stats.total_bid_volume, 0.0);
        assert_eq!(stats.total_ask_volume, 0.0);
        assert_eq!(stats.spread, 0.0);
        assert_eq!(stats.bid_ask_ratio, 0.0);
        assert_eq!(stats.total_bid_orders, 0);
        assert_eq!(stats.total_ask_orders, 0);
        assert_eq!(stats.bid_levels, 0);
        assert_eq!(stats.ask_levels, 0);
    }

    #[test]
    fn test_single_level_orderbook_stats() {
        let bids = vec![OrderBookLevel::new(99.0, 100.0, 10)];
        let asks = vec![OrderBookLevel::new(101.0, 200.0, 20)];
        let snapshot = OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 0);
        let stats = snapshot.get_stats();
        
        assert_eq!(stats.total_bid_volume, 100.0);
        assert_eq!(stats.total_ask_volume, 200.0);
        assert!((stats.spread - 2.0).abs() < PRICE_EPSILON);
        assert!((stats.bid_ask_ratio - 0.5).abs() < 0.001);
        assert_eq!(stats.total_bid_orders, 10);
        assert_eq!(stats.total_ask_orders, 20);
        assert_eq!(stats.bid_levels, 1);
        assert_eq!(stats.ask_levels, 1);
    }

    #[test]
    fn test_multi_level_orderbook_stats() {
        let bids = vec![
            OrderBookLevel::new(99.0, 100.0, 10),
            OrderBookLevel::new(98.0, 200.0, 20),
            OrderBookLevel::new(97.0, 300.0, 30),
        ];
        let asks = vec![
            OrderBookLevel::new(101.0, 150.0, 15),
            OrderBookLevel::new(102.0, 250.0, 25),
        ];
        let snapshot = OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 0);
        let stats = snapshot.get_stats();
        
        // Total bid volume: 100 + 200 + 300 = 600
        assert_eq!(stats.total_bid_volume, 600.0);
        // Total ask volume: 150 + 250 = 400
        assert_eq!(stats.total_ask_volume, 400.0);
        // Spread: 101 - 99 = 2
        assert!((stats.spread - 2.0).abs() < PRICE_EPSILON);
        // Bid/ask ratio: 600 / 400 = 1.5
        assert!((stats.bid_ask_ratio - 1.5).abs() < 0.001);
        // Total bid orders: 10 + 20 + 30 = 60
        assert_eq!(stats.total_bid_orders, 60);
        // Total ask orders: 15 + 25 = 40
        assert_eq!(stats.total_ask_orders, 40);
        assert_eq!(stats.bid_levels, 3);
        assert_eq!(stats.ask_levels, 2);
    }

    #[test]
    fn test_independent_calculations() {
        let bids = vec![
            OrderBookLevel::new(99.0, 100.0, 10),
            OrderBookLevel::new(98.0, 200.0, 20),
        ];
        let asks = vec![
            OrderBookLevel::new(101.0, 150.0, 15),
            OrderBookLevel::new(102.0, 250.0, 25),
        ];
        let snapshot = OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 0);
        
        assert_eq!(independent_total_bid_volume(&snapshot), 300.0);
        assert_eq!(independent_total_ask_volume(&snapshot), 400.0);
        assert!((independent_spread(&snapshot) - 2.0).abs() < PRICE_EPSILON);
        assert!((independent_bid_ask_ratio(&snapshot) - 0.75).abs() < 0.001);
        assert_eq!(independent_total_bid_orders(&snapshot), 30);
        assert_eq!(independent_total_ask_orders(&snapshot), 40);
    }
}
