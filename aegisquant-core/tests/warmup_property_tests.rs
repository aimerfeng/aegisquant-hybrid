//! Property-based tests for Warmup mechanism correctness.
//!
//! Feature: aegisquant-optimizations, Property 7: Warmup Mechanism Correctness
//! Validates: Requirements 7.2, 7.3, 7.4
//!
//! Tests that:
//! - During warmup period, no trading signals are generated
//! - During warmup period, no trades are executed
//! - After warmup, trading can begin normally
//! - actual_start_bar equals warmup_bars

use proptest::prelude::*;
use aegisquant_core::warmup::WarmupManager;

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]
    
    /// Property 7: No signals during warmup period
    #[test]
    fn prop_no_warmup_complete_during_warmup(
        warmup_bars in 1i32..100,
    ) {
        let mut manager = WarmupManager::new(warmup_bars);
        
        for i in 0..(warmup_bars - 1) {
            let result = manager.tick(i as i64 * 1000);
            prop_assert!(
                !result,
                "Warmup should not be complete at bar {} (warmup_bars={})",
                i, warmup_bars
            );
            prop_assert!(
                !manager.is_warmed_up(),
                "is_warmed_up should be false at bar {} (warmup_bars={})",
                i, warmup_bars
            );
        }
    }
    
    /// Property 7: Warmup completes at exactly warmup_bars
    #[test]
    fn prop_warmup_completes_at_exact_bar(
        warmup_bars in 1i32..100,
    ) {
        let mut manager = WarmupManager::new(warmup_bars);
        
        for i in 0..(warmup_bars - 1) {
            manager.tick(i as i64 * 1000);
        }
        prop_assert!(!manager.is_warmed_up());
        
        let result = manager.tick((warmup_bars - 1) as i64 * 1000);
        prop_assert!(
            result,
            "Warmup should complete at bar {} (warmup_bars={})",
            warmup_bars - 1, warmup_bars
        );
        prop_assert!(manager.is_warmed_up());
    }
    
    /// Property 7: actual_start_bar equals warmup_bars
    #[test]
    fn prop_actual_start_bar_equals_warmup_bars(
        warmup_bars in 0i32..100,
    ) {
        let manager = WarmupManager::new(warmup_bars);
        
        prop_assert_eq!(
            manager.actual_start_bar() as i32,
            warmup_bars.max(0),
            "actual_start_bar should equal warmup_bars"
        );
    }
    
    /// Property 7: Zero warmup means immediate trading
    #[test]
    fn prop_zero_warmup_immediate_trading(
        _num_ticks in 1usize..100,
    ) {
        let manager = WarmupManager::new(0);
        
        prop_assert!(manager.is_warmed_up());
        prop_assert_eq!(manager.remaining_bars(), 0);
        prop_assert_eq!(manager.actual_start_bar(), 0);
    }
    
    /// Property 7: Remaining bars decreases correctly
    #[test]
    fn prop_remaining_bars_decreases(
        warmup_bars in 1i32..50,
    ) {
        let mut manager = WarmupManager::new(warmup_bars);
        
        for i in 0..warmup_bars {
            let remaining = manager.remaining_bars();
            prop_assert_eq!(
                remaining as i32,
                warmup_bars - i,
                "Remaining bars should be {} at tick {}, got {}",
                warmup_bars - i, i, remaining
            );
            manager.tick(i as i64 * 1000);
        }
        
        prop_assert_eq!(manager.remaining_bars(), 0);
    }
    
    /// Property 7: Warmup timestamp is recorded correctly
    #[test]
    fn prop_warmup_timestamp_recorded(
        warmup_bars in 1i32..50,
        timestamp_base in 0i64..1_000_000,
    ) {
        let mut manager = WarmupManager::new(warmup_bars);
        
        for i in 0..warmup_bars {
            let timestamp = timestamp_base + i as i64 * 1000;
            manager.tick(timestamp);
        }
        
        let expected_timestamp = timestamp_base + (warmup_bars - 1) as i64 * 1000;
        prop_assert_eq!(
            manager.warmup_complete_timestamp(),
            Some(expected_timestamp),
            "Warmup complete timestamp should be {}",
            expected_timestamp
        );
    }
    
    /// Property 7: Reset restores initial state
    #[test]
    fn prop_reset_restores_state(
        warmup_bars in 1i32..50,
        ticks_before_reset in 1usize..100,
    ) {
        let mut manager = WarmupManager::new(warmup_bars);
        
        for i in 0..ticks_before_reset {
            manager.tick(i as i64 * 1000);
        }
        
        manager.reset();
        
        prop_assert_eq!(manager.current_bar(), 0);
        prop_assert_eq!(manager.remaining_bars(), warmup_bars as usize);
        
        if warmup_bars > 0 {
            prop_assert!(!manager.is_warmed_up());
        }
    }
    
    /// Property 7: After warmup, all subsequent ticks return true
    #[test]
    fn prop_after_warmup_always_true(
        warmup_bars in 1i32..20,
        extra_ticks in 1usize..50,
    ) {
        let mut manager = WarmupManager::new(warmup_bars);
        
        for i in 0..warmup_bars {
            manager.tick(i as i64 * 1000);
        }
        prop_assert!(manager.is_warmed_up());
        
        for i in 0..extra_ticks {
            let result = manager.tick((warmup_bars as i64 + i as i64) * 1000);
            prop_assert!(
                result,
                "After warmup, tick should return true (tick {})",
                i
            );
        }
    }
    
    /// Property 7: Negative warmup_bars treated as zero
    #[test]
    fn prop_negative_warmup_treated_as_zero(
        negative_warmup in -100i32..-1,
    ) {
        let manager = WarmupManager::new(negative_warmup);
        
        prop_assert!(manager.is_warmed_up());
        prop_assert_eq!(manager.warmup_bars(), 0);
        prop_assert_eq!(manager.remaining_bars(), 0);
    }
}
