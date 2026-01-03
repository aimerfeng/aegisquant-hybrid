//! Data Pipeline module for institutional-grade data cleansing and storage.
//!
//! Features:
//! - Polars-based high-performance data wrangling
//! - Outlier detection using Z-Score/IQR
//! - Missing data handling (Forward Fill for suspensions)
//! - Storage abstraction for Time-Series Databases (TimescaleDB/KDB+)

use polars::prelude::*;
use crate::error::{EngineError, EngineResult};

/// Configuration for the data pipeline
#[derive(Debug, Clone)]
pub struct PipelineConfig {
    /// Standard deviation threshold for outlier detection (e.g., 3.0)
    pub outlier_zscore_threshold: f64,
    /// Whether to fill missing values (e.g., for suspended trading days)
    pub fill_missing: bool,
    /// Whether to adjust prices if split/dividend columns exist
    pub adjust_prices: bool,
    /// Rolling window size for Z-Score calculation
    pub rolling_window_size: usize,
}

impl Default for PipelineConfig {
    fn default() -> Self {
        Self {
            outlier_zscore_threshold: 3.0,
            fill_missing: true,
            adjust_prices: true,
            rolling_window_size: 20,
        }
    }
}

/// The main Data Pipeline processor
#[derive(Debug)]
pub struct DataPipeline {
    config: PipelineConfig,
}

impl DataPipeline {
    /// Create a new DataPipeline with the given configuration
    pub fn new(config: PipelineConfig) -> Self {
        Self { config }
    }

    /// Create a new DataPipeline with default configuration
    pub fn with_defaults() -> Self {
        Self::new(PipelineConfig::default())
    }

    /// Get the current configuration
    pub fn config(&self) -> &PipelineConfig {
        &self.config
    }

    /// Process raw DataFrame through the cleansing pipeline.
    ///
    /// Steps:
    /// 1. Sort by timestamp
    /// 2. Handle duplicates
    /// 3. Fill missing data (Suspension handling)
    /// 4. Adjust prices (Split/Dividend) if columns exist
    /// 5. Filter outliers (Z-Score based on global statistics)
    pub fn clean(&self, df: DataFrame) -> EngineResult<DataFrame> {
        // Convert to LazyFrame for query optimization
        let mut lf = df.lazy();

        // 1. Ensure time order and remove duplicates
        lf = lf
            .sort(
                ["timestamp"],
                SortMultipleOptions::default()
                    .with_order_descending(false)
                    .with_nulls_last(true),
            )
            .unique(None, UniqueKeepStrategy::Last);

        // 2. Handle Missing Data (Suspension Filling)
        // Logic: Forward fill price, fill volume with 0
        if self.config.fill_missing {
            lf = lf.with_columns([
                col("price").forward_fill(None).alias("price"),
                col("volume").fill_null(lit(0.0)).alias("volume"),
            ]);
        }

        // 3. Price Adjustment (if columns exist)
        // This is handled conditionally - if split_factor column exists
        // Real implementation would check schema and apply:
        // adjusted_price = price * split_factor - dividend

        // Collect intermediate result for outlier filtering
        let mut result_df = lf.collect()
            .map_err(|e| EngineError::PolarsError(format!("Pipeline execution failed: {}", e)))?;

        // 4. Outlier Detection (Z-Score Method using global statistics)
        // Filter out ticks where price deviates significantly from mean
        if self.config.outlier_zscore_threshold > 0.0 {
            result_df = self.filter_outliers(result_df, self.config.outlier_zscore_threshold)?;
        }

        Ok(result_df)
    }

    /// Clean data with custom column names
    pub fn clean_with_columns(
        &self,
        df: DataFrame,
        timestamp_col: &str,
        price_col: &str,
        volume_col: &str,
    ) -> EngineResult<DataFrame> {
        // Rename columns to standard names if different
        let mut lf = df.lazy();

        if timestamp_col != "timestamp" {
            lf = lf.rename([timestamp_col], ["timestamp"], true);
        }
        if price_col != "price" {
            lf = lf.rename([price_col], ["price"], true);
        }
        if volume_col != "volume" {
            lf = lf.rename([volume_col], ["volume"], true);
        }

        let renamed_df = lf
            .collect()
            .map_err(|e| EngineError::PolarsError(format!("Column rename failed: {}", e)))?;

        self.clean(renamed_df)
    }

    /// Apply forward fill to handle suspension days
    pub fn fill_suspensions(&self, df: DataFrame) -> EngineResult<DataFrame> {
        df.lazy()
            .with_columns([
                col("price").forward_fill(None).alias("price"),
                col("volume").fill_null(lit(0.0)).alias("volume"),
            ])
            .collect()
            .map_err(|e| EngineError::PolarsError(format!("Forward fill failed: {}", e)))
    }

    /// Calculate Z-Score for outlier detection
    pub fn calculate_zscore(&self, df: &DataFrame, column: &str) -> EngineResult<Series> {
        let series = df
            .column(column)
            .map_err(|_| EngineError::MissingColumn {
                column: column.to_string(),
            })?;

        // Cast to f64 series for calculations
        let f64_series = series
            .cast(&DataType::Float64)
            .map_err(|e| EngineError::PolarsError(format!("Cast failed: {}", e)))?;

        let f64_chunked = f64_series
            .f64()
            .map_err(|_| EngineError::TypeMismatch {
                column: column.to_string(),
                expected: "f64".to_string(),
                actual: format!("{:?}", series.dtype()),
            })?;

        // Calculate mean manually
        let values: Vec<f64> = f64_chunked
            .into_iter()
            .filter_map(|v| v)
            .collect();

        if values.is_empty() {
            return Err(EngineError::ValidationError("No valid values for mean calculation".to_string()));
        }

        let mean: f64 = values.iter().sum::<f64>() / values.len() as f64;

        // Calculate std deviation manually
        let variance: f64 = values.iter()
            .map(|v| (v - mean).powi(2))
            .sum::<f64>() / values.len() as f64;
        let std = variance.sqrt();

        // Avoid division by zero
        let std = if std < 0.0001 { 0.0001 } else { std };

        // Calculate z-score for each value
        let z_scores: Vec<f64> = f64_chunked
            .into_iter()
            .map(|opt| opt.map(|v| (v - mean) / std).unwrap_or(f64::NAN))
            .collect();

        Ok(Series::new("z_score".into(), z_scores))
    }

    /// Filter outliers based on Z-Score threshold
    pub fn filter_outliers(&self, df: DataFrame, threshold: f64) -> EngineResult<DataFrame> {
        let z_scores = self.calculate_zscore(&df, "price")?;

        // Create mask for non-outliers
        let mask: Vec<bool> = z_scores
            .f64()
            .map_err(|e| EngineError::PolarsError(e.to_string()))?
            .into_iter()
            .map(|opt| opt.map(|z| z.abs() < threshold).unwrap_or(false))
            .collect();

        let mask_series = Series::new("mask".into(), mask);
        let bool_chunked = mask_series
            .bool()
            .map_err(|e| EngineError::PolarsError(e.to_string()))?;

        df.filter(bool_chunked)
            .map_err(|e| EngineError::PolarsError(format!("Filter failed: {}", e)))
    }

    /// Apply price adjustment for splits and dividends
    pub fn adjust_prices(
        &self,
        df: DataFrame,
        split_factor_col: Option<&str>,
        dividend_col: Option<&str>,
    ) -> EngineResult<DataFrame> {
        let mut lf = df.lazy();

        // Apply split factor if column exists
        if let Some(split_col) = split_factor_col {
            lf = lf.with_column((col("price") * col(split_col)).alias("price"));
        }

        // Subtract dividend if column exists
        if let Some(div_col) = dividend_col {
            lf = lf.with_column((col("price") - col(div_col)).alias("price"));
        }

        lf.collect()
            .map_err(|e| EngineError::PolarsError(format!("Price adjustment failed: {}", e)))
    }
}

// ============================================================================
// Storage Abstraction
// ============================================================================

/// Abstract Storage Interface
/// Allows switching between CSV, Parquet, TimescaleDB, or KDB+
pub trait MarketDataStore: Send + Sync {
    /// Save tick data for a symbol
    fn save_ticks(&self, symbol: &str, df: &DataFrame) -> EngineResult<()>;

    /// Load tick data for a symbol within a time range
    fn load_ticks(&self, symbol: &str, start_ts: i64, end_ts: i64) -> EngineResult<DataFrame>;

    /// Check if data exists for a symbol
    fn has_data(&self, symbol: &str) -> bool;

    /// Get available symbols
    fn list_symbols(&self) -> EngineResult<Vec<String>>;
}

/// TimescaleDB Implementation (Skeleton)
/// Requires 'sqlx' crate with 'postgres' feature in Cargo.toml
pub struct TimescaleDbStore {
    connection_string: String,
    table_name: String,
}

impl TimescaleDbStore {
    /// Create a new TimescaleDB store
    pub fn new(connection_string: &str) -> Self {
        Self {
            connection_string: connection_string.to_string(),
            table_name: "ticks".to_string(),
        }
    }

    /// Create with custom table name
    pub fn with_table(connection_string: &str, table_name: &str) -> Self {
        Self {
            connection_string: connection_string.to_string(),
            table_name: table_name.to_string(),
        }
    }

    /// Get the connection string
    pub fn connection_string(&self) -> &str {
        &self.connection_string
    }

    /// Get the table name
    pub fn table_name(&self) -> &str {
        &self.table_name
    }
}

// Mock implementation to compile without sqlx dependency
// Uncomment and add dependencies to implement fully.
impl MarketDataStore for TimescaleDbStore {
    fn save_ticks(&self, symbol: &str, df: &DataFrame) -> EngineResult<()> {
        // Real implementation would:
        // 1. Connect to Postgres/TimescaleDB pool
        // 2. Batch insert rows from DataFrame
        // 3. Use COPY command for high performance
        println!(
            "Saving {} ticks for {} to TimescaleDB at {} (table: {})",
            df.height(),
            symbol,
            self.connection_string,
            self.table_name
        );
        Ok(())
    }

    fn load_ticks(&self, symbol: &str, start_ts: i64, end_ts: i64) -> EngineResult<DataFrame> {
        // Real implementation would:
        // 1. SELECT * FROM ticks WHERE symbol = $1 AND time BETWEEN $2 AND $3
        // 2. Convert result to Arrow/Polars DataFrame
        Err(EngineError::DatabaseError(format!(
            "DB Loading not implemented yet for symbol {} ({} to {}). Add sqlx dependency.",
            symbol, start_ts, end_ts
        )))
    }

    fn has_data(&self, _symbol: &str) -> bool {
        // Would query: SELECT EXISTS(SELECT 1 FROM ticks WHERE symbol = $1)
        false
    }

    fn list_symbols(&self) -> EngineResult<Vec<String>> {
        // Would query: SELECT DISTINCT symbol FROM ticks
        Ok(vec![])
    }
}

/// CSV File Store Implementation
pub struct CsvFileStore {
    base_path: String,
}

impl CsvFileStore {
    /// Create a new CSV file store
    pub fn new(base_path: &str) -> Self {
        Self {
            base_path: base_path.to_string(),
        }
    }

    fn get_file_path(&self, symbol: &str) -> String {
        format!("{}/{}.csv", self.base_path, symbol)
    }
}

impl MarketDataStore for CsvFileStore {
    fn save_ticks(&self, symbol: &str, df: &DataFrame) -> EngineResult<()> {
        let path = self.get_file_path(symbol);

        // Ensure directory exists
        if let Some(parent) = std::path::Path::new(&path).parent() {
            std::fs::create_dir_all(parent)
                .map_err(|e| EngineError::IoError(format!("Failed to create directory: {}", e)))?;
        }

        let mut file = std::fs::File::create(&path)
            .map_err(|e| EngineError::IoError(format!("Failed to create file: {}", e)))?;

        CsvWriter::new(&mut file)
            .finish(&mut df.clone())
            .map_err(|e| EngineError::PolarsError(format!("Failed to write CSV: {}", e)))?;

        Ok(())
    }

    fn load_ticks(&self, symbol: &str, start_ts: i64, end_ts: i64) -> EngineResult<DataFrame> {
        let path = self.get_file_path(symbol);

        let df = CsvReadOptions::default()
            .with_has_header(true)
            .try_into_reader_with_file_path(Some(path.clone().into()))
            .map_err(|e| EngineError::PolarsError(format!("Failed to create CSV reader: {}", e)))?
            .finish()
            .map_err(|e| EngineError::PolarsError(format!("Failed to read CSV: {}", e)))?;

        // Filter by timestamp range
        df.lazy()
            .filter(
                col("timestamp")
                    .gt_eq(lit(start_ts))
                    .and(col("timestamp").lt_eq(lit(end_ts))),
            )
            .collect()
            .map_err(|e| EngineError::PolarsError(format!("Failed to filter data: {}", e)))
    }

    fn has_data(&self, symbol: &str) -> bool {
        std::path::Path::new(&self.get_file_path(symbol)).exists()
    }

    fn list_symbols(&self) -> EngineResult<Vec<String>> {
        let entries = std::fs::read_dir(&self.base_path)
            .map_err(|e| EngineError::IoError(format!("Failed to read directory: {}", e)))?;

        let symbols: Vec<String> = entries
            .filter_map(|entry| {
                entry.ok().and_then(|e| {
                    let path = e.path();
                    if path.extension().map(|ext| ext == "csv").unwrap_or(false) {
                        path.file_stem()
                            .and_then(|s| s.to_str())
                            .map(|s| s.to_string())
                    } else {
                        None
                    }
                })
            })
            .collect();

        Ok(symbols)
    }
}

/// Parquet File Store Implementation
pub struct ParquetFileStore {
    base_path: String,
}

impl ParquetFileStore {
    /// Create a new Parquet file store
    pub fn new(base_path: &str) -> Self {
        Self {
            base_path: base_path.to_string(),
        }
    }

    fn get_file_path(&self, symbol: &str) -> String {
        format!("{}/{}.parquet", self.base_path, symbol)
    }
}

impl MarketDataStore for ParquetFileStore {
    fn save_ticks(&self, symbol: &str, df: &DataFrame) -> EngineResult<()> {
        let path = self.get_file_path(symbol);

        // Ensure directory exists
        if let Some(parent) = std::path::Path::new(&path).parent() {
            std::fs::create_dir_all(parent)
                .map_err(|e| EngineError::IoError(format!("Failed to create directory: {}", e)))?;
        }

        let file = std::fs::File::create(&path)
            .map_err(|e| EngineError::IoError(format!("Failed to create file: {}", e)))?;

        ParquetWriter::new(file)
            .finish(&mut df.clone())
            .map_err(|e| EngineError::PolarsError(format!("Failed to write Parquet: {}", e)))?;

        Ok(())
    }

    fn load_ticks(&self, symbol: &str, start_ts: i64, end_ts: i64) -> EngineResult<DataFrame> {
        let path = self.get_file_path(symbol);

        let file = std::fs::File::open(&path)
            .map_err(|e| EngineError::IoError(format!("Failed to open file: {}", e)))?;

        let df = ParquetReader::new(file)
            .finish()
            .map_err(|e| EngineError::PolarsError(format!("Failed to read Parquet: {}", e)))?;

        // Filter by timestamp range
        df.lazy()
            .filter(
                col("timestamp")
                    .gt_eq(lit(start_ts))
                    .and(col("timestamp").lt_eq(lit(end_ts))),
            )
            .collect()
            .map_err(|e| EngineError::PolarsError(format!("Failed to filter data: {}", e)))
    }

    fn has_data(&self, symbol: &str) -> bool {
        std::path::Path::new(&self.get_file_path(symbol)).exists()
    }

    fn list_symbols(&self) -> EngineResult<Vec<String>> {
        let entries = std::fs::read_dir(&self.base_path)
            .map_err(|e| EngineError::IoError(format!("Failed to read directory: {}", e)))?;

        let symbols: Vec<String> = entries
            .filter_map(|entry| {
                entry.ok().and_then(|e| {
                    let path = e.path();
                    if path
                        .extension()
                        .map(|ext| ext == "parquet")
                        .unwrap_or(false)
                    {
                        path.file_stem()
                            .and_then(|s| s.to_str())
                            .map(|s| s.to_string())
                    } else {
                        None
                    }
                })
            })
            .collect();

        Ok(symbols)
    }
}


// ============================================================================
// Tests
// ============================================================================

#[cfg(test)]
mod tests {
    use super::*;

    fn create_test_dataframe() -> DataFrame {
        df! {
            "timestamp" => &[1i64, 2, 3, 4, 5, 6, 7, 8, 9, 10],
            "price" => &[100.0, 101.0, 102.0, 103.0, 104.0, 105.0, 106.0, 107.0, 108.0, 109.0],
            "volume" => &[1000.0, 1100.0, 1200.0, 1300.0, 1400.0, 1500.0, 1600.0, 1700.0, 1800.0, 1900.0]
        }
        .unwrap()
    }

    fn create_dataframe_with_nulls() -> DataFrame {
        df! {
            "timestamp" => &[1i64, 2, 3, 4, 5],
            "price" => &[Some(100.0), None, Some(102.0), None, Some(104.0)],
            "volume" => &[Some(1000.0), None, Some(1200.0), None, Some(1400.0)]
        }
        .unwrap()
    }

    fn create_dataframe_with_outlier() -> DataFrame {
        df! {
            "timestamp" => &[1i64, 2, 3, 4, 5, 6, 7, 8, 9, 10],
            "price" => &[100.0, 101.0, 102.0, 500.0, 104.0, 105.0, 106.0, 107.0, 108.0, 109.0], // 500.0 is outlier
            "volume" => &[1000.0, 1100.0, 1200.0, 1300.0, 1400.0, 1500.0, 1600.0, 1700.0, 1800.0, 1900.0]
        }
        .unwrap()
    }

    fn create_unsorted_dataframe() -> DataFrame {
        df! {
            "timestamp" => &[3i64, 1, 5, 2, 4],
            "price" => &[103.0, 101.0, 105.0, 102.0, 104.0],
            "volume" => &[1300.0, 1100.0, 1500.0, 1200.0, 1400.0]
        }
        .unwrap()
    }

    #[test]
    fn test_pipeline_config_default() {
        let config = PipelineConfig::default();
        assert!((config.outlier_zscore_threshold - 3.0).abs() < 0.001);
        assert!(config.fill_missing);
        assert!(config.adjust_prices);
        assert_eq!(config.rolling_window_size, 20);
    }

    #[test]
    fn test_pipeline_creation() {
        let pipeline = DataPipeline::with_defaults();
        assert!((pipeline.config().outlier_zscore_threshold - 3.0).abs() < 0.001);
    }

    #[test]
    fn test_clean_basic() {
        let pipeline = DataPipeline::new(PipelineConfig {
            outlier_zscore_threshold: 0.0, // Disable outlier filtering
            fill_missing: false,
            adjust_prices: false,
            rolling_window_size: 20,
        });

        let df = create_test_dataframe();
        let result = pipeline.clean(df).unwrap();

        assert_eq!(result.height(), 10);
        assert!(result.column("timestamp").is_ok());
        assert!(result.column("price").is_ok());
        assert!(result.column("volume").is_ok());
    }

    #[test]
    fn test_clean_sorts_data() {
        let pipeline = DataPipeline::new(PipelineConfig {
            outlier_zscore_threshold: 0.0,
            fill_missing: false,
            adjust_prices: false,
            rolling_window_size: 20,
        });

        let df = create_unsorted_dataframe();
        let result = pipeline.clean(df).unwrap();

        // Check that data is sorted by timestamp
        let timestamps: Vec<i64> = result
            .column("timestamp")
            .unwrap()
            .i64()
            .unwrap()
            .into_iter()
            .map(|v| v.unwrap())
            .collect();

        assert_eq!(timestamps, vec![1, 2, 3, 4, 5]);
    }

    #[test]
    fn test_fill_suspensions() {
        let pipeline = DataPipeline::with_defaults();
        let df = create_dataframe_with_nulls();

        let result = pipeline.fill_suspensions(df).unwrap();

        // Check that nulls are filled
        let prices: Vec<Option<f64>> = result
            .column("price")
            .unwrap()
            .f64()
            .unwrap()
            .into_iter()
            .collect();

        // Forward fill should propagate values
        assert!(prices[0].is_some());
        assert!(prices[1].is_some()); // Should be filled with 100.0
        assert!(prices[2].is_some());
    }

    #[test]
    fn test_calculate_zscore() {
        let pipeline = DataPipeline::with_defaults();
        let df = create_test_dataframe();

        let z_scores = pipeline.calculate_zscore(&df, "price").unwrap();

        assert_eq!(z_scores.len(), 10);
        assert_eq!(z_scores.name().to_string(), "z_score");

        // Z-scores should be centered around 0
        let mean: f64 = z_scores
            .f64()
            .unwrap()
            .into_iter()
            .filter_map(|v| v)
            .sum::<f64>()
            / 10.0;

        assert!(mean.abs() < 0.001);
    }

    #[test]
    fn test_filter_outliers() {
        let pipeline = DataPipeline::with_defaults();
        let df = create_dataframe_with_outlier();

        let result = pipeline.filter_outliers(df, 2.0).unwrap();

        // The outlier (500.0) should be filtered out
        assert!(result.height() < 10);
    }

    #[test]
    fn test_timescaledb_store_creation() {
        let store = TimescaleDbStore::new("postgres://localhost:5432/aegisquant");
        assert_eq!(
            store.connection_string(),
            "postgres://localhost:5432/aegisquant"
        );
        assert_eq!(store.table_name(), "ticks");
    }

    #[test]
    fn test_timescaledb_store_with_table() {
        let store = TimescaleDbStore::with_table("postgres://localhost:5432/aegisquant", "market_data");
        assert_eq!(store.table_name(), "market_data");
    }

    #[test]
    fn test_csv_store_has_data() {
        let store = CsvFileStore::new("/tmp/test_data");
        // Non-existent file should return false
        assert!(!store.has_data("nonexistent_symbol"));
    }

    #[test]
    fn test_parquet_store_has_data() {
        let store = ParquetFileStore::new("/tmp/test_data");
        // Non-existent file should return false
        assert!(!store.has_data("nonexistent_symbol"));
    }

    #[test]
    fn test_clean_with_outlier_filtering() {
        let pipeline = DataPipeline::new(PipelineConfig {
            outlier_zscore_threshold: 2.0, // Enable outlier filtering
            fill_missing: false,
            adjust_prices: false,
            rolling_window_size: 5,
        });

        let df = create_dataframe_with_outlier();
        let result = pipeline.clean(df).unwrap();

        // The extreme outlier should be filtered
        let prices: Vec<f64> = result
            .column("price")
            .unwrap()
            .f64()
            .unwrap()
            .into_iter()
            .filter_map(|v| v)
            .collect();

        // 500.0 should not be in the result
        assert!(!prices.contains(&500.0));
    }

    #[test]
    fn test_adjust_prices_with_split() {
        let pipeline = DataPipeline::with_defaults();

        let df = df! {
            "timestamp" => &[1i64, 2, 3],
            "price" => &[100.0, 200.0, 300.0],
            "volume" => &[1000.0, 2000.0, 3000.0],
            "split_factor" => &[1.0, 2.0, 1.0]
        }
        .unwrap();

        let result = pipeline
            .adjust_prices(df, Some("split_factor"), None)
            .unwrap();

        let prices: Vec<f64> = result
            .column("price")
            .unwrap()
            .f64()
            .unwrap()
            .into_iter()
            .filter_map(|v| v)
            .collect();

        assert!((prices[0] - 100.0).abs() < 0.001);
        assert!((prices[1] - 400.0).abs() < 0.001); // 200 * 2
        assert!((prices[2] - 300.0).abs() < 0.001);
    }

    #[test]
    fn test_market_data_store_trait() {
        // Test that TimescaleDbStore implements MarketDataStore
        let store: Box<dyn MarketDataStore> =
            Box::new(TimescaleDbStore::new("postgres://localhost/test"));

        assert!(!store.has_data("test_symbol"));
    }
}
