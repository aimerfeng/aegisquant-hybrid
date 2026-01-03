//! Property-based tests for technical indicators.
//!
//! **Feature: aegisquant-optimizations, Property 8: 技术指标计算正确性**
//! **Validates: Requirements 19.6**
//!
//! Tests that IndicatorCalculator produces results consistent with
//! independent implementations of the same algorithms.

use proptest::prelude::*;

use aegisquant_core::indicators::{
    calculate_bollinger_bands, calculate_macd, calculate_sma, IndicatorCalculator,
};
use aegisquant_core::precision::PRICE_EPSILON;

/// Generate a realistic price series with bounded changes.
fn price_series_strategy(len: usize) -> impl Strategy<Value = Vec<f64>> {
    prop::collection::vec(50.0f64..150.0, len)
}

/// Independent SMA calculation for verification.
fn independent_sma(prices: &[f64], period: usize) -> Vec<f64> {
    if period == 0 || prices.is_empty() {
        return vec![];
    }

    let mut result = Vec::with_capacity(prices.len());

    for i in 0..prices.len() {
        if i < period - 1 {
            // Before we have enough data, use available prices
            let sum: f64 = prices[..=i].iter().sum();
            result.push(sum / (i + 1) as f64);
        } else {
            // Full period available
            let sum: f64 = prices[i + 1 - period..=i].iter().sum();
            result.push(sum / period as f64);
        }
    }

    result
}

/// Independent Bollinger Bands calculation for verification.
fn independent_bollinger(prices: &[f64], period: usize, std_dev_mult: f64) -> Vec<(f64, f64, f64)> {
    if period == 0 || prices.is_empty() {
        return vec![];
    }

    let mut result = Vec::with_capacity(prices.len());

    for i in 0..prices.len() {
        let window_start = if i >= period - 1 { i + 1 - period } else { 0 };
        let window = &prices[window_start..=i];
        let n = window.len() as f64;

        let mean: f64 = window.iter().sum::<f64>() / n;

        let variance: f64 = window.iter().map(|&x| (x - mean).powi(2)).sum::<f64>() / n;
        let std_dev = variance.sqrt();

        let upper = mean + std_dev_mult * std_dev;
        let lower = mean - std_dev_mult * std_dev;

        result.push((upper, mean, lower));
    }

    result
}

proptest! {
    #![proptest_config(ProptestConfig::with_cases(100))]

    /// Property 8: SMA calculation matches independent implementation.
    ///
    /// **Feature: aegisquant-optimizations, Property 8: 技术指标计算正确性**
    /// **Validates: Requirements 19.6**
    ///
    /// For any price series, the SMA values from IndicatorCalculator
    /// should match values from an independent SMA calculation.
    #[test]
    fn prop_sma_matches_independent(
        prices in price_series_strategy(100)
    ) {
        // Calculate using ta crate via our module
        let ta_sma5 = calculate_sma(&prices, 5);
        let ta_sma10 = calculate_sma(&prices, 10);
        let ta_sma20 = calculate_sma(&prices, 20);

        // Calculate using independent implementation
        let ind_sma5 = independent_sma(&prices, 5);
        let ind_sma10 = independent_sma(&prices, 10);
        let ind_sma20 = independent_sma(&prices, 20);

        // After warmup period, values should match closely
        // (first few values may differ due to initialization)
        for i in 20..prices.len() {
            let diff5 = (ta_sma5[i] - ind_sma5[i]).abs();
            let diff10 = (ta_sma10[i] - ind_sma10[i]).abs();
            let diff20 = (ta_sma20[i] - ind_sma20[i]).abs();

            prop_assert!(
                diff5 < 0.01,
                "SMA5 mismatch at index {}: ta={}, ind={}, diff={}",
                i, ta_sma5[i], ind_sma5[i], diff5
            );
            prop_assert!(
                diff10 < 0.01,
                "SMA10 mismatch at index {}: ta={}, ind={}, diff={}",
                i, ta_sma10[i], ind_sma10[i], diff10
            );
            prop_assert!(
                diff20 < 0.01,
                "SMA20 mismatch at index {}: ta={}, ind={}, diff={}",
                i, ta_sma20[i], ind_sma20[i], diff20
            );
        }
    }

    /// Property 8: Bollinger Bands maintain upper > middle > lower invariant.
    ///
    /// **Feature: aegisquant-optimizations, Property 8: 技术指标计算正确性**
    /// **Validates: Requirements 19.6**
    ///
    /// For any price series, Bollinger Bands should always satisfy:
    /// upper >= middle >= lower
    #[test]
    fn prop_bollinger_bands_ordering(
        prices in price_series_strategy(100)
    ) {
        let bands = calculate_bollinger_bands(&prices, 20, 2.0);

        for (i, (upper, middle, lower)) in bands.iter().enumerate() {
            prop_assert!(
                upper >= middle,
                "Bollinger upper < middle at index {}: upper={}, middle={}",
                i, upper, middle
            );
            prop_assert!(
                middle >= lower,
                "Bollinger middle < lower at index {}: middle={}, lower={}",
                i, middle, lower
            );
        }
    }

    /// Property 8: Bollinger middle band equals SMA.
    ///
    /// **Feature: aegisquant-optimizations, Property 8: 技术指标计算正确性**
    /// **Validates: Requirements 19.6**
    ///
    /// The middle band of Bollinger Bands should equal the SMA
    /// of the same period.
    #[test]
    fn prop_bollinger_middle_equals_sma(
        prices in price_series_strategy(100)
    ) {
        let bands = calculate_bollinger_bands(&prices, 20, 2.0);
        let sma20 = calculate_sma(&prices, 20);

        // After warmup, middle band should equal SMA20
        for i in 20..prices.len() {
            let diff = (bands[i].1 - sma20[i]).abs();
            prop_assert!(
                diff < 0.001,
                "Bollinger middle != SMA20 at index {}: middle={}, sma={}",
                i, bands[i].1, sma20[i]
            );
        }
    }

    /// Property 8: MACD histogram equals DIF - DEA.
    ///
    /// **Feature: aegisquant-optimizations, Property 8: 技术指标计算正确性**
    /// **Validates: Requirements 19.6**
    ///
    /// For any price series, MACD histogram should always equal DIF - DEA.
    #[test]
    fn prop_macd_histogram_consistency(
        prices in price_series_strategy(100)
    ) {
        let macd = calculate_macd(&prices, 12, 26, 9);

        for (i, (dif, dea, histogram)) in macd.iter().enumerate() {
            let expected_histogram = dif - dea;
            let diff = (histogram - expected_histogram).abs();

            prop_assert!(
                diff < PRICE_EPSILON,
                "MACD histogram != DIF - DEA at index {}: histogram={}, expected={}",
                i, histogram, expected_histogram
            );
        }
    }

    /// Property 8: IndicatorCalculator streaming matches batch calculation.
    ///
    /// **Feature: aegisquant-optimizations, Property 8: 技术指标计算正确性**
    /// **Validates: Requirements 19.6**
    ///
    /// Processing prices one at a time should produce the same results
    /// as processing them in batch.
    #[test]
    fn prop_streaming_matches_batch(
        prices in price_series_strategy(100)
    ) {
        // Streaming calculation
        let mut calc = IndicatorCalculator::new();
        let streaming_results: Vec<_> = prices.iter()
            .map(|&p| calc.update(p))
            .collect();

        // Batch calculation using standalone functions
        let batch_sma5 = calculate_sma(&prices, 5);
        let batch_sma10 = calculate_sma(&prices, 10);
        let batch_sma20 = calculate_sma(&prices, 20);
        let batch_boll = calculate_bollinger_bands(&prices, 20, 2.0);
        let batch_macd = calculate_macd(&prices, 12, 26, 9);

        // Compare results (after warmup period)
        for i in 60..prices.len() {
            let streaming = &streaming_results[i];

            // SMA comparisons
            let diff_ma5 = (streaming.ma5 - batch_sma5[i]).abs();
            let diff_ma10 = (streaming.ma10 - batch_sma10[i]).abs();
            let diff_ma20 = (streaming.ma20 - batch_sma20[i]).abs();

            prop_assert!(diff_ma5 < 0.001, "MA5 mismatch at {}", i);
            prop_assert!(diff_ma10 < 0.001, "MA10 mismatch at {}", i);
            prop_assert!(diff_ma20 < 0.001, "MA20 mismatch at {}", i);

            // Bollinger comparisons
            let diff_boll_upper = (streaming.boll_upper - batch_boll[i].0).abs();
            let diff_boll_middle = (streaming.boll_middle - batch_boll[i].1).abs();
            let diff_boll_lower = (streaming.boll_lower - batch_boll[i].2).abs();

            prop_assert!(diff_boll_upper < 0.001, "Boll upper mismatch at {}", i);
            prop_assert!(diff_boll_middle < 0.001, "Boll middle mismatch at {}", i);
            prop_assert!(diff_boll_lower < 0.001, "Boll lower mismatch at {}", i);

            // MACD comparisons
            let diff_macd_dif = (streaming.macd_dif - batch_macd[i].0).abs();
            let diff_macd_dea = (streaming.macd_dea - batch_macd[i].1).abs();
            let diff_macd_hist = (streaming.macd_histogram - batch_macd[i].2).abs();

            prop_assert!(diff_macd_dif < 0.001, "MACD DIF mismatch at {}", i);
            prop_assert!(diff_macd_dea < 0.001, "MACD DEA mismatch at {}", i);
            prop_assert!(diff_macd_hist < 0.001, "MACD histogram mismatch at {}", i);
        }
    }

    /// Property 8: Indicator values are finite and reasonable.
    ///
    /// **Feature: aegisquant-optimizations, Property 8: 技术指标计算正确性**
    /// **Validates: Requirements 19.6**
    ///
    /// All indicator values should be finite (not NaN or Inf) and
    /// within reasonable bounds relative to input prices.
    #[test]
    fn prop_indicator_values_finite_and_reasonable(
        prices in price_series_strategy(100)
    ) {
        let mut calc = IndicatorCalculator::new();

        let min_price = prices.iter().cloned().fold(f64::INFINITY, f64::min);
        let max_price = prices.iter().cloned().fold(f64::NEG_INFINITY, f64::max);

        for &price in &prices {
            let result = calc.update(price);

            // All values should be finite
            prop_assert!(result.ma5.is_finite(), "MA5 is not finite");
            prop_assert!(result.ma10.is_finite(), "MA10 is not finite");
            prop_assert!(result.ma20.is_finite(), "MA20 is not finite");
            prop_assert!(result.ma60.is_finite(), "MA60 is not finite");
            prop_assert!(result.boll_upper.is_finite(), "Boll upper is not finite");
            prop_assert!(result.boll_middle.is_finite(), "Boll middle is not finite");
            prop_assert!(result.boll_lower.is_finite(), "Boll lower is not finite");
            prop_assert!(result.macd_dif.is_finite(), "MACD DIF is not finite");
            prop_assert!(result.macd_dea.is_finite(), "MACD DEA is not finite");
            prop_assert!(result.macd_histogram.is_finite(), "MACD histogram is not finite");

            // MA values should be within price range (with some tolerance)
            let tolerance = (max_price - min_price) * 0.5;
            prop_assert!(
                result.ma5 >= min_price - tolerance && result.ma5 <= max_price + tolerance,
                "MA5 out of range: {} not in [{}, {}]",
                result.ma5, min_price - tolerance, max_price + tolerance
            );
        }
    }
}

#[cfg(test)]
mod unit_tests {
    use super::*;

    #[test]
    fn test_independent_sma_basic() {
        let prices = vec![1.0, 2.0, 3.0, 4.0, 5.0];
        let sma = independent_sma(&prices, 3);

        // After 3 prices: (1+2+3)/3 = 2
        assert!((sma[2] - 2.0).abs() < 0.001);
        // After 4 prices: (2+3+4)/3 = 3
        assert!((sma[3] - 3.0).abs() < 0.001);
        // After 5 prices: (3+4+5)/3 = 4
        assert!((sma[4] - 4.0).abs() < 0.001);
    }

    #[test]
    fn test_independent_bollinger_basic() {
        let prices = vec![100.0; 20];
        let bands = independent_bollinger(&prices, 20, 2.0);

        // With constant prices, std dev = 0, so upper = middle = lower
        let (upper, middle, lower) = bands[19];
        assert!((upper - 100.0).abs() < 0.001);
        assert!((middle - 100.0).abs() < 0.001);
        assert!((lower - 100.0).abs() < 0.001);
    }
}
