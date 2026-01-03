using AegisQuant.Interop;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Services;
using ScottPlot;

namespace AegisQuant.UI.Models;

/// <summary>
/// Event arguments for OHLC data loaded.
/// </summary>
public class OhlcDataLoadedEventArgs : EventArgs
{
    public List<OHLC> OhlcData { get; }
    public List<double> Volumes { get; }

    public OhlcDataLoadedEventArgs(List<OHLC> ohlcData, List<double> volumes)
    {
        OhlcData = ohlcData;
        Volumes = volumes;
    }
}

/// <summary>
/// Event arguments for status updates during backtest execution.
/// </summary>
public class StatusUpdatedEventArgs : EventArgs
{
    public AccountStatus Status { get; }
    public double Progress { get; }
    public int CurrentTick { get; }
    public int TotalTicks { get; }

    public StatusUpdatedEventArgs(AccountStatus status, double progress, int currentTick, int totalTicks)
    {
        Status = status;
        Progress = progress;
        CurrentTick = currentTick;
        TotalTicks = totalTicks;
    }
}

/// <summary>
/// Event arguments for log messages received from the engine.
/// </summary>
public class LogReceivedEventArgs : EventArgs
{
    public LogLevel Level { get; }
    public string Message { get; }
    public DateTime Timestamp { get; }

    public LogReceivedEventArgs(LogLevel level, string message)
    {
        Level = level;
        Message = message;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Event arguments for backtest completion.
/// </summary>
public class BacktestCompletedEventArgs : EventArgs
{
    public bool Success { get; }
    public AccountStatus FinalStatus { get; }
    public string? ErrorMessage { get; }
    public TimeSpan Duration { get; }

    public BacktestCompletedEventArgs(bool success, AccountStatus finalStatus, TimeSpan duration, string? errorMessage = null)
    {
        Success = success;
        FinalStatus = finalStatus;
        Duration = duration;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Event arguments for strategy signal generation.
/// </summary>
public class StrategySignalEventArgs : EventArgs
{
    public Signal Signal { get; }
    public double Price { get; }
    public long Timestamp { get; }

    public StrategySignalEventArgs(Signal signal, double price, long timestamp)
    {
        Signal = signal;
        Price = price;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Service for managing backtest operations.
/// Encapsulates EngineWrapper calls and provides async execution.
/// Supports both built-in Rust strategies and external C#/Python strategies.
/// </summary>
public class BacktestService : IDisposable
{
    private EngineWrapper? _engine;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;
    private bool _isRunning;

    // Strategy management
    private readonly StrategyManagerService _strategyManager;
    private readonly StrategyContext _strategyContext;
    private bool _useExternalStrategy;

    /// <summary>
    /// Event raised when account status is updated during backtest.
    /// </summary>
    public event EventHandler<StatusUpdatedEventArgs>? OnStatusUpdated;

    /// <summary>
    /// Event raised when a log message is received from the engine.
    /// </summary>
    public event EventHandler<LogReceivedEventArgs>? OnLogReceived;

    /// <summary>
    /// Event raised when the backtest completes.
    /// </summary>
    public event EventHandler<BacktestCompletedEventArgs>? OnBacktestCompleted;

    /// <summary>
    /// Event raised when an external strategy generates a signal.
    /// </summary>
    public event EventHandler<StrategySignalEventArgs>? OnStrategySignal;

    /// <summary>
    /// Event raised when OHLC data is loaded.
    /// </summary>
    public event EventHandler<OhlcDataLoadedEventArgs>? OnOhlcDataLoaded;

    /// <summary>
    /// Gets whether a backtest is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets the current strategy parameters (for built-in strategy).
    /// </summary>
    public StrategyParams CurrentParams { get; private set; } = StrategyParams.Default;

    /// <summary>
    /// Gets the current risk configuration.
    /// </summary>
    public RiskConfig CurrentRiskConfig { get; private set; } = RiskConfig.Default;

    /// <summary>
    /// Gets the data quality report from the last data load.
    /// </summary>
    public DataQualityReport? LastDataQualityReport { get; private set; }

    /// <summary>
    /// Gets the OHLC data from the last data load.
    /// </summary>
    public List<OHLC>? OhlcData { get; private set; }

    /// <summary>
    /// Gets the volume data from the last data load.
    /// </summary>
    public List<double>? VolumeData { get; private set; }

    /// <summary>
    /// Gets the strategy manager service.
    /// </summary>
    public StrategyManagerService StrategyManager => _strategyManager;

    /// <summary>
    /// Gets whether an external strategy is being used.
    /// </summary>
    public bool UseExternalStrategy => _useExternalStrategy;

    /// <summary>
    /// Gets the current external strategy name, or null if using built-in.
    /// </summary>
    public string? CurrentStrategyName => _strategyManager.CurrentStrategy?.Name;

    public BacktestService()
    {
        _strategyManager = new StrategyManagerService();
        _strategyContext = new StrategyContext();

        // Subscribe to strategy events
        _strategyManager.StrategyLoaded += OnExternalStrategyLoaded;
        _strategyManager.StrategyError += OnExternalStrategyError;
    }

    /// <summary>
    /// Initializes the engine with the specified parameters.
    /// </summary>
    public void Initialize(StrategyParams parameters, RiskConfig riskConfig)
    {
        ThrowIfDisposed();

        // Dispose existing engine if any
        _engine?.Dispose();

        CurrentParams = parameters;
        CurrentRiskConfig = riskConfig;

        _engine = new EngineWrapper(parameters, riskConfig);

        // Set up log callback
        _engine.SetLogCallback((level, message) =>
        {
            OnLogReceived?.Invoke(this, new LogReceivedEventArgs(level, message));
        });

        RaiseLog(LogLevel.Info, "Engine initialized successfully");
    }

    /// <summary>
    /// Loads an external strategy from a file.
    /// </summary>
    /// <param name="filePath">Path to the strategy file (.json or .py)</param>
    public async Task LoadExternalStrategyAsync(string filePath)
    {
        ThrowIfDisposed();

        RaiseLog(LogLevel.Info, $"Loading external strategy from: {filePath}");

        try
        {
            await _strategyManager.LoadFromFileAsync(filePath);
            _useExternalStrategy = true;
            RaiseLog(LogLevel.Info, $"External strategy loaded: {_strategyManager.CurrentStrategy?.Name}");
        }
        catch (Exception ex)
        {
            RaiseLog(LogLevel.Error, $"Failed to load strategy: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Switches back to using the built-in Rust strategy.
    /// </summary>
    public void UseBuiltInStrategy()
    {
        _strategyManager.UnloadStrategy();
        _useExternalStrategy = false;
        RaiseLog(LogLevel.Info, "Switched to built-in DualMA strategy");
    }

    /// <summary>
    /// Sets an external strategy for backtesting.
    /// </summary>
    /// <param name="strategy">The strategy to use</param>
    public void SetExternalStrategy(IStrategy strategy)
    {
        _strategyManager.SetStrategy(strategy);
        _useExternalStrategy = true;
        RaiseLog(LogLevel.Info, $"External strategy set: {strategy.Name}");
    }

    /// <summary>
    /// Clears the external strategy and reverts to built-in.
    /// </summary>
    public void ClearExternalStrategy()
    {
        UseBuiltInStrategy();
    }

    /// <summary>
    /// Loads data from a file asynchronously.
    /// </summary>
    /// <param name="filePath">Path to the data file (CSV or Parquet)</param>
    /// <returns>Data quality report</returns>
    public async Task<DataQualityReport> LoadDataAsync(string filePath)
    {
        ThrowIfDisposed();
        EnsureEngineInitialized();

        RaiseLog(LogLevel.Info, $"Loading data from: {filePath}");

        // Run on thread pool to avoid blocking UI
        var report = await Task.Run(() => _engine!.LoadData(filePath));

        LastDataQualityReport = report;

        // Reset strategy context for new data
        _strategyContext.Reset();

        // Convert tick data to OHLC for charting
        await ConvertDataToOhlcAsync(filePath);

        RaiseLog(LogLevel.Info, $"Data loaded: {report.ValidTicks} valid ticks, {report.InvalidTicks} invalid, {report.AnomalyTicks} anomalies");

        return report;
    }

    /// <summary>
    /// Converts tick data from file to OHLC format for charting.
    /// </summary>
    private async Task ConvertDataToOhlcAsync(string filePath)
    {
        try
        {
            var ohlcData = new List<OHLC>();
            var volumeData = new List<double>();

            await Task.Run(() =>
            {
                // Read CSV file and convert to OHLC
                var lines = System.IO.File.ReadAllLines(filePath);
                if (lines.Length <= 1) return;

                // Parse header to find column indices
                var header = lines[0].Split(',');
                int timestampIdx = Array.FindIndex(header, h => h.Trim().ToLower() == "timestamp");
                int priceIdx = Array.FindIndex(header, h => h.Trim().ToLower() == "price");
                int volumeIdx = Array.FindIndex(header, h => h.Trim().ToLower() == "volume");

                if (timestampIdx < 0 || priceIdx < 0)
                {
                    RaiseLog(LogLevel.Warn, "CSV file missing required columns (timestamp, price)");
                    return;
                }

                // Group ticks by minute for OHLC aggregation
                var ticksByMinute = new Dictionary<DateTime, List<(double price, double volume)>>();

                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = lines[i].Split(',');
                    if (parts.Length <= Math.Max(timestampIdx, priceIdx)) continue;

                    if (!long.TryParse(parts[timestampIdx].Trim(), out var timestamp)) continue;
                    if (!double.TryParse(parts[priceIdx].Trim(), out var price)) continue;
                    
                    double volume = 0;
                    if (volumeIdx >= 0 && parts.Length > volumeIdx)
                    {
                        double.TryParse(parts[volumeIdx].Trim(), out volume);
                    }

                    // Convert nanoseconds to DateTime and round to minute
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp / 1_000_000).DateTime;
                    var minuteKey = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0);

                    if (!ticksByMinute.ContainsKey(minuteKey))
                    {
                        ticksByMinute[minuteKey] = new List<(double, double)>();
                    }
                    ticksByMinute[minuteKey].Add((price, volume));
                }

                // Convert grouped ticks to OHLC
                foreach (var kvp in ticksByMinute.OrderBy(k => k.Key))
                {
                    var ticks = kvp.Value;
                    if (ticks.Count == 0) continue;

                    var open = ticks.First().price;
                    var close = ticks.Last().price;
                    var high = ticks.Max(t => t.price);
                    var low = ticks.Min(t => t.price);
                    var totalVolume = ticks.Sum(t => t.volume);

                    ohlcData.Add(new OHLC(open, high, low, close, kvp.Key, TimeSpan.FromMinutes(1)));
                    volumeData.Add(totalVolume);
                }
            });

            OhlcData = ohlcData;
            VolumeData = volumeData;

            if (ohlcData.Count > 0)
            {
                RaiseLog(LogLevel.Info, $"Converted to {ohlcData.Count} OHLC bars");
                OnOhlcDataLoaded?.Invoke(this, new OhlcDataLoadedEventArgs(ohlcData, volumeData));
            }
        }
        catch (Exception ex)
        {
            RaiseLog(LogLevel.Warn, $"Failed to convert data to OHLC: {ex.Message}");
            OhlcData = new List<OHLC>();
            VolumeData = new List<double>();
        }
    }

    /// <summary>
    /// Gets the OHLC data for charting.
    /// </summary>
    public List<OHLC> GetOhlcData() => OhlcData ?? new List<OHLC>();

    /// <summary>
    /// Gets the volume data for charting.
    /// </summary>
    public List<double> GetVolumeData() => VolumeData ?? new List<double>();

    /// <summary>
    /// Runs the backtest asynchronously with progress reporting.
    /// </summary>
    /// <param name="updateIntervalMs">Interval in milliseconds between status updates</param>
    /// <returns>Task representing the backtest operation</returns>
    public async Task RunBacktestAsync(int updateIntervalMs = 100)
    {
        ThrowIfDisposed();
        EnsureEngineInitialized();

        if (_isRunning)
        {
            throw new InvalidOperationException("A backtest is already running");
        }

        _isRunning = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;
        var startTime = DateTime.Now;

        RaiseLog(LogLevel.Info, "Starting backtest...");

        try
        {
            // Start the backtest on a background thread
            var backtestTask = Task.Run(() =>
            {
                _engine!.RunBacktest();
            }, cancellationToken);

            // Poll for status updates while backtest is running
            var statusUpdateTask = Task.Run(async () =>
            {
                while (!backtestTask.IsCompleted && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var status = _engine!.GetAccountStatus();
                        var totalTicks = LastDataQualityReport?.ValidTicks ?? 0;

                        // Calculate progress (simplified - actual progress would need tick counter from engine)
                        var progress = backtestTask.IsCompleted ? 100.0 : 50.0; // Placeholder

                        OnStatusUpdated?.Invoke(this, new StatusUpdatedEventArgs(
                            status,
                            progress,
                            0,
                            (int)totalTicks));
                    }
                    catch
                    {
                        // Ignore errors during status polling
                    }

                    await Task.Delay(updateIntervalMs, cancellationToken);
                }
            }, cancellationToken);

            // Wait for backtest to complete
            await backtestTask;

            // Get final status
            var finalStatus = _engine!.GetAccountStatus();
            var duration = DateTime.Now - startTime;

            RaiseLog(LogLevel.Info, $"Backtest completed in {duration.TotalSeconds:F2}s. Final equity: {finalStatus.Equity:F2}");

            OnBacktestCompleted?.Invoke(this, new BacktestCompletedEventArgs(
                success: true,
                finalStatus: finalStatus,
                duration: duration));
        }
        catch (OperationCanceledException)
        {
            var duration = DateTime.Now - startTime;
            var status = _engine!.GetAccountStatus();

            RaiseLog(LogLevel.Warn, "Backtest cancelled by user");

            OnBacktestCompleted?.Invoke(this, new BacktestCompletedEventArgs(
                success: false,
                finalStatus: status,
                duration: duration,
                errorMessage: "Backtest cancelled by user"));
        }
        catch (Exception ex)
        {
            var duration = DateTime.Now - startTime;
            var status = new AccountStatus();

            try
            {
                status = _engine!.GetAccountStatus();
            }
            catch { }

            RaiseLog(LogLevel.Error, $"Backtest failed: {ex.Message}");

            OnBacktestCompleted?.Invoke(this, new BacktestCompletedEventArgs(
                success: false,
                finalStatus: status,
                duration: duration,
                errorMessage: ex.Message));
        }
        finally
        {
            _isRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    /// Stops the currently running backtest.
    /// </summary>
    public void StopBacktest()
    {
        if (_isRunning && _cancellationTokenSource != null)
        {
            RaiseLog(LogLevel.Info, "Stopping backtest...");
            _cancellationTokenSource.Cancel();
        }
    }

    /// <summary>
    /// Gets the current account status.
    /// </summary>
    public AccountStatus GetCurrentStatus()
    {
        ThrowIfDisposed();
        EnsureEngineInitialized();
        return _engine!.GetAccountStatus();
    }

    private void EnsureEngineInitialized()
    {
        if (_engine == null)
        {
            throw new InvalidOperationException("Engine not initialized. Call Initialize() first.");
        }
    }

    private void RaiseLog(LogLevel level, string message)
    {
        OnLogReceived?.Invoke(this, new LogReceivedEventArgs(level, message));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BacktestService));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopBacktest();
            _strategyManager.StrategyLoaded -= OnExternalStrategyLoaded;
            _strategyManager.StrategyError -= OnExternalStrategyError;
            _strategyManager.Dispose();
            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
    }

    private void OnExternalStrategyLoaded(object? sender, StrategyLoadedEventArgs e)
    {
        RaiseLog(LogLevel.Info, $"Strategy loaded: {e.Strategy.Name} ({e.Strategy.Type})");
    }

    private void OnExternalStrategyError(object? sender, StrategyErrorEventArgs e)
    {
        RaiseLog(LogLevel.Error, $"Strategy error: {e.Message}");
    }

    /// <summary>
    /// Processes a single tick with the external strategy.
    /// </summary>
    /// <param name="tick">Tick data to process</param>
    /// <returns>Signal generated by the strategy</returns>
    public Signal ProcessTickWithExternalStrategy(AegisQuant.Interop.Tick tick)
    {
        if (!_useExternalStrategy || _strategyManager.CurrentStrategy == null)
        {
            return Signal.None;
        }

        try
        {
            // Update strategy context
            _strategyContext.UpdateTick(tick);
            _strategyContext.UpdateAccount(_engine?.GetAccountStatus() ?? new AccountStatus());

            // Get signal from external strategy
            var signal = _strategyManager.ProcessTick(_strategyContext);

            if (signal != Signal.None)
            {
                OnStrategySignal?.Invoke(this, new StrategySignalEventArgs(signal, tick.Price, tick.Timestamp));
            }

            return signal;
        }
        catch (Exception ex)
        {
            RaiseLog(LogLevel.Error, $"External strategy error: {ex.Message}");
            return Signal.None;
        }
    }
}
