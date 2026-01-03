using System.Runtime.InteropServices;

namespace AegisQuant.Interop;

/// <summary>
/// Log callback delegate for receiving log messages from Rust.
/// </summary>
/// <param name="level">Log level (0=Trace, 1=Debug, 2=Info, 3=Warn, 4=Error)</param>
/// <param name="message">Null-terminated UTF-8 message string</param>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void LogCallback(int level, IntPtr message);

/// <summary>
/// String callback delegate for receiving strings from Rust.
/// Used for error messages and other string data.
/// </summary>
/// <param name="message">Null-terminated UTF-8 string</param>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void StringCallback(IntPtr message);

/// <summary>
/// String callback with length delegate for receiving strings from Rust.
/// Used when string length is known.
/// </summary>
/// <param name="message">UTF-8 string (may not be null-terminated)</param>
/// <param name="length">Length of the string in bytes</param>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void StringWithLenCallback(IntPtr message, int length);

/// <summary>
/// P/Invoke declarations for the Rust aegisquant_core library.
/// </summary>
public static partial class NativeMethods
{
    private const string DllName = "aegisquant_core";

    /// <summary>
    /// Initialize a new backtest engine.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "init_engine")]
    public static unsafe partial IntPtr InitEngine(
        StrategyParams* parameters,
        RiskConfig* riskConfig);

    /// <summary>
    /// Free engine resources.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "free_engine")]
    public static partial void FreeEngine(IntPtr engine);

    /// <summary>
    /// Process a single tick.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "process_tick")]
    public static unsafe partial int ProcessTick(
        IntPtr engine,
        Tick* tick);

    /// <summary>
    /// Get current account status.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "get_account_status")]
    public static unsafe partial int GetAccountStatus(
        IntPtr engine,
        AccountStatus* status);

    /// <summary>
    /// Load data from file using Polars.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "load_data_from_file", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int LoadDataFromFile(
        IntPtr engine,
        string filePath,
        DataQualityReport* report);

    /// <summary>
    /// Run complete backtest.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "run_backtest")]
    public static partial int RunBacktest(IntPtr engine);

    /// <summary>
    /// Set the global log callback.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "set_log_callback")]
    public static partial int SetLogCallback(IntPtr callback);

    /// <summary>
    /// Clear the global log callback.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "clear_log_callback")]
    public static partial int ClearLogCallback();

    /// <summary>
    /// Get the last error message using a callback.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "get_last_error_message")]
    public static partial void GetLastErrorMessage(IntPtr callback);

    /// <summary>
    /// Get the last error message with length using a callback.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "get_last_error_message_with_len")]
    public static partial void GetLastErrorMessageWithLen(IntPtr callback);

    /// <summary>
    /// Clear the last error message.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "clear_last_error_message")]
    public static partial void ClearLastErrorMessage();

    /// <summary>
    /// Check if there is an error message.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "has_error_message")]
    public static partial int HasErrorMessage();

    // ============================================================================
    // Latency Monitoring FFI Functions (Requirements: 13.1, 13.2)
    // ============================================================================

    /// <summary>
    /// Get latency statistics.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "get_latency_stats_ffi")]
    public static unsafe partial int GetLatencyStats(LatencyStats* stats);

    /// <summary>
    /// Reset latency statistics.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "reset_latency_stats_ffi")]
    public static partial int ResetLatencyStats();

    /// <summary>
    /// Set latency sampling rate.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "set_latency_sample_rate_ffi")]
    public static partial int SetLatencySampleRate(int rate);

    /// <summary>
    /// Enable or disable latency tracking.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "set_latency_enabled_ffi")]
    public static partial int SetLatencyEnabled(int enabled);

    // ============================================================================
    // Emergency Control FFI Functions (Requirements: 16.1, 16.2, 16.6, 16.7)
    // ============================================================================

    /// <summary>
    /// Trigger emergency stop - halts all automatic trading.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "emergency_stop")]
    public static partial int EmergencyStop();

    /// <summary>
    /// Reset emergency stop - resumes automatic trading.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "reset_emergency_stop_ffi")]
    public static partial int ResetEmergencyStop();

    /// <summary>
    /// Check if emergency halt is active.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "is_emergency_halted")]
    public static partial int IsEmergencyHalted();

    /// <summary>
    /// Close all positions.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "close_all_positions")]
    public static unsafe partial int CloseAllPositions(
        IntPtr engine,
        OrderRequest* orders,
        int maxOrders,
        int* orderCount);
}
