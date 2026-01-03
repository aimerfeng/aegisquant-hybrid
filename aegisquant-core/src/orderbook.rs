//! L1 Order Book module for market microstructure simulation.
//!
//! Provides order book data structures and statistics for simulating
//! realistic market depth and liquidity conditions.

use crate::precision::{Price, Quantity, spread_bps};

/// Maximum number of price levels in the order book.
pub const MAX_LEVELS: usize = 10;

/// Single price level in the order book.
///
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct OrderBookLevel {
    /// Price at this level
    pub price: Price,
    /// Total quantity at this level
    pub quantity: Quantity,
    /// Number of orders at this level
    pub order_count: i32,
}

impl OrderBookLevel {
    /// Create a new order book level.
    pub fn new(price: Price, quantity: Quantity, order_count: i32) -> Self {
        Self {
            price,
            quantity,
            order_count,
        }
    }

    /// Check if this level is empty (no quantity).
    pub fn is_empty(&self) -> bool {
        self.quantity <= 0.0
    }
}

/// Order book snapshot containing bid and ask levels.
///
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct OrderBookSnapshot {
    /// Bid levels (buy orders), sorted by price descending (best bid first)
    pub bids: [OrderBookLevel; MAX_LEVELS],
    /// Ask levels (sell orders), sorted by price ascending (best ask first)
    pub asks: [OrderBookLevel; MAX_LEVELS],
    /// Number of valid bid levels
    pub bid_count: i32,
    /// Number of valid ask levels
    pub ask_count: i32,
    /// Last traded price
    pub last_price: Price,
    /// Timestamp in nanoseconds
    pub timestamp: i64,
}

impl Default for OrderBookSnapshot {
    fn default() -> Self {
        Self {
            bids: [OrderBookLevel::default(); MAX_LEVELS],
            asks: [OrderBookLevel::default(); MAX_LEVELS],
            bid_count: 0,
            ask_count: 0,
            last_price: 0.0,
            timestamp: 0,
        }
    }
}

impl OrderBookSnapshot {
    /// Create a new empty order book snapshot.
    pub fn new() -> Self {
        Self::default()
    }

    /// Create an order book snapshot with the given bid and ask levels.
    pub fn with_levels(
        bids: &[OrderBookLevel],
        asks: &[OrderBookLevel],
        last_price: Price,
        timestamp: i64,
    ) -> Self {
        let mut snapshot = Self::new();
        
        let bid_count = bids.len().min(MAX_LEVELS);
        for (i, level) in bids.iter().take(bid_count).enumerate() {
            snapshot.bids[i] = *level;
        }
        snapshot.bid_count = bid_count as i32;
        
        let ask_count = asks.len().min(MAX_LEVELS);
        for (i, level) in asks.iter().take(ask_count).enumerate() {
            snapshot.asks[i] = *level;
        }
        snapshot.ask_count = ask_count as i32;
        
        snapshot.last_price = last_price;
        snapshot.timestamp = timestamp;
        
        snapshot
    }

    /// Get the best bid price (highest buy price).
    pub fn best_bid(&self) -> Option<Price> {
        if self.bid_count > 0 {
            Some(self.bids[0].price)
        } else {
            None
        }
    }

    /// Get the best ask price (lowest sell price).
    pub fn best_ask(&self) -> Option<Price> {
        if self.ask_count > 0 {
            Some(self.asks[0].price)
        } else {
            None
        }
    }

    /// Get the mid price (average of best bid and best ask).
    pub fn mid_price(&self) -> Option<Price> {
        match (self.best_bid(), self.best_ask()) {
            (Some(bid), Some(ask)) => Some((bid + ask) / 2.0),
            _ => None,
        }
    }

    /// Get the spread (difference between best ask and best bid).
    pub fn spread(&self) -> Option<Price> {
        match (self.best_bid(), self.best_ask()) {
            (Some(bid), Some(ask)) => Some(ask - bid),
            _ => None,
        }
    }

    /// Calculate order book statistics.
    pub fn get_stats(&self) -> OrderBookStats {
        let total_bid_volume: Quantity = self.bids[..self.bid_count as usize]
            .iter()
            .map(|l| l.quantity)
            .sum();
        
        let total_ask_volume: Quantity = self.asks[..self.ask_count as usize]
            .iter()
            .map(|l| l.quantity)
            .sum();
        
        let spread = self.spread().unwrap_or(0.0);
        
        let spread_bps_value = match (self.best_bid(), self.best_ask()) {
            (Some(bid), Some(ask)) => spread_bps(bid, ask),
            _ => 0.0,
        };
        
        let bid_ask_ratio = if total_ask_volume > 0.0 {
            total_bid_volume / total_ask_volume
        } else {
            0.0
        };
        
        let total_bid_orders: i32 = self.bids[..self.bid_count as usize]
            .iter()
            .map(|l| l.order_count)
            .sum();
        
        let total_ask_orders: i32 = self.asks[..self.ask_count as usize]
            .iter()
            .map(|l| l.order_count)
            .sum();
        
        OrderBookStats {
            total_bid_volume,
            total_ask_volume,
            spread,
            spread_bps: spread_bps_value,
            bid_ask_ratio,
            total_bid_orders,
            total_ask_orders,
            bid_levels: self.bid_count,
            ask_levels: self.ask_count,
        }
    }

    /// Set a bid level at the given index.
    pub fn set_bid(&mut self, index: usize, level: OrderBookLevel) {
        if index < MAX_LEVELS {
            self.bids[index] = level;
            if index as i32 >= self.bid_count {
                self.bid_count = (index + 1) as i32;
            }
        }
    }

    /// Set an ask level at the given index.
    pub fn set_ask(&mut self, index: usize, level: OrderBookLevel) {
        if index < MAX_LEVELS {
            self.asks[index] = level;
            if index as i32 >= self.ask_count {
                self.ask_count = (index + 1) as i32;
            }
        }
    }
}

/// Order book statistics.
///
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct OrderBookStats {
    /// Total volume on bid side
    pub total_bid_volume: Quantity,
    /// Total volume on ask side
    pub total_ask_volume: Quantity,
    /// Spread (best ask - best bid)
    pub spread: Price,
    /// Spread in basis points
    pub spread_bps: f64,
    /// Bid/Ask volume ratio
    pub bid_ask_ratio: f64,
    /// Total number of bid orders
    pub total_bid_orders: i32,
    /// Total number of ask orders
    pub total_ask_orders: i32,
    /// Number of bid levels
    pub bid_levels: i32,
    /// Number of ask levels
    pub ask_levels: i32,
}

/// FFI function to get order book snapshot.
///
/// # Safety
/// - `snapshot` must be a valid pointer to an OrderBookSnapshot
/// - The caller must ensure the pointer remains valid during the call
#[no_mangle]
pub unsafe extern "C" fn get_orderbook(
    snapshot: *mut OrderBookSnapshot,
    engine_ptr: *const std::ffi::c_void,
) -> i32 {
    use crate::ffi::{ERR_NULL_POINTER, ERR_SUCCESS};
    
    if snapshot.is_null() || engine_ptr.is_null() {
        return ERR_NULL_POINTER;
    }
    
    // For now, return a default snapshot
    // In a real implementation, this would get the orderbook from the engine
    *snapshot = OrderBookSnapshot::default();
    
    ERR_SUCCESS
}

/// FFI function to get order book statistics.
///
/// # Safety
/// - `stats` must be a valid pointer to an OrderBookStats
/// - `snapshot` must be a valid pointer to an OrderBookSnapshot
#[no_mangle]
pub unsafe extern "C" fn get_orderbook_stats(
    snapshot: *const OrderBookSnapshot,
    stats: *mut OrderBookStats,
) -> i32 {
    use crate::ffi::{ERR_NULL_POINTER, ERR_SUCCESS};
    
    if snapshot.is_null() || stats.is_null() {
        return ERR_NULL_POINTER;
    }
    
    let snapshot_ref = &*snapshot;
    *stats = snapshot_ref.get_stats();
    
    ERR_SUCCESS
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_order_book_level_creation() {
        let level = OrderBookLevel::new(100.0, 50.0, 5);
        assert_eq!(level.price, 100.0);
        assert_eq!(level.quantity, 50.0);
        assert_eq!(level.order_count, 5);
    }

    #[test]
    fn test_order_book_level_is_empty() {
        let empty = OrderBookLevel::default();
        assert!(empty.is_empty());
        
        let non_empty = OrderBookLevel::new(100.0, 50.0, 1);
        assert!(!non_empty.is_empty());
    }

    #[test]
    fn test_order_book_snapshot_default() {
        let snapshot = OrderBookSnapshot::default();
        assert_eq!(snapshot.bid_count, 0);
        assert_eq!(snapshot.ask_count, 0);
        assert_eq!(snapshot.last_price, 0.0);
    }

    #[test]
    fn test_order_book_snapshot_with_levels() {
        let bids = vec![
            OrderBookLevel::new(99.0, 100.0, 10),
            OrderBookLevel::new(98.0, 200.0, 20),
        ];
        let asks = vec![
            OrderBookLevel::new(101.0, 150.0, 15),
            OrderBookLevel::new(102.0, 250.0, 25),
        ];
        
        let snapshot = OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 1234567890);
        
        assert_eq!(snapshot.bid_count, 2);
        assert_eq!(snapshot.ask_count, 2);
        assert_eq!(snapshot.last_price, 100.0);
        assert_eq!(snapshot.timestamp, 1234567890);
        
        assert_eq!(snapshot.bids[0].price, 99.0);
        assert_eq!(snapshot.asks[0].price, 101.0);
    }

    #[test]
    fn test_best_bid_ask() {
        let bids = vec![OrderBookLevel::new(99.0, 100.0, 10)];
        let asks = vec![OrderBookLevel::new(101.0, 150.0, 15)];
        
        let snapshot = OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 0);
        
        assert_eq!(snapshot.best_bid(), Some(99.0));
        assert_eq!(snapshot.best_ask(), Some(101.0));
    }

    #[test]
    fn test_mid_price() {
        let bids = vec![OrderBookLevel::new(99.0, 100.0, 10)];
        let asks = vec![OrderBookLevel::new(101.0, 150.0, 15)];
        
        let snapshot = OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 0);
        
        assert_eq!(snapshot.mid_price(), Some(100.0));
    }

    #[test]
    fn test_spread() {
        let bids = vec![OrderBookLevel::new(99.0, 100.0, 10)];
        let asks = vec![OrderBookLevel::new(101.0, 150.0, 15)];
        
        let snapshot = OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 0);
        
        assert_eq!(snapshot.spread(), Some(2.0));
    }

    #[test]
    fn test_order_book_stats() {
        let bids = vec![
            OrderBookLevel::new(99.0, 100.0, 10),
            OrderBookLevel::new(98.0, 200.0, 20),
        ];
        let asks = vec![
            OrderBookLevel::new(101.0, 150.0, 15),
            OrderBookLevel::new(102.0, 250.0, 25),
        ];
        
        let snapshot = OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 0);
        let stats = snapshot.get_stats();
        
        assert_eq!(stats.total_bid_volume, 300.0);
        assert_eq!(stats.total_ask_volume, 400.0);
        assert_eq!(stats.spread, 2.0);
        assert_eq!(stats.total_bid_orders, 30);
        assert_eq!(stats.total_ask_orders, 40);
        assert_eq!(stats.bid_levels, 2);
        assert_eq!(stats.ask_levels, 2);
        
        // bid_ask_ratio = 300 / 400 = 0.75
        assert!((stats.bid_ask_ratio - 0.75).abs() < 0.001);
    }

    #[test]
    fn test_empty_orderbook_stats() {
        let snapshot = OrderBookSnapshot::default();
        let stats = snapshot.get_stats();
        
        assert_eq!(stats.total_bid_volume, 0.0);
        assert_eq!(stats.total_ask_volume, 0.0);
        assert_eq!(stats.spread, 0.0);
        assert_eq!(stats.bid_ask_ratio, 0.0);
    }

    #[test]
    fn test_set_bid_ask() {
        let mut snapshot = OrderBookSnapshot::new();
        
        snapshot.set_bid(0, OrderBookLevel::new(99.0, 100.0, 10));
        snapshot.set_ask(0, OrderBookLevel::new(101.0, 150.0, 15));
        
        assert_eq!(snapshot.bid_count, 1);
        assert_eq!(snapshot.ask_count, 1);
        assert_eq!(snapshot.bids[0].price, 99.0);
        assert_eq!(snapshot.asks[0].price, 101.0);
    }
}
