//! Error Handling Module
//!
//! Provides structured error types for the AegisQuant engine.
//! Uses `thiserror` for ergonomic error definitions and implements
//! conversion to FFI error codes.
//!
//! # Design Principles
//! 1. All errors are typed and descriptive
//! 2. Errors map to specific FFI error codes
//! 3. Error messages are stored globally for FFI retrieval
//! 4. No panics in hot paths - use Result<T, EngineError>

use thiserror::Error;
use crate::ffi::{
    ERR_DATA_LOAD_FAILED, ERR_FILE_NOT_FOUND, ERR_INVALID_DATA,
    ERR_INVALID_PARAM, ERR_RISK_REJECTED, ERR_INSUFFICIENT_CAPITAL,
    ERR_THROTTLE_EXCEEDED, ERR_POSITION_LIMIT, ERR_NULL_POINTER,
    ERR_ENGINE_NOT_INIT, ERR_INTERNAL_PANIC,
};
use crate::ffi_string::set_last_error_message;

// ============================================================================
// Error Types
// ============================================================================

/// Main error type for the AegisQuant engine.
///
/// All errors in the engine should be represented by this enum.
/// Each variant maps to a specific FFI error code.
#[derive(Error, Debug, Clone)]
pub enum EngineError {
    // Data Loading Errors
    #[error("File not found: {path}")]
    FileNotFound { path: String },

    #[error("Data parse error at line {line}: {message}")]
    ParseError { line: usize, message: String },

    #[error("Missing required column: {column}")]
    MissingColumn { column: String },

    #[error("Invalid data type in column '{column}': expected {expected}, got {actual}")]
    TypeMismatch {
        column: String,
        expected: String,
        actual: String,
    },

    #[error("Data validation failed: {0}")]
    ValidationError(String),

    #[error("Empty data file: {path}")]
    EmptyFile { path: String },

    #[error("Encoding error: {0}")]
    EncodingError(String),

    // Risk Management Errors
    #[error("Risk check failed: {reason}")]
    RiskRejected { reason: String },

    #[error("Insufficient capital: required {required:.2}, available {available:.2}")]
    InsufficientCapital { required: f64, available: f64 },

    #[error("Order rate throttle exceeded: {orders_per_second:.1} orders/sec")]
    ThrottleExceeded { orders_per_second: f64 },

    #[error("Position limit exceeded: current {current}, limit {limit}")]
    PositionLimitExceeded { current: i32, limit: i32 },

    // Parameter Errors
    #[error("Invalid parameter '{name}': {reason}")]
    InvalidParameter { name: String, reason: String },

    #[error("Null pointer passed for parameter: {name}")]
    NullPointer { name: String },

    // Engine State Errors
    #[error("Engine not initialized")]
    EngineNotInitialized,

    #[error("Engine already initialized")]
    EngineAlreadyInitialized,

    // IO Errors
    #[error("IO error: {0}")]
    IoError(String),

    // Polars Errors (wrapped)
    #[error("Polars error: {0}")]
    PolarsError(String),

    // Internal Errors
    #[error("Internal error: {0}")]
    InternalError(String),

    // Database Errors
    #[error("Database error: {0}")]
    DatabaseError(String),
}

// ============================================================================
// Error Code Conversion
// ============================================================================

impl EngineError {
    /// Convert error to FFI error code.
    ///
    /// This maps each error variant to a specific integer code
    /// that can be returned across the FFI boundary.
    pub fn to_error_code(&self) -> i32 {
        match self {
            // Data Loading Errors -> ERR_DATA_LOAD_FAILED or ERR_FILE_NOT_FOUND
            EngineError::FileNotFound { .. } => ERR_FILE_NOT_FOUND,
            EngineError::ParseError { .. } => ERR_DATA_LOAD_FAILED,
            EngineError::MissingColumn { .. } => ERR_DATA_LOAD_FAILED,
            EngineError::TypeMismatch { .. } => ERR_DATA_LOAD_FAILED,
            EngineError::ValidationError(_) => ERR_INVALID_DATA,
            EngineError::EmptyFile { .. } => ERR_DATA_LOAD_FAILED,
            EngineError::EncodingError(_) => ERR_DATA_LOAD_FAILED,

            // Risk Management Errors
            EngineError::RiskRejected { .. } => ERR_RISK_REJECTED,
            EngineError::InsufficientCapital { .. } => ERR_INSUFFICIENT_CAPITAL,
            EngineError::ThrottleExceeded { .. } => ERR_THROTTLE_EXCEEDED,
            EngineError::PositionLimitExceeded { .. } => ERR_POSITION_LIMIT,

            // Parameter Errors
            EngineError::InvalidParameter { .. } => ERR_INVALID_PARAM,
            EngineError::NullPointer { .. } => ERR_NULL_POINTER,

            // Engine State Errors
            EngineError::EngineNotInitialized => ERR_ENGINE_NOT_INIT,
            EngineError::EngineAlreadyInitialized => ERR_INVALID_PARAM,

            // IO Errors
            EngineError::IoError(_) => ERR_DATA_LOAD_FAILED,

            // Polars Errors
            EngineError::PolarsError(_) => ERR_DATA_LOAD_FAILED,

            // Internal Errors
            EngineError::InternalError(_) => ERR_INTERNAL_PANIC,

            // Database Errors
            EngineError::DatabaseError(_) => -13, // ERR_DB_ERROR
        }
    }

    /// Set this error as the last error and return the error code.
    ///
    /// This is a convenience method for FFI functions that need to
    /// both store the error message and return the error code.
    pub fn set_and_return_code(&self) -> i32 {
        set_last_error(self);
        self.to_error_code()
    }
}


// ============================================================================
// Error Conversion Implementations
// ============================================================================

impl From<std::io::Error> for EngineError {
    fn from(err: std::io::Error) -> Self {
        if err.kind() == std::io::ErrorKind::NotFound {
            EngineError::FileNotFound {
                path: "unknown".to_string(),
            }
        } else {
            EngineError::IoError(err.to_string())
        }
    }
}

impl From<polars::error::PolarsError> for EngineError {
    fn from(err: polars::error::PolarsError) -> Self {
        EngineError::PolarsError(err.to_string())
    }
}

impl From<std::str::Utf8Error> for EngineError {
    fn from(err: std::str::Utf8Error) -> Self {
        EngineError::EncodingError(err.to_string())
    }
}

impl From<std::string::FromUtf8Error> for EngineError {
    fn from(err: std::string::FromUtf8Error) -> Self {
        EngineError::EncodingError(err.to_string())
    }
}

// ============================================================================
// Global Error Storage
// ============================================================================

/// Set the last error from an EngineError.
///
/// This stores the error message in the global error storage
/// so it can be retrieved via FFI.
pub fn set_last_error(error: &EngineError) {
    set_last_error_message(error.to_string());
}

/// Create a Result type alias for convenience.
pub type EngineResult<T> = Result<T, EngineError>;

// ============================================================================
// Error Construction Helpers
// ============================================================================

impl EngineError {
    /// Create a file not found error.
    pub fn file_not_found(path: impl Into<String>) -> Self {
        EngineError::FileNotFound { path: path.into() }
    }

    /// Create a parse error.
    pub fn parse_error(line: usize, message: impl Into<String>) -> Self {
        EngineError::ParseError {
            line,
            message: message.into(),
        }
    }

    /// Create a missing column error.
    pub fn missing_column(column: impl Into<String>) -> Self {
        EngineError::MissingColumn {
            column: column.into(),
        }
    }

    /// Create a type mismatch error.
    pub fn type_mismatch(
        column: impl Into<String>,
        expected: impl Into<String>,
        actual: impl Into<String>,
    ) -> Self {
        EngineError::TypeMismatch {
            column: column.into(),
            expected: expected.into(),
            actual: actual.into(),
        }
    }

    /// Create a validation error.
    pub fn validation(message: impl Into<String>) -> Self {
        EngineError::ValidationError(message.into())
    }

    /// Create an empty file error.
    pub fn empty_file(path: impl Into<String>) -> Self {
        EngineError::EmptyFile { path: path.into() }
    }

    /// Create a risk rejected error.
    pub fn risk_rejected(reason: impl Into<String>) -> Self {
        EngineError::RiskRejected {
            reason: reason.into(),
        }
    }

    /// Create an insufficient capital error.
    pub fn insufficient_capital(required: f64, available: f64) -> Self {
        EngineError::InsufficientCapital { required, available }
    }

    /// Create an invalid parameter error.
    pub fn invalid_param(name: impl Into<String>, reason: impl Into<String>) -> Self {
        EngineError::InvalidParameter {
            name: name.into(),
            reason: reason.into(),
        }
    }

    /// Create a null pointer error.
    pub fn null_pointer(name: impl Into<String>) -> Self {
        EngineError::NullPointer { name: name.into() }
    }

    /// Create an internal error.
    pub fn internal(message: impl Into<String>) -> Self {
        EngineError::InternalError(message.into())
    }

    /// Create a database error.
    pub fn database(message: impl Into<String>) -> Self {
        EngineError::DatabaseError(message.into())
    }
}


// ============================================================================
// Tests
// ============================================================================

#[cfg(test)]
mod tests {
    use super::*;
    use crate::ffi_string::{clear_last_error, get_last_error};

    #[test]
    fn test_error_to_code_file_not_found() {
        let err = EngineError::file_not_found("test.csv");
        assert_eq!(err.to_error_code(), ERR_FILE_NOT_FOUND);
    }

    #[test]
    fn test_error_to_code_parse_error() {
        let err = EngineError::parse_error(42, "invalid format");
        assert_eq!(err.to_error_code(), ERR_DATA_LOAD_FAILED);
    }

    #[test]
    fn test_error_to_code_missing_column() {
        let err = EngineError::missing_column("price");
        assert_eq!(err.to_error_code(), ERR_DATA_LOAD_FAILED);
    }

    #[test]
    fn test_error_to_code_validation() {
        let err = EngineError::validation("negative price");
        assert_eq!(err.to_error_code(), ERR_INVALID_DATA);
    }

    #[test]
    fn test_error_to_code_risk_rejected() {
        let err = EngineError::risk_rejected("position limit exceeded");
        assert_eq!(err.to_error_code(), ERR_RISK_REJECTED);
    }

    #[test]
    fn test_error_to_code_insufficient_capital() {
        let err = EngineError::insufficient_capital(10000.0, 5000.0);
        assert_eq!(err.to_error_code(), ERR_INSUFFICIENT_CAPITAL);
    }

    #[test]
    fn test_error_to_code_invalid_param() {
        let err = EngineError::invalid_param("period", "must be positive");
        assert_eq!(err.to_error_code(), ERR_INVALID_PARAM);
    }

    #[test]
    fn test_error_to_code_null_pointer() {
        let err = EngineError::null_pointer("engine");
        assert_eq!(err.to_error_code(), ERR_NULL_POINTER);
    }

    #[test]
    fn test_error_to_code_engine_not_init() {
        let err = EngineError::EngineNotInitialized;
        assert_eq!(err.to_error_code(), ERR_ENGINE_NOT_INIT);
    }

    #[test]
    fn test_error_display() {
        let err = EngineError::file_not_found("data/test.csv");
        assert_eq!(err.to_string(), "File not found: data/test.csv");

        let err = EngineError::parse_error(10, "unexpected character");
        assert_eq!(
            err.to_string(),
            "Data parse error at line 10: unexpected character"
        );

        let err = EngineError::insufficient_capital(10000.0, 5000.0);
        assert_eq!(
            err.to_string(),
            "Insufficient capital: required 10000.00, available 5000.00"
        );
    }

    #[test]
    fn test_set_last_error() {
        // Note: This test may be affected by parallel test execution
        // since LAST_ERROR is global state. We set and immediately check.
        let err = EngineError::file_not_found("missing.csv");
        set_last_error(&err);

        let stored = get_last_error();
        assert!(stored.is_some());
        // The error message should contain our file name
        let msg = stored.unwrap();
        assert!(
            msg.contains("missing.csv") || msg.contains("File not found"),
            "Expected error message to contain 'missing.csv' or 'File not found', got: {}",
            msg
        );
    }

    #[test]
    fn test_set_and_return_code() {
        clear_last_error();

        let err = EngineError::missing_column("volume");
        let code = err.set_and_return_code();

        assert_eq!(code, ERR_DATA_LOAD_FAILED);

        let stored = get_last_error();
        assert!(stored.is_some());
        assert!(stored.unwrap().contains("volume"));
    }

    #[test]
    fn test_from_io_error_not_found() {
        let io_err = std::io::Error::new(std::io::ErrorKind::NotFound, "file not found");
        let engine_err: EngineError = io_err.into();

        match engine_err {
            EngineError::FileNotFound { .. } => {}
            _ => panic!("Expected FileNotFound error"),
        }
    }

    #[test]
    fn test_from_io_error_other() {
        let io_err = std::io::Error::new(std::io::ErrorKind::PermissionDenied, "access denied");
        let engine_err: EngineError = io_err.into();

        match engine_err {
            EngineError::IoError(msg) => {
                assert!(msg.contains("access denied"));
            }
            _ => panic!("Expected IoError"),
        }
    }

    #[test]
    fn test_type_mismatch_error() {
        let err = EngineError::type_mismatch("price", "f64", "string");
        assert_eq!(
            err.to_string(),
            "Invalid data type in column 'price': expected f64, got string"
        );
        assert_eq!(err.to_error_code(), ERR_DATA_LOAD_FAILED);
    }

    #[test]
    fn test_throttle_exceeded_error() {
        let err = EngineError::ThrottleExceeded {
            orders_per_second: 150.5,
        };
        assert!(err.to_string().contains("150.5"));
        assert_eq!(err.to_error_code(), ERR_THROTTLE_EXCEEDED);
    }

    #[test]
    fn test_position_limit_error() {
        let err = EngineError::PositionLimitExceeded {
            current: 10,
            limit: 5,
        };
        assert!(err.to_string().contains("10"));
        assert!(err.to_string().contains("5"));
        assert_eq!(err.to_error_code(), ERR_POSITION_LIMIT);
    }
}
