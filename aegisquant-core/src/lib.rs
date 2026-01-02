//! AegisQuant-Hybrid Core Engine
//!
//! High-performance quantitative backtesting engine written in Rust.
//! Provides FFI interface for C# interop.

pub mod types;
pub mod ffi;

pub use types::*;
pub use ffi::*;
