using ScottPlot;
using AegisQuant.UI.Strategy;
using Interop = AegisQuant.Interop;

namespace AegisQuant.UI.Services.Interfaces;

/// <summary>
/// Trade record for replay tracking.
/// </summary>
public class ReplayTradeRecord
{
    public int BarIndex { get; set; }
    public DateTime Time { get; set; }
    public Strategy.Signal Signal { get; set; }
    public double Price { get; set; }
    public double Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Replay state containing current position and P&L.
/// </summary>
public class ReplayStateInfo
{
    public int CurrentBarIndex { get; set; }
    public double Equity { get; set; }
    public double Position { get; set; }
    public double AvgPrice { get; set; }
    public double UnrealizedPnL { get; set; }
    public double RealizedPnL { get; set; }
    public List<ReplayTradeRecord> Trades { get; } = new();
}

/// <summary>
/// Event arguments for replay step events.
/// </summary>
public class ReplayStepEventArgs : EventArgs
{
    public int BarIndex { get; set; }
    public OHLC CurrentBar { get; set; }
    public ReplayStateInfo State { get; set; } = new();
    public ReplayTradeRecord? Trade { get; set; }
}

/// <summary>
/// Interface for strategy replay service supporting step-by-step playback.
/// Requirements: 4.2, 4.5, 4.7, 4.8, 4.11
/// </summary>
public interface IReplayService
{
    /// <summary>
    /// Gets the current replay index.
    /// </summary>
    int CurrentIndex { get; }
    
    /// <summary>
    /// Gets the total number of bars.
    /// </summary>
    int TotalBars { get; }
    
    /// <summary>
    /// Gets whether replay is currently playing.
    /// </summary>
    bool IsPlaying { get; }
    
    /// <summary>
    /// Gets the current replay state.
    /// </summary>
    ReplayStateInfo State { get; }
    
    /// <summary>
    /// Gets whether the engine state is synchronized with the current replay position.
    /// Requirements: 4.11
    /// </summary>
    bool IsEngineSynced { get; }
    
    /// <summary>
    /// Gets or sets the visible window size for chart updates.
    /// </summary>
    int VisibleWindowSize { get; set; }
    
    /// <summary>
    /// Gets or sets the playback speed in milliseconds per bar.
    /// </summary>
    int PlaybackSpeed { get; set; }
    
    /// <summary>
    /// Gets or sets the initial capital for replay.
    /// </summary>
    double InitialCapital { get; set; }
    
    /// <summary>
    /// Gets or sets the trade quantity per signal.
    /// </summary>
    double TradeQuantity { get; set; }
    
    /// <summary>
    /// Event raised on each replay step.
    /// </summary>
    event EventHandler<ReplayStepEventArgs>? OnReplayStep;
    
    /// <summary>
    /// Event raised when replay completes.
    /// </summary>
    event EventHandler<ReplayStateInfo>? OnReplayCompleted;
    
    /// <summary>
    /// Event raised when a trade signal is generated.
    /// </summary>
    event EventHandler<ReplayTradeRecord>? OnTradeSignal;
    
    /// <summary>
    /// Event raised when visible chart data should be updated.
    /// </summary>
    event EventHandler<List<OHLC>>? OnVisibleDataChanged;
    
    /// <summary>
    /// Sets the OHLC data for replay.
    /// </summary>
    void SetData(List<OHLC> ohlcData, List<double> volumes);
    
    /// <summary>
    /// Loads data from the injected MarketDataStore for a specific timeframe.
    /// Requirements: 4.2 - Integration with IMarketDataStore
    /// </summary>
    /// <param name="timeframe">Timeframe to load (e.g., "1m", "5m", "1h")</param>
    void LoadFromMarketDataStore(string timeframe = "1m");
    
    /// <summary>
    /// Sets the engine wrapper for state synchronization.
    /// </summary>
    /// <param name="engine">Engine wrapper instance</param>
    void SetEngine(Interop.EngineWrapper engine);
    
    /// <summary>
    /// Sets the strategy for replay.
    /// </summary>
    void SetStrategy(IStrategy strategy);
    
    /// <summary>
    /// Resets the replay to the beginning.
    /// </summary>
    void Reset();
    
    /// <summary>
    /// Advances replay by one bar.
    /// Requirements: 4.5 - StepForward SHALL advance exactly one bar
    /// </summary>
    /// <returns>Event args for the step, or null if at end</returns>
    ReplayStepEventArgs? StepForward();
    
    /// <summary>
    /// Steps backward by one bar (recomputes from start).
    /// </summary>
    void StepBackward();
    
    /// <summary>
    /// Seeks to a specific bar index.
    /// Requirements: 4.8, 4.11 - SeekTo with FastForwardTo optimization
    /// </summary>
    void SeekTo(int barIndex);
    
    /// <summary>
    /// Starts automatic playback.
    /// </summary>
    Task PlayAsync();
    
    /// <summary>
    /// Pauses playback.
    /// </summary>
    void Pause();
    
    /// <summary>
    /// Stops playback.
    /// </summary>
    void Stop();
    
    /// <summary>
    /// Fast-forwards to the next trade.
    /// </summary>
    ReplayStepEventArgs? NextTrade();
    
    /// <summary>
    /// Runs a full backtest without triggering events.
    /// </summary>
    ReplayStateInfo RunFullBacktest();
    
    /// <summary>
    /// Gets all trade records from the current replay.
    /// </summary>
    List<ReplayTradeRecord> GetAllTrades();
    
    /// <summary>
    /// Gets the visible OHLC data for the current replay position.
    /// Requirements: 4.7 - Chart SHALL only show data up to current replay position
    /// </summary>
    /// <returns>List of visible OHLC bars</returns>
    List<OHLC> GetVisibleData();
    
    /// <summary>
    /// Gets all OHLC data up to the current replay position.
    /// Requirements: 4.7 - Chart SHALL only show data up to current replay position
    /// </summary>
    /// <returns>List of OHLC bars from start to current position</returns>
    List<OHLC> GetDataUpToCurrentPosition();
}
