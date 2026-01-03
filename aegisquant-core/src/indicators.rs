//! Technical Indicators module using the `ta` crate.
//!
//! Provides calculation of common technical indicators:
//! - Moving Averages (MA5, MA10, MA20, MA60)
//! - Bollinger Bands
//! - MACD (Moving Average Convergence Divergence)

use std::panic::catch_unwind;

use ta::indicators::{
    BollingerBands, ExponentialMovingAverage, MovingAverageConvergenceDivergence,
    SimpleMovingAverage,
};
use ta::Next;

use crate::ffi::{ERR_INTERNAL_PANIC, ERR_NULL_POINTER, ERR_SUCCESS};
use crate::precision::Price;

/// Result of indicator calculations for a single price point.
///
/// # FFI Safety
/// This struct uses `repr(C)` layout matching C# StructLayout.Sequential.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct IndicatorResult {
    /// 5-period Simple Moving Average
    pub ma5: f64,
    /// 10-period Simple Moving Average
    pub ma10: f64,
    /// 20-period Simple Moving Average
    pub ma20: f64,
    /// 60-period Simple Moving Average
    pub ma60: f64,
    /// Bollinger Bands upper band
    pub boll_upper: f64,
    /// Bollinger Bands middle band (20-period SMA)
    pub boll_middle: f64,
    /// Bollinger Bands lower band
    pub boll_lower: f64,
    /// MACD DIF line (fast EMA - slow EMA)
    pub macd_dif: f64,
    /// MACD DEA line (signal line, 9-period EMA of DIF)
    pub macd_dea: f64,
    /// MACD histogram (DIF - DEA)
    pub macd_histogram: f64,
}

/// Calculator for technical indicators.
///
/// Maintains internal state for streaming indicator calculations.
/// Each call to `update()` processes a new price and returns the current indicator values.
pub struct IndicatorCalculator {
    /// 5-period Simple Moving Average
    ma5: SimpleMovingAverage,
    /// 10-period Simple Moving Average
    ma10: SimpleMovingAverage,
    /// 20-period Simple Moving Average
    ma20: SimpleMovingAverage,
    /// 60-period Simple Moving Average
    ma60: SimpleMovingAverage,
    /// Bollinger Bands (20-period, 2 standard deviations)
    boll: BollingerBands,
    /// MACD (12, 26, 9)
    macd: MovingAverageConvergenceDivergence,
    /// Count of prices processed
    count: usize,
}

impl IndicatorCalculator {
    /// Create a new IndicatorCalculator with default parameters.
    ///
    /// Default parameters:
    /// - MA periods: 5, 10, 20, 60
    /// - Bollinger Bands: 20-period, 2 standard deviations
    /// - MACD: 12, 26, 9 (fast, slow, signal)
    pub fn new() -> Self {
        Self {
            ma5: SimpleMovingAverage::new(5).expect("Invalid MA5 period"),
            ma10: SimpleMovingAverage::new(10).expect("Invalid MA10 period"),
            ma20: SimpleMovingAverage::new(20).expect("Invalid MA20 period"),
            ma60: SimpleMovingAverage::new(60).expect("Invalid MA60 period"),
            boll: BollingerBands::new(20, 2.0).expect("Invalid Bollinger Bands params"),
            macd: MovingAverageConvergenceDivergence::new(12, 26, 9)
                .expect("Invalid MACD params"),
            count: 0,
        }
    }

    /// Create a new IndicatorCalculator with custom MA periods.
    pub fn with_ma_periods(ma5: usize, ma10: usize, ma20: usize, ma60: usize) -> Option<Self> {
        Some(Self {
            ma5: SimpleMovingAverage::new(ma5).ok()?,
            ma10: SimpleMovingAverage::new(ma10).ok()?,
            ma20: SimpleMovingAverage::new(ma20).ok()?,
            ma60: SimpleMovingAverage::new(ma60).ok()?,
            boll: BollingerBands::new(20, 2.0).ok()?,
            macd: MovingAverageConvergenceDivergence::new(12, 26, 9).ok()?,
            count: 0,
        })
    }

    /// Create a new IndicatorCalculator with custom MACD parameters.
    pub fn with_macd_params(fast: usize, slow: usize, signal: usize) -> Option<Self> {
        Some(Self {
            ma5: SimpleMovingAverage::new(5).ok()?,
            ma10: SimpleMovingAverage::new(10).ok()?,
            ma20: SimpleMovingAverage::new(20).ok()?,
            ma60: SimpleMovingAverage::new(60).ok()?,
            boll: BollingerBands::new(20, 2.0).ok()?,
            macd: MovingAverageConvergenceDivergence::new(fast, slow, signal).ok()?,
            count: 0,
        })
    }

    /// Update indicators with a new close price.
    ///
    /// Returns the current indicator values after processing the new price.
    pub fn update(&mut self, close: Price) -> IndicatorResult {
        self.count += 1;

        // Calculate moving averages
        let ma5_val = self.ma5.next(close);
        let ma10_val = self.ma10.next(close);
        let ma20_val = self.ma20.next(close);
        let ma60_val = self.ma60.next(close);

        // Calculate Bollinger Bands
        let boll_output = self.boll.next(close);

        // Calculate MACD
        let macd_output = self.macd.next(close);

        IndicatorResult {
            ma5: ma5_val,
            ma10: ma10_val,
            ma20: ma20_val,
            ma60: ma60_val,
            boll_upper: boll_output.upper,
            boll_middle: boll_output.average,
            boll_lower: boll_output.lower,
            macd_dif: macd_output.macd,
            macd_dea: macd_output.signal,
            macd_histogram: macd_output.histogram,
        }
    }

    /// Get the number of prices processed.
    pub fn count(&self) -> usize {
        self.count
    }

    /// Reset the calculator to initial state.
    pub fn reset(&mut self) {
        *self = Self::new();
    }
}

impl Default for IndicatorCalculator {
    fn default() -> Self {
        Self::new()
    }
}

// ============================================================================
// FFI Functions
// ============================================================================

/// Create a new IndicatorCalculator.
///
/// # Safety
/// - Caller must call `free_indicator_calculator` to release the returned pointer
///
/// # Returns
/// - Valid pointer on success
/// - Null pointer on failure
#[no_mangle]
pub extern "C" fn create_indicator_calculator() -> *mut IndicatorCalculator {
    let result = catch_unwind(|| {
        let calc = Box::new(IndicatorCalculator::new());
        Box::into_raw(calc)
    });

    match result {
        Ok(ptr) => ptr,
        Err(_) => std::ptr::null_mut(),
    }
}

/// Free an IndicatorCalculator.
///
/// # Safety
/// - `calc` must be a valid pointer returned by `create_indicator_calculator`
/// - Must only be called once per calculator
#[no_mangle]
pub unsafe extern "C" fn free_indicator_calculator(calc: *mut IndicatorCalculator) {
    if calc.is_null() {
        return;
    }

    let _ = catch_unwind(|| {
        let _ = Box::from_raw(calc);
    });
}

/// Update indicators with a new close price.
///
/// # Safety
/// - `calc` must be a valid pointer from `create_indicator_calculator`
/// - `result` must be a valid pointer to write IndicatorResult
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if calc or result is null
#[no_mangle]
pub unsafe extern "C" fn calculate_indicators(
    calc: *mut IndicatorCalculator,
    close: f64,
    result: *mut IndicatorResult,
) -> i32 {
    if calc.is_null() || result.is_null() {
        return ERR_NULL_POINTER;
    }

    let outcome = catch_unwind(|| {
        let calc_ref = &mut *calc;
        let indicator_result = calc_ref.update(close);
        *result = indicator_result;
        ERR_SUCCESS
    });

    match outcome {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Calculate indicators for an array of prices.
///
/// # Safety
/// - `calc` must be a valid pointer from `create_indicator_calculator`
/// - `prices` must be a valid pointer to an array of f64 with at least `count` elements
/// - `results` must be a valid pointer to an array of IndicatorResult with at least `count` elements
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if any pointer is null
#[no_mangle]
pub unsafe extern "C" fn calculate_indicators_batch(
    calc: *mut IndicatorCalculator,
    prices: *const f64,
    count: i32,
    results: *mut IndicatorResult,
) -> i32 {
    if calc.is_null() || prices.is_null() || results.is_null() {
        return ERR_NULL_POINTER;
    }

    if count <= 0 {
        return ERR_SUCCESS;
    }

    let outcome = catch_unwind(|| {
        let calc_ref = &mut *calc;
        let prices_slice = std::slice::from_raw_parts(prices, count as usize);
        let results_slice = std::slice::from_raw_parts_mut(results, count as usize);

        for (i, &price) in prices_slice.iter().enumerate() {
            results_slice[i] = calc_ref.update(price);
        }

        ERR_SUCCESS
    });

    match outcome {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

/// Reset the indicator calculator to initial state.
///
/// # Safety
/// - `calc` must be a valid pointer from `create_indicator_calculator`
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if calc is null
#[no_mangle]
pub unsafe extern "C" fn reset_indicator_calculator(calc: *mut IndicatorCalculator) -> i32 {
    if calc.is_null() {
        return ERR_NULL_POINTER;
    }

    let outcome = catch_unwind(|| {
        let calc_ref = &mut *calc;
        calc_ref.reset();
        ERR_SUCCESS
    });

    match outcome {
        Ok(code) => code,
        Err(_) => ERR_INTERNAL_PANIC,
    }
}

// ============================================================================
// Standalone calculation functions for verification
// ============================================================================

/// Calculate Simple Moving Average for a price series.
///
/// Returns the SMA values for each price point.
pub fn calculate_sma(prices: &[f64], period: usize) -> Vec<f64> {
    if period == 0 || prices.is_empty() {
        return vec![];
    }

    let mut sma = SimpleMovingAverage::new(period).expect("Invalid period");
    prices.iter().map(|&p| sma.next(p)).collect()
}

/// Calculate Exponential Moving Average for a price series.
///
/// Returns the EMA values for each price point.
pub fn calculate_ema(prices: &[f64], period: usize) -> Vec<f64> {
    if period == 0 || prices.is_empty() {
        return vec![];
    }

    let mut ema = ExponentialMovingAverage::new(period).expect("Invalid period");
    prices.iter().map(|&p| ema.next(p)).collect()
}

/// Calculate Bollinger Bands for a price series.
///
/// Returns (upper, middle, lower) bands for each price point.
pub fn calculate_bollinger_bands(
    prices: &[f64],
    period: usize,
    std_dev: f64,
) -> Vec<(f64, f64, f64)> {
    if period == 0 || prices.is_empty() {
        return vec![];
    }

    let mut boll = BollingerBands::new(period, std_dev).expect("Invalid params");
    prices
        .iter()
        .map(|&p| {
            let output = boll.next(p);
            (output.upper, output.average, output.lower)
        })
        .collect()
}

/// Calculate MACD for a price series.
///
/// Returns (dif, dea, histogram) for each price point.
pub fn calculate_macd(
    prices: &[f64],
    fast: usize,
    slow: usize,
    signal: usize,
) -> Vec<(f64, f64, f64)> {
    if prices.is_empty() {
        return vec![];
    }

    let mut macd =
        MovingAverageConvergenceDivergence::new(fast, slow, signal).expect("Invalid params");
    prices
        .iter()
        .map(|&p| {
            let output = macd.next(p);
            (output.macd, output.signal, output.histogram)
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_indicator_calculator_creation() {
        let calc = IndicatorCalculator::new();
        assert_eq!(calc.count(), 0);
    }

    #[test]
    fn test_indicator_calculator_update() {
        let mut calc = IndicatorCalculator::new();

        // Feed some prices
        for i in 1..=10 {
            let result = calc.update(100.0 + i as f64);
            assert!(result.ma5 > 0.0);
        }

        assert_eq!(calc.count(), 10);
    }

    #[test]
    fn test_ma5_calculation() {
        let mut calc = IndicatorCalculator::new();

        // Feed 5 prices: 100, 101, 102, 103, 104
        // MA5 should be (100+101+102+103+104)/5 = 102
        for i in 0..5 {
            calc.update(100.0 + i as f64);
        }

        let result = calc.update(105.0); // 6th price
        // MA5 of last 5 prices: (101+102+103+104+105)/5 = 103
        assert!((result.ma5 - 103.0).abs() < 0.001);
    }

    #[test]
    fn test_bollinger_bands() {
        let mut calc = IndicatorCalculator::new();

        // Feed enough prices for Bollinger Bands to stabilize
        for i in 0..30 {
            calc.update(100.0 + (i % 5) as f64);
        }

        let result = calc.update(102.0);

        // Upper should be > middle > lower
        assert!(result.boll_upper > result.boll_middle);
        assert!(result.boll_middle > result.boll_lower);
    }

    #[test]
    fn test_macd_calculation() {
        let mut calc = IndicatorCalculator::new();

        // Feed enough prices for MACD to stabilize
        for i in 0..50 {
            calc.update(100.0 + (i as f64 * 0.1));
        }

        let result = calc.update(105.0);

        // In an uptrend, DIF should be positive
        // histogram = DIF - DEA
        assert!((result.macd_histogram - (result.macd_dif - result.macd_dea)).abs() < 0.0001);
    }

    #[test]
    fn test_reset() {
        let mut calc = IndicatorCalculator::new();

        for i in 0..10 {
            calc.update(100.0 + i as f64);
        }

        assert_eq!(calc.count(), 10);

        calc.reset();
        assert_eq!(calc.count(), 0);
    }

    #[test]
    fn test_calculate_sma() {
        let prices = vec![1.0, 2.0, 3.0, 4.0, 5.0];
        let sma = calculate_sma(&prices, 3);

        assert_eq!(sma.len(), 5);
        // After 3 prices, SMA should be (1+2+3)/3 = 2
        assert!((sma[2] - 2.0).abs() < 0.001);
        // After 5 prices, SMA should be (3+4+5)/3 = 4
        assert!((sma[4] - 4.0).abs() < 0.001);
    }

    #[test]
    fn test_calculate_bollinger_bands() {
        let prices: Vec<f64> = (0..30).map(|i| 100.0 + (i % 5) as f64).collect();
        let bands = calculate_bollinger_bands(&prices, 20, 2.0);

        assert_eq!(bands.len(), 30);

        // Check that upper > middle > lower for all points
        for (upper, middle, lower) in &bands {
            assert!(upper >= middle);
            assert!(middle >= lower);
        }
    }

    #[test]
    fn test_calculate_macd() {
        let prices: Vec<f64> = (0..50).map(|i| 100.0 + i as f64 * 0.5).collect();
        let macd = calculate_macd(&prices, 12, 26, 9);

        assert_eq!(macd.len(), 50);

        // Check histogram = dif - dea
        for (dif, dea, histogram) in &macd {
            assert!((histogram - (dif - dea)).abs() < 0.0001);
        }
    }

    #[test]
    fn test_ffi_create_and_free() {
        unsafe {
            let calc = create_indicator_calculator();
            assert!(!calc.is_null());
            free_indicator_calculator(calc);
        }
    }

    #[test]
    fn test_ffi_calculate_indicators() {
        unsafe {
            let calc = create_indicator_calculator();
            let mut result = IndicatorResult::default();

            let code = calculate_indicators(calc, 100.0, &mut result);
            assert_eq!(code, ERR_SUCCESS);
            assert!(result.ma5 > 0.0);

            free_indicator_calculator(calc);
        }
    }

    #[test]
    fn test_ffi_null_pointers() {
        unsafe {
            let mut result = IndicatorResult::default();

            // Null calculator
            let code = calculate_indicators(std::ptr::null_mut(), 100.0, &mut result);
            assert_eq!(code, ERR_NULL_POINTER);

            // Null result
            let calc = create_indicator_calculator();
            let code = calculate_indicators(calc, 100.0, std::ptr::null_mut());
            assert_eq!(code, ERR_NULL_POINTER);

            free_indicator_calculator(calc);
        }
    }

    #[test]
    fn test_ffi_batch_calculation() {
        unsafe {
            let calc = create_indicator_calculator();
            let prices = [100.0, 101.0, 102.0, 103.0, 104.0];
            let mut results = [IndicatorResult::default(); 5];

            let code = calculate_indicators_batch(
                calc,
                prices.as_ptr(),
                5,
                results.as_mut_ptr(),
            );
            assert_eq!(code, ERR_SUCCESS);

            // Check that all results have valid MA5 values
            for result in &results {
                assert!(result.ma5 > 0.0);
            }

            free_indicator_calculator(calc);
        }
    }
}
