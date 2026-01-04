using AegisQuant.Interop;
using AegisQuant.UI.Models;
using AegisQuant.UI.Strategy;

namespace AegisQuant.UI.Services.Interfaces;

/// <summary>
/// Backtest execution mode.
/// </summary>
public enum BacktestMode
{
    /// <summary>
    /// High-speed Rust-driven backtest (for built-in strategies).
    /// </summary>
    HighSpeed,
    
    /// <summary>
    /// Visual C#-driven backtest (for external strategies with UI updates).
    /// </summary>
    Visual
}

/// <summary>
/// Service state for state machine management.
/// Requirements: 12.1, 12.2
/// </summary>
public enum ServiceState
{
    /// <summary>
    /// Service is ready to accept new backtest requests.
    /// </summary>
    Ready,
    
    /// <summary>
    /// Service is currently running a backtest.
    /// </summary>
    Running,
    
    /// <summary>
    /// Service encountered a critical error and needs reset.
    /// Requirements: 12.1 - WHEN a critical error occurs, THE BacktestService SHALL enter Faulted state
    /// </summary>
    Faulted
}

/// <summary>
/// Event arguments for state change events.
/// </summary>
public class StateChangedEventArgs : EventArgs
{
    public ServiceState OldState { get; }
    public ServiceState NewState { get; }
    public string? Reason { get; }

    public StateChangedEventArgs(ServiceState oldState, ServiceState newState, string? reason = null)
    {
        OldState = oldState;
        NewState = newState;
        Reason = reason;
    }
}

/// <summary>
/// Event arguments for execution events during backtest.
/// </summary>
public class ExecutionEventArgs : EventArgs
{
    public ExecutionEventType EventType { get; }
    public long Timestamp { get; }
    public double Price { get; }
    public double Quantity { get; }
    public int Side { get; }

    public ExecutionEventArgs(ExecutionEventType eventType, long timestamp, double price, double quantity, int side)
    {
        EventType = eventType;
        Timestamp = timestamp;
        Price = price;
        Quantity = quantity;
        Side = side;
    }
}

/// <summary>
/// Execution event types from the Rust engine.
/// </summary>
public enum ExecutionEventType
{
    Trade = 0,
    OrderRejected = 1,
    StopTriggered = 2
}

/// <summary>
/// Interface for backtest service supporting hybrid execution modes.
/// </summary>
public interface IBacktestService : IDisposable
{
    /// <summary>
    /// Gets or sets the current backtest execution mode.
    /// </summary>
    BacktestMode Mode { get; set; }
    
    /// <summary>
    /// Gets the current service state.
    /// </summary>
    ServiceState State { get; }
    
    /// <summary>
    /// Gets whether a backtest is currently running.
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// Gets the current strategy parameters.
    /// </summary>
    StrategyParams CurrentParams { get; }
    
    /// <summary>
    /// Gets the current risk configuration.
    /// </summary>
    RiskConfig CurrentRiskConfig { get; }
    
    /// <summary>
    /// Gets the data quality report from the last data load.
    /// </summary>
    DataQualityReport? LastDataQualityReport { get; }
    
    /// <summary>
    /// Gets whether an external strategy is being used.
    /// </summary>
    bool UseExternalStrategy { get; }
    
    /// <summary>
    /// Gets the current external strategy name, or null if using built-in.
    /// </summary>
    string? CurrentStrategyName { get; }
    
    /// <summary>
    /// Event raised when an execution event occurs during backtest.
    /// </summary>
    event EventHandler<ExecutionEventArgs>? OnExecution;
    
    /// <summary>
    /// Event raised when account status is updated during backtest.
    /// </summary>
    event EventHandler<StatusUpdatedEventArgs>? OnStatusUpdated;
    
    /// <summary>
    /// Event raised when a log message is received from the engine.
    /// </summary>
    event EventHandler<LogReceivedEventArgs>? OnLogReceived;
    
    /// <summary>
    /// Event raised when the backtest completes.
    /// </summary>
    event EventHandler<BacktestCompletedEventArgs>? OnBacktestCompleted;
    
    /// <summary>
    /// Event raised when an external strategy generates a signal.
    /// </summary>
    event EventHandler<StrategySignalEventArgs>? OnStrategySignal;
    
    /// <summary>
    /// Event raised when OHLC data is loaded.
    /// </summary>
    event EventHandler<OhlcDataLoadedEventArgs>? OnOhlcDataLoaded;
    
    /// <summary>
    /// Event raised when service state changes.
    /// </summary>
    event EventHandler<StateChangedEventArgs>? OnStateChanged;
    
    /// <summary>
    /// Initializes the engine with the specified parameters.
    /// </summary>
    void Initialize(StrategyParams parameters, RiskConfig riskConfig);
    
    /// <summary>
    /// Runs the backtest asynchronously.
    /// </summary>
    /// <param name="token">Cancellation token</param>
    Task RunBacktestAsync(CancellationToken token = default);
    
    /// <summary>
    /// Stops the currently running backtest.
    /// </summary>
    void StopBacktest();
    
    /// <summary>
    /// Resets the service from faulted state.
    /// </summary>
    void Reset();
    
    /// <summary>
    /// Loads data from a file asynchronously.
    /// </summary>
    Task<DataQualityReport> LoadDataAsync(string filePath);
    
    /// <summary>
    /// Loads an external strategy from a file.
    /// </summary>
    Task LoadExternalStrategyAsync(string filePath);
    
    /// <summary>
    /// Sets an external strategy for backtesting.
    /// </summary>
    void SetExternalStrategy(IStrategy strategy);
    
    /// <summary>
    /// Switches back to using the built-in Rust strategy.
    /// </summary>
    void UseBuiltInStrategy();
    
    /// <summary>
    /// Clears the external strategy and reverts to built-in.
    /// </summary>
    void ClearExternalStrategy();
    
    /// <summary>
    /// Gets the current account status.
    /// </summary>
    AccountStatus GetCurrentStatus();
    
    /// <summary>
    /// Gets the OHLC data.
    /// </summary>
    List<ScottPlot.OHLC>? OhlcData { get; }
    
    /// <summary>
    /// Gets the volume data.
    /// </summary>
    List<double>? VolumeData { get; }
    
    /// <summary>
    /// Sets OHLC data directly (for data imported from Excel or other sources).
    /// </summary>
    void SetOhlcData(List<ScottPlot.OHLC> ohlcData, List<double> volumeData);
}
