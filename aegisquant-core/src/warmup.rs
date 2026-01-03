//! Warmup mechanism module for strategy indicator initialization.
//!
//! Provides a warmup period during which strategies update their indicators
//! but do not generate trading signals. This ensures indicators have
//! sufficient historical data before making trading decisions.
//!
//! # Requirements
//! - Requirement 7.1: Add warmup_bars field to StrategyParams
//! - Requirement 7.2: During warmup, only update indicators, no signals
//! - Requirement 7.3: BacktestEngine does not execute orders during warmup
//! - Requirement 7.4: BacktestResult reports actual trading start time
//! - Requirement 7.6: Support configuring warmup via init_engine_with_params

use crate::log_info;

/// Warmup manager for tracking and controlling the warmup period.
///
/// During the warmup period:
/// - Indicators are updated with incoming data
/// - No trading signals are generated
/// - No orders are executed
#[derive(Debug, Clone)]
pub struct WarmupManager {
    /// Number of bars required for warmup
    warmup_bars: usize,
    /// Current bar count
    current_bar: usize,
    /// Whether warmup is complete
    is_warmed_up: bool,
    /// Timestamp when warmup completed
    warmup_complete_timestamp: Option<i64>,
}

impl Default for WarmupManager {
    fn default() -> Self {
        Self::new(0)
    }
}

impl WarmupManager {
    /// Create a new warmup manager with the specified warmup period.
    ///
    /// # Arguments
    /// * `warmup_bars` - Number of bars to wait before allowing trading.
    ///   If 0 or negative, warmup is immediately complete.
    pub fn new(warmup_bars: i32) -> Self {
        let warmup_bars = warmup_bars.max(0) as usize;
        Self {
            warmup_bars,
            current_bar: 0,
            is_warmed_up: warmup_bars == 0,
            warmup_complete_timestamp: if warmup_bars == 0 { Some(0) } else { None },
        }
    }

    /// Process a new bar/tick and check if warmup is complete.
    ///
    /// # Arguments
    /// * `timestamp` - The timestamp of the current bar/tick
    ///
    /// # Returns
    /// `true` if warmup is complete and trading is allowed, `false` otherwise.
    pub fn tick(&mut self, timestamp: i64) -> bool {
        if self.is_warmed_up {
            return true;
        }

        self.current_bar += 1;

        if self.current_bar >= self.warmup_bars {
            self.is_warmed_up = true;
            self.warmup_complete_timestamp = Some(timestamp);
            log_info!(
                "Warmup complete at bar {} (timestamp: {})",
                self.current_bar,
                timestamp
            );
        }

        self.is_warmed_up
    }

    /// Check if warmup is complete.
    pub fn is_warmed_up(&self) -> bool {
        self.is_warmed_up
    }

    /// Get the current bar count.
    pub fn current_bar(&self) -> usize {
        self.current_bar
    }

    /// Get the warmup period in bars.
    pub fn warmup_bars(&self) -> usize {
        self.warmup_bars
    }

    /// Get the timestamp when warmup completed.
    pub fn warmup_complete_timestamp(&self) -> Option<i64> {
        self.warmup_complete_timestamp
    }

    /// Get the number of remaining warmup bars.
    pub fn remaining_bars(&self) -> usize {
        if self.is_warmed_up {
            0
        } else {
            self.warmup_bars.saturating_sub(self.current_bar)
        }
    }

    /// Reset the warmup manager to its initial state.
    pub fn reset(&mut self) {
        self.current_bar = 0;
        self.is_warmed_up = self.warmup_bars == 0;
        self.warmup_complete_timestamp = if self.warmup_bars == 0 { Some(0) } else { None };
    }

    /// Get the actual start bar (first bar after warmup).
    pub fn actual_start_bar(&self) -> usize {
        self.warmup_bars
    }
}

/// Trait for strategies that support warmup.
pub trait WarmupAware {
    /// Get the warmup manager.
    fn warmup_manager(&self) -> &WarmupManager;

    /// Get a mutable reference to the warmup manager.
    fn warmup_manager_mut(&mut self) -> &mut WarmupManager;

    /// Check if the strategy is warmed up.
    fn is_warmed_up(&self) -> bool {
        self.warmup_manager().is_warmed_up()
    }

    /// Process a tick during warmup (update indicators only).
    fn warmup_tick(&mut self, timestamp: i64) -> bool {
        self.warmup_manager_mut().tick(timestamp)
    }
}

/// FFI function to check if warmup is complete.
///
/// # Safety
/// `manager` must be a valid pointer to a WarmupManager.
#[no_mangle]
pub unsafe extern "C" fn is_warmup_complete(manager: *const WarmupManager) -> i32 {
    if manager.is_null() {
        return 0;
    }
    if (*manager).is_warmed_up() { 1 } else { 0 }
}

/// FFI function to get the current bar count.
///
/// # Safety
/// `manager` must be a valid pointer to a WarmupManager.
#[no_mangle]
pub unsafe extern "C" fn get_warmup_current_bar(manager: *const WarmupManager) -> i32 {
    if manager.is_null() {
        return 0;
    }
    (*manager).current_bar() as i32
}

/// FFI function to get remaining warmup bars.
///
/// # Safety
/// `manager` must be a valid pointer to a WarmupManager.
#[no_mangle]
pub unsafe extern "C" fn get_warmup_remaining_bars(manager: *const WarmupManager) -> i32 {
    if manager.is_null() {
        return 0;
    }
    (*manager).remaining_bars() as i32
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_warmup_manager_creation() {
        let manager = WarmupManager::new(10);
        assert_eq!(manager.warmup_bars(), 10);
        assert_eq!(manager.current_bar(), 0);
        assert!(!manager.is_warmed_up());
    }

    #[test]
    fn test_warmup_manager_zero_warmup() {
        let manager = WarmupManager::new(0);
        assert!(manager.is_warmed_up());
        assert_eq!(manager.remaining_bars(), 0);
    }

    #[test]
    fn test_warmup_manager_negative_warmup() {
        let manager = WarmupManager::new(-5);
        assert!(manager.is_warmed_up());
        assert_eq!(manager.warmup_bars(), 0);
    }

    #[test]
    fn test_warmup_tick_progression() {
        let mut manager = WarmupManager::new(5);

        // First 4 ticks should not complete warmup
        for i in 0..4 {
            let result = manager.tick(i as i64 * 1000);
            assert!(!result, "Tick {} should not complete warmup", i);
            assert!(!manager.is_warmed_up());
        }

        // 5th tick should complete warmup
        let result = manager.tick(4000);
        assert!(result, "5th tick should complete warmup");
        assert!(manager.is_warmed_up());
        assert_eq!(manager.warmup_complete_timestamp(), Some(4000));
    }

    #[test]
    fn test_warmup_remaining_bars() {
        let mut manager = WarmupManager::new(10);

        assert_eq!(manager.remaining_bars(), 10);

        manager.tick(0);
        assert_eq!(manager.remaining_bars(), 9);

        for _ in 0..9 {
            manager.tick(0);
        }
        assert_eq!(manager.remaining_bars(), 0);
    }

    #[test]
    fn test_warmup_reset() {
        let mut manager = WarmupManager::new(5);

        // Complete warmup
        for i in 0..5 {
            manager.tick(i as i64 * 1000);
        }
        assert!(manager.is_warmed_up());

        // Reset
        manager.reset();
        assert!(!manager.is_warmed_up());
        assert_eq!(manager.current_bar(), 0);
        assert_eq!(manager.remaining_bars(), 5);
    }

    #[test]
    fn test_warmup_actual_start_bar() {
        let manager = WarmupManager::new(20);
        assert_eq!(manager.actual_start_bar(), 20);

        let manager = WarmupManager::new(0);
        assert_eq!(manager.actual_start_bar(), 0);
    }

    #[test]
    fn test_warmup_idempotent_after_complete() {
        let mut manager = WarmupManager::new(3);

        // Complete warmup
        for _ in 0..3 {
            manager.tick(0);
        }
        assert!(manager.is_warmed_up());

        // Additional ticks should still return true
        for _ in 0..10 {
            assert!(manager.tick(0));
        }
    }
}
