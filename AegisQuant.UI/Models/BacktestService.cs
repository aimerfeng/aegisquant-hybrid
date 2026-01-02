using AegisQuant.Interop;

namespace AegisQuant.UI.Models;

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
/// Service for managing backtest operations.
/// Encapsulates EngineWrapper calls and provides async execution.
/// </summary>
public class BacktestService : IDisposable
{
    private EngineWrapper? _engine;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;
    private bool _isRunning;

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
    /// Gets whether a backtest is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets the current strategy parameters.
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

        RaiseLog(LogLevel.Info, $"Data loaded: {report.ValidTicks} valid ticks, {report.InvalidTicks} invalid, {report.AnomalyTicks} anomalies");

        return report;
    }

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
            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
    }
}
