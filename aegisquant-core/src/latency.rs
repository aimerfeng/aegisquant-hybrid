//! Latency monitoring module.
//!
//! Provides nanosecond-precision latency tracking for performance monitoring.
//! Requirements: 13.1, 13.2

use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::time::Instant;

/// Maximum number of latency samples to keep for percentile calculation
const MAX_SAMPLES: usize = 10000;

/// Latency statistics structure for FFI
#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct LatencyStats {
    /// Minimum latency in nanoseconds
    pub min_ns: u64,
    /// Maximum latency in nanoseconds
    pub max_ns: u64,
    /// Average latency in nanoseconds
    pub avg_ns: u64,
    /// 50th percentile (median) latency in nanoseconds
    pub p50_ns: u64,
    /// 95th percentile latency in nanoseconds
    pub p95_ns: u64,
    /// 99th percentile latency in nanoseconds
    pub p99_ns: u64,
    /// Total number of samples
    pub sample_count: u64,
    /// Last recorded latency in nanoseconds
    pub last_ns: u64,
}

/// Global latency tracker using atomic operations for thread safety
pub struct LatencyTracker {
    /// Minimum latency observed
    min_ns: AtomicU64,
    /// Maximum latency observed
    max_ns: AtomicU64,
    /// Sum of all latencies (for average calculation)
    sum_ns: AtomicU64,
    /// Number of samples
    count: AtomicUsize,
    /// Last recorded latency
    last_ns: AtomicU64,
    /// Circular buffer for recent samples (for percentile calculation)
    samples: Vec<AtomicU64>,
    /// Current write index in circular buffer
    write_index: AtomicUsize,
    /// Whether sampling is enabled
    enabled: std::sync::atomic::AtomicBool,
    /// Sampling rate (1 = every tick, 10 = every 10th tick)
    sample_rate: AtomicUsize,
    /// Counter for sampling rate
    sample_counter: AtomicUsize,
}

impl LatencyTracker {
    /// Create a new latency tracker
    pub fn new() -> Self {
        let samples: Vec<AtomicU64> = (0..MAX_SAMPLES)
            .map(|_| AtomicU64::new(0))
            .collect();

        Self {
            min_ns: AtomicU64::new(u64::MAX),
            max_ns: AtomicU64::new(0),
            sum_ns: AtomicU64::new(0),
            count: AtomicUsize::new(0),
            last_ns: AtomicU64::new(0),
            samples,
            write_index: AtomicUsize::new(0),
            enabled: std::sync::atomic::AtomicBool::new(true),
            sample_rate: AtomicUsize::new(1),
            sample_counter: AtomicUsize::new(0),
        }
    }

    /// Record a latency measurement
    pub fn record(&self, latency_ns: u64) {
        if !self.enabled.load(Ordering::Relaxed) {
            return;
        }

        // Check sampling rate
        let counter = self.sample_counter.fetch_add(1, Ordering::Relaxed);
        let rate = self.sample_rate.load(Ordering::Relaxed);
        if rate > 1 && !counter.is_multiple_of(rate) {
            return;
        }

        // Update min
        let mut current_min = self.min_ns.load(Ordering::Relaxed);
        while latency_ns < current_min {
            match self.min_ns.compare_exchange_weak(
                current_min,
                latency_ns,
                Ordering::Relaxed,
                Ordering::Relaxed,
            ) {
                Ok(_) => break,
                Err(x) => current_min = x,
            }
        }

        // Update max
        let mut current_max = self.max_ns.load(Ordering::Relaxed);
        while latency_ns > current_max {
            match self.max_ns.compare_exchange_weak(
                current_max,
                latency_ns,
                Ordering::Relaxed,
                Ordering::Relaxed,
            ) {
                Ok(_) => break,
                Err(x) => current_max = x,
            }
        }

        // Update sum and count
        self.sum_ns.fetch_add(latency_ns, Ordering::Relaxed);
        self.count.fetch_add(1, Ordering::Relaxed);
        self.last_ns.store(latency_ns, Ordering::Relaxed);

        // Store in circular buffer
        let index = self.write_index.fetch_add(1, Ordering::Relaxed) % MAX_SAMPLES;
        self.samples[index].store(latency_ns, Ordering::Relaxed);
    }

    /// Get current latency statistics
    pub fn get_stats(&self) -> LatencyStats {
        let count = self.count.load(Ordering::Relaxed);
        if count == 0 {
            return LatencyStats::default();
        }

        let min_ns = self.min_ns.load(Ordering::Relaxed);
        let max_ns = self.max_ns.load(Ordering::Relaxed);
        let sum_ns = self.sum_ns.load(Ordering::Relaxed);
        let last_ns = self.last_ns.load(Ordering::Relaxed);

        let avg_ns = sum_ns / count as u64;

        // Calculate percentiles from samples
        let sample_count = count.min(MAX_SAMPLES);
        let mut sorted_samples: Vec<u64> = self.samples[..sample_count]
            .iter()
            .map(|s| s.load(Ordering::Relaxed))
            .filter(|&s| s > 0)
            .collect();
        sorted_samples.sort_unstable();

        let (p50_ns, p95_ns, p99_ns) = if sorted_samples.is_empty() {
            (0, 0, 0)
        } else {
            let len = sorted_samples.len();
            let p50_idx = len * 50 / 100;
            let p95_idx = len * 95 / 100;
            let p99_idx = len * 99 / 100;
            (
                sorted_samples.get(p50_idx).copied().unwrap_or(0),
                sorted_samples.get(p95_idx).copied().unwrap_or(0),
                sorted_samples.get(p99_idx).copied().unwrap_or(0),
            )
        };

        LatencyStats {
            min_ns: if min_ns == u64::MAX { 0 } else { min_ns },
            max_ns,
            avg_ns,
            p50_ns,
            p95_ns,
            p99_ns,
            sample_count: count as u64,
            last_ns,
        }
    }

    /// Reset all statistics
    pub fn reset(&self) {
        self.min_ns.store(u64::MAX, Ordering::Relaxed);
        self.max_ns.store(0, Ordering::Relaxed);
        self.sum_ns.store(0, Ordering::Relaxed);
        self.count.store(0, Ordering::Relaxed);
        self.last_ns.store(0, Ordering::Relaxed);
        self.write_index.store(0, Ordering::Relaxed);
        for sample in &self.samples {
            sample.store(0, Ordering::Relaxed);
        }
    }

    /// Enable or disable latency tracking
    pub fn set_enabled(&self, enabled: bool) {
        self.enabled.store(enabled, Ordering::Relaxed);
    }

    /// Set sampling rate (1 = every tick, 10 = every 10th tick)
    pub fn set_sample_rate(&self, rate: usize) {
        self.sample_rate.store(rate.max(1), Ordering::Relaxed);
    }
}

impl Default for LatencyTracker {
    fn default() -> Self {
        Self::new()
    }
}

// Global latency tracker instance
lazy_static::lazy_static! {
    static ref GLOBAL_TRACKER: LatencyTracker = LatencyTracker::new();
}

/// RAII guard for measuring latency
pub struct LatencyGuard {
    start: Instant,
}

impl LatencyGuard {
    /// Start measuring latency
    pub fn new() -> Self {
        Self {
            start: Instant::now(),
        }
    }
}

impl Default for LatencyGuard {
    fn default() -> Self {
        Self::new()
    }
}

impl Drop for LatencyGuard {
    fn drop(&mut self) {
        let elapsed = self.start.elapsed();
        GLOBAL_TRACKER.record(elapsed.as_nanos() as u64);
    }
}

/// Record a latency measurement to the global tracker
pub fn record_latency(latency_ns: u64) {
    GLOBAL_TRACKER.record(latency_ns);
}

/// Get latency statistics from the global tracker
pub fn get_latency_stats() -> LatencyStats {
    GLOBAL_TRACKER.get_stats()
}

/// Reset the global latency tracker
pub fn reset_latency_stats() {
    GLOBAL_TRACKER.reset();
}

/// Set the sampling rate for the global tracker
pub fn set_latency_sample_rate(rate: usize) {
    GLOBAL_TRACKER.set_sample_rate(rate);
}

/// Enable or disable latency tracking
pub fn set_latency_enabled(enabled: bool) {
    GLOBAL_TRACKER.set_enabled(enabled);
}

// ============================================================================
// FFI Functions
// ============================================================================

/// Get latency statistics.
///
/// # Safety
/// - `stats` must be a valid pointer to write LatencyStats
///
/// # Returns
/// - 0 on success
/// - -1 if stats is null
#[no_mangle]
pub unsafe extern "C" fn get_latency_stats_ffi(stats: *mut LatencyStats) -> i32 {
    if stats.is_null() {
        return -1;
    }

    *stats = get_latency_stats();
    0
}

/// Reset latency statistics.
///
/// # Returns
/// - 0 on success
#[no_mangle]
pub extern "C" fn reset_latency_stats_ffi() -> i32 {
    reset_latency_stats();
    0
}

/// Set latency sampling rate.
///
/// # Arguments
/// - `rate`: Sampling rate (1 = every tick, 10 = every 10th tick)
///
/// # Returns
/// - 0 on success
#[no_mangle]
pub extern "C" fn set_latency_sample_rate_ffi(rate: i32) -> i32 {
    set_latency_sample_rate(rate.max(1) as usize);
    0
}

/// Enable or disable latency tracking.
///
/// # Arguments
/// - `enabled`: 1 to enable, 0 to disable
///
/// # Returns
/// - 0 on success
#[no_mangle]
pub extern "C" fn set_latency_enabled_ffi(enabled: i32) -> i32 {
    set_latency_enabled(enabled != 0);
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_latency_tracker_basic() {
        let tracker = LatencyTracker::new();
        
        tracker.record(1000);
        tracker.record(2000);
        tracker.record(3000);
        
        let stats = tracker.get_stats();
        assert_eq!(stats.min_ns, 1000);
        assert_eq!(stats.max_ns, 3000);
        assert_eq!(stats.avg_ns, 2000);
        assert_eq!(stats.sample_count, 3);
    }

    #[test]
    fn test_latency_tracker_reset() {
        let tracker = LatencyTracker::new();
        
        tracker.record(1000);
        tracker.reset();
        
        let stats = tracker.get_stats();
        assert_eq!(stats.sample_count, 0);
    }

    #[test]
    fn test_latency_guard() {
        reset_latency_stats();
        
        {
            let _guard = LatencyGuard::new();
            std::thread::sleep(std::time::Duration::from_micros(100));
        }
        
        let stats = get_latency_stats();
        assert!(stats.sample_count >= 1);
        assert!(stats.last_ns > 0);
    }

    #[test]
    fn test_sampling_rate() {
        let tracker = LatencyTracker::new();
        tracker.set_sample_rate(2);
        
        for i in 0..10 {
            tracker.record((i + 1) * 1000);
        }
        
        let stats = tracker.get_stats();
        // With rate=2, only every other sample is recorded
        assert_eq!(stats.sample_count, 5);
    }

    #[test]
    fn test_disabled_tracking() {
        let tracker = LatencyTracker::new();
        tracker.set_enabled(false);
        
        tracker.record(1000);
        
        let stats = tracker.get_stats();
        assert_eq!(stats.sample_count, 0);
    }
}
