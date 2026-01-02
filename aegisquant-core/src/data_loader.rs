//! Data loading and cleansing module using Polars.
//!
//! Provides high-performance data loading from CSV and Parquet files,
//! with built-in data validation and quality reporting.

use polars::prelude::*;
use std::path::Path;
use thiserror::Error;

use crate::types::{DataQualityReport, Tick};

/// Data loading error types.
#[derive(Debug, Error)]
pub enum DataLoaderError {
    #[error("File not found: {0}")]
    FileNotFound(String),

    #[error("Unsupported file format: {0}")]
    UnsupportedFormat(String),

    #[error("Failed to read file: {0}")]
    ReadError(String),

    #[error("Missing required column: {0}")]
    MissingColumn(String),

    #[error("Data validation error: {0}")]
    ValidationError(String),

    #[error("Polars error: {0}")]
    PolarsError(#[from] PolarsError),
}

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
#[derive(Debug, Default)]
pub struct DataLoader {
    /// Price jump threshold (default 10%)
    price_jump_threshold: f64,
}

impl DataLoader {
    /// Create a new DataLoader with default settings.
    pub fn new() -> Self {
        Self {
            price_jump_threshold: 0.10, // 10% price jump threshold
        }
    }

    /// Set the price jump threshold for anomaly detection.
    pub fn with_price_jump_threshold(mut self, threshold: f64) -> Self {
        self.price_jump_threshold = threshold;
        self
    }

    /// Load data from a file (CSV or Parquet).
    ///
    /// # Arguments
    /// * `path` - Path to the data file
    ///
    /// # Returns
    /// * `Ok(CleansingResult)` - Cleansed data with quality report
    /// * `Err(DataLoaderError)` - If loading or validation fails
    pub fn load_from_file<P: AsRef<Path>>(&self, path: P) -> Result<CleansingResult, DataLoaderError> {
        let path = path.as_ref();
        
        if !path.exists() {
            return Err(DataLoaderError::FileNotFound(path.display().to_string()));
        }

        let extension = path.extension()
            .and_then(|e| e.to_str())
            .unwrap_or("");

        let df = match extension.to_lowercase().as_str() {
            "csv" => self.load_csv(path)?,
            "parquet" => self.load_parquet(path)?,
            _ => return Err(DataLoaderError::UnsupportedFormat(extension.to_string())),
        };

        self.process_dataframe(df)
    }

    /// Load CSV file using Polars.
    fn load_csv(&self, path: &Path) -> Result<DataFrame, DataLoaderError> {
        CsvReadOptions::default()
            .with_has_header(true)
            .try_into_reader_with_file_path(Some(path.to_path_buf()))
            .map_err(|e| DataLoaderError::ReadError(e.to_string()))?
            .finish()
            .map_err(DataLoaderError::from)
    }

    /// Load Parquet file using Polars.
    fn load_parquet(&self, path: &Path) -> Result<DataFrame, DataLoaderError> {
        let file = std::fs::File::open(path)
            .map_err(|e| DataLoaderError::ReadError(e.to_string()))?;
        
        ParquetReader::new(file)
            .finish()
            .map_err(DataLoaderError::from)
    }

    /// Process DataFrame and perform data cleansing.
    fn process_dataframe(&self, df: DataFrame) -> Result<CleansingResult, DataLoaderError> {
        // Validate required columns
        self.validate_columns(&df)?;

        // Extract columns
        let timestamps = self.extract_i64_column(&df, "timestamp")?;
        let prices = self.extract_f64_column(&df, "price")?;
        let volumes = self.extract_f64_column(&df, "volume")?;

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
            // Validate price > 0
            if price <= 0.0 {
                invalid_count += 1;
                continue;
            }

            // Validate volume >= 0
            if volume < 0.0 {
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
                let change_pct = ((price - prev_p) / prev_p).abs();
                change_pct > self.price_jump_threshold
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
    fn validate_columns(&self, df: &DataFrame) -> Result<(), DataLoaderError> {
        let required = ["timestamp", "price", "volume"];
        for col in required {
            if df.column(col).is_err() {
                return Err(DataLoaderError::MissingColumn(col.to_string()));
            }
        }
        Ok(())
    }

    /// Extract i64 column from DataFrame.
    fn extract_i64_column(&self, df: &DataFrame, name: &str) -> Result<Vec<i64>, DataLoaderError> {
        let series = df.column(name)
            .map_err(|_| DataLoaderError::MissingColumn(name.to_string()))?;
        
        let chunked = series.i64()
            .map_err(|_| DataLoaderError::ValidationError(
                format!("Column '{}' is not i64 type", name)
            ))?;
        
        Ok(chunked.into_iter()
            .map(|opt| opt.unwrap_or(0))
            .collect())
    }

    /// Extract f64 column from DataFrame.
    fn extract_f64_column(&self, df: &DataFrame, name: &str) -> Result<Vec<f64>, DataLoaderError> {
        let series = df.column(name)
            .map_err(|_| DataLoaderError::MissingColumn(name.to_string()))?;
        
        let chunked = series.f64()
            .map_err(|_| DataLoaderError::ValidationError(
                format!("Column '{}' is not f64 type", name)
            ))?;
        
        Ok(chunked.into_iter()
            .map(|opt| opt.unwrap_or(0.0))
            .collect())
    }

    /// Load and cleanse data from raw vectors (for testing).
    pub fn load_from_vectors(
        &self,
        timestamps: Vec<i64>,
        prices: Vec<f64>,
        volumes: Vec<f64>,
    ) -> Result<CleansingResult, DataLoaderError> {
        if timestamps.len() != prices.len() || prices.len() != volumes.len() {
            return Err(DataLoaderError::ValidationError(
                "Vector lengths must match".to_string()
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
            // Validate price > 0
            if price <= 0.0 {
                invalid_count += 1;
                continue;
            }

            // Validate volume >= 0
            if volume < 0.0 {
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
                let change_pct = ((price - prev_p) / prev_p).abs();
                change_pct > self.price_jump_threshold
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
        assert!(matches!(result, Err(DataLoaderError::FileNotFound(_))));
    }

    #[test]
    fn test_unsupported_format() {
        let loader = DataLoader::new();
        // Create a temp file with unsupported extension
        let temp_dir = std::env::temp_dir();
        let temp_file = temp_dir.join("test.xyz");
        std::fs::write(&temp_file, "test").unwrap();
        
        let result = loader.load_from_file(&temp_file);
        assert!(matches!(result, Err(DataLoaderError::UnsupportedFormat(_))));
        
        std::fs::remove_file(temp_file).ok();
    }
}
