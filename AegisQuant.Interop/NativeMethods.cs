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
internal static partial class NativeMethods
{
    private const string DllName = "aegisquant_core";

    /// <summary>
    /// Initialize a new backtest engine.
    /// </summary>
    /// <param name="parameters">Strategy parameters (can be null for defaults)</param>
    /// <param name="riskConfig">Risk configuration (can be null for defaults)</param>
    /// <returns>Engine handle pointer, or IntPtr.Zero on failure</returns>
    /// <remarks>
    /// SAFETY: Caller must call free_engine to release the returned pointer.
    /// </remarks>
    [LibraryImport(DllName, EntryPoint = "init_engine")]
    public static unsafe partial IntPtr InitEngine(
        StrategyParams* parameters,
        RiskConfig* riskConfig);

    /// <summary>
    /// Free engine resources.
    /// </summary>
    /// <param name="engine">Engine handle from init_engine</param>
    /// <remarks>
    /// SAFETY: Must only be called once per engine. After calling, the pointer is invalid.
    /// </remarks>
    [LibraryImport(DllName, EntryPoint = "free_engine")]
    public static partial void FreeEngine(IntPtr engine);

    /// <summary>
    /// Process a single tick.
    /// </summary>
    /// <param name="engine">Engine handle</param>
    /// <param name="tick">Tick data pointer</param>
    /// <returns>Error code (0 = success)</returns>
    [LibraryImport(DllName, EntryPoint = "process_tick")]
    public static unsafe partial int ProcessTick(
        IntPtr engine,
        Tick* tick);

    /// <summary>
    /// Get current account status.
    /// </summary>
    /// <param name="engine">Engine handle</param>
    /// <param name="status">Output pointer for account status</param>
    /// <returns>Error code (0 = success)</returns>
    [LibraryImport(DllName, EntryPoint = "get_account_status")]
    public static unsafe partial int GetAccountStatus(
        IntPtr engine,
        AccountStatus* status);

    /// <summary>
    /// Load data from file using Polars.
    /// </summary>
    /// <param name="engine">Engine handle</param>
    /// <param name="filePath">Path to CSV/Parquet file (UTF-8)</param>
    /// <param name="report">Output pointer for data quality report</param>
    /// <returns>Error code (0 = success)</returns>
    [LibraryImport(DllName, EntryPoint = "load_data_from_file", StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int LoadDataFromFile(
        IntPtr engine,
        string filePath,
        DataQualityReport* report);

    /// <summary>
    /// Run complete backtest.
    /// </summary>
    /// <param name="engine">Engine handle</param>
    /// <returns>Error code (0 = success)</returns>
    [LibraryImport(DllName, EntryPoint = "run_backtest")]
    public static partial int RunBacktest(IntPtr engine);

    /// <summary>
    /// Set the global log callback.
    /// </summary>
    /// <param name="callback">Function pointer for log callback</param>
    /// <returns>Error code (0 = success)</returns>
    /// <remarks>
    /// SAFETY: The callback must remain valid for the lifetime of the engine.
    /// Keep a reference to the delegate to prevent GC collection.
    /// </remarks>
    [LibraryImport(DllName, EntryPoint = "set_log_callback")]
    public static partial int SetLogCallback(IntPtr callback);

    /// <summary>
    /// Clear the global log callback.
    /// </summary>
    /// <returns>Error code (0 = success)</returns>
    /// <remarks>
    /// Call this before disposing the engine to ensure no callbacks are invoked
    /// after the delegate has been garbage collected.
    /// </remarks>
    [LibraryImport(DllName, EntryPoint = "clear_log_callback")]
    public static partial int ClearLogCallback();

    /// <summary>
    /// Get the last error message using a callback.
    /// </summary>
    /// <param name="callback">Function pointer for string callback</param>
    /// <remarks>
    /// SAFETY: The callback is invoked synchronously during this call.
    /// The string pointer is only valid during the callback invocation.
    /// </remarks>
    [LibraryImport(DllName, EntryPoint = "get_last_error_message")]
    public static partial void GetLastErrorMessage(IntPtr callback);

    /// <summary>
    /// Get the last error message with length using a callback.
    /// </summary>
    /// <param name="callback">Function pointer for string with length callback</param>
    /// <remarks>
    /// SAFETY: The callback is invoked synchronously during this call.
    /// The string pointer is only valid during the callback invocation.
    /// </remarks>
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
    /// <returns>1 if there is an error message, 0 otherwise</returns>
    [LibraryImport(DllName, EntryPoint = "has_error_message")]
    public static partial int HasErrorMessage();
}


    // ============================================================================
    // Latency Monitoring FFI Functions (Requirements: 13.1, 13.2)
    // ============================================================================

    /// <summary>
    /// Get latency statistics.
    /// </summary>
    /// <param name="stats">Output pointer for latency stats</param>
    /// <returns>0 on success, -1 if stats is null</returns>
    [LibraryImport(DllName, EntryPoint = "get_latency_stats_ffi")]
    public static unsafe partial int GetLatencyStats(LatencyStats* stats);

    /// <summary>
    /// Reset latency statistics.
    /// </summary>
    /// <returns>0 on success</returns>
    [LibraryImport(DllName, EntryPoint = "reset_latency_stats_ffi")]
    public static partial int ResetLatencyStats();

    /// <summary>
    /// Set latency sampling rate.
    /// </summary>
    /// <param name="rate">Sampling rate (1 = every tick, 10 = every 10th tick)</param>
    /// <returns>0 on success</returns>
    [LibraryImport(DllName, EntryPoint = "set_latency_sample_rate_ffi")]
    public static partial int SetLatencySampleRate(int rate);

    /// <summary>
    /// Enable or disable latency tracking.
    /// </summary>
    /// <param name="enabled">1 to enable, 0 to disable</param>
    /// <returns>0 on success</returns>
    [LibraryImport(DllName, EntryPoint = "set_latency_enabled_ffi")]
    public static partial int SetLatencyEnabled(int enabled);


    // ============================================================================
    // Emergency Control FFI Functions (Requirements: 16.1, 16.2, 16.6, 16.7)
    // ============================================================================

    /// <summary>
    /// Trigger emergency stop - halts all automatic trading.
    /// </summary>
    /// <returns>0 on success</returns>
    [LibraryImport(DllName, EntryPoint = "emergency_stop")]
    public static partial int EmergencyStop();

    /// <summary>
    /// Reset emergency stop - resumes automatic trading.
    /// </summary>
    /// <returns>0 on success</returns>
    [LibraryImport(DllName, EntryPoint = "reset_emergency_stop_ffi")]
    public static partial int ResetEmergencyStop();

    /// <summary>
    /// Check if emergency halt is active.
    /// </summary>
    /// <returns>1 if halted, 0 otherwise</returns>
    [LibraryImport(DllName, EntryPoint = "is_emergency_halted")]
    public static partial int IsEmergencyHalted();

    /// <summary>
    /// Close all positions.
    /// </summary>
    /// <param name="engine">Engine handle</param>
    /// <param name="orders">Output buffer for close orders</param>
    /// <param name="maxOrders">Maximum number of orders to return</param>
    /// <param name="orderCount">Output: actual number of orders</param>
    /// <returns>0 on success</returns>
    [LibraryImport(DllName, EntryPoint = "close_all_positions")]
    public static unsafe partial int CloseAllPositions(
        IntPtr engine,
        OrderRequest* orders,
        int maxOrders,
        int* orderCount);
