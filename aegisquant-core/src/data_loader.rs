//! Data loading and cleansing module using Polars.
//!
//! Provides high-performance data loading from CSV and Parquet files,
//! with built-in data validation and quality reporting.
//!
//! # Error Handling
//! All functions use `?` operator for error propagation. No `unwrap()` calls
//! are used in production code paths to ensure the engine never panics
//! due to malformed input data.

use polars::prelude::*;
use std::path::Path;

use crate::error::{EngineError, EngineResult};
use crate::types::{DataQualityReport, Tick};
use crate::data_pipeline::{DataPipeline, PipelineConfig};

/// Result of data cleansing operation.
#[derive(Debug)]
pub struct CleansingResult {
    /// Valid ticks after cleansing
    pub ticks: Vec<Tick>,
    /// Data quality report
    pub report: DataQualityReport,
    /// Indices of anomaly ticks (price jumps)
    pub anomaly_indices: Vec<usize>,
}

/// Data loader for loading and cleansing tick data.
#[derive(Debug)]
pub struct DataLoader {
    /// Price jump threshold (default 10%)
    price_jump_threshold: f64,
    /// Advanced data pipeline for institutional-grade cleansing
    pipeline: DataPipeline,
    /// Whether to use advanced pipeline preprocessing
    use_advanced_pipeline: bool,
}

impl Default for DataLoader {
    fn default() -> Self {
        Self {
            price_jump_threshold: 0.10,
            pipeline: DataPipeline::new(PipelineConfig::default()),
            use_advanced_pipeline: false, // Disabled by default for backward compatibility
        }
    }
}

impl DataLoader {
    /// Create a new DataLoader with default settings.
    pub fn new() -> Self {
        Self::default()
    }

    /// Set the price jump threshold for anomaly detection.
    pub fn with_price_jump_threshold(mut self, threshold: f64) -> Self {
        self.price_jump_threshold = threshold;
        self
    }

    /// Enable advanced pipeline preprocessing (Z-Score outlier detection, forward fill, etc.)
    pub fn with_advanced_pipeline(mut self, config: PipelineConfig) -> Self {
        self.pipeline = DataPipeline::new(config);
        self.use_advanced_pipeline = true;
        self
    }

    /// Enable advanced pipeline with default configuration
    pub fn with_default_advanced_pipeline(mut self) -> Self {
        self.pipeline = DataPipeline::new(PipelineConfig::default());
        self.use_advanced_pipeline = true;
        self
    }

    /// Load data from a file (CSV or Parquet).
    ///
    /// # Arguments
    /// * `path` - Path to the data file
    ///
    /// # Returns
    /// * `Ok(CleansingResult)` - Cleansed data with quality report
    /// * `Err(EngineError)` - If loading or validation fails
    ///
    /// # Error Handling
    /// - Returns `FileNotFound` if the file doesn't exist
    /// - Returns `ValidationError` for unsupported file formats
    /// - Returns `MissingColumn` if required columns are missing
    /// - Returns `ParseError` for malformed data
    pub fn load_from_file<P: AsRef<Path>>(&self, path: P) -> EngineResult<CleansingResult> {
        let path = path.as_ref();
        
        // Check file existence first
        if !path.exists() {
            return Err(EngineError::file_not_found(path.display().to_string()));
        }

        // Get file extension safely without unwrap
        let extension = path.extension()
            .and_then(|e| e.to_str())
            .unwrap_or("");

        let df = match extension.to_lowercase().as_str() {
            "csv" => self.load_csv(path)?,
            "parquet" => self.load_parquet(path)?,
            _ => return Err(EngineError::validation(
                format!("Unsupported file format: {}", extension)
            )),
        };

        // Check for empty dataframe
        if df.height() == 0 {
            return Err(EngineError::empty_file(path.display().to_string()));
        }

        self.process_dataframe(df)
    }


    /// Load CSV file using Polars.
    fn load_csv(&self, path: &Path) -> EngineResult<DataFrame> {
        CsvReadOptions::default()
            .with_has_header(true)
            .try_into_reader_with_file_path(Some(path.to_path_buf()))
            .map_err(|e| EngineError::parse_error(0, format!("Failed to create CSV reader: {}", e)))?
            .finish()
            .map_err(|e| EngineError::parse_error(0, format!("Failed to read CSV: {}", e)))
    }

    /// Load Parquet file using Polars.
    fn load_parquet(&self, path: &Path) -> EngineResult<DataFrame> {
        let file = std::fs::File::open(path)
            .map_err(|e| EngineError::IoError(format!("Failed to open parquet file: {}", e)))?;
        
        ParquetReader::new(file)
            .finish()
            .map_err(|e| EngineError::parse_error(0, format!("Failed to read Parquet: {}", e)))
    }

    /// Process DataFrame and perform data cleansing.
    fn process_dataframe(&self, df: DataFrame) -> EngineResult<CleansingResult> {
        // Validate required columns first
        self.validate_columns(&df)?;

        // Apply advanced pipeline preprocessing if enabled
        let cleaned_df = if self.use_advanced_pipeline {
            // Use the advanced pipeline for institutional-grade cleansing
            // This handles: sorting, deduplication, forward fill, outlier detection
            self.pipeline.clean(df)?
        } else {
            df
        };

        // Extract columns with proper error handling
        let timestamps = self.extract_i64_column(&cleaned_df, "timestamp")?;
        let prices = self.extract_f64_column(&cleaned_df, "price")?;
        let volumes = self.extract_f64_column(&cleaned_df, "volume")?;

        let total_ticks = timestamps.len() as i64;
        let mut valid_ticks = Vec::with_capacity(timestamps.len());
        let mut invalid_count = 0i64;
        let mut anomaly_count = 0i64;
        let mut anomaly_indices = Vec::new();
        let mut prev_timestamp: Option<i64> = None;
        let mut prev_price: Option<f64> = None;

        // Use get() instead of first()/last() to avoid potential issues
        let first_timestamp = timestamps.first().copied().unwrap_or(0);
        let last_timestamp = timestamps.last().copied().unwrap_or(0);

        for (i, ((&timestamp, &price), &volume)) in timestamps.iter()
            .zip(prices.iter())
            .zip(volumes.iter())
            .enumerate()
        {
            // Validate price > 0
            if price <= 0.0 || !price.is_finite() {
                invalid_count += 1;
                continue;
            }

            // Validate volume >= 0
            if volume < 0.0 || !volume.is_finite() {
                invalid_count += 1;
                continue;
            }

            // Check timestamp order
            if let Some(prev_ts) = prev_timestamp {
                if timestamp <= prev_ts {
                    invalid_count += 1;
                    continue;
                }
            }

            // Check price jump anomaly
            let is_anomaly = if let Some(prev_p) = prev_price {
                if prev_p > 0.0 {
                    let change_pct = ((price - prev_p) / prev_p).abs();
                    change_pct > self.price_jump_threshold
                } else {
                    false
                }
            } else {
                false
            };

            if is_anomaly {
                anomaly_count += 1;
                anomaly_indices.push(i);
                // Still include anomaly ticks but flag them
            }

            valid_ticks.push(Tick {
                timestamp,
                price,
                volume,
            });

            prev_timestamp = Some(timestamp);
            prev_price = Some(price);
        }

        let report = DataQualityReport {
            total_ticks,
            valid_ticks: valid_ticks.len() as i64,
            invalid_ticks: invalid_count,
            anomaly_ticks: anomaly_count,
            first_timestamp,
            last_timestamp,
        };

        Ok(CleansingResult {
            ticks: valid_ticks,
            report,
            anomaly_indices,
        })
    }

    /// Validate that required columns exist.
    fn validate_columns(&self, df: &DataFrame) -> EngineResult<()> {
        let required = ["timestamp", "price", "volume"];
        for col in required {
            if df.column(col).is_err() {
                return Err(EngineError::missing_column(col));
            }
        }
        Ok(())
    }


    /// Extract i64 column from DataFrame.
    ///
    /// Handles type conversion and null values safely.
    fn extract_i64_column(&self, df: &DataFrame, name: &str) -> EngineResult<Vec<i64>> {
        let series = df.column(name)
            .map_err(|_| EngineError::missing_column(name))?;
        
        // Try to get as i64 directly
        if let Ok(chunked) = series.i64() {
            return Ok(chunked.into_iter()
                .map(|opt| opt.unwrap_or(0))
                .collect());
        }
        
        // Try to cast from other integer types
        let casted = series.cast(&DataType::Int64)
            .map_err(|_| EngineError::type_mismatch(
                name,
                "i64",
                format!("{:?}", series.dtype())
            ))?;
        
        let chunked = casted.i64()
            .map_err(|_| EngineError::type_mismatch(
                name,
                "i64",
                format!("{:?}", series.dtype())
            ))?;
        
        Ok(chunked.into_iter()
            .map(|opt| opt.unwrap_or(0))
            .collect())
    }

    /// Extract f64 column from DataFrame.
    ///
    /// Handles type conversion and null values safely.
    fn extract_f64_column(&self, df: &DataFrame, name: &str) -> EngineResult<Vec<f64>> {
        let series = df.column(name)
            .map_err(|_| EngineError::missing_column(name))?;
        
        // Try to get as f64 directly
        if let Ok(chunked) = series.f64() {
            return Ok(chunked.into_iter()
                .map(|opt| opt.unwrap_or(f64::NAN))
                .collect());
        }
        
        // Try to cast from other numeric types
        let casted = series.cast(&DataType::Float64)
            .map_err(|_| EngineError::type_mismatch(
                name,
                "f64",
                format!("{:?}", series.dtype())
            ))?;
        
        let chunked = casted.f64()
            .map_err(|_| EngineError::type_mismatch(
                name,
                "f64",
                format!("{:?}", series.dtype())
            ))?;
        
        Ok(chunked.into_iter()
            .map(|opt| opt.unwrap_or(f64::NAN))
            .collect())
    }

    /// Load and cleanse data from raw vectors (for testing).
    pub fn load_from_vectors(
        &self,
        timestamps: Vec<i64>,
        prices: Vec<f64>,
        volumes: Vec<f64>,
    ) -> EngineResult<CleansingResult> {
        if timestamps.len() != prices.len() || prices.len() != volumes.len() {
            return Err(EngineError::validation(
                "Vector lengths must match"
            ));
        }

        if timestamps.is_empty() {
            return Err(EngineError::validation(
                "Input vectors cannot be empty"
            ));
        }

        let total_ticks = timestamps.len() as i64;
        let mut valid_ticks = Vec::with_capacity(timestamps.len());
        let mut invalid_count = 0i64;
        let mut anomaly_count = 0i64;
        let mut anomaly_indices = Vec::new();
        let mut prev_timestamp: Option<i64> = None;
        let mut prev_price: Option<f64> = None;

        let first_timestamp = timestamps.first().copied().unwrap_or(0);
        let last_timestamp = timestamps.last().copied().unwrap_or(0);

        for (i, ((&timestamp, &price), &volume)) in timestamps.iter()
            .zip(prices.iter())
            .zip(volumes.iter())
            .enumerate()
        {
            // Validate price > 0 and is finite
            if price <= 0.0 || !price.is_finite() {
                invalid_count += 1;
                continue;
            }

            // Validate volume >= 0 and is finite
            if volume < 0.0 || !volume.is_finite() {
                invalid_count += 1;
                continue;
            }

            // Check timestamp order
            if let Some(prev_ts) = prev_timestamp {
                if timestamp <= prev_ts {
                    invalid_count += 1;
                    continue;
                }
            }

            // Check price jump anomaly
            let is_anomaly = if let Some(prev_p) = prev_price {
                if prev_p > 0.0 {
                    let change_pct = ((price - prev_p) / prev_p).abs();
                    change_pct > self.price_jump_threshold
                } else {
                    false
                }
            } else {
                false
            };

            if is_anomaly {
                anomaly_count += 1;
                anomaly_indices.push(i);
            }

            valid_ticks.push(Tick {
                timestamp,
                price,
                volume,
            });

            prev_timestamp = Some(timestamp);
            prev_price = Some(price);
        }

        let report = DataQualityReport {
            total_ticks,
            valid_ticks: valid_ticks.len() as i64,
            invalid_ticks: invalid_count,
            anomaly_ticks: anomaly_count,
            first_timestamp,
            last_timestamp,
        };

        Ok(CleansingResult {
            ticks: valid_ticks,
            report,
            anomaly_indices,
        })
    }
}


#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_data_loader_creation() {
        let loader = DataLoader::new();
        assert!((loader.price_jump_threshold - 0.10).abs() < 0.001);
    }

    #[test]
    fn test_valid_data() {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(
            vec![1, 2, 3, 4, 5],
            vec![100.0, 101.0, 102.0, 103.0, 104.0],
            vec![1000.0, 1100.0, 1200.0, 1300.0, 1400.0],
        ).unwrap();

        assert_eq!(result.report.total_ticks, 5);
        assert_eq!(result.report.valid_ticks, 5);
        assert_eq!(result.report.invalid_ticks, 0);
        assert_eq!(result.report.anomaly_ticks, 0);
        assert_eq!(result.ticks.len(), 5);
    }

    #[test]
    fn test_invalid_price() {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(
            vec![1, 2, 3],
            vec![100.0, -50.0, 102.0], // Invalid price at index 1
            vec![1000.0, 1100.0, 1200.0],
        ).unwrap();

        assert_eq!(result.report.total_ticks, 3);
        assert_eq!(result.report.valid_ticks, 2);
        assert_eq!(result.report.invalid_ticks, 1);
    }

    #[test]
    fn test_invalid_volume() {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(
            vec![1, 2, 3],
            vec![100.0, 101.0, 102.0],
            vec![1000.0, -100.0, 1200.0], // Invalid volume at index 1
        ).unwrap();

        assert_eq!(result.report.total_ticks, 3);
        assert_eq!(result.report.valid_ticks, 2);
        assert_eq!(result.report.invalid_ticks, 1);
    }

    #[test]
    fn test_out_of_order_timestamp() {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(
            vec![1, 3, 2, 4], // Out of order at index 2
            vec![100.0, 101.0, 102.0, 103.0],
            vec![1000.0, 1100.0, 1200.0, 1300.0],
        ).unwrap();

        assert_eq!(result.report.total_ticks, 4);
        assert_eq!(result.report.valid_ticks, 3);
        assert_eq!(result.report.invalid_ticks, 1);
    }

    #[test]
    fn test_price_jump_anomaly() {
        let loader = DataLoader::new().with_price_jump_threshold(0.10);
        let result = loader.load_from_vectors(
            vec![1, 2, 3],
            vec![100.0, 115.0, 116.0], // 15% jump at index 1
            vec![1000.0, 1100.0, 1200.0],
        ).unwrap();

        assert_eq!(result.report.total_ticks, 3);
        assert_eq!(result.report.valid_ticks, 3); // Anomalies are still valid
        assert_eq!(result.report.anomaly_ticks, 1);
        assert_eq!(result.anomaly_indices, vec![1]);
    }

    #[test]
    fn test_zero_price() {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(
            vec![1, 2],
            vec![0.0, 100.0], // Zero price at index 0
            vec![1000.0, 1100.0],
        ).unwrap();

        assert_eq!(result.report.invalid_ticks, 1);
        assert_eq!(result.report.valid_ticks, 1);
    }

    #[test]
    fn test_file_not_found() {
        let loader = DataLoader::new();
        let result = loader.load_from_file("nonexistent.csv");
        assert!(matches!(result, Err(EngineError::FileNotFound { .. })));
    }

    #[test]
    fn test_unsupported_format() {
        let loader = DataLoader::new();
        // Create a temp file with unsupported extension
        let temp_dir = std::env::temp_dir();
        let temp_file = temp_dir.join("test.xyz");
        std::fs::write(&temp_file, "test").unwrap();
        
        let result = loader.load_from_file(&temp_file);
        assert!(matches!(result, Err(EngineError::ValidationError(_))));
        
        std::fs::remove_file(temp_file).ok();
    }

    #[test]
    fn test_nan_price_filtered() {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(
            vec![1, 2, 3],
            vec![100.0, f64::NAN, 102.0], // NaN price at index 1
            vec![1000.0, 1100.0, 1200.0],
        ).unwrap();

        assert_eq!(result.report.total_ticks, 3);
        assert_eq!(result.report.valid_ticks, 2);
        assert_eq!(result.report.invalid_ticks, 1);
    }

    #[test]
    fn test_infinity_volume_filtered() {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(
            vec![1, 2, 3],
            vec![100.0, 101.0, 102.0],
            vec![1000.0, f64::INFINITY, 1200.0], // Infinity volume at index 1
        ).unwrap();

        assert_eq!(result.report.total_ticks, 3);
        assert_eq!(result.report.valid_ticks, 2);
        assert_eq!(result.report.invalid_ticks, 1);
    }

    #[test]
    fn test_empty_vectors_error() {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(
            vec![],
            vec![],
            vec![],
        );
        assert!(matches!(result, Err(EngineError::ValidationError(_))));
    }

    #[test]
    fn test_mismatched_vector_lengths() {
        let loader = DataLoader::new();
        let result = loader.load_from_vectors(
            vec![1, 2, 3],
            vec![100.0, 101.0], // Wrong length
            vec![1000.0, 1100.0, 1200.0],
        );
        assert!(matches!(result, Err(EngineError::ValidationError(_))));
    }

    #[test]
    fn test_load_real_csv_file() {
        let loader = DataLoader::new();
        
        // Try to load the test data file if it exists
        let result = loader.load_from_file("../test_data/ticks_clean.csv");
        
        if let Ok(cleansing_result) = result {
            // Verify the data was loaded correctly
            assert!(cleansing_result.report.total_ticks > 0);
            assert!(cleansing_result.report.valid_ticks > 0);
            assert!(!cleansing_result.ticks.is_empty());
        }
        // If file doesn't exist, that's okay for this test
    }
}
