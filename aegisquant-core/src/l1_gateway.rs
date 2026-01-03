//! L1 Simulated Gateway for realistic order execution.
//!
//! Provides order execution simulation based on L1 order book depth,
//! including partial fills and slippage modeling.

use std::collections::HashMap;
use std::sync::atomic::{AtomicI32, Ordering};

use crate::gateway::{Fill, Gateway, GatewayError, OrderId};
use crate::orderbook::{OrderBookLevel, OrderBookSnapshot};
use crate::precision::{Price, Quantity};
use crate::types::{AccountStatus, OrderRequest, Position, DIRECTION_BUY, DIRECTION_SELL};

/// Gateway mode for order execution.
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum GatewayMode {
    /// Simple mode: immediate fill at current price with fixed slippage
    #[default]
    Simple = 0,
    /// L1 mode: fill based on order book depth with realistic slippage
    L1 = 1,
}

impl From<i32> for GatewayMode {
    fn from(value: i32) -> Self {
        match value {
            1 => GatewayMode::L1,
            _ => GatewayMode::Simple,
        }
    }
}

/// Global gateway mode setting.
static GATEWAY_MODE: AtomicI32 = AtomicI32::new(0);

/// Set the global gateway mode.
pub fn set_gateway_mode_internal(mode: GatewayMode) {
    GATEWAY_MODE.store(mode as i32, Ordering::SeqCst);
}

/// Get the current gateway mode.
pub fn get_gateway_mode() -> GatewayMode {
    GatewayMode::from(GATEWAY_MODE.load(Ordering::SeqCst))
}

/// Slippage model for order execution.
#[derive(Debug, Clone, Copy)]
pub struct SlippageModel {
    /// Base slippage as a fraction (e.g., 0.001 = 0.1%)
    pub base_slippage: f64,
    /// Impact factor for large orders (slippage increases with order size)
    pub impact_factor: f64,
    /// Maximum slippage cap
    pub max_slippage: f64,
}

impl Default for SlippageModel {
    fn default() -> Self {
        Self {
            base_slippage: 0.0001,  // 1 bps
            impact_factor: 0.00001, // Additional slippage per unit
            max_slippage: 0.01,     // 1% max
        }
    }
}

impl SlippageModel {
    /// Create a new slippage model.
    pub fn new(base_slippage: f64, impact_factor: f64, max_slippage: f64) -> Self {
        Self {
            base_slippage,
            impact_factor,
            max_slippage,
        }
    }

    /// Calculate slippage for a given order quantity.
    pub fn calculate(&self, quantity: Quantity) -> f64 {
        let slippage = self.base_slippage + self.impact_factor * quantity;
        slippage.min(self.max_slippage)
    }
}

/// Fill result from L1 order execution.
#[derive(Debug, Clone)]
pub struct FillResult {
    /// Individual fills at each price level
    pub fills: Vec<LevelFill>,
    /// Unfilled quantity
    pub unfilled: Quantity,
    /// Volume-weighted average fill price
    pub average_price: Price,
    /// Total filled quantity
    pub filled_quantity: Quantity,
}

/// Fill at a single price level.
#[derive(Debug, Clone, Copy)]
pub struct LevelFill {
    /// Fill price (including slippage)
    pub price: Price,
    /// Fill quantity
    pub quantity: Quantity,
    /// Level index (0 = best price)
    pub level: usize,
}

/// L1 Simulated Gateway for realistic order execution.
///
/// Executes orders based on order book depth, supporting partial fills
/// and realistic slippage modeling.
#[derive(Debug)]
pub struct L1SimulatedGateway {
    /// Current order book snapshot
    orderbook: OrderBookSnapshot,
    /// Slippage model
    slippage_model: SlippageModel,
    /// Commission rate as a fraction
    commission_rate: f64,
    /// Maximum fill ratio (e.g., 0.5 = can only fill 50% of available liquidity)
    fill_ratio: f64,
    /// Current market prices by symbol
    current_prices: HashMap<String, Price>,
    /// Positions by symbol
    positions: HashMap<String, PositionInternal>,
    /// Account balance
    balance: f64,
    /// Initial balance (kept for potential future reporting)
    #[allow(dead_code)]
    initial_balance: f64,
    /// Next order ID
    next_order_id: OrderId,
    /// Pending fills
    pending_fills: Vec<Fill>,
    /// Current timestamp
    current_timestamp: i64,
}

/// Internal position representation.
#[derive(Debug, Clone)]
struct PositionInternal {
    symbol: String,
    quantity: f64,
    average_price: f64,
    realized_pnl: f64,
}

impl L1SimulatedGateway {
    /// Create a new L1 simulated gateway.
    pub fn new(initial_balance: f64, slippage_model: SlippageModel, commission_rate: f64) -> Self {
        Self {
            orderbook: OrderBookSnapshot::default(),
            slippage_model,
            commission_rate,
            fill_ratio: 0.5, // Default: can fill up to 50% of available liquidity
            current_prices: HashMap::new(),
            positions: HashMap::new(),
            balance: initial_balance,
            initial_balance,
            next_order_id: 1,
            pending_fills: Vec::new(),
            current_timestamp: 0,
        }
    }

    /// Set the fill ratio (maximum percentage of available liquidity that can be filled).
    pub fn set_fill_ratio(&mut self, ratio: f64) {
        self.fill_ratio = ratio.clamp(0.0, 1.0);
    }

    /// Get the current fill ratio.
    pub fn fill_ratio(&self) -> f64 {
        self.fill_ratio
    }

    /// Update the order book snapshot.
    pub fn update_orderbook(&mut self, orderbook: OrderBookSnapshot) {
        self.orderbook = orderbook;
    }

    /// Get the current order book snapshot.
    pub fn orderbook(&self) -> &OrderBookSnapshot {
        &self.orderbook
    }

    /// Set the current timestamp.
    pub fn set_timestamp(&mut self, timestamp: i64) {
        self.current_timestamp = timestamp;
    }

    /// Execute an order against the order book.
    ///
    /// Returns a FillResult containing individual fills at each price level,
    /// the unfilled quantity, and the volume-weighted average price.
    pub fn execute_order(&self, order: &OrderRequest) -> FillResult {
        let mut remaining = order.quantity;
        let mut total_cost = 0.0;
        let mut fills = Vec::new();
        
        // Select the appropriate side of the order book
        let levels: &[OrderBookLevel] = if order.direction == DIRECTION_BUY {
            // Buy orders consume ask (sell) side
            &self.orderbook.asks[..self.orderbook.ask_count as usize]
        } else {
            // Sell orders consume bid (buy) side
            &self.orderbook.bids[..self.orderbook.bid_count as usize]
        };
        
        for (level_idx, level) in levels.iter().enumerate() {
            if remaining <= 0.0 || level.is_empty() {
                break;
            }
            
            // Calculate available quantity at this level (limited by fill_ratio)
            let available = level.quantity * self.fill_ratio;
            let fill_qty = remaining.min(available);
            
            // Calculate fill price with slippage
            let slippage = self.slippage_model.calculate(fill_qty);
            let fill_price = if order.direction == DIRECTION_BUY {
                level.price * (1.0 + slippage) // Buy at higher price
            } else {
                level.price * (1.0 - slippage) // Sell at lower price
            };
            
            fills.push(LevelFill {
                price: fill_price,
                quantity: fill_qty,
                level: level_idx,
            });
            
            total_cost += fill_price * fill_qty;
            remaining -= fill_qty;
        }
        
        let filled_quantity = order.quantity - remaining;
        let average_price = if filled_quantity > 0.0 {
            total_cost / filled_quantity
        } else {
            0.0
        };
        
        FillResult {
            fills,
            unfilled: remaining,
            average_price,
            filled_quantity,
        }
    }

    /// Calculate commission for a trade.
    fn calculate_commission(&self, trade_value: f64) -> f64 {
        trade_value * self.commission_rate
    }

    /// Calculate unrealized PnL for a position.
    fn calculate_unrealized_pnl(&self, position: &PositionInternal) -> f64 {
        if let Some(&current_price) = self.current_prices.get(&position.symbol) {
            (current_price - position.average_price) * position.quantity
        } else {
            0.0
        }
    }

    /// Get total unrealized PnL.
    fn total_unrealized_pnl(&self) -> f64 {
        self.positions
            .values()
            .map(|p| self.calculate_unrealized_pnl(p))
            .sum()
    }

    /// Get total realized PnL.
    fn total_realized_pnl(&self) -> f64 {
        self.positions.values().map(|p| p.realized_pnl).sum()
    }
}

impl Default for L1SimulatedGateway {
    fn default() -> Self {
        Self::new(100_000.0, SlippageModel::default(), 0.0001)
    }
}

impl Gateway for L1SimulatedGateway {
    fn submit_order(&mut self, order: &OrderRequest, current_price: f64) -> Result<OrderId, GatewayError> {
        // Validate order
        if order.quantity <= 0.0 {
            return Err(GatewayError::InvalidOrder("Quantity must be positive".to_string()));
        }
        if order.direction != DIRECTION_BUY && order.direction != DIRECTION_SELL {
            return Err(GatewayError::InvalidOrder("Invalid direction".to_string()));
        }

        let symbol = order.symbol_str().to_string();
        
        // Execute order against order book
        let fill_result = self.execute_order(order);
        
        // If no fills, check if we can do a simple fill at current price
        let (fill_price, fill_quantity) = if fill_result.filled_quantity > 0.0 {
            (fill_result.average_price, fill_result.filled_quantity)
        } else {
            // Fallback to simple execution at current price with slippage
            let slippage = self.slippage_model.calculate(order.quantity);
            let price = if order.direction == DIRECTION_BUY {
                current_price * (1.0 + slippage)
            } else {
                current_price * (1.0 - slippage)
            };
            (price, order.quantity)
        };
        
        let trade_value = fill_quantity * fill_price;
        let commission = self.calculate_commission(trade_value);

        // Check funds for buy orders
        if order.direction == DIRECTION_BUY {
            let current_position = self.positions.get(&symbol).map(|p| p.quantity).unwrap_or(0.0);
            if current_position >= 0.0 {
                let total_cost = trade_value + commission;
                if total_cost > self.balance {
                    return Err(GatewayError::InsufficientFunds);
                }
            }
        }

        // Generate order ID
        let order_id = self.next_order_id;
        self.next_order_id += 1;

        // Update position
        let position = self.positions.entry(symbol.clone()).or_insert(PositionInternal {
            symbol: symbol.clone(),
            quantity: 0.0,
            average_price: 0.0,
            realized_pnl: 0.0,
        });

        if order.direction == DIRECTION_BUY {
            let new_quantity = position.quantity + fill_quantity;
            if position.quantity > 0.0 {
                position.average_price = (position.average_price * position.quantity + fill_price * fill_quantity) / new_quantity;
            } else if position.quantity < 0.0 {
                let cover_quantity = fill_quantity.min(-position.quantity);
                let pnl = (position.average_price - fill_price) * cover_quantity;
                position.realized_pnl += pnl;
                if fill_quantity > -position.quantity {
                    position.average_price = fill_price;
                }
            } else {
                position.average_price = fill_price;
            }
            position.quantity = new_quantity;
            self.balance -= trade_value + commission;
        } else {
            let new_quantity = position.quantity - fill_quantity;
            if position.quantity > 0.0 {
                let close_quantity = fill_quantity.min(position.quantity);
                let pnl = (fill_price - position.average_price) * close_quantity;
                position.realized_pnl += pnl;
                if fill_quantity > position.quantity {
                    position.average_price = fill_price;
                }
            } else if position.quantity < 0.0 {
                position.average_price = (position.average_price * (-position.quantity) + fill_price * fill_quantity) / (-new_quantity);
            } else {
                position.average_price = fill_price;
            }
            position.quantity = new_quantity;
            self.balance += trade_value - commission;
        }

        // Update current price
        self.current_prices.insert(symbol, current_price);

        // Record fill
        let fill = Fill {
            order_id,
            symbol: order.symbol,
            quantity: fill_quantity,
            price: fill_price,
            commission,
            direction: order.direction,
            timestamp: self.current_timestamp,
        };
        self.pending_fills.push(fill);

        Ok(order_id)
    }

    fn cancel_order(&mut self, order_id: OrderId) -> Result<(), GatewayError> {
        Err(GatewayError::OrderNotFound(order_id))
    }

    fn query_position(&self, symbol: &str) -> Option<Position> {
        self.positions.get(symbol).map(|p| {
            let mut pos = Position::with_symbol(symbol);
            pos.quantity = p.quantity;
            pos.average_price = p.average_price;
            pos.unrealized_pnl = self.calculate_unrealized_pnl(p);
            pos.realized_pnl = p.realized_pnl;
            pos
        })
    }

    fn query_account(&self) -> AccountStatus {
        let unrealized_pnl = self.total_unrealized_pnl();
        let realized_pnl = self.total_realized_pnl();
        let equity = self.balance + unrealized_pnl;
        
        AccountStatus {
            balance: self.balance,
            equity,
            available: self.balance,
            position_count: self.positions.values().filter(|p| p.quantity.abs() > 0.0001).count() as i32,
            total_pnl: realized_pnl + unrealized_pnl,
        }
    }

    fn get_fills(&mut self) -> Vec<Fill> {
        std::mem::take(&mut self.pending_fills)
    }

    fn update_price(&mut self, symbol: &str, price: f64) {
        self.current_prices.insert(symbol.to_string(), price);
    }
}

/// FFI function to set gateway mode.
///
/// # Safety
/// This function is safe to call from any thread.
#[no_mangle]
pub extern "C" fn set_gateway_mode(mode: i32) -> i32 {
    use crate::ffi::ERR_SUCCESS;
    set_gateway_mode_internal(GatewayMode::from(mode));
    ERR_SUCCESS
}

/// FFI function to get current gateway mode.
#[no_mangle]
pub extern "C" fn get_gateway_mode_ffi() -> i32 {
    get_gateway_mode() as i32
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::orderbook::OrderBookLevel;

    fn create_test_orderbook() -> OrderBookSnapshot {
        let bids = vec![
            OrderBookLevel::new(99.0, 100.0, 10),
            OrderBookLevel::new(98.0, 200.0, 20),
            OrderBookLevel::new(97.0, 300.0, 30),
        ];
        let asks = vec![
            OrderBookLevel::new(101.0, 100.0, 10),
            OrderBookLevel::new(102.0, 200.0, 20),
            OrderBookLevel::new(103.0, 300.0, 30),
        ];
        OrderBookSnapshot::with_levels(&bids, &asks, 100.0, 0)
    }

    #[test]
    fn test_slippage_model() {
        let model = SlippageModel::new(0.001, 0.0001, 0.05);
        
        // Small order
        let slippage = model.calculate(10.0);
        assert!((slippage - 0.002).abs() < 0.0001); // 0.001 + 0.0001 * 10
        
        // Large order (capped)
        let slippage = model.calculate(1000.0);
        assert_eq!(slippage, 0.05); // Capped at max
    }

    #[test]
    fn test_gateway_mode() {
        set_gateway_mode_internal(GatewayMode::L1);
        assert_eq!(get_gateway_mode(), GatewayMode::L1);
        
        set_gateway_mode_internal(GatewayMode::Simple);
        assert_eq!(get_gateway_mode(), GatewayMode::Simple);
    }

    #[test]
    fn test_l1_gateway_creation() {
        let gateway = L1SimulatedGateway::default();
        assert_eq!(gateway.fill_ratio(), 0.5);
    }

    #[test]
    fn test_execute_buy_order() {
        let mut gateway = L1SimulatedGateway::default();
        gateway.update_orderbook(create_test_orderbook());
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 50.0;
        order.direction = DIRECTION_BUY;
        
        let result = gateway.execute_order(&order);
        
        // Should fill from ask side
        assert!(result.filled_quantity > 0.0);
        assert!(result.unfilled < order.quantity || result.unfilled == 0.0);
        assert!(result.average_price >= 101.0); // At least best ask price
    }

    #[test]
    fn test_execute_sell_order() {
        let mut gateway = L1SimulatedGateway::default();
        gateway.update_orderbook(create_test_orderbook());
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 50.0;
        order.direction = DIRECTION_SELL;
        
        let result = gateway.execute_order(&order);
        
        // Should fill from bid side
        assert!(result.filled_quantity > 0.0);
        assert!(result.average_price <= 99.0); // At most best bid price
    }

    #[test]
    fn test_partial_fill() {
        let mut gateway = L1SimulatedGateway::new(100_000.0, SlippageModel::default(), 0.0);
        gateway.set_fill_ratio(0.5);
        gateway.update_orderbook(create_test_orderbook());
        
        // Order larger than available at best price
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 200.0; // More than 100 * 0.5 = 50 at best ask
        order.direction = DIRECTION_BUY;
        
        let result = gateway.execute_order(&order);
        
        // Should have multiple fills across levels
        assert!(result.fills.len() > 1);
        assert!(result.filled_quantity > 0.0);
    }

    #[test]
    fn test_submit_order_updates_position() {
        let mut gateway = L1SimulatedGateway::new(100_000.0, SlippageModel::default(), 0.0);
        gateway.update_orderbook(create_test_orderbook());
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 10.0;
        order.direction = DIRECTION_BUY;
        
        let result = gateway.submit_order(&order, 100.0);
        assert!(result.is_ok());
        
        let position = gateway.query_position("BTCUSDT");
        assert!(position.is_some());
        assert!(position.unwrap().quantity > 0.0);
    }

    #[test]
    fn test_insufficient_funds() {
        let mut gateway = L1SimulatedGateway::new(100.0, SlippageModel::default(), 0.0);
        gateway.update_orderbook(create_test_orderbook());
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 10.0; // Would cost ~1010
        order.direction = DIRECTION_BUY;
        
        let result = gateway.submit_order(&order, 100.0);
        assert!(matches!(result, Err(GatewayError::InsufficientFunds)));
    }

    #[test]
    fn test_fill_ratio() {
        let mut gateway = L1SimulatedGateway::default();
        
        gateway.set_fill_ratio(0.3);
        assert_eq!(gateway.fill_ratio(), 0.3);
        
        gateway.set_fill_ratio(1.5); // Should be clamped
        assert_eq!(gateway.fill_ratio(), 1.0);
        
        gateway.set_fill_ratio(-0.5); // Should be clamped
        assert_eq!(gateway.fill_ratio(), 0.0);
    }

    #[test]
    fn test_account_status() {
        let mut gateway = L1SimulatedGateway::new(100_000.0, SlippageModel::default(), 0.0);
        gateway.update_orderbook(create_test_orderbook());
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 10.0;
        order.direction = DIRECTION_BUY;
        
        gateway.submit_order(&order, 100.0).unwrap();
        gateway.update_price("BTCUSDT", 105.0);
        
        let status = gateway.query_account();
        assert!(status.balance < 100_000.0); // Spent money
        assert_eq!(status.position_count, 1);
    }
}
