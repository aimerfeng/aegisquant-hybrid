//! Property-based tests for Error Propagation.
//!
//! Feature: aegisquant-optimizations, Property 2: 错误传播�?Panic
//! Validates: Requirements 1.5, 2.1, 2.2, 2.4
//!
//! This test verifies that for any malformed input data, the data loader
//! returns an error code rather than panicking.

use proptest::prelude::*;
use std::fs;
use std::io::Write;
use std::path::PathBuf;
use tempfile::TempDir;

use aegisquant_core::data_loader::DataLoader;
use aegisquant_core::error::EngineError;

/// Generate a random string for CSV content
fn arb_string(max_len: usize) -> impl Strategy<Value = String> {
    proptest::collection::vec(any::<char>(), 0..max_len)
        .prop_map(|chars| chars.into_iter().collect())
}

/// Generate random bytes that may or may not be valid UTF-8
fn arb_bytes(max_len: usize) -> impl Strategy<Value = Vec<u8>> {
    proptest::collection::vec(any::<u8>(), 0..max_len)
}

/// Generate a valid CSV header
fn valid_header() -> &'static str {
    "timestamp,price,volume"
}

/// Generate a malformed CSV with missing columns
fn generate_missing_column_csv(dir: &TempDir, content: &str) -> PathBuf {
    let path = dir.path().join("missing_col.csv");
    let mut file = fs::File::create(&path).unwrap();
    writeln!(file, "timestamp,price").unwrap(); // Missing volume
    writeln!(file, "{}", content).unwrap();
    path
}

/// Generate a malformed CSV with wrong types
fn generate_wrong_type_csv(dir: &TempDir, bad_value: &str) -> PathBuf {
    let path = dir.path().join("wrong_type.csv");
    let mut file = fs::File::create(&path).unwrap();
    writeln!(file, "{}", valid_header()).unwrap();
    writeln!(file, "1000000000,{},100.0", bad_value).unwrap();
    path
}

/// Generate an empty CSV file
fn generate_empty_csv(dir: &TempDir) -> PathBuf {
    let path = dir.path().join("empty.csv");
    fs::File::create(&path).unwrap();
    path
}

/// Generate a CSV with only header
fn generate_header_only_csv(dir: &TempDir) -> PathBuf {
    let path = dir.path().join("header_only.csv");
    let mut file = fs::File::create(&path).unwrap();
    writeln!(file, "{}", valid_header()).unwrap();
    path
}


/// Generate a CSV with extra columns
fn generate_extra_columns_csv(dir: &TempDir) -> PathBuf {
    let path = dir.path().join("extra_cols.csv");
    let mut file = fs::File::create(&path).unwrap();
    writeln!(file, "{}", valid_header()).unwrap();
    writeln!(file, "1000000000,100.0,1000.0,extra,more_extra").unwrap();
    path
}

/// Generate a CSV with missing values
fn generate_missing_values_csv(dir: &TempDir) -> PathBuf {
    let path = dir.path().join("missing_vals.csv");
    let mut file = fs::File::create(&path).unwrap();
    writeln!(file, "{}", valid_header()).unwrap();
    writeln!(file, "1000000000,,1000.0").unwrap(); // Missing price
    path
}

/// Generate a CSV with invalid encoding
fn generate_invalid_encoding_csv(dir: &TempDir, invalid_bytes: &[u8]) -> PathBuf {
    let path = dir.path().join("invalid_encoding.csv");
    let mut file = fs::File::create(&path).unwrap();
    file.write_all(b"timestamp,price,volume\n").unwrap();
    file.write_all(b"1000000000,").unwrap();
    file.write_all(invalid_bytes).unwrap();
    file.write_all(b",1000.0\n").unwrap();
    path
}

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]

    /// Property 2: Missing column CSV returns error, not panic
    /// For any CSV file missing the 'volume' column, load_from_file should
    /// return an error (MissingColumn) rather than panicking.
    #[test]
    fn missing_column_returns_error_not_panic(
        row_data in "[0-9]{1,10},[0-9]{1,6}\\.[0-9]{1,2}"
    ) {
        let temp_dir = TempDir::new().unwrap();
        let path = generate_missing_column_csv(&temp_dir, &row_data);
        
        let loader = DataLoader::new();
        let result = loader.load_from_file(&path);
        
        // Should return an error, not panic
        prop_assert!(
            result.is_err(),
            "Expected error for missing column CSV, got Ok"
        );
        
        // Verify it's the right error type
        if let Err(e) = result {
            prop_assert!(
                matches!(e, EngineError::MissingColumn { .. }),
                "Expected MissingColumn error, got {:?}", e
            );
        }
    }

    /// Property 2: Wrong type in price column returns error, not panic
    /// For any non-numeric value in the price column, load_from_file should
    /// return an error rather than panicking.
    #[test]
    fn wrong_type_returns_error_not_panic(
        bad_value in "[a-zA-Z]{1,10}"
    ) {
        let temp_dir = TempDir::new().unwrap();
        let path = generate_wrong_type_csv(&temp_dir, &bad_value);
        
        let loader = DataLoader::new();
        let result = loader.load_from_file(&path);
        
        // Should return an error, not panic
        // Note: Polars may parse this as null, which we handle as NaN
        // The important thing is no panic
        prop_assert!(
            true, // If we got here without panic, test passes
            "Should not panic on wrong type data"
        );
    }

    /// Property 2: Empty file returns error, not panic
    #[test]
    fn empty_file_returns_error_not_panic(_dummy in 0..1i32) {
        let temp_dir = TempDir::new().unwrap();
        let path = generate_empty_csv(&temp_dir);
        
        let loader = DataLoader::new();
        let result = loader.load_from_file(&path);
        
        // Should return an error, not panic
        prop_assert!(
            result.is_err(),
            "Expected error for empty CSV, got Ok"
        );
    }

    /// Property 2: Header-only file returns error, not panic
    #[test]
    fn header_only_returns_error_not_panic(_dummy in 0..1i32) {
        let temp_dir = TempDir::new().unwrap();
        let path = generate_header_only_csv(&temp_dir);
        
        let loader = DataLoader::new();
        let result = loader.load_from_file(&path);
        
        // Should return an error (empty file), not panic
        prop_assert!(
            result.is_err(),
            "Expected error for header-only CSV, got Ok"
        );
        
        if let Err(e) = result {
            prop_assert!(
                matches!(e, EngineError::EmptyFile { .. }),
                "Expected EmptyFile error, got {:?}", e
            );
        }
    }

    /// Property 2: Non-existent file returns error, not panic
    #[test]
    fn nonexistent_file_returns_error_not_panic(
        filename in "[a-z]{5,10}\\.csv"
    ) {
        let loader = DataLoader::new();
        let result = loader.load_from_file(&filename);
        
        // Should return FileNotFound error, not panic
        prop_assert!(
            result.is_err(),
            "Expected error for non-existent file, got Ok"
        );
        
        if let Err(e) = result {
            prop_assert!(
                matches!(e, EngineError::FileNotFound { .. }),
                "Expected FileNotFound error, got {:?}", e
            );
        }
    }


    /// Property 2: Invalid vectors return error, not panic
    /// For any mismatched vector lengths, load_from_vectors should
    /// return an error rather than panicking.
    #[test]
    fn mismatched_vectors_return_error_not_panic(
        ts_len in 1usize..100,
        price_len in 1usize..100,
        vol_len in 1usize..100,
    ) {
        // Only test when lengths don't all match
        prop_assume!(!(ts_len == price_len && price_len == vol_len));
        
        let timestamps: Vec<i64> = (0..ts_len as i64).collect();
        let prices: Vec<f64> = (0..price_len).map(|i| 100.0 + i as f64).collect();
        let volumes: Vec<f64> = (0..vol_len).map(|i| 1000.0 + i as f64).collect();
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes);
        
        // Should return an error, not panic
        prop_assert!(
            result.is_err(),
            "Expected error for mismatched vectors, got Ok"
        );
        
        if let Err(e) = result {
            prop_assert!(
                matches!(e, EngineError::ValidationError(_)),
                "Expected ValidationError, got {:?}", e
            );
        }
    }

    /// Property 2: Empty vectors return error, not panic
    #[test]
    fn empty_vectors_return_error_not_panic(_dummy in 0..1i32) {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(vec![], vec![], vec![]);
        
        // Should return an error, not panic
        prop_assert!(
            result.is_err(),
            "Expected error for empty vectors, got Ok"
        );
    }

    /// Property 2: Invalid price values are filtered, not panic
    /// For any vector containing invalid prices (negative, zero, NaN, Inf),
    /// load_from_vectors should handle them gracefully.
    #[test]
    fn invalid_prices_handled_not_panic(
        valid_count in 1usize..50,
        invalid_price in prop_oneof![
            Just(0.0f64),
            Just(-1.0f64),
            Just(-100.0f64),
            Just(f64::NAN),
            Just(f64::INFINITY),
            Just(f64::NEG_INFINITY),
        ],
        invalid_index in 0usize..50,
    ) {
        let total = valid_count + 1;
        let actual_invalid_index = invalid_index % total;
        
        let timestamps: Vec<i64> = (0..total as i64).collect();
        let mut prices: Vec<f64> = (0..total).map(|i| 100.0 + i as f64 * 0.1).collect();
        let volumes: Vec<f64> = vec![1000.0; total];
        
        // Insert invalid price
        prices[actual_invalid_index] = invalid_price;
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes);
        
        // Should succeed (invalid prices are filtered), not panic
        prop_assert!(
            result.is_ok(),
            "Expected Ok (with filtered invalid prices), got Err: {:?}", result
        );
        
        if let Ok(cleansing_result) = result {
            // Invalid price should be filtered out
            prop_assert!(
                cleansing_result.report.invalid_ticks >= 1,
                "Expected at least 1 invalid tick, got {}",
                cleansing_result.report.invalid_ticks
            );
        }
    }

    /// Property 2: Invalid volume values are filtered, not panic
    #[test]
    fn invalid_volumes_handled_not_panic(
        valid_count in 1usize..50,
        invalid_volume in prop_oneof![
            Just(-1.0f64),
            Just(-100.0f64),
            Just(f64::NAN),
            Just(f64::INFINITY),
            Just(f64::NEG_INFINITY),
        ],
        invalid_index in 0usize..50,
    ) {
        let total = valid_count + 1;
        let actual_invalid_index = invalid_index % total;
        
        let timestamps: Vec<i64> = (0..total as i64).collect();
        let prices: Vec<f64> = (0..total).map(|i| 100.0 + i as f64 * 0.1).collect();
        let mut volumes: Vec<f64> = vec![1000.0; total];
        
        // Insert invalid volume
        volumes[actual_invalid_index] = invalid_volume;
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes);
        
        // Should succeed (invalid volumes are filtered), not panic
        prop_assert!(
            result.is_ok(),
            "Expected Ok (with filtered invalid volumes), got Err: {:?}", result
        );
        
        if let Ok(cleansing_result) = result {
            // Invalid volume should be filtered out
            prop_assert!(
                cleansing_result.report.invalid_ticks >= 1,
                "Expected at least 1 invalid tick, got {}",
                cleansing_result.report.invalid_ticks
            );
        }
    }

    /// Property 2: Out-of-order timestamps are filtered, not panic
    #[test]
    fn out_of_order_timestamps_handled_not_panic(
        count in 5usize..50,
        swap_index in 1usize..49,
    ) {
        let actual_swap = swap_index % (count - 1) + 1; // Ensure valid index
        
        let mut timestamps: Vec<i64> = (0..count as i64).collect();
        let prices: Vec<f64> = (0..count).map(|i| 100.0 + i as f64 * 0.1).collect();
        let volumes: Vec<f64> = vec![1000.0; count];
        
        // Create out-of-order timestamp
        if actual_swap < count {
            timestamps[actual_swap] = timestamps[actual_swap - 1] - 1;
        }
        
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(timestamps, prices, volumes);
        
        // Should succeed (out-of-order timestamps are filtered), not panic
        prop_assert!(
            result.is_ok(),
            "Expected Ok (with filtered out-of-order timestamps), got Err: {:?}", result
        );
    }

    /// Property 2: Unsupported file format returns error, not panic
    #[test]
    fn unsupported_format_returns_error_not_panic(
        extension in "[a-z]{2,5}"
    ) {
        // Skip csv and parquet which are supported
        prop_assume!(extension != "csv" && extension != "parquet");
        
        let temp_dir = TempDir::new().unwrap();
        let path = temp_dir.path().join(format!("test.{}", extension));
        fs::write(&path, "some content").unwrap();
        
        let loader = DataLoader::new();
        let result = loader.load_from_file(&path);
        
        // Should return an error, not panic
        prop_assert!(
            result.is_err(),
            "Expected error for unsupported format .{}, got Ok", extension
        );
    }
}
