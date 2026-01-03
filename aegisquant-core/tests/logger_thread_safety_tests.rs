//! Property-based tests for Atomic Logger Thread Safety.
//!
//! Feature: aegisquant-optimizations, Property 4: 原子日志线程安全
//! Validates: Requirements 4.1, 4.4
//!
//! This test verifies that:
//! 1. Concurrent log() calls don't cause data races or crashes
//! 2. Concurrent set_callback/clear_callback don't cause crashes
//! 3. Log calls with no callback set are silently ignored
//!
//! Note: These tests MUST run serially (--test-threads=1) to avoid interference
//! between tests that share the global LOG_CALLBACK state.

use serial_test::serial;
use std::sync::atomic::{AtomicI32, AtomicU32, Ordering};
use std::sync::Arc;
use std::thread;

use aegisquant_core::logger::{
    LogLevel, Logger,
    set_log_callback, clear_log_callback, has_log_callback, log,
};

/// Thread-safe counter for tracking callback invocations
/// Each test should reset this before use
static CALLBACK_COUNT: AtomicI32 = AtomicI32::new(0);

/// Test callback that increments a counter
extern "C" fn counting_callback(_level: i32, _message: *const std::ffi::c_char) {
    CALLBACK_COUNT.fetch_add(1, Ordering::SeqCst);
}

/// Another test callback for switching tests
extern "C" fn alternate_callback(_level: i32, _message: *const std::ffi::c_char) {
    // Just a different callback for testing switching
}

/// Helper to ensure clean state before each test
fn reset_state() {
    clear_log_callback();
    CALLBACK_COUNT.store(0, Ordering::SeqCst);
    // Small delay to ensure any pending operations complete
    std::thread::sleep(std::time::Duration::from_millis(10));
}

// Note: We use regular tests instead of proptest for thread safety tests
// because proptest's parallel execution can interfere with global state.

/// Property 4: Concurrent logging doesn't crash
/// Multiple threads logging simultaneously should not cause data races or crashes.
#[test]
#[serial]
fn test_concurrent_logging_no_crash() {
    reset_state();
    set_log_callback(counting_callback);
    
    let thread_count = 4;
    let messages_per_thread = 25;

    let handles: Vec<_> = (0..thread_count)
        .map(|thread_id| {
            thread::spawn(move || {
                for i in 0..messages_per_thread {
                    let msg = format!("Thread {} message {}", thread_id, i);
                    log(LogLevel::Info, &msg);
                }
            })
        })
        .collect();

    // Wait for all threads to complete
    for handle in handles {
        handle.join().expect("Thread should not panic");
    }

    // Verify callback was called
    let count = CALLBACK_COUNT.load(Ordering::SeqCst);
    assert!(
        count > 0,
        "Callback should have been called at least once, got {}",
        count
    );

    clear_log_callback();
}

/// Property 4: Concurrent set/clear callback doesn't crash
/// Multiple threads setting and clearing the callback should not cause crashes.
#[test]
#[serial]
fn test_concurrent_callback_switching_no_crash() {
    reset_state();
    
    let iterations = 30;
    
    let handles: Vec<_> = (0..4)
        .map(|thread_id| {
            thread::spawn(move || {
                for _ in 0..iterations {
                    if thread_id % 2 == 0 {
                        set_log_callback(counting_callback);
                    } else {
                        set_log_callback(alternate_callback);
                    }
                    // Log something
                    log(LogLevel::Info, "test message");
                    
                    if thread_id % 3 == 0 {
                        clear_log_callback();
                    }
                }
            })
        })
        .collect();

    for handle in handles {
        handle.join().expect("Thread should not panic");
    }

    // Clean up
    clear_log_callback();
    
    // If we got here without panic, test passes
}

/// Property 4: Logging with no callback is silently ignored
#[test]
#[serial]
fn test_logging_without_callback_silent() {
    reset_state();
    
    assert!(!has_log_callback(), "Callback should not be set");

    // These should not panic or crash
    log(LogLevel::Trace, "trace message");
    log(LogLevel::Debug, "debug message");
    log(LogLevel::Info, "info message");
    log(LogLevel::Warn, "warn message");
    log(LogLevel::Error, "error message");

    // Using Logger instance
    let logger = Logger::new("Test");
    logger.trace("trace");
    logger.debug("debug");
    logger.info("info");
    logger.warn("warn");
    logger.error("error");

    // If we got here without panic, test passes
}

/// Property 4: Mixed logging and callback switching
#[test]
#[serial]
fn test_mixed_logging_and_switching() {
    reset_state();
    set_log_callback(counting_callback);

    let log_threads = 3;
    let switch_threads = 2;
    let iterations = 30;
    
    let completed = Arc::new(AtomicU32::new(0));
    let total_threads = log_threads + switch_threads;

    let mut handles = Vec::new();

    // Logging threads
    for thread_id in 0..log_threads {
        let completed = Arc::clone(&completed);
        handles.push(thread::spawn(move || {
            for i in 0..iterations {
                let msg = format!("Logger {} iteration {}", thread_id, i);
                log(LogLevel::Info, &msg);
                
                // Small yield to increase interleaving
                if i % 10 == 0 {
                    thread::yield_now();
                }
            }
            completed.fetch_add(1, Ordering::SeqCst);
        }));
    }

    // Callback switching threads
    for _thread_id in 0..switch_threads {
        let completed = Arc::clone(&completed);
        handles.push(thread::spawn(move || {
            for i in 0..iterations {
                if i % 3 == 0 {
                    set_log_callback(counting_callback);
                } else if i % 3 == 1 {
                    set_log_callback(alternate_callback);
                } else {
                    clear_log_callback();
                }
                
                // Small yield to increase interleaving
                if i % 5 == 0 {
                    thread::yield_now();
                }
            }
            completed.fetch_add(1, Ordering::SeqCst);
        }));
    }

    // Wait for all threads
    for handle in handles {
        handle.join().expect("Thread should not panic");
    }

    // Verify all threads completed
    let completed_count = completed.load(Ordering::SeqCst);
    assert_eq!(
        completed_count as usize, total_threads,
        "All threads should complete"
    );

    clear_log_callback();
}

/// Property 4: Logger instance is thread-safe
#[test]
#[serial]
fn test_logger_instance_thread_safe() {
    reset_state();
    set_log_callback(counting_callback);

    let thread_count = 4;
    let messages_per_thread = 20;
    
    let handles: Vec<_> = (0..thread_count)
        .map(|thread_id| {
            thread::spawn(move || {
                let logger = Logger::new(&format!("Thread{}", thread_id))
                    .with_level(LogLevel::Debug);
                
                for i in 0..messages_per_thread {
                    logger.debug(&format!("Debug message {}", i));
                    logger.info(&format!("Info message {}", i));
                    
                    if i % 5 == 0 {
                        logger.warn(&format!("Warn message {}", i));
                    }
                }
            })
        })
        .collect();

    for handle in handles {
        handle.join().expect("Thread should not panic");
    }

    let count = CALLBACK_COUNT.load(Ordering::SeqCst);
    assert!(count > 0, "Callback should have been called");

    clear_log_callback();
}

/// Property 4: Rapid callback switching doesn't lose all messages
#[test]
#[serial]
fn test_rapid_switching_message_delivery() {
    reset_state();

    let switch_count = 30;
    
    for i in 0..switch_count {
        set_log_callback(counting_callback);
        log(LogLevel::Info, &format!("Message {}", i));
        
        // Rapidly switch
        if i % 2 == 0 {
            clear_log_callback();
            set_log_callback(counting_callback);
        }
    }

    // Some messages should have been delivered
    let count = CALLBACK_COUNT.load(Ordering::SeqCst);
    assert!(
        count > 0,
        "At least some messages should be delivered, got {}",
        count
    );

    clear_log_callback();
}

/// Non-proptest thread safety test for deterministic verification
#[test]
#[serial]
fn test_atomic_ptr_thread_safety_deterministic() {
    reset_state();
    set_log_callback(counting_callback);

    let thread_count = 4;
    let messages_per_thread = 100;

    let handles: Vec<_> = (0..thread_count)
        .map(|_| {
            thread::spawn(move || {
                for _ in 0..messages_per_thread {
                    log(LogLevel::Info, "test");
                }
            })
        })
        .collect();

    for handle in handles {
        handle.join().unwrap();
    }

    let count = CALLBACK_COUNT.load(Ordering::SeqCst);
    assert_eq!(
        count,
        (thread_count * messages_per_thread) as i32,
        "All messages should be delivered"
    );

    clear_log_callback();
}

#[test]
#[serial]
fn test_clear_callback_stops_delivery() {
    reset_state();
    
    // Set callback and log
    set_log_callback(counting_callback);
    log(LogLevel::Info, "message 1");
    
    let count_after_first = CALLBACK_COUNT.load(Ordering::SeqCst);
    assert_eq!(count_after_first, 1);
    
    // Clear callback and log
    clear_log_callback();
    log(LogLevel::Info, "message 2");
    
    let count_after_clear = CALLBACK_COUNT.load(Ordering::SeqCst);
    assert_eq!(count_after_clear, 1, "No new messages after clear");
    
    // Set callback again and log
    set_log_callback(counting_callback);
    log(LogLevel::Info, "message 3");
    
    let count_final = CALLBACK_COUNT.load(Ordering::SeqCst);
    assert_eq!(count_final, 2, "Message delivered after re-setting callback");
}
