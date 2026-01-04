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
    /// Lock object for thread-safe callback management.
    /// </summary>
    private readonly object _callbackLock = new();

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
    /// CRITICAL: Keep OHLC callback delegate reference to prevent GC collection.
    /// Used for GetOhlcData callback.
    /// </summary>
    private OhlcCallback? _ohlcCallbackKeepAlive;

    /// <summary>
    /// User-provided log handler.
    /// </summary>
    private Action<LogLevel, string>? _logHandler;

    /// <summary>
    /// Pre-allocated buffer for execution events (zero GC in hot path).
    /// Default capacity of 16 events should be sufficient for most scenarios.
    /// </summary>
    private readonly ExecutionEvent[] _eventBuffer = new ExecutionEvent[16];

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

    // ============================================================================
    // Hybrid Backtest Mode Methods (Requirements: 2.5, 2.6, 8.3)
    // ============================================================================

    /// <summary>
    /// Processes a single tick and returns execution events.
    /// This is the primary method for hybrid backtest mode.
    /// </summary>
    /// <param name="tick">Tick data to process</param>
    /// <param name="events">Output: array of execution events that occurred</param>
    /// <returns>Number of events returned</returns>
    /// <exception cref="InvalidDataException">Thrown if tick data is invalid</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    /// <remarks>
    /// Uses pre-allocated buffer to avoid GC pressure in hot path.
    /// Events include trades, stop-loss triggers, and take-profit triggers.
    /// </remarks>
    public int ProcessTickWithResult(Tick tick, out ExecutionEvent[] events)
    {
        ThrowIfDisposed();

        unsafe
        {
            int eventCount = 0;

            fixed (ExecutionEvent* bufferPtr = _eventBuffer)
            {
                // SAFETY: tick is stack-allocated, buffer is pre-allocated array.
                // Rust writes events into the buffer and sets eventCount.
                int result = NativeMethods.ProcessTickWithResult(
                    _handle!.DangerousGetHandle(),
                    &tick,
                    bufferPtr,
                    _eventBuffer.Length,
                    &eventCount);

                ErrorHandler.CheckResult(result, "ProcessTickWithResult");
            }

            // Copy events to output array (only the actual events, not the whole buffer)
            events = new ExecutionEvent[eventCount];
            Array.Copy(_eventBuffer, events, eventCount);
            return eventCount;
        }
    }

    /// <summary>
    /// Processes a single tick and writes execution events to the provided buffer.
    /// Zero-allocation version for performance-critical scenarios.
    /// </summary>
    /// <param name="tick">Tick data to process</param>
    /// <param name="buffer">Pre-allocated buffer to receive events</param>
    /// <param name="eventCount">Output: number of events written to buffer</param>
    /// <exception cref="InvalidDataException">Thrown if tick data is invalid</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    /// <remarks>
    /// This overload allows callers to provide their own buffer for zero-allocation processing.
    /// </remarks>
    public void ProcessTickWithResult(Tick tick, ExecutionEvent[] buffer, out int eventCount)
    {
        ThrowIfDisposed();

        if (buffer == null || buffer.Length == 0)
        {
            throw new ArgumentException("Buffer cannot be null or empty", nameof(buffer));
        }

        unsafe
        {
            int count = 0;

            fixed (ExecutionEvent* bufferPtr = buffer)
            {
                int result = NativeMethods.ProcessTickWithResult(
                    _handle!.DangerousGetHandle(),
                    &tick,
                    bufferPtr,
                    buffer.Length,
                    &count);

                ErrorHandler.CheckResult(result, "ProcessTickWithResult");
            }

            eventCount = count;
        }
    }

    /// <summary>
    /// Places an order based on external strategy signal.
    /// </summary>
    /// <param name="signal">Order signal: Signal.Buy or Signal.Sell</param>
    /// <param name="price">Current market price for execution</param>
    /// <param name="quantity">Order quantity (if 0, uses strategy params position_size)</param>
    /// <returns>Order result with fill details or rejection reason</returns>
    /// <exception cref="ArgumentException">Thrown if signal is invalid</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public OrderResult PlaceOrder(int signal, double price, double quantity = 0)
    {
        ThrowIfDisposed();

        if (signal != Signal.Buy && signal != Signal.Sell)
        {
            throw new ArgumentException($"Invalid signal: {signal}. Must be Signal.Buy (1) or Signal.Sell (2)", nameof(signal));
        }

        if (price <= 0)
        {
            throw new ArgumentException("Price must be positive", nameof(price));
        }

        unsafe
        {
            OrderResult result;

            int code = NativeMethods.PlaceOrder(
                _handle!.DangerousGetHandle(),
                signal,
                price,
                quantity,
                &result);

            ErrorHandler.CheckResult(code, "PlaceOrder");
            return result;
        }
    }

    /// <summary>
    /// Gets OHLC data for a specified timeframe.
    /// </summary>
    /// <param name="timeframe">Timeframe string (e.g., "1m", "5m", "15m", "1h", "1d")</param>
    /// <returns>Array of OHLC bars</returns>
    /// <exception cref="ArgumentException">Thrown if timeframe is invalid</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public OhlcBar[] GetOhlcData(string timeframe)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(timeframe))
        {
            throw new ArgumentException("Timeframe cannot be null or empty", nameof(timeframe));
        }

        OhlcBar[]? result = null;

        // Create callback and keep reference to prevent GC
        _ohlcCallbackKeepAlive = (barsPtr, count) =>
        {
            if (count <= 0 || barsPtr == IntPtr.Zero)
            {
                result = Array.Empty<OhlcBar>();
                return;
            }

            result = new OhlcBar[count];
            unsafe
            {
                var bars = (OhlcBar*)barsPtr;
                for (int i = 0; i < count; i++)
                {
                    result[i] = bars[i];
                }
            }
        };

        IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(_ohlcCallbackKeepAlive);
        int code = NativeMethods.GetOhlcData(
            _handle!.DangerousGetHandle(),
            timeframe,
            callbackPtr);

        ErrorHandler.CheckResult(code, "GetOhlcData");
        return result ?? Array.Empty<OhlcBar>();
    }

    /// <summary>
    /// Loads CSV file with custom column mapping.
    /// </summary>
    /// <param name="filePath">Path to the CSV file</param>
    /// <param name="mapping">Column mapping configuration</param>
    /// <returns>Data quality report with statistics about the loaded data</returns>
    /// <exception cref="DataLoadException">Thrown if data loading fails</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public DataQualityReport LoadCsvWithMapping(string filePath, CsvMapping mapping)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        unsafe
        {
            DataQualityReport report;

            int result = NativeMethods.LoadCsvWithMapping(
                _handle!.DangerousGetHandle(),
                filePath,
                &mapping,
                &report);

            ErrorHandler.CheckResult(result, "LoadCsvWithMapping");
            return report;
        }
    }

    /// <summary>
    /// Fast-forwards to a specific tick index without invoking callbacks.
    /// Allows efficient seeking in replay mode.
    /// </summary>
    /// <param name="targetIndex">The tick index to fast-forward to (0-based)</param>
    /// <returns>Timestamp at the target index</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if targetIndex is out of bounds</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public long FastForwardTo(long targetIndex)
    {
        ThrowIfDisposed();

        if (targetIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex), "Target index must be non-negative");
        }

        unsafe
        {
            long timestamp;

            int result = NativeMethods.FastForwardTo(
                _handle!.DangerousGetHandle(),
                targetIndex,
                &timestamp);

            ErrorHandler.CheckResult(result, "FastForwardTo");
            return timestamp;
        }
    }

    /// <summary>
    /// Gets the current tick index in the loaded data.
    /// </summary>
    /// <returns>Current tick index (0-based)</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public long GetCurrentTickIndex()
    {
        ThrowIfDisposed();

        unsafe
        {
            long index;

            int result = NativeMethods.GetCurrentTickIndex(
                _handle!.DangerousGetHandle(),
                &index);

            ErrorHandler.CheckResult(result, "GetCurrentTickIndex");
            return index;
        }
    }

    /// <summary>
    /// Gets the total number of ticks loaded.
    /// </summary>
    /// <returns>Total tick count</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the engine has been disposed</exception>
    public long GetTickCount()
    {
        ThrowIfDisposed();

        unsafe
        {
            long count;

            int result = NativeMethods.GetTickCount(
                _handle!.DangerousGetHandle(),
                &count);

            ErrorHandler.CheckResult(result, "GetTickCount");
            return count;
        }
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
            // Use lock to prevent race condition with callback invocation
            lock (_callbackLock)
            {
                // CRITICAL: Clear the log callback in Rust BEFORE releasing the delegate reference.
                // This ensures Rust won't try to call a GC'd delegate.
                if (_logCallbackKeepAlive != null)
                {
                    NativeMethods.ClearLogCallback();
                    _logCallbackKeepAlive = null;
                }

                _stringCallbackKeepAlive = null;
                _ohlcCallbackKeepAlive = null;
                _logHandler = null;
            }

            // Release the native handle (outside lock to avoid potential deadlock)
            _handle?.Dispose();
            _handle = null;

            _disposed = true;
        }
    }

    /// <summary>
    /// Clears the log callback, stopping log message delivery.
    /// </summary>
    public void ClearLogCallback()
    {
        ThrowIfDisposed();
        
        lock (_callbackLock)
        {
            NativeMethods.ClearLogCallback();
            _logCallbackKeepAlive = null;
            _logHandler = null;
        }
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
