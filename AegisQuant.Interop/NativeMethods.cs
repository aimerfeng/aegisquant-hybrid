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
}
