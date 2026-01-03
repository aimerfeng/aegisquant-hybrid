//! FFI String Safety Module
//!
//! Provides safe string passing between Rust and C# using callback patterns.
//! This avoids memory leaks by keeping string ownership in Rust.
//!
//! # Design Principles
//! 1. Rust owns all strings - never return Rust-allocated strings to C#
//! 2. Use callbacks to pass string references within a safe scope
//! 3. C# must copy string content immediately in the callback
//! 4. Provide `get_last_error_message` for detailed error information

use std::ffi::{c_char, CString};
use std::sync::Mutex;

// ============================================================================
// String Callback Types
// ============================================================================

/// Callback function type for receiving strings from Rust.
///
/// # Safety Contract
/// - The string pointer is only valid during the callback invocation
/// - C# must copy the string content before the callback returns
/// - The pointer must not be stored or used after the callback returns
pub type StringCallback = extern "C" fn(*const c_char);

/// Callback function type for receiving strings with length.
/// Useful for strings that may contain null bytes.
pub type StringWithLenCallback = extern "C" fn(*const c_char, len: i32);

// ============================================================================
// Global Error Storage
// ============================================================================

/// Thread-safe storage for the last error message.
/// This allows C# to retrieve detailed error information after an FFI call fails.
static LAST_ERROR: Mutex<Option<String>> = Mutex::new(None);

/// Set the last error message.
///
/// This is called internally when an error occurs in FFI functions.
/// The message can be retrieved via `get_last_error_message`.
pub fn set_last_error_message(message: impl Into<String>) {
    if let Ok(mut guard) = LAST_ERROR.lock() {
        *guard = Some(message.into());
    }
}

/// Clear the last error message.
pub fn clear_last_error() {
    if let Ok(mut guard) = LAST_ERROR.lock() {
        *guard = None;
    }
}


/// Get the last error message (internal use).
pub fn get_last_error() -> Option<String> {
    if let Ok(guard) = LAST_ERROR.lock() {
        guard.clone()
    } else {
        None
    }
}

// ============================================================================
// Safe String Passing Functions
// ============================================================================

/// Pass a string to a callback safely.
///
/// The string is converted to a C string and passed to the callback.
/// The callback must copy the string content before returning.
///
/// # Arguments
/// * `s` - The string to pass
/// * `callback` - The callback function to receive the string
///
/// # Returns
/// * `true` if the callback was invoked successfully
/// * `false` if the string could not be converted (contains null bytes)
///
/// # Example (Rust side)
/// ```ignore
/// with_string_callback("Hello, World!", callback);
/// ```
///
/// # Example (C# side)
/// ```csharp
/// [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
/// delegate void StringCallback(IntPtr str);
///
/// void OnString(IntPtr str) {
///     string message = Marshal.PtrToStringUTF8(str);
///     // Use message - it's now a managed copy
/// }
/// ```
pub fn with_string_callback(s: &str, callback: StringCallback) -> bool {
    match CString::new(s) {
        Ok(c_str) => {
            callback(c_str.as_ptr());
            true
        }
        Err(_) => {
            // String contains null bytes, cannot convert
            false
        }
    }
}

/// Pass a string with length to a callback safely.
///
/// This variant passes the string length, allowing strings with embedded nulls.
pub fn with_string_len_callback(s: &str, callback: StringWithLenCallback) -> bool {
    match CString::new(s) {
        Ok(c_str) => {
            callback(c_str.as_ptr(), s.len() as i32);
            true
        }
        Err(_) => {
            // For strings with null bytes, pass the raw bytes
            // C# will need to handle this specially
            let bytes = s.as_bytes();
            if bytes.is_empty() {
                callback(std::ptr::null(), 0);
            } else {
                // Create a buffer with null terminator
                let mut buffer = bytes.to_vec();
                buffer.push(0);
                callback(buffer.as_ptr() as *const c_char, bytes.len() as i32);
            }
            true
        }
    }
}

// ============================================================================
// FFI Functions
// ============================================================================

/// Get the last error message via callback.
///
/// # Safety
/// - `callback` must be a valid function pointer
/// - The callback must copy the string before returning
///
/// # Returns
/// - 1 if an error message was available and passed to callback
/// - 0 if no error message was available
/// - -1 if callback is null
#[no_mangle]
pub unsafe extern "C" fn get_last_error_message(callback: StringCallback) -> i32 {
    if callback as usize == 0 {
        return -1;
    }

    if let Ok(guard) = LAST_ERROR.lock() {
        if let Some(ref error) = *guard {
            if with_string_callback(error, callback) {
                return 1;
            }
        }
    }
    0
}

/// Get the last error message via callback with length.
///
/// # Safety
/// - `callback` must be a valid function pointer
/// - The callback must copy the string before returning
///
/// # Returns
/// - 1 if an error message was available and passed to callback
/// - 0 if no error message was available
/// - -1 if callback is null
#[no_mangle]
pub unsafe extern "C" fn get_last_error_message_with_len(callback: StringWithLenCallback) -> i32 {
    if callback as usize == 0 {
        return -1;
    }

    if let Ok(guard) = LAST_ERROR.lock() {
        if let Some(ref error) = *guard {
            if with_string_len_callback(error, callback) {
                return 1;
            }
        }
    }
    0
}

/// Clear the last error message.
///
/// Call this before an FFI operation to ensure you get fresh error info.
#[no_mangle]
pub extern "C" fn clear_last_error_message() {
    clear_last_error();
}

/// Check if there is a pending error message.
///
/// # Returns
/// - 1 if there is an error message
/// - 0 if there is no error message
#[no_mangle]
pub extern "C" fn has_error_message() -> i32 {
    if let Ok(guard) = LAST_ERROR.lock() {
        if guard.is_some() {
            return 1;
        }
    }
    0
}


// ============================================================================
// Tests
// ============================================================================

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicI32, Ordering};
    use std::cell::RefCell;

    thread_local! {
        static RECEIVED_STRING: RefCell<Option<String>> = RefCell::new(None);
        static RECEIVED_LEN: RefCell<i32> = RefCell::new(0);
    }

    static CALLBACK_COUNT: AtomicI32 = AtomicI32::new(0);

    extern "C" fn test_string_callback(s: *const c_char) {
        CALLBACK_COUNT.fetch_add(1, Ordering::SeqCst);
        if !s.is_null() {
            unsafe {
                let c_str = std::ffi::CStr::from_ptr(s);
                if let Ok(str_slice) = c_str.to_str() {
                    RECEIVED_STRING.with(|cell| {
                        *cell.borrow_mut() = Some(str_slice.to_string());
                    });
                }
            }
        }
    }

    extern "C" fn test_string_len_callback(s: *const c_char, len: i32) {
        CALLBACK_COUNT.fetch_add(1, Ordering::SeqCst);
        RECEIVED_LEN.with(|cell| {
            *cell.borrow_mut() = len;
        });
        if !s.is_null() && len > 0 {
            unsafe {
                let slice = std::slice::from_raw_parts(s as *const u8, len as usize);
                if let Ok(str_slice) = std::str::from_utf8(slice) {
                    RECEIVED_STRING.with(|cell| {
                        *cell.borrow_mut() = Some(str_slice.to_string());
                    });
                }
            }
        }
    }

    fn reset_test_state() {
        CALLBACK_COUNT.store(0, Ordering::SeqCst);
        RECEIVED_STRING.with(|cell| {
            *cell.borrow_mut() = None;
        });
        RECEIVED_LEN.with(|cell| {
            *cell.borrow_mut() = 0;
        });
        clear_last_error();
    }

    #[test]
    fn test_with_string_callback() {
        reset_test_state();

        let result = with_string_callback("Hello, World!", test_string_callback);
        assert!(result);
        assert_eq!(CALLBACK_COUNT.load(Ordering::SeqCst), 1);

        RECEIVED_STRING.with(|cell| {
            assert_eq!(cell.borrow().as_deref(), Some("Hello, World!"));
        });
    }

    #[test]
    fn test_with_string_callback_empty() {
        reset_test_state();

        let result = with_string_callback("", test_string_callback);
        assert!(result);
        assert_eq!(CALLBACK_COUNT.load(Ordering::SeqCst), 1);

        RECEIVED_STRING.with(|cell| {
            assert_eq!(cell.borrow().as_deref(), Some(""));
        });
    }

    #[test]
    fn test_with_string_callback_unicode() {
        reset_test_state();

        let result = with_string_callback("你好世界 🌍", test_string_callback);
        assert!(result);
        assert_eq!(CALLBACK_COUNT.load(Ordering::SeqCst), 1);

        RECEIVED_STRING.with(|cell| {
            assert_eq!(cell.borrow().as_deref(), Some("你好世界 🌍"));
        });
    }

    #[test]
    fn test_with_string_len_callback() {
        reset_test_state();

        let result = with_string_len_callback("Test String", test_string_len_callback);
        assert!(result);
        assert_eq!(CALLBACK_COUNT.load(Ordering::SeqCst), 1);

        RECEIVED_LEN.with(|cell| {
            assert_eq!(*cell.borrow(), 11);
        });

        RECEIVED_STRING.with(|cell| {
            assert_eq!(cell.borrow().as_deref(), Some("Test String"));
        });
    }

    #[test]
    fn test_set_and_get_last_error() {
        reset_test_state();

        set_last_error_message("Test error message");

        let error = get_last_error();
        assert_eq!(error, Some("Test error message".to_string()));
    }

    #[test]
    fn test_clear_last_error() {
        reset_test_state();

        set_last_error_message("Error to clear");
        assert!(get_last_error().is_some());

        clear_last_error();
        assert!(get_last_error().is_none());
    }

    #[test]
    fn test_get_last_error_message_ffi() {
        reset_test_state();

        set_last_error_message("FFI error test");

        unsafe {
            let result = get_last_error_message(test_string_callback);
            assert_eq!(result, 1);
        }

        RECEIVED_STRING.with(|cell| {
            assert_eq!(cell.borrow().as_deref(), Some("FFI error test"));
        });
    }

    #[test]
    fn test_get_last_error_message_no_error() {
        reset_test_state();

        unsafe {
            let result = get_last_error_message(test_string_callback);
            assert_eq!(result, 0);
        }

        // Callback should not have been called
        assert_eq!(CALLBACK_COUNT.load(Ordering::SeqCst), 0);
    }

    #[test]
    fn test_has_error_message() {
        reset_test_state();

        assert_eq!(has_error_message(), 0);

        set_last_error_message("Some error");
        assert_eq!(has_error_message(), 1);

        clear_last_error();
        assert_eq!(has_error_message(), 0);
    }

    #[test]
    fn test_clear_last_error_message_ffi() {
        reset_test_state();

        set_last_error_message("Error to clear via FFI");
        assert_eq!(has_error_message(), 1);

        clear_last_error_message();
        assert_eq!(has_error_message(), 0);
    }
}
