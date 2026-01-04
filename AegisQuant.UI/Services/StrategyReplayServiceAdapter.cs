using ScottPlot;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Services.Interfaces;
using Interop = AegisQuant.Interop;

namespace AegisQuant.UI.Services;

/// <summary>
/// Adapter that wraps StrategyReplayService to implement IReplayService interface.
/// Supports both default construction and DI injection.
/// Requirements: 4.2 - Integration with IMarketDataStore and EngineWrapper
/// </summary>
public class StrategyReplayServiceAdapter : IReplayService
{
    private readonly StrategyReplayService _innerService;

    /// <summary>
    /// Default constructor for backward compatibility.
    /// </summary>
    public StrategyReplayServiceAdapter()
    {
        _innerService = new StrategyReplayService();
        WireUpEvents();
    }
    
    /// <summary>
    /// Constructor with dependency injection.
    /// Requirements: 4.2 - Inject IMarketDataStore and EngineWrapper
    /// </summary>
    /// <param name="marketDataStore">Market data store for OHLC data access</param>
    /// <param name="engine">Engine wrapper for state synchronization</param>
    public StrategyReplayServiceAdapter(IMarketDataStore marketDataStore, Interop.EngineWrapper engine)
    {
        _innerService = new StrategyReplayService(marketDataStore, engine);
        WireUpEvents();
    }
    
    /// <summary>
    /// Wires up events from inner service to adapter events.
    /// </summary>
    private void WireUpEvents()
    {
        _innerService.OnReplayStep += (s, e) => OnReplayStep?.Invoke(this, ConvertEventArgs(e));
        _innerService.OnReplayCompleted += (s, e) => OnReplayCompleted?.Invoke(this, ConvertState(e));
        _innerService.OnTradeSignal += (s, e) => OnTradeSignal?.Invoke(this, ConvertTradeRecord(e));
        _innerService.OnVisibleDataChanged += (s, e) => OnVisibleDataChanged?.Invoke(this, e);
    }

    public int CurrentIndex => _innerService.CurrentIndex;
    public int TotalBars => _innerService.TotalBars;
    public bool IsPlaying => _innerService.IsPlaying;
    public ReplayStateInfo State => ConvertState(_innerService.State);
    
    /// <summary>
    /// Gets whether the engine state is synchronized with the current replay position.
    /// Requirements: 4.11
    /// </summary>
    public bool IsEngineSynced => _innerService.IsEngineSynced;
    
    /// <summary>
    /// Gets or sets the visible window size for chart updates.
    /// </summary>
    public int VisibleWindowSize
    {
        get => _innerService.VisibleWindowSize;
        set => _innerService.VisibleWindowSize = value;
    }
    
    public int PlaybackSpeed
    {
        get => _innerService.PlaybackSpeed;
        set => _innerService.PlaybackSpeed = value;
    }
    
    public double InitialCapital
    {
        get => _innerService.InitialCapital;
        set => _innerService.InitialCapital = value;
    }
    
    public double TradeQuantity
    {
        get => _innerService.TradeQuantity;
        set => _innerService.TradeQuantity = value;
    }

    public event EventHandler<ReplayStepEventArgs>? OnReplayStep;
    public event EventHandler<ReplayStateInfo>? OnReplayCompleted;
    public event EventHandler<ReplayTradeRecord>? OnTradeSignal;
    
    /// <summary>
    /// Event raised when visible chart data should be updated.
    /// </summary>
    public event EventHandler<List<OHLC>>? OnVisibleDataChanged;

    public void SetData(List<OHLC> ohlcData, List<double> volumes)
    {
        _innerService.SetData(ohlcData, volumes);
    }

    public void SetStrategy(IStrategy strategy)
    {
        _innerService.SetStrategy(strategy);
    }

    public void Reset()
    {
        _innerService.Reset();
    }

    public ReplayStepEventArgs? StepForward()
    {
        var result = _innerService.StepForward();
        return result != null ? ConvertEventArgs(result) : null;
    }

    public void StepBackward()
    {
        _innerService.StepBackward();
    }

    public void SeekTo(int barIndex)
    {
        _innerService.SeekTo(barIndex);
    }

    public async Task PlayAsync()
    {
        await _innerService.PlayAsync();
    }

    public void Pause()
    {
        _innerService.Pause();
    }

    public void Stop()
    {
        _innerService.Stop();
    }

    public ReplayStepEventArgs? NextTrade()
    {
        var result = _innerService.NextTrade();
        return result != null ? ConvertEventArgs(result) : null;
    }

    public ReplayStateInfo RunFullBacktest()
    {
        return ConvertState(_innerService.RunFullBacktest());
    }

    public List<ReplayTradeRecord> GetAllTrades()
    {
        return _innerService.GetAllTrades().Select(ConvertTradeRecord).ToList();
    }
    
    /// <summary>
    /// Loads data from the injected MarketDataStore for a specific timeframe.
    /// Requirements: 4.2 - Integration with IMarketDataStore
    /// </summary>
    /// <param name="timeframe">Timeframe to load (e.g., "1m", "5m", "1h")</param>
    public void LoadFromMarketDataStore(string timeframe = "1m")
    {
        _innerService.LoadFromMarketDataStore(timeframe);
    }
    
    /// <summary>
    /// Sets the engine wrapper for state synchronization.
    /// </summary>
    /// <param name="engine">Engine wrapper instance</param>
    public void SetEngine(Interop.EngineWrapper engine)
    {
        _innerService.SetEngine(engine);
    }
    
    /// <summary>
    /// Gets the visible OHLC data for the current replay position.
    /// Requirements: 4.7 - Chart SHALL only show data up to current replay position
    /// </summary>
    /// <returns>List of visible OHLC bars</returns>
    public List<OHLC> GetVisibleData()
    {
        return _innerService.GetVisibleData();
    }
    
    /// <summary>
    /// Gets all OHLC data up to the current replay position.
    /// Requirements: 4.7 - Chart SHALL only show data up to current replay position
    /// </summary>
    /// <returns>List of OHLC bars from start to current position</returns>
    public List<OHLC> GetDataUpToCurrentPosition()
    {
        return _innerService.GetDataUpToCurrentPosition();
    }

    private static ReplayStepEventArgs ConvertEventArgs(ReplayEventArgs e)
    {
        return new ReplayStepEventArgs
        {
            BarIndex = e.BarIndex,
            CurrentBar = e.CurrentBar,
            State = ConvertState(e.State),
            Trade = e.Trade != null ? ConvertTradeRecord(e.Trade) : null
        };
    }

    private static ReplayStateInfo ConvertState(ReplayState state)
    {
        var result = new ReplayStateInfo
        {
            CurrentBarIndex = state.CurrentBarIndex,
            Equity = state.Equity,
            Position = state.Position,
            AvgPrice = state.AvgPrice,
            UnrealizedPnL = state.UnrealizedPnL,
            RealizedPnL = state.RealizedPnL
        };
        
        foreach (var trade in state.Trades)
        {
            result.Trades.Add(ConvertTradeRecord(trade));
        }
        
        return result;
    }

    private static ReplayTradeRecord ConvertTradeRecord(TradeRecord trade)
    {
        return new ReplayTradeRecord
        {
            BarIndex = trade.BarIndex,
            Time = trade.Time,
            Signal = trade.Signal,
            Price = trade.Price,
            Quantity = trade.Quantity,
            Reason = trade.Reason
        };
    }
}
