//! Gateway abstraction layer for order execution.
//!
//! Provides a unified interface for both simulated (backtest) and live trading.
//! The Gateway trait abstracts order submission, cancellation, and account queries.

use std::collections::HashMap;
use thiserror::Error;

use crate::types::{AccountStatus, OrderRequest, Position, DIRECTION_BUY, DIRECTION_SELL};

/// Unique identifier for orders.
pub type OrderId = u64;

/// Order fill information.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Fill {
    /// Order ID that was filled
    pub order_id: OrderId,
    /// Symbol traded
    pub symbol: [u8; 16],
    /// Filled quantity
    pub quantity: f64,
    /// Fill price (including slippage)
    pub price: f64,
    /// Commission charged
    pub commission: f64,
    /// Direction: 1 = Buy, -1 = Sell
    pub direction: i32,
    /// Timestamp of fill
    pub timestamp: i64,
}

/// Gateway error types.
#[derive(Debug, Error, Clone, PartialEq)]
pub enum GatewayError {
    #[error("Order not found: {0}")]
    OrderNotFound(OrderId),

    #[error("Invalid order: {0}")]
    InvalidOrder(String),

    #[error("Insufficient funds for order")]
    InsufficientFunds,

    #[error("Gateway not connected")]
    NotConnected,

    #[error("Order already cancelled: {0}")]
    AlreadyCancelled(OrderId),

    #[error("Gateway error: {0}")]
    Other(String),
}

/// Gateway trait for order execution abstraction.
///
/// This trait provides a unified interface for both simulated and live trading.
/// Implementations handle the specifics of order routing and execution.
pub trait Gateway: Send + Sync {
    /// Submit an order for execution.
    ///
    /// # Arguments
    /// * `order` - The order request to submit
    /// * `current_price` - Current market price for execution
    ///
    /// # Returns
    /// * `Ok(OrderId)` - Unique identifier for the submitted order
    /// * `Err(GatewayError)` - If order submission fails
    fn submit_order(&mut self, order: &OrderRequest, current_price: f64) -> Result<OrderId, GatewayError>;

    /// Cancel an existing order.
    ///
    /// # Arguments
    /// * `order_id` - The ID of the order to cancel
    ///
    /// # Returns
    /// * `Ok(())` - If cancellation succeeds
    /// * `Err(GatewayError)` - If cancellation fails
    fn cancel_order(&mut self, order_id: OrderId) -> Result<(), GatewayError>;

    /// Query position for a specific symbol.
    ///
    /// # Arguments
    /// * `symbol` - The symbol to query
    ///
    /// # Returns
    /// * `Some(Position)` - If position exists
    /// * `None` - If no position for symbol
    fn query_position(&self, symbol: &str) -> Option<Position>;

    /// Query current account status.
    ///
    /// # Returns
    /// Current account status including balance, equity, and positions.
    fn query_account(&self) -> AccountStatus;

    /// Get all fills that occurred since last query.
    ///
    /// # Returns
    /// Vector of fills, cleared after retrieval.
    fn get_fills(&mut self) -> Vec<Fill>;

    /// Update current market price for a symbol.
    ///
    /// # Arguments
    /// * `symbol` - The symbol to update
    /// * `price` - New market price
    fn update_price(&mut self, symbol: &str, price: f64);
}

/// Simulated gateway for backtesting.
///
/// Executes orders immediately with configurable slippage and commission.
/// Maintains internal position and account state.
#[derive(Debug)]
pub struct SimulatedGateway {
    /// Slippage as a fraction (e.g., 0.001 = 0.1%)
    slippage: f64,
    /// Commission rate as a fraction (e.g., 0.0001 = 0.01%)
    commission_rate: f64,
    /// Current market prices by symbol
    current_prices: HashMap<String, f64>,
    /// Positions by symbol
    positions: HashMap<String, PositionInternal>,
    /// Account balance
    balance: f64,
    /// Initial balance for PnL calculation (kept for potential future reporting)
    #[allow(dead_code)]
    initial_balance: f64,
    /// Next order ID
    next_order_id: OrderId,
    /// Pending fills to be retrieved
    pending_fills: Vec<Fill>,
    /// Current timestamp for fills
    current_timestamp: i64,
}

/// Internal position representation with more detail.
#[derive(Debug, Clone)]
struct PositionInternal {
    symbol: String,
    quantity: f64,
    average_price: f64,
    realized_pnl: f64,
}

impl SimulatedGateway {
    /// Create a new simulated gateway.
    ///
    /// # Arguments
    /// * `initial_balance` - Starting account balance
    /// * `slippage` - Slippage fraction (e.g., 0.001 = 0.1%)
    /// * `commission_rate` - Commission rate fraction (e.g., 0.0001 = 0.01%)
    pub fn new(initial_balance: f64, slippage: f64, commission_rate: f64) -> Self {
        Self {
            slippage,
            commission_rate,
            current_prices: HashMap::new(),
            positions: HashMap::new(),
            balance: initial_balance,
            initial_balance,
            next_order_id: 1,
            pending_fills: Vec::new(),
            current_timestamp: 0,
        }
    }

    /// Set the current timestamp for fills.
    pub fn set_timestamp(&mut self, timestamp: i64) {
        self.current_timestamp = timestamp;
    }

    /// Calculate fill price with slippage.
    fn calculate_fill_price(&self, base_price: f64, direction: i32) -> f64 {
        let slippage_amount = base_price * self.slippage;
        if direction == DIRECTION_BUY {
            base_price + slippage_amount // Buy at higher price
        } else {
            base_price - slippage_amount // Sell at lower price
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

    /// Get total unrealized PnL across all positions.
    fn total_unrealized_pnl(&self) -> f64 {
        self.positions
            .values()
            .map(|p| self.calculate_unrealized_pnl(p))
            .sum()
    }

    /// Get total realized PnL across all positions.
    fn total_realized_pnl(&self) -> f64 {
        self.positions.values().map(|p| p.realized_pnl).sum()
    }

    /// Get slippage configuration.
    pub fn slippage(&self) -> f64 {
        self.slippage
    }

    /// Get commission rate configuration.
    pub fn commission_rate(&self) -> f64 {
        self.commission_rate
    }
}

impl Default for SimulatedGateway {
    fn default() -> Self {
        Self::new(100_000.0, 0.001, 0.0001)
    }
}

impl Gateway for SimulatedGateway {
    fn submit_order(&mut self, order: &OrderRequest, current_price: f64) -> Result<OrderId, GatewayError> {
        // Validate order
        if order.quantity <= 0.0 {
            return Err(GatewayError::InvalidOrder("Quantity must be positive".to_string()));
        }
        if order.direction != DIRECTION_BUY && order.direction != DIRECTION_SELL {
            return Err(GatewayError::InvalidOrder("Invalid direction".to_string()));
        }

        let symbol = order.symbol_str().to_string();
        
        // Calculate fill price with slippage
        let fill_price = self.calculate_fill_price(current_price, order.direction);
        let trade_value = order.quantity * fill_price;
        let commission = self.calculate_commission(trade_value);

        // Check if we have sufficient funds for buy orders (opening new long or covering short)
        if order.direction == DIRECTION_BUY {
            // Check if we're covering a short position
            let current_position = self.positions.get(&symbol).map(|p| p.quantity).unwrap_or(0.0);
            if current_position >= 0.0 {
                // Opening or adding to long position - need funds
                let total_cost = trade_value + commission;
                if total_cost > self.balance {
                    return Err(GatewayError::InsufficientFunds);
                }
            }
            // If covering short, we don't need additional funds (we're closing a position)
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
            // Buying: increase position
            let new_quantity = position.quantity + order.quantity;
            if position.quantity > 0.0 {
                // Average up existing long position
                position.average_price = (position.average_price * position.quantity + fill_price * order.quantity) / new_quantity;
            } else if position.quantity < 0.0 {
                // Covering short position
                let cover_quantity = order.quantity.min(-position.quantity);
                let pnl = (position.average_price - fill_price) * cover_quantity;
                position.realized_pnl += pnl;
                
                if order.quantity > -position.quantity {
                    // Flipping from short to long
                    position.average_price = fill_price;
                }
            } else {
                // New position
                position.average_price = fill_price;
            }
            position.quantity = new_quantity;
            self.balance -= trade_value + commission;
        } else {
            // Selling: decrease position
            let new_quantity = position.quantity - order.quantity;
            if position.quantity > 0.0 {
                // Closing long position
                let close_quantity = order.quantity.min(position.quantity);
                let pnl = (fill_price - position.average_price) * close_quantity;
                position.realized_pnl += pnl;
                
                if order.quantity > position.quantity {
                    // Flipping from long to short
                    position.average_price = fill_price;
                }
            } else if position.quantity < 0.0 {
                // Adding to short position
                position.average_price = (position.average_price * (-position.quantity) + fill_price * order.quantity) / (-new_quantity);
            } else {
                // New short position
                position.average_price = fill_price;
            }
            position.quantity = new_quantity;
            self.balance += trade_value - commission;
        }

        // Update current price
        self.current_prices.insert(symbol.clone(), current_price);

        // Record fill
        let fill = Fill {
            order_id,
            symbol: order.symbol,
            quantity: order.quantity,
            price: fill_price,
            commission,
            direction: order.direction,
            timestamp: self.current_timestamp,
        };
        self.pending_fills.push(fill);

        Ok(order_id)
    }

    fn cancel_order(&mut self, order_id: OrderId) -> Result<(), GatewayError> {
        // In simulated gateway, orders are filled immediately, so cancellation is not applicable
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
            available: self.balance, // Simplified: available = balance
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

/// Extension trait for live trading gateways (placeholder for future implementation).
pub trait LiveGateway: Gateway {
    /// Connect to the trading venue.
    fn connect(&mut self) -> Result<(), GatewayError>;

    /// Disconnect from the trading venue.
    fn disconnect(&mut self);

    /// Check if connected.
    fn is_connected(&self) -> bool;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_simulated_gateway_creation() {
        let gateway = SimulatedGateway::new(100_000.0, 0.001, 0.0001);
        assert_eq!(gateway.slippage(), 0.001);
        assert_eq!(gateway.commission_rate(), 0.0001);
    }

    #[test]
    fn test_buy_order_execution() {
        let mut gateway = SimulatedGateway::new(100_000.0, 0.001, 0.0001);
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 1.0;
        order.direction = DIRECTION_BUY;
        
        let result = gateway.submit_order(&order, 50_000.0);
        assert!(result.is_ok());
        
        let position = gateway.query_position("BTCUSDT");
        assert!(position.is_some());
        assert_eq!(position.unwrap().quantity, 1.0);
    }

    #[test]
    fn test_sell_order_execution() {
        let mut gateway = SimulatedGateway::new(200_000.0, 0.001, 0.0001); // Increased initial balance
        
        // First buy
        let mut buy_order = OrderRequest::with_symbol("BTCUSDT");
        buy_order.quantity = 2.0;
        buy_order.direction = DIRECTION_BUY;
        gateway.submit_order(&buy_order, 50_000.0).unwrap();
        
        // Then sell half
        let mut sell_order = OrderRequest::with_symbol("BTCUSDT");
        sell_order.quantity = 1.0;
        sell_order.direction = DIRECTION_SELL;
        gateway.submit_order(&sell_order, 51_000.0).unwrap();
        
        let position = gateway.query_position("BTCUSDT");
        assert!(position.is_some());
        assert!((position.unwrap().quantity - 1.0).abs() < 0.0001);
    }

    #[test]
    fn test_insufficient_funds() {
        let mut gateway = SimulatedGateway::new(1_000.0, 0.001, 0.0001);
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 1.0;
        order.direction = DIRECTION_BUY;
        
        let result = gateway.submit_order(&order, 50_000.0);
        assert!(matches!(result, Err(GatewayError::InsufficientFunds)));
    }

    #[test]
    fn test_slippage_applied() {
        let mut gateway = SimulatedGateway::new(100_000.0, 0.01, 0.0); // 1% slippage
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 1.0;
        order.direction = DIRECTION_BUY;
        
        gateway.submit_order(&order, 50_000.0).unwrap();
        
        let fills = gateway.get_fills();
        assert_eq!(fills.len(), 1);
        // Buy should have higher fill price due to slippage
        assert!((fills[0].price - 50_500.0).abs() < 0.01); // 50000 * 1.01 = 50500
    }

    #[test]
    fn test_commission_charged() {
        let mut gateway = SimulatedGateway::new(100_000.0, 0.0, 0.001); // 0.1% commission
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 1.0;
        order.direction = DIRECTION_BUY;
        
        gateway.submit_order(&order, 50_000.0).unwrap();
        
        let fills = gateway.get_fills();
        assert_eq!(fills.len(), 1);
        // Commission should be 0.1% of trade value
        assert!((fills[0].commission - 50.0).abs() < 0.01); // 50000 * 0.001 = 50
    }

    #[test]
    fn test_account_status() {
        let mut gateway = SimulatedGateway::new(100_000.0, 0.0, 0.0);
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 1.0;
        order.direction = DIRECTION_BUY;
        
        gateway.submit_order(&order, 50_000.0).unwrap();
        gateway.update_price("BTCUSDT", 51_000.0);
        
        let status = gateway.query_account();
        assert_eq!(status.balance, 50_000.0); // 100000 - 50000
        assert_eq!(status.position_count, 1);
        // Equity should include unrealized PnL
        assert!((status.equity - 51_000.0).abs() < 0.01); // 50000 + 1000 unrealized
    }

    #[test]
    fn test_invalid_order() {
        let mut gateway = SimulatedGateway::new(100_000.0, 0.0, 0.0);
        
        let mut order = OrderRequest::with_symbol("BTCUSDT");
        order.quantity = 0.0; // Invalid
        order.direction = DIRECTION_BUY;
        
        let result = gateway.submit_order(&order, 50_000.0);
        assert!(matches!(result, Err(GatewayError::InvalidOrder(_))));
    }
}
