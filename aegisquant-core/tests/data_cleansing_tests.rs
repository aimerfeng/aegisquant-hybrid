//! Property-based tests for Data Cleansing.
//!
//! Feature: aegisquant-hybrid, Property 5: Data Cleansing Filters Invalid Ticks
//! Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5

use proptest::prelude::*;
use aegisquant_core::data_loader::DataLoader;

proptest! {
    #![proptest_config(ProptestConfig::with_cases(100))]

    /// Property 5: Invalid prices (price <= 0) are filtered out
    /// For any tick with price <= 0, it should be marked as invalid.
    #[test]
    fn invalid_prices_are_filtered(
        valid_count in 1usize..10,
        invalid_price in prop_oneof![
            Just(0.0f64),
            Just(-1.0f64),
            -1000.0f64..0.0,
        ],
        invalid_index in 0usize..10,
    ) {
        let actual_invalid_index = invalid_index % (valid_count + 1);
        
        let mut timestamps: Vec<i64> = (0..valid_count as i64 + 1).collect();
        let mut prices: Vec<f64> = (0..valid_count + 1).map(|i| 100.0 + i as f64).collect();
        let volumes: Vec<f64> = vec![1000.0; valid_count + 1];
        
        // Insert invalid price
        prices[actual_invalid_index] = invalid_price;
        
        // Adjust timestamps to be strictly increasing
        for i in 1..timestamps.len() {
            timestamps[i] = timestamps[i - 1] + 1;
        }
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes).unwrap();
        
        prop_assert!(
            result.report.invalid_ticks >= 1,
            "Should have at least 1 invalid tick for price {}",
            invalid_price
        );
    }

    /// Property 5: Invalid volumes (volume < 0) are filtered out
    /// For any tick with volume < 0, it should be marked as invalid.
    #[test]
    fn invalid_volumes_are_filtered(
        valid_count in 1usize..10,
        invalid_volume in -1000.0f64..-0.001,
        invalid_index in 0usize..10,
    ) {
        let actual_invalid_index = invalid_index % (valid_count + 1);
        
        let timestamps: Vec<i64> = (0..valid_count as i64 + 1).collect();
        let prices: Vec<f64> = (0..valid_count + 1).map(|i| 100.0 + i as f64).collect();
        let mut volumes: Vec<f64> = vec![1000.0; valid_count + 1];
        
        // Insert invalid volume
        volumes[actual_invalid_index] = invalid_volume;
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes).unwrap();
        
        prop_assert!(
            result.report.invalid_ticks >= 1,
            "Should have at least 1 invalid tick for volume {}",
            invalid_volume
        );
    }

    /// Property 5: Out-of-order timestamps are filtered out
    /// For any tick with timestamp <= previous timestamp, it should be marked as invalid.
    #[test]
    fn out_of_order_timestamps_are_filtered(
        base_count in 3usize..10,
    ) {
        // Create timestamps with one out of order
        let mut timestamps: Vec<i64> = (0..base_count as i64).collect();
        let prices: Vec<f64> = (0..base_count).map(|i| 100.0 + i as f64).collect();
        let volumes: Vec<f64> = vec![1000.0; base_count];
        
        // Make timestamp at index 2 out of order (less than index 1)
        if base_count > 2 {
            timestamps[2] = timestamps[1] - 1;
        }
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes).unwrap();
        
        prop_assert!(
            result.report.invalid_ticks >= 1,
            "Should have at least 1 invalid tick for out-of-order timestamp"
        );
    }

    /// Property 5: Price jumps > 10% are flagged as anomalies
    /// For any tick with price change > 10%, it should be flagged as anomaly.
    #[test]
    fn price_jumps_are_flagged(
        base_price in 100.0f64..1000.0,
        jump_pct in 0.15f64..0.5, // 15% to 50% jump
    ) {
        let timestamps = vec![1, 2, 3];
        let jumped_price = base_price * (1.0 + jump_pct);
        let prices = vec![base_price, jumped_price, jumped_price + 1.0];
        let volumes = vec![1000.0, 1000.0, 1000.0];
        
        let loader = DataLoader::new().with_price_jump_threshold(0.10);
        let result = loader.load_from_vectors(timestamps, prices, volumes).unwrap();
        
        prop_assert!(
            result.report.anomaly_ticks >= 1,
            "Should flag price jump of {}% as anomaly",
            jump_pct * 100.0
        );
    }

    /// Property 5: Price changes within threshold are not flagged
    /// For any tick with price change <= threshold, it should not be flagged.
    #[test]
    fn normal_price_changes_not_flagged(
        base_price in 100.0f64..1000.0,
        change_pct in 0.0f64..0.09, // 0% to 9% change (under 10% threshold)
    ) {
        let timestamps = vec![1, 2, 3];
        let changed_price = base_price * (1.0 + change_pct);
        let prices = vec![base_price, changed_price, changed_price + 0.01];
        let volumes = vec![1000.0, 1000.0, 1000.0];
        
        let loader = DataLoader::new().with_price_jump_threshold(0.10);
        let result = loader.load_from_vectors(timestamps, prices, volumes).unwrap();
        
        prop_assert_eq!(
            result.report.anomaly_ticks, 0,
            "Should not flag price change of {}% as anomaly",
            change_pct * 100.0
        );
    }

    /// Property 5: Valid tick count + invalid tick count = total tick count
    /// The sum of valid and invalid ticks should equal total ticks.
    #[test]
    fn tick_counts_are_consistent(
        tick_count in 1usize..20,
        invalid_ratio in 0.0f64..0.5,
    ) {
        let invalid_count = ((tick_count as f64) * invalid_ratio) as usize;
        
        let timestamps: Vec<i64> = (0..tick_count as i64).collect();
        let mut prices: Vec<f64> = (0..tick_count).map(|i| 100.0 + i as f64).collect();
        let volumes: Vec<f64> = vec![1000.0; tick_count];
        
        // Make some prices invalid
        for price in prices.iter_mut().take(invalid_count.min(tick_count)) {
            *price = -1.0;
        }
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes).unwrap();
        
        prop_assert_eq!(
            result.report.total_ticks,
            tick_count as i64,
            "Total ticks should equal input count"
        );
        
        // Note: valid_ticks + invalid_ticks should equal total_ticks
        // But some invalid ticks might also be anomalies, so we check:
        prop_assert!(
            result.report.valid_ticks + result.report.invalid_ticks == result.report.total_ticks,
            "valid ({}) + invalid ({}) should equal total ({})",
            result.report.valid_ticks, result.report.invalid_ticks, result.report.total_ticks
        );
    }

    /// Property 5: All valid ticks have positive prices
    /// For any tick in the valid output, price should be > 0.
    #[test]
    fn all_valid_ticks_have_positive_prices(
        tick_count in 5usize..20,
    ) {
        // Mix of valid and invalid prices
        let timestamps: Vec<i64> = (0..tick_count as i64).collect();
        let prices: Vec<f64> = (0..tick_count)
            .map(|i| if i % 3 == 0 { -1.0 } else { 100.0 + i as f64 })
            .collect();
        let volumes: Vec<f64> = vec![1000.0; tick_count];
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes).unwrap();
        
        for tick in &result.ticks {
            prop_assert!(
                tick.price > 0.0,
                "All valid ticks should have positive price, got {}",
                tick.price
            );
        }
    }

    /// Property 5: All valid ticks have non-negative volumes
    /// For any tick in the valid output, volume should be >= 0.
    #[test]
    fn all_valid_ticks_have_nonnegative_volumes(
        tick_count in 5usize..20,
    ) {
        // Mix of valid and invalid volumes
        let timestamps: Vec<i64> = (0..tick_count as i64).collect();
        let prices: Vec<f64> = (0..tick_count).map(|i| 100.0 + i as f64).collect();
        let volumes: Vec<f64> = (0..tick_count)
            .map(|i| if i % 4 == 0 { -100.0 } else { 1000.0 })
            .collect();
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes).unwrap();
        
        for tick in &result.ticks {
            prop_assert!(
                tick.volume >= 0.0,
                "All valid ticks should have non-negative volume, got {}",
                tick.volume
            );
        }
    }

    /// Property 5: Valid ticks maintain timestamp order
    /// For any sequence of valid ticks, timestamps should be strictly increasing.
    #[test]
    fn valid_ticks_maintain_timestamp_order(
        tick_count in 5usize..20,
    ) {
        // Create some out-of-order timestamps
        let mut timestamps: Vec<i64> = (0..tick_count as i64).collect();
        let prices: Vec<f64> = (0..tick_count).map(|i| 100.0 + i as f64).collect();
        let volumes: Vec<f64> = vec![1000.0; tick_count];
        
        // Make some timestamps out of order
        if tick_count > 3 {
            timestamps[3] = timestamps[2]; // Duplicate timestamp
        }
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes).unwrap();
        
        // Check that valid ticks are in order
        for i in 1..result.ticks.len() {
            prop_assert!(
                result.ticks[i].timestamp > result.ticks[i - 1].timestamp,
                "Valid ticks should have strictly increasing timestamps: {} should be > {}",
                result.ticks[i].timestamp, result.ticks[i - 1].timestamp
            );
        }
    }
}
