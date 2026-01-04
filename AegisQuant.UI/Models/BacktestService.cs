using AegisQuant.Interop;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Services;
using AegisQuant.UI.Services.Interfaces;
using ScottPlot;
using Interop = AegisQuant.Interop;

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
    public Strategy.Signal Signal { get; }
    public double Price { get; }
    public long Timestamp { get; }

    public StrategySignalEventArgs(Strategy.Signal signal, double price, long timestamp)
    {
        Signal = signal;
        Price = price;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Display update data for UI thread communication.
/// Requirements: 9.10, 9.11 - Channel-based display updates
/// </summary>
public class DisplayUpdate
{
    public int BarIndex { get; set; }
    public AccountStatus Status { get; set; }
    public double Progress { get; set; }
    public OHLC? NewBar { get; set; }
    public Strategy.Signal? Signal { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// Service for managing backtest operations.
/// Encapsulates EngineWrapper calls and provides async execution.
/// Supports both built-in Rust strategies and external C#/Python strategies.
/// Implements state machine for service lifecycle management (Requirements: 12.1, 12.2).
/// </summary>
public class BacktestService : IBacktestService
{
    private EngineWrapper? _engine;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;
    private bool _isRunning;
    private ServiceState _state = ServiceState.Ready;
    private readonly object _stateLock = new();

    // Strategy management
    private readonly StrategyManagerService _strategyManager;
    private readonly StrategyContext _strategyContext;
    private bool _useExternalStrategy;
    
    // Visual mode driver loop support (Requirements: 9.9, 9.10, 9.11)
    private readonly System.Threading.Channels.Channel<DisplayUpdate> _displayChannel;
    private readonly ExecutionEvent[] _eventBuffer = new ExecutionEvent[16]; // Pre-allocated buffer
    private int _driverLoopThreadId = -1; // Track driver loop thread for verification

    /// <summary>
    /// Gets the display update channel reader for UI consumption.
    /// Requirements: 9.10 - Channel-based display updates
    /// </summary>
    public System.Threading.Channels.ChannelReader<DisplayUpdate> DisplayUpdates => _displayChannel.Reader;

    /// <summary>
    /// Event raised when an execution event occurs during backtest.
    /// </summary>
    public event EventHandler<ExecutionEventArgs>? OnExecution;

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
    /// Event raised when service state changes.
    /// </summary>
    public event EventHandler<StateChangedEventArgs>? OnStateChanged;

    /// <summary>
    /// Gets or sets the current backtest execution mode.
    /// Requirements: 1.1, 1.2, 1.3 - Mode selection based on strategy type
    /// </summary>
    public BacktestMode Mode { get; set; } = BacktestMode.HighSpeed;

    /// <summary>
    /// Automatically selects the appropriate backtest mode based on strategy type.
    /// Requirements: 1.2 - WHEN an external strategy is loaded, THE BacktestService SHALL default to Visual_Mode
    /// Requirements: 1.3 - WHEN the built-in Rust strategy is selected, THE BacktestService SHALL use HighSpeed_Mode
    /// </summary>
    public void AutoSelectMode()
    {
        if (_useExternalStrategy && _strategyManager.HasExternalStrategy)
        {
            Mode = BacktestMode.Visual;
            RaiseLog(LogLevel.Info, "Auto-selected Visual mode for external strategy");
        }
        else
        {
            Mode = BacktestMode.HighSpeed;
            RaiseLog(LogLevel.Info, "Auto-selected HighSpeed mode for built-in strategy");
        }
    }

    /// <summary>
    /// Gets the recommended mode based on current strategy configuration.
    /// </summary>
    public BacktestMode RecommendedMode => 
        (_useExternalStrategy && _strategyManager.HasExternalStrategy) 
            ? BacktestMode.Visual 
            : BacktestMode.HighSpeed;

    /// <summary>
    /// Gets the current service state.
    /// </summary>
    public ServiceState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Transitions to a new state with validation.
    /// Requirements: 12.1, 12.2 - State machine transitions
    /// </summary>
    /// <param name="newState">Target state</param>
    /// <param name="reason">Reason for transition (optional)</param>
    /// <returns>True if transition was successful</returns>
    private bool TransitionTo(ServiceState newState, string? reason = null)
    {
        lock (_stateLock)
        {
            var oldState = _state;
            
            // Validate transition
            if (!IsValidTransition(oldState, newState))
            {
                RaiseLog(LogLevel.Warn, $"Invalid state transition: {oldState} -> {newState}");
                return false;
            }
            
            _state = newState;
            RaiseLog(LogLevel.Debug, $"State transition: {oldState} -> {newState}" + (reason != null ? $" ({reason})" : ""));
            
            // Raise event outside lock to prevent deadlocks
            Task.Run(() => OnStateChanged?.Invoke(this, new StateChangedEventArgs(oldState, newState, reason)));
            
            return true;
        }
    }

    /// <summary>
    /// Validates if a state transition is allowed.
    /// State machine rules:
    /// - Ready -> Running (start backtest)
    /// - Running -> Ready (backtest completed)
    /// - Running -> Faulted (error occurred)
    /// - Faulted -> Ready (reset called)
    /// - Any -> Faulted (critical error)
    /// </summary>
    private static bool IsValidTransition(ServiceState from, ServiceState to)
    {
        return (from, to) switch
        {
            (ServiceState.Ready, ServiceState.Running) => true,
            (ServiceState.Running, ServiceState.Ready) => true,
            (ServiceState.Running, ServiceState.Faulted) => true,
            (ServiceState.Faulted, ServiceState.Ready) => true,
            (_, ServiceState.Faulted) => true, // Any state can transition to Faulted
            _ => false
        };
    }

    /// <summary>
    /// Forces transition to Faulted state from any state.
    /// Requirements: 12.1 - WHEN a critical error occurs, THE BacktestService SHALL enter Faulted state
    /// </summary>
    private void TransitionToFaulted(string reason)
    {
        lock (_stateLock)
        {
            var oldState = _state;
            _state = ServiceState.Faulted;
            _isRunning = false;
            
            RaiseLog(LogLevel.Error, $"Service faulted: {reason}");
            Task.Run(() => OnStateChanged?.Invoke(this, new StateChangedEventArgs(oldState, ServiceState.Faulted, reason)));
        }
    }

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
    
    /// <summary>
    /// Gets the thread ID of the driver loop (for testing thread isolation).
    /// Returns -1 if driver loop is not running.
    /// </summary>
    public int DriverLoopThreadId => _driverLoopThreadId;

    public BacktestService()
    {
        _strategyManager = new StrategyManagerService();
        _strategyContext = new StrategyContext();
        
        // Initialize bounded channel for display updates (Requirements: 9.10)
        _displayChannel = System.Threading.Channels.Channel.CreateBounded<DisplayUpdate>(
            new System.Threading.Channels.BoundedChannelOptions(1000)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
            });

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
            
            // Requirements: 1.2 - Auto-select Visual mode for external strategies
            AutoSelectMode();
            
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
    /// Requirements: 1.3 - WHEN the built-in Rust strategy is selected, THE BacktestService SHALL use HighSpeed_Mode
    /// </summary>
    public void UseBuiltInStrategy()
    {
        _strategyManager.UnloadStrategy();
        _useExternalStrategy = false;
        
        // Requirements: 1.3 - Auto-select HighSpeed mode for built-in strategy
        AutoSelectMode();
        
        RaiseLog(LogLevel.Info, "Switched to built-in DualMA strategy");
    }

    /// <summary>
    /// Sets an external strategy for backtesting.
    /// Requirements: 1.2 - WHEN an external strategy is loaded, THE BacktestService SHALL default to Visual_Mode
    /// </summary>
    /// <param name="strategy">The strategy to use</param>
    public void SetExternalStrategy(IStrategy strategy)
    {
        _strategyManager.SetStrategy(strategy);
        _useExternalStrategy = true;
        
        // Requirements: 1.2 - Auto-select Visual mode for external strategies
        AutoSelectMode();
        
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
                    // Auto-detect nanoseconds (19 digits) vs milliseconds (13 digits)
                    var ms = timestamp > 9999999999999L ? timestamp / 1_000_000 : timestamp;
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).DateTime;
                    var minuteKey = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0);

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

                    ohlcData.Add(new OHLC(open, high, low, close, kvp.Key, TimeSpan.FromDays(1)));
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
    /// Requirements: 12.2 - WHILE in Faulted state, THE BacktestService SHALL reject new backtest requests
    /// Requirements: 1.5, 1.6, 1.7 - Visual mode driver loop
    /// </summary>
    /// <param name="token">Cancellation token</param>
    /// <returns>Task representing the backtest operation</returns>
    public async Task RunBacktestAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        EnsureEngineInitialized();

        // Requirements: 12.2 - Reject requests in Faulted state
        if (State == ServiceState.Faulted)
        {
            throw new InvalidOperationException("Service is faulted. Call Reset() first.");
        }

        if (_isRunning)
        {
            throw new InvalidOperationException("A backtest is already running");
        }

        // Transition to Running state
        if (!TransitionTo(ServiceState.Running, "Starting backtest"))
        {
            throw new InvalidOperationException("Cannot start backtest from current state");
        }

        _isRunning = true;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancellationToken = _cancellationTokenSource.Token;
        var startTime = DateTime.Now;

        RaiseLog(LogLevel.Info, $"Starting backtest in {Mode} mode...");

        try
        {
            // Choose execution mode based on Mode property
            if (Mode == BacktestMode.HighSpeed && !_useExternalStrategy)
            {
                // HighSpeed mode: Rust-driven backtest
                await RunHighSpeedModeAsync(cancellationToken);
            }
            else
            {
                // Visual mode: C#-driven backtest with external strategy
                // Requirements: 1.5, 1.6, 1.7, 9.9
                await RunVisualModeAsync(cancellationToken);
            }

            // Get final status
            var finalStatus = _engine!.GetAccountStatus();
            var duration = DateTime.Now - startTime;

            RaiseLog(LogLevel.Info, $"Backtest completed in {duration.TotalSeconds:F2}s. Final equity: {finalStatus.Equity:F2}");

            OnBacktestCompleted?.Invoke(this, new BacktestCompletedEventArgs(
                success: true,
                finalStatus: finalStatus,
                duration: duration));
                
            // Transition back to Ready
            TransitionTo(ServiceState.Ready, "Backtest completed successfully");
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
                
            // Transition back to Ready on cancellation
            TransitionTo(ServiceState.Ready, "Backtest cancelled");
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

            // Requirements: 12.1 - Transition to Faulted on critical error
            TransitionToFaulted($"Backtest failed: {ex.Message}");

            OnBacktestCompleted?.Invoke(this, new BacktestCompletedEventArgs(
                success: false,
                finalStatus: status,
                duration: duration,
                errorMessage: ex.Message));
        }
        finally
        {
            _isRunning = false;
            _driverLoopThreadId = -1;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    /// Runs the backtest in HighSpeed mode (Rust-driven).
    /// </summary>
    private async Task RunHighSpeedModeAsync(CancellationToken cancellationToken)
    {
        const int updateIntervalMs = 100;
        
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
                    var progress = backtestTask.IsCompleted ? 100.0 : 50.0;

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
    }

    /// <summary>
    /// Runs the backtest in Visual mode (C#-driven driver loop).
    /// Requirements: 1.5, 1.6, 1.7, 9.9 - Visual mode with external strategy
    /// </summary>
    private async Task RunVisualModeAsync(CancellationToken cancellationToken)
    {
        // Run driver loop on background thread (Requirements: 9.9)
        await Task.Run(() => RunDriverLoop(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// The C# driver loop for Visual mode.
    /// Requirements: 1.5, 1.6, 1.7 - Iterate through bars and invoke external strategy
    /// Requirements: 9.9 - Run on dedicated background thread
    /// </summary>
    private void RunDriverLoop(CancellationToken cancellationToken)
    {
        // Record thread ID for verification (Requirements: 9.9)
        _driverLoopThreadId = Environment.CurrentManagedThreadId;
        
        var bars = OhlcData ?? new List<OHLC>();
        var volumes = VolumeData ?? new List<double>();
        
        if (bars.Count == 0)
        {
            RaiseLog(LogLevel.Warn, "No OHLC data available for Visual mode backtest");
            return;
        }
        
        RaiseLog(LogLevel.Info, $"Visual mode: Processing {bars.Count} bars");
        
        // Reset strategy context
        _strategyContext.Reset();
        
        int signalCount = 0;
        int barInvocationCount = 0;
        
        for (int i = 0; i < bars.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var bar = bars[i];
            var volume = i < volumes.Count ? volumes[i] : 0;
            
            // Convert OHLC to tick for engine processing
            var tick = new AegisQuant.Interop.Tick
            {
                Timestamp = new DateTimeOffset(bar.DateTime).ToUnixTimeMilliseconds() * 1_000_000, // Convert to nanoseconds
                Price = bar.Close,
                Volume = volume
            };
            
            // 1. Feed tick to Rust engine with pre-allocated buffer (Requirements: 2.1)
            int eventCount = 0;
            try
            {
                _engine!.ProcessTickWithResult(tick, _eventBuffer, out eventCount);
            }
            catch (Exception ex)
            {
                RaiseLog(LogLevel.Warn, $"ProcessTick error at bar {i}: {ex.Message}");
            }
            
            // 2. Process any execution events (zero allocation)
            for (int j = 0; j < eventCount; j++)
            {
                var evt = _eventBuffer[j];
                OnExecution?.Invoke(this, new ExecutionEventArgs(
                    (Services.Interfaces.ExecutionEventType)evt.EventType,
                    evt.Timestamp,
                    evt.Price,
                    evt.Quantity,
                    evt.Side));
            }
            
            // 3. Update strategy context (Requirements: 2.7)
            _strategyContext.UpdateTick(tick);
            _strategyContext.AddOhlc(bar);
            try
            {
                _strategyContext.UpdateAccount(_engine!.GetAccountStatus());
            }
            catch { }
            
            // 4. Call external strategy (Requirements: 1.6, 2.2)
            Strategy.Signal signal = Strategy.Signal.None;
            if (_useExternalStrategy && _strategyManager.CurrentStrategy != null)
            {
                barInvocationCount++;
                signal = _strategyManager.ProcessTick(_strategyContext);
            }
            
            // 5. Place order if signal generated (Requirements: 1.7, 2.3, 2.4)
            if (signal != Strategy.Signal.None)
            {
                signalCount++;
                try
                {
                    // Convert Strategy.Signal to Interop.Signal
                    int interopSignal = signal == Strategy.Signal.Buy ? AegisQuant.Interop.Signal.Buy : AegisQuant.Interop.Signal.Sell;
                    var orderResult = _engine!.PlaceOrder(interopSignal, bar.Close);
                    
                    OnStrategySignal?.Invoke(this, new StrategySignalEventArgs(signal, bar.Close, tick.Timestamp));
                    
                    RaiseLog(LogLevel.Debug, $"Signal {signal} at bar {i}, price {bar.Close:F2}, accepted: {orderResult.IsAccepted}");
                }
                catch (Exception ex)
                {
                    RaiseLog(LogLevel.Warn, $"PlaceOrder error: {ex.Message}");
                }
            }
            
            // 6. Queue display update (throttled - every 100 bars or on signal)
            if (i % 100 == 0 || signal != Strategy.Signal.None)
            {
                var update = new DisplayUpdate
                {
                    BarIndex = i,
                    Status = _engine!.GetAccountStatus(),
                    Progress = (double)(i + 1) / bars.Count * 100,
                    NewBar = bar,
                    Signal = signal != Strategy.Signal.None ? signal : null,
                    Timestamp = tick.Timestamp
                };
                
                // Non-blocking write to channel
                _displayChannel.Writer.TryWrite(update);
                
                // Also raise status update event
                OnStatusUpdated?.Invoke(this, new StatusUpdatedEventArgs(
                    update.Status,
                    update.Progress,
                    i,
                    bars.Count));
            }
        }
        
        RaiseLog(LogLevel.Info, $"Visual mode completed: {barInvocationCount} strategy invocations, {signalCount} signals generated");
    }

    /// <summary>
    /// Resets the service from faulted state.
    /// Requirements: 12.4, 12.5 - Reset SHALL dispose and reinitialize the engine, restore Ready state
    /// </summary>
    public void Reset()
    {
        ThrowIfDisposed();
        
        StopBacktest();
        _engine?.Dispose();
        _engine = new EngineWrapper(CurrentParams, CurrentRiskConfig);
        
        // Set up log callback
        _engine.SetLogCallback((level, message) =>
        {
            OnLogReceived?.Invoke(this, new LogReceivedEventArgs(level, message));
        });
        
        // Reset strategy context
        _strategyContext.Reset();
        
        // Transition to Ready state
        lock (_stateLock)
        {
            var oldState = _state;
            _state = ServiceState.Ready;
            _isRunning = false;
            
            RaiseLog(LogLevel.Info, "Service reset successfully");
            Task.Run(() => OnStateChanged?.Invoke(this, new StateChangedEventArgs(oldState, ServiceState.Ready, "Service reset")));
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
    public Strategy.Signal ProcessTickWithExternalStrategy(AegisQuant.Interop.Tick tick)
    {
        if (!_useExternalStrategy || _strategyManager.CurrentStrategy == null)
        {
            return Strategy.Signal.None;
        }

        try
        {
            // Update strategy context
            _strategyContext.UpdateTick(tick);
            _strategyContext.UpdateAccount(_engine?.GetAccountStatus() ?? new AccountStatus());

            // Get signal from external strategy
            var signal = _strategyManager.ProcessTick(_strategyContext);

            if (signal != Strategy.Signal.None)
            {
                OnStrategySignal?.Invoke(this, new StrategySignalEventArgs(signal, tick.Price, tick.Timestamp));
            }

            return signal;
        }
        catch (Exception ex)
        {
            RaiseLog(LogLevel.Error, $"External strategy error: {ex.Message}");
            return Strategy.Signal.None;
        }
    }
}
