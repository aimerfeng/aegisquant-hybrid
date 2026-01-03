using System.Runtime.InteropServices;

namespace AegisQuant.Interop;

/// <summary>
/// High-level wrapper for the Rust backtest engine.
/// Provides a safe, managed interface to the native engine.
/// </summary>
/// <remarks>
/// This class implements IDisposable to ensure proper cleanup of native resources.
/// All unsafe operations are documented with SAFETY comments.
/// </remarks>
public sealed class EngineWrapper : IDisposable
{
    private EngineHandle? _handle;
    private bool _disposed;

    /// <summary>
    /// CRITICAL: Keep delegate reference to prevent GC collection.
    /// When Rust calls the callback, if the delegate has been GC'd,
    /// it will cause an access violation crash.
    /// </summary>
    private LogCallback? _logCallbackKeepAlive;

    /// <summary>
    /// CRITICAL: Keep string callback delegate reference to prevent GC collection.
    /// Used for error message retrieval.
    /// </summary>
    private StringCallback? _stringCallbackKeepAlive;

    /// <summary>
    /// User-provided log handler.
    /// </summary>
    private Action<LogLevel, string>? _logHandler;

    /// <summary>
    /// Creates a new engine with the specified parameters.
    /// </summary>
    /// <param name="parameters">Strategy parameters</param>
    /// <param name="riskConfig">Risk configuration</param>
    /// <exception cref="EngineException">Thrown if engine initialization fails</exception>
    public EngineWrapper(StrategyParams parameters, RiskConfig riskConfig)
    {
        unsafe
        {
            // SAFETY: Passing stack-allocated structs by pointer.
            // Rust will copy the data, so the pointers only need to be valid during the call.
            IntPtr ptr = NativeMethods.InitEngine(&parameters, &riskConfig);

            if (ptr == IntPtr.Zero)
            {
                throw new EngineException("Failed to initialize engine");
            }

            _handle = new EngineHandle(ptr);
        }
    }

    /// <summary>
    /// Creates a new engine with default parameters.
    /// </summary>
    /// <exception cref="EngineException">Thrown if engine initialization fails</exception>
    public EngineWrapper() : this(StrategyParams.Default, RiskConfig.Default)
    {
    }

    /// <summary>
    /// Gets the underlying engine handle for advanced operations.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public EngineHandle Handle
    {
        get
        {
            ThrowIfDisposed();
            return _handle!;
        }
    }


    /// <summary>
    /// Sets the log callback to receive log messages from the Rust engine.
    /// </summary>
    /// <param name="handler">Handler function receiving log level and message</param>
    /// <remarks>
    /// SAFETY: The callback delegate is stored in _logCallbackKeepAlive to prevent
    /// GC collection. If the delegate were collected while Rust still holds the
    /// function pointer, calling it would cause undefined behavior.
    /// </remarks>
    public void SetLogCallback(Action<LogLevel, string> handler)
    {
        ThrowIfDisposed();

        _logHandler = handler;

        // Create the native callback and keep a reference to prevent GC
        _logCallbackKeepAlive = (level, messagePtr) =>
        {
            // SAFETY: messagePtr is a valid null-terminated UTF-8 string from Rust
            string message = Marshal.PtrToStringUTF8(messagePtr) ?? string.Empty;
            var logLevel = (LogLevel)level;
            _logHandler?.Invoke(logLevel, message);
        };

        // SAFETY: Getting function pointer for the delegate.
        // The delegate is kept alive by _logCallbackKeepAlive field.
        IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(_logCallbackKeepAlive);
        int result = NativeMethods.SetLogCallback(callbackPtr);
        ErrorHandler.CheckResult(result, "SetLogCallback");
    }

    /// <summary>
    /// Loads tick data from a CSV or Parquet file.
    /// </summary>
    /// <param name="filePath">Path to the data file</param>
    /// <returns>Data quality report with statistics about the loaded data</returns>
    /// <exception cref="DataLoadException">Thrown if data loading fails</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public DataQualityReport LoadData(string filePath)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        unsafe
        {
            DataQualityReport report;

            // SAFETY: report is a stack-allocated struct.
            // Rust will write to it during the call, and we read it immediately after.
            int result = NativeMethods.LoadDataFromFile(
                _handle!.DangerousGetHandle(),
                filePath,
                &report);

            ErrorHandler.CheckResult(result, "LoadData");
            return report;
        }
    }

    /// <summary>
    /// Processes a single tick through the engine.
    /// </summary>
    /// <param name="tick">Tick data to process</param>
    /// <exception cref="InvalidDataException">Thrown if tick data is invalid</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public void ProcessTick(Tick tick)
    {
        ThrowIfDisposed();

        unsafe
        {
            // SAFETY: tick is a stack-allocated struct passed by pointer.
            // Rust reads it during the call and does not retain the pointer.
            int result = NativeMethods.ProcessTick(
                _handle!.DangerousGetHandle(),
                &tick);

            ErrorHandler.CheckResult(result, "ProcessTick");
        }
    }

    /// <summary>
    /// Gets the current account status.
    /// </summary>
    /// <returns>Current account status including balance, equity, and positions</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public AccountStatus GetAccountStatus()
    {
        ThrowIfDisposed();

        unsafe
        {
            AccountStatus status;

            // SAFETY: status is a stack-allocated struct.
            // Rust writes to it during the call, and we read it immediately after.
            int result = NativeMethods.GetAccountStatus(
                _handle!.DangerousGetHandle(),
                &status);

            ErrorHandler.CheckResult(result, "GetAccountStatus");
            return status;
        }
    }

    /// <summary>
    /// Runs the complete backtest on loaded data.
    /// </summary>
    /// <exception cref="EngineException">Thrown if backtest execution fails</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public void RunBacktest()
    {
        ThrowIfDisposed();

        int result = NativeMethods.RunBacktest(_handle!.DangerousGetHandle());
        ErrorHandler.CheckResult(result, "RunBacktest");
    }

    /// <summary>
    /// Throws ObjectDisposedException if the engine has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EngineWrapper));
        }
    }

    /// <summary>
    /// Releases all resources used by the engine.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            // CRITICAL: Clear the log callback in Rust BEFORE releasing the delegate reference.
            // This ensures Rust won't try to call a GC'd delegate.
            if (_logCallbackKeepAlive != null)
            {
                NativeMethods.ClearLogCallback();
            }

            // Release the native handle
            _handle?.Dispose();
            _handle = null;

            // Clear callback references AFTER clearing in Rust
            _logCallbackKeepAlive = null;
            _stringCallbackKeepAlive = null;
            _logHandler = null;

            _disposed = true;
        }
    }

    /// <summary>
    /// Clears the log callback, stopping log message delivery.
    /// </summary>
    public void ClearLogCallback()
    {
        ThrowIfDisposed();
        
        NativeMethods.ClearLogCallback();
        _logCallbackKeepAlive = null;
        _logHandler = null;
    }

    /// <summary>
    /// Gets the last error message from the Rust engine.
    /// </summary>
    /// <returns>The error message, or null if no error</returns>
    public string? GetLastErrorMessage()
    {
        ThrowIfDisposed();

        if (NativeMethods.HasErrorMessage() == 0)
        {
            return null;
        }

        string? result = null;

        // Create callback and keep reference
        _stringCallbackKeepAlive = (messagePtr) =>
        {
            result = Marshal.PtrToStringUTF8(messagePtr);
        };

        IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(_stringCallbackKeepAlive);
        NativeMethods.GetLastErrorMessage(callbackPtr);

        return result;
    }

    /// <summary>
    /// Clears the last error message in the Rust engine.
    /// </summary>
    public void ClearLastErrorMessage()
    {
        ThrowIfDisposed();
        NativeMethods.ClearLastErrorMessage();
    }
}

/// <summary>
/// Log levels matching Rust LogLevel enum.
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4
}
