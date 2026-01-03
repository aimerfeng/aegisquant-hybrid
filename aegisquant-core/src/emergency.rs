//! Emergency Stop Module
//!
//! Provides global emergency halt functionality for the trading engine.
//! When activated, all trading operations are immediately stopped.
//!
//! Requirements: 16.1, 16.2, 16.6, 16.7

use std::sync::atomic::{AtomicBool, Ordering};

use crate::error::{EngineError, EngineResult};
use crate::ffi::{ERR_NULL_POINTER, ERR_SUCCESS};
use crate::logger::{log, LogLevel};
use crate::types::{OrderRequest, Position};

/// Global emergency halt flag.
///
/// When set to true, all trading operations should be blocked.
static EMERGENCY_HALT: AtomicBool = AtomicBool::new(false);

/// Check if the system is in emergency halt state.
#[inline]
pub fn is_halted() -> bool {
    EMERGENCY_HALT.load(Ordering::SeqCst)
}

/// Activate emergency stop.
///
/// This immediately sets the global halt flag, preventing any new
/// trading operations from being executed.
pub fn activate_emergency_stop() {
    EMERGENCY_HALT.store(true, Ordering::SeqCst);
    log(LogLevel::Error, "EMERGENCY STOP ACTIVATED - All trading halted");
}

/// Reset emergency stop.
///
/// This clears the global halt flag, allowing trading operations to resume.
/// Should only be called after the emergency situation has been resolved.
pub fn reset_emergency_stop() {
    EMERGENCY_HALT.store(false, Ordering::SeqCst);
    log(LogLevel::Info, "Emergency stop reset - Trading can resume");
}

/// Generate close orders for all positions.
///
/// Creates market orders to close all open positions.
/// Returns a vector of OrderRequest that should be submitted to close positions.
pub fn generate_close_all_orders(positions: &[Position]) -> Vec<OrderRequest> {
    let mut orders = Vec::new();

    for position in positions {
        if position.quantity.abs() > 0.0 {
            // Determine direction: if long (qty > 0), sell; if short (qty < 0), buy
            let direction = if position.quantity > 0.0 { -1 } else { 1 };

            let mut order = OrderRequest::with_symbol(position.symbol_str());
            order.quantity = position.quantity.abs();
            order.direction = direction;
            order.order_type = 0; // Market order
            order.limit_price = 0.0;

            orders.push(order);
        }
    }

    if !orders.is_empty() {
        log(
            LogLevel::Warn,
            &format!("Close all positions: {} orders generated", orders.len()),
        );
    }

    orders
}

/// Check if an operation should be blocked due to emergency halt.
///
/// Returns an error if the system is halted, otherwise Ok(()).
pub fn check_halt() -> EngineResult<()> {
    if is_halted() {
        Err(EngineError::risk_rejected("Emergency halt is active"))
    } else {
        Ok(())
    }
}

// ============================================================================
// FFI Functions
// ============================================================================

/// Activate emergency stop via FFI.
///
/// # Returns
/// - ERR_SUCCESS on success
#[no_mangle]
pub extern "C" fn emergency_stop() -> i32 {
    activate_emergency_stop();
    ERR_SUCCESS
}

/// Reset emergency stop via FFI.
///
/// # Returns
/// - ERR_SUCCESS on success
#[no_mangle]
pub extern "C" fn reset_emergency_stop_ffi() -> i32 {
    reset_emergency_stop();
    ERR_SUCCESS
}

/// Check if emergency halt is active via FFI.
///
/// # Returns
/// - 1 if halted, 0 if not halted
#[no_mangle]
pub extern "C" fn is_emergency_halted() -> i32 {
    if is_halted() { 1 } else { 0 }
}

/// Generate close orders for all positions via FFI.
///
/// # Safety
/// - `positions` must be a valid pointer to an array of Position
/// - `position_count` must be the number of positions in the array
/// - `orders` must be a valid pointer to an array of OrderRequest with at least `max_orders` capacity
/// - `order_count` must be a valid pointer to write the number of orders generated
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if any pointer is null
#[no_mangle]
pub unsafe extern "C" fn close_all_positions(
    positions: *const Position,
    position_count: i32,
    orders: *mut OrderRequest,
    max_orders: i32,
    order_count: *mut i32,
) -> i32 {
    if positions.is_null() || orders.is_null() || order_count.is_null() {
        return ERR_NULL_POINTER;
    }

    if position_count <= 0 || max_orders <= 0 {
        *order_count = 0;
        return ERR_SUCCESS;
    }

    // SAFETY: Pointers validated above
    let positions_slice = std::slice::from_raw_parts(positions, position_count as usize);
    let close_orders = generate_close_all_orders(positions_slice);

    let count = close_orders.len().min(max_orders as usize);
    for (i, order) in close_orders.iter().take(count).enumerate() {
        *orders.add(i) = *order;
    }
    *order_count = count as i32;

    ERR_SUCCESS
}

#[cfg(test)]
mod tests {
    use super::*;

    fn reset_state() {
        EMERGENCY_HALT.store(false, Ordering::SeqCst);
    }

    #[test]
    fn test_emergency_halt_default() {
        reset_state();
        assert!(!is_halted());
    }

    #[test]
    fn test_activate_emergency_stop() {
        reset_state();
        activate_emergency_stop();
        assert!(is_halted());
        reset_state();
    }

    #[test]
    fn test_reset_emergency_stop() {
        reset_state();
        activate_emergency_stop();
        assert!(is_halted());
        reset_emergency_stop();
        assert!(!is_halted());
    }

    #[test]
    fn test_check_halt_when_not_halted() {
        reset_state();
        assert!(check_halt().is_ok());
    }

    #[test]
    fn test_check_halt_when_halted() {
        reset_state();
        activate_emergency_stop();
        assert!(check_halt().is_err());
        reset_state();
    }

    #[test]
    fn test_generate_close_orders_long_position() {
        let mut position = Position::with_symbol("BTCUSDT");
        position.quantity = 10.0;
        position.average_price = 40000.0;

        let orders = generate_close_all_orders(&[position]);

        assert_eq!(orders.len(), 1);
        assert_eq!(orders[0].symbol_str(), "BTCUSDT");
        assert_eq!(orders[0].quantity, 10.0);
        assert_eq!(orders[0].direction, -1); // Sell to close long
        assert_eq!(orders[0].order_type, 0); // Market order
    }

    #[test]
    fn test_generate_close_orders_short_position() {
        let mut position = Position::with_symbol("ETHUSDT");
        position.quantity = -5.0;
        position.average_price = 2500.0;

        let orders = generate_close_all_orders(&[position]);

        assert_eq!(orders.len(), 1);
        assert_eq!(orders[0].symbol_str(), "ETHUSDT");
        assert_eq!(orders[0].quantity, 5.0);
        assert_eq!(orders[0].direction, 1); // Buy to close short
    }

    #[test]
    fn test_generate_close_orders_zero_position() {
        let position = Position::with_symbol("BTCUSDT");
        // quantity is 0 by default

        let orders = generate_close_all_orders(&[position]);

        assert!(orders.is_empty());
    }

    #[test]
    fn test_generate_close_orders_multiple_positions() {
        let mut pos1 = Position::with_symbol("BTCUSDT");
        pos1.quantity = 10.0;

        let mut pos2 = Position::with_symbol("ETHUSDT");
        pos2.quantity = -5.0;

        let pos3 = Position::with_symbol("XRPUSDT");
        // pos3 has zero quantity

        let orders = generate_close_all_orders(&[pos1, pos2, pos3]);

        assert_eq!(orders.len(), 2);
    }

    #[test]
    fn test_ffi_emergency_stop() {
        reset_state();
        let result = emergency_stop();
        assert_eq!(result, ERR_SUCCESS);
        assert!(is_halted());
        reset_state();
    }

    #[test]
    fn test_ffi_reset_emergency_stop() {
        reset_state();
        emergency_stop();
        let result = reset_emergency_stop_ffi();
        assert_eq!(result, ERR_SUCCESS);
        assert!(!is_halted());
    }

    #[test]
    fn test_ffi_is_emergency_halted() {
        reset_state();
        assert_eq!(is_emergency_halted(), 0);
        emergency_stop();
        assert_eq!(is_emergency_halted(), 1);
        reset_state();
    }

    #[test]
    fn test_ffi_close_all_positions() {
        let mut pos = Position::with_symbol("BTCUSDT");
        pos.quantity = 10.0;

        let positions = [pos];
        let mut orders = [OrderRequest::default(); 10];
        let mut order_count: i32 = 0;

        unsafe {
            let result = close_all_positions(
                positions.as_ptr(),
                1,
                orders.as_mut_ptr(),
                10,
                &mut order_count,
            );

            assert_eq!(result, ERR_SUCCESS);
            assert_eq!(order_count, 1);
            assert_eq!(orders[0].quantity, 10.0);
            assert_eq!(orders[0].direction, -1);
        }
    }

    #[test]
    fn test_ffi_close_all_positions_null_pointers() {
        let mut order_count: i32 = 0;

        unsafe {
            let result = close_all_positions(
                std::ptr::null(),
                0,
                std::ptr::null_mut(),
                0,
                &mut order_count,
            );
            assert_eq!(result, ERR_NULL_POINTER);
        }
    }
}
