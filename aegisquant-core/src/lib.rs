//! AegisQuant-Hybrid Core Engine
//!
//! High-performance quantitative backtesting engine written in Rust.
//! Provides FFI interface for C# interop.

pub mod types;
pub mod ffi;
pub mod risk;
pub mod gateway;
pub mod data_loader;
pub mod strategy;
pub mod engine;
pub mod logger;
pub mod optimizer;

pub use types::*;
pub use ffi::*;
pub use risk::*;
pub use gateway::*;
pub use data_loader::*;
pub use strategy::*;
pub use engine::*;
// Note: logger::set_log_callback is intentionally not re-exported here
// to avoid conflict with ffi::set_log_callback. Use ffi::set_log_callback for FFI.
pub use logger::{Logger, LogLevel, LogCallback, clear_log_callback};
pub use optimizer::*;
