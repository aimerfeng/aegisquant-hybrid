using System.Windows.Threading;
using ScottPlot;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Models;
using AegisQuant.UI.Services.Interfaces;
using Interop = AegisQuant.Interop;

namespace AegisQuant.UI.Services;

/// <summary>
/// 交易记录
/// </summary>
public class TradeRecord
{
    public int BarIndex { get; set; }
    public DateTime Time { get; set; }
    public Strategy.Signal Signal { get; set; }
    public double Price { get; set; }
    public double Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 回放状态
/// </summary>
public class ReplayState
{
    public int CurrentBarIndex { get; set; }
    public double Equity { get; set; }
    public double Position { get; set; }
    public double AvgPrice { get; set; }
    public double UnrealizedPnL { get; set; }
    public double RealizedPnL { get; set; }
    public List<TradeRecord> Trades { get; } = new();
}

/// <summary>
/// 回放事件参数
/// </summary>
public class ReplayEventArgs : EventArgs
{
    public int BarIndex { get; set; }
    public OHLC CurrentBar { get; set; }
    public ReplayState State { get; set; }
    public TradeRecord? Trade { get; set; }
}

/// <summary>
/// 策略回放服务 - 支持逐K线回放，查看策略买卖点
/// Refactored to support DI injection of IMarketDataStore and EngineWrapper.
/// Requirements: 4.2, 4.5, 4.7, 4.8, 4.11
/// </summary>
public class StrategyReplayService
{
    private List<OHLC> _ohlcData = new();
    private List<double> _volumes = new();
    private IStrategy? _strategy;
    private StrategyContext _context = new();
    private ReplayState _state = new();
    
    // Injected dependencies (Requirements: 4.2)
    private readonly IMarketDataStore? _marketDataStore;
    private Interop.EngineWrapper? _engine;
    
    // 回放控制
    private int _currentIndex = 0;
    private bool _isPlaying = false;
    private CancellationTokenSource? _playbackCts;
    
    // DispatcherTimer for UI-thread-safe playback
    private DispatcherTimer? _playbackTimer;
    
    // Track the last synced engine index for state consistency (Requirements: 4.11)
    private int _lastSyncedEngineIndex = -1;
    
    // Visible window size for chart updates (Requirements: 4.8)
    private const int DefaultVisibleWindowSize = 1000;
    
    // 回放速度（毫秒/K线）
    public int PlaybackSpeed { get; set; } = 500;
    
    // 初始资金
    public double InitialCapital { get; set; } = 100000;
    
    // 每次交易数量
    public double TradeQuantity { get; set; } = 100;
    
    /// <summary>
    /// Gets or sets the visible window size for chart updates.
    /// </summary>
    public int VisibleWindowSize { get; set; } = DefaultVisibleWindowSize;

    /// <summary>
    /// 回放进度事件
    /// </summary>
    public event EventHandler<ReplayEventArgs>? OnReplayStep;

    /// <summary>
    /// 回放完成事件
    /// </summary>
    public event EventHandler<ReplayState>? OnReplayCompleted;

    /// <summary>
    /// 交易信号事件
    /// </summary>
    public event EventHandler<TradeRecord>? OnTradeSignal;
    
    /// <summary>
    /// Event raised when visible chart data should be updated.
    /// </summary>
    public event EventHandler<List<OHLC>>? OnVisibleDataChanged;

    /// <summary>
    /// 当前回放索引
    /// </summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>
    /// 总K线数
    /// </summary>
    public int TotalBars => _ohlcData.Count;

    /// <summary>
    /// 是否正在播放
    /// </summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// 当前回放状态
    /// </summary>
    public ReplayState State => _state;
    
    /// <summary>
    /// Gets the injected market data store (may be null if not injected).
    /// </summary>
    public IMarketDataStore? MarketDataStore => _marketDataStore;
    
    /// <summary>
    /// Gets the injected engine wrapper (may be null if not injected).
    /// </summary>
    public Interop.EngineWrapper? Engine => _engine;
    
    /// <summary>
    /// Gets whether the engine state is synchronized with the current replay position.
    /// Requirements: 4.11
    /// </summary>
    public bool IsEngineSynced => _engine != null && _lastSyncedEngineIndex == _currentIndex - 1;

    /// <summary>
    /// Default constructor for backward compatibility.
    /// </summary>
    public StrategyReplayService()
    {
        _marketDataStore = null;
        _engine = null;
    }
    
    /// <summary>
    /// Constructor with dependency injection.
    /// Requirements: 4.2 - Inject IMarketDataStore and EngineWrapper
    /// </summary>
    /// <param name="marketDataStore">Market data store for OHLC data access</param>
    /// <param name="engine">Engine wrapper for state synchronization</param>
    public StrategyReplayService(IMarketDataStore marketDataStore, Interop.EngineWrapper engine)
    {
        _marketDataStore = marketDataStore ?? throw new ArgumentNullException(nameof(marketDataStore));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }
    
    /// <summary>
    /// Sets the engine wrapper for state synchronization.
    /// Useful when engine is created after replay service.
    /// </summary>
    /// <param name="engine">Engine wrapper instance</param>
    public void SetEngine(Interop.EngineWrapper engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _lastSyncedEngineIndex = -1; // Reset sync state
    }

    /// <summary>
    /// 设置数据
    /// </summary>
    public void SetData(List<OHLC> ohlcData, List<double> volumes)
    {
        _ohlcData = ohlcData;
        _volumes = volumes;
        Reset();
    }
    
    /// <summary>
    /// Loads data from the injected MarketDataStore for a specific timeframe.
    /// Requirements: 4.2 - Integration with IMarketDataStore
    /// </summary>
    /// <param name="timeframe">Timeframe to load (e.g., "1m", "5m", "1h")</param>
    public void LoadFromMarketDataStore(string timeframe = "1m")
    {
        if (_marketDataStore == null)
        {
            throw new InvalidOperationException("MarketDataStore not injected. Use SetData() instead or inject via constructor.");
        }
        
        var ohlcData = _marketDataStore.GetOhlcData(timeframe);
        if (ohlcData.Count == 0)
        {
            throw new InvalidOperationException($"No OHLC data available for timeframe '{timeframe}'");
        }
        
        // For now, we don't have volume data from MarketDataStore, use empty volumes
        var volumes = new List<double>(new double[ohlcData.Count]);
        
        SetData(ohlcData, volumes);
    }

    /// <summary>
    /// 设置策略
    /// </summary>
    public void SetStrategy(IStrategy strategy)
    {
        _strategy = strategy;
        Reset();
    }

    /// <summary>
    /// 重置回放
    /// </summary>
    public void Reset()
    {
        Stop();
        _currentIndex = 0;
        _lastSyncedEngineIndex = -1; // Reset engine sync state
        _state = new ReplayState
        {
            CurrentBarIndex = 0,
            Equity = InitialCapital,
            Position = 0,
            AvgPrice = 0,
            UnrealizedPnL = 0,
            RealizedPnL = 0
        };
        _context.Reset();
    }

    /// <summary>
    /// 单步前进
    /// Requirements: 4.5 - StepForward SHALL advance exactly one bar
    /// </summary>
    public ReplayEventArgs? StepForward()
    {
        if (_currentIndex >= _ohlcData.Count)
            return null;

        var bar = _ohlcData[_currentIndex];
        var volume = _currentIndex < _volumes.Count ? _volumes[_currentIndex] : 0;

        // Sync engine state if engine is available (Requirements: 4.11)
        SyncEngineState(_currentIndex);

        // 更新策略上下文
        UpdateContext(bar, volume);

        // 获取策略信号
        TradeRecord? trade = null;
        if (_strategy != null)
        {
            var signal = _strategy.OnBar(_context);
            if (signal != Strategy.Signal.None)
            {
                trade = ExecuteTrade(signal, bar);
            }
        }

        // 更新未实现盈亏
        UpdateUnrealizedPnL(bar.Close);

        // 更新状态
        _state.CurrentBarIndex = _currentIndex;
        _state.Equity = InitialCapital + _state.RealizedPnL + _state.UnrealizedPnL;

        var eventArgs = new ReplayEventArgs
        {
            BarIndex = _currentIndex,
            CurrentBar = bar,
            State = _state,
            Trade = trade
        };

        OnReplayStep?.Invoke(this, eventArgs);

        _currentIndex++;

        if (_currentIndex >= _ohlcData.Count)
        {
            OnReplayCompleted?.Invoke(this, _state);
        }

        return eventArgs;
    }
    
    /// <summary>
    /// Synchronizes the Rust engine state with the current replay position.
    /// Requirements: 4.11 - Maintain Rust_Engine state consistency
    /// </summary>
    /// <param name="targetIndex">Target bar index to sync to</param>
    private void SyncEngineState(int targetIndex)
    {
        if (_engine == null)
            return;
            
        // If already synced to this position, nothing to do
        if (_lastSyncedEngineIndex >= targetIndex)
            return;
            
        try
        {
            // Process tick for the current bar to update engine state
            var bar = _ohlcData[targetIndex];
            var volume = targetIndex < _volumes.Count ? _volumes[targetIndex] : 0;
            
            var tick = new Interop.Tick
            {
                Timestamp = new DateTimeOffset(bar.DateTime).ToUnixTimeMilliseconds() * 1_000_000,
                Price = bar.Close,
                Volume = volume
            };
            
            _engine.ProcessTick(tick);
            _lastSyncedEngineIndex = targetIndex;
        }
        catch (Exception)
        {
            // Engine sync failed, continue without sync
            // This allows replay to work even if engine is not properly initialized
        }
    }

    /// <summary>
    /// 单步后退
    /// </summary>
    public void StepBackward()
    {
        if (_currentIndex <= 0)
            return;

        // 重新从头计算到前一个位置
        var targetIndex = _currentIndex - 1;
        Reset();
        
        while (_currentIndex < targetIndex)
        {
            StepForward();
        }
    }

    /// <summary>
    /// 跳转到指定位置
    /// Requirements: 4.8, 4.11 - SeekTo with FastForwardTo optimization
    /// </summary>
    public void SeekTo(int barIndex)
    {
        // Valid range is 0 to _ohlcData.Count (inclusive)
        // barIndex == _ohlcData.Count means "at the end of data"
        if (barIndex < 0 || barIndex > _ohlcData.Count)
            return;

        // Use optimized seek if engine is available
        if (_engine != null)
        {
            SeekToOptimized(barIndex);
        }
        else
        {
            // Fallback to sequential replay
            SeekToSequential(barIndex);
        }
    }
    
    /// <summary>
    /// Optimized seek using FastForwardTo.
    /// Requirements: 4.8, 4.11 - Use FastForwardTo for acceleration
    /// </summary>
    /// <param name="barIndex">Target bar index</param>
    private void SeekToOptimized(int barIndex)
    {
        if (_engine == null)
        {
            SeekToSequential(barIndex);
            return;
        }
        
        try
        {
            if (barIndex < _currentIndex)
            {
                // Seeking backward: must reset and fast-forward
                // Reset internal state
                _currentIndex = 0;
                _lastSyncedEngineIndex = -1;
                _state = new ReplayState
                {
                    CurrentBarIndex = 0,
                    Equity = InitialCapital,
                    Position = 0,
                    AvgPrice = 0,
                    UnrealizedPnL = 0,
                    RealizedPnL = 0
                };
                _context.Reset();
                
                // Use FastForwardTo to quickly advance engine state
                if (barIndex > 0)
                {
                    _engine.FastForwardTo(barIndex);
                    _lastSyncedEngineIndex = barIndex - 1;
                }
            }
            else if (barIndex > _currentIndex)
            {
                // Seeking forward: fast-forward from current position
                _engine.FastForwardTo(barIndex);
                _lastSyncedEngineIndex = barIndex - 1;
            }
            
            // Update current index
            _currentIndex = barIndex;
            _state.CurrentBarIndex = barIndex;
            
            // Update visible chart data (only visible range)
            UpdateVisibleChartData(barIndex);
            
            // Try to get account status from engine
            try
            {
                var status = _engine.GetAccountStatus();
                _state.Equity = status.Equity;
                _state.RealizedPnL = status.TotalPnl;
            }
            catch
            {
                // Engine status not available, use calculated values
            }
        }
        catch (Exception)
        {
            // FastForwardTo failed, fallback to sequential
            SeekToSequential(barIndex);
        }
    }
    
    /// <summary>
    /// Sequential seek (fallback when engine is not available).
    /// </summary>
    /// <param name="barIndex">Target bar index</param>
    private void SeekToSequential(int barIndex)
    {
        Reset();
        while (_currentIndex < barIndex)
        {
            StepForward();
        }
    }
    
    /// <summary>
    /// Updates the visible chart data for the current position.
    /// Requirements: 4.8 - Only update visible range
    /// </summary>
    /// <param name="currentPosition">Current replay position</param>
    private void UpdateVisibleChartData(int currentPosition)
    {
        // Get visible bars (last N bars up to current position)
        // currentPosition represents the number of bars processed
        var visibleBars = _ohlcData
            .Take(currentPosition)
            .TakeLast(VisibleWindowSize)
            .ToList();
        
        OnVisibleDataChanged?.Invoke(this, visibleBars);
    }
    
    /// <summary>
    /// Gets the visible OHLC data for the current replay position.
    /// Requirements: 4.7 - Chart SHALL only show data up to current replay position
    /// </summary>
    /// <returns>List of visible OHLC bars</returns>
    public List<OHLC> GetVisibleData()
    {
        // CurrentIndex represents the number of bars processed (0-indexed position after last processed bar)
        // So we take exactly CurrentIndex bars (indices 0 to CurrentIndex-1)
        return _ohlcData
            .Take(_currentIndex)
            .TakeLast(VisibleWindowSize)
            .ToList();
    }
    
    /// <summary>
    /// Gets all OHLC data up to the current replay position.
    /// Requirements: 4.7 - Chart SHALL only show data up to current replay position
    /// </summary>
    /// <returns>List of OHLC bars from start to current position</returns>
    public List<OHLC> GetDataUpToCurrentPosition()
    {
        // CurrentIndex represents the number of bars processed (0-indexed position after last processed bar)
        // After N StepForward() calls, CurrentIndex = N, and we return bars 0 to N-1 (N bars total)
        return _ohlcData.Take(_currentIndex).ToList();
    }

    /// <summary>
    /// 开始自动播放 - 使用 DispatcherTimer 确保 UI 线程安全
    /// </summary>
    public async Task PlayAsync()
    {
        if (_isPlaying) return;

        _isPlaying = true;
        _playbackCts = new CancellationTokenSource();

        // 使用 DispatcherTimer 在 UI 线程上执行回放
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PlaybackSpeed)
        };
        
        _playbackTimer.Tick += (s, e) =>
        {
            if (_currentIndex >= _ohlcData.Count || _playbackCts?.Token.IsCancellationRequested == true)
            {
                StopPlaybackTimer();
                return;
            }
            
            StepForward();
        };
        
        _playbackTimer.Start();
        
        // 等待播放完成或取消
        try
        {
            while (_isPlaying && !_playbackCts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, _playbackCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
    }
    
    /// <summary>
    /// 停止播放定时器
    /// </summary>
    private void StopPlaybackTimer()
    {
        _playbackTimer?.Stop();
        _playbackTimer = null;
        _isPlaying = false;
        
        if (_currentIndex >= _ohlcData.Count)
        {
            OnReplayCompleted?.Invoke(this, _state);
        }
    }

    /// <summary>
    /// 暂停播放
    /// </summary>
    public void Pause()
    {
        _playbackCts?.Cancel();
        StopPlaybackTimer();
    }

    /// <summary>
    /// 停止播放
    /// </summary>
    public void Stop()
    {
        Pause();
    }

    /// <summary>
    /// 快进到下一个交易点
    /// </summary>
    public ReplayEventArgs? NextTrade()
    {
        while (_currentIndex < _ohlcData.Count)
        {
            var result = StepForward();
            if (result?.Trade != null)
                return result;
        }
        return null;
    }

    /// <summary>
    /// 运行完整回测（不触发事件，快速计算）
    /// </summary>
    public ReplayState RunFullBacktest()
    {
        Reset();
        while (_currentIndex < _ohlcData.Count)
        {
            var bar = _ohlcData[_currentIndex];
            var volume = _currentIndex < _volumes.Count ? _volumes[_currentIndex] : 0;

            UpdateContext(bar, volume);

            if (_strategy != null)
            {
                var signal = _strategy.OnBar(_context);
                if (signal != Strategy.Signal.None)
                {
                    ExecuteTrade(signal, bar);
                }
            }

            UpdateUnrealizedPnL(bar.Close);
            _state.CurrentBarIndex = _currentIndex;
            _state.Equity = InitialCapital + _state.RealizedPnL + _state.UnrealizedPnL;

            _currentIndex++;
        }

        return _state;
    }

    /// <summary>
    /// 获取所有交易记录
    /// </summary>
    public List<TradeRecord> GetAllTrades() => _state.Trades.ToList();

    private void UpdateContext(OHLC bar, double volume)
    {
        // 将 OHLC 转换为 Tick 格式供策略使用
        var tick = new AegisQuant.Interop.Tick
        {
            Timestamp = new DateTimeOffset(bar.DateTime).ToUnixTimeMilliseconds(),
            Price = bar.Close,
            Volume = volume
        };

        _context.UpdateTick(tick);
        _context.AddOhlc(bar);

        // 更新账户状态
        var accountStatus = new AegisQuant.Interop.AccountStatus
        {
            Balance = InitialCapital + _state.RealizedPnL,
            Equity = InitialCapital + _state.RealizedPnL + _state.UnrealizedPnL,
            Available = InitialCapital + _state.RealizedPnL + _state.UnrealizedPnL - Math.Abs(_state.Position * _state.AvgPrice),
            PositionCount = _state.Position != 0 ? 1 : 0,
            TotalPnl = _state.RealizedPnL + _state.UnrealizedPnL
        };
        _context.UpdateAccount(accountStatus);
    }

    private TradeRecord ExecuteTrade(Strategy.Signal signal, OHLC bar)
    {
        var trade = new TradeRecord
        {
            BarIndex = _currentIndex,
            Time = bar.DateTime,
            Signal = signal,
            Price = bar.Close,
            Quantity = TradeQuantity
        };

        switch (signal)
        {
            case Strategy.Signal.Buy:
                if (_state.Position <= 0)
                {
                    // 平空仓
                    if (_state.Position < 0)
                    {
                        _state.RealizedPnL += (_state.AvgPrice - bar.Close) * Math.Abs(_state.Position);
                        trade.Reason = "平空开多";
                    }
                    else
                    {
                        trade.Reason = "开多";
                    }
                    _state.Position = TradeQuantity;
                    _state.AvgPrice = bar.Close;
                }
                break;

            case Strategy.Signal.Sell:
                if (_state.Position >= 0)
                {
                    // 平多仓
                    if (_state.Position > 0)
                    {
                        _state.RealizedPnL += (bar.Close - _state.AvgPrice) * _state.Position;
                        trade.Reason = "平多开空";
                    }
                    else
                    {
                        trade.Reason = "开空";
                    }
                    _state.Position = -TradeQuantity;
                    _state.AvgPrice = bar.Close;
                }
                break;

            case Strategy.Signal.CloseLong:
                if (_state.Position > 0)
                {
                    _state.RealizedPnL += (bar.Close - _state.AvgPrice) * _state.Position;
                    _state.Position = 0;
                    _state.AvgPrice = 0;
                    trade.Reason = "平多";
                }
                break;

            case Strategy.Signal.CloseShort:
                if (_state.Position < 0)
                {
                    _state.RealizedPnL += (_state.AvgPrice - bar.Close) * Math.Abs(_state.Position);
                    _state.Position = 0;
                    _state.AvgPrice = 0;
                    trade.Reason = "平空";
                }
                break;
        }

        _state.Trades.Add(trade);
        OnTradeSignal?.Invoke(this, trade);

        return trade;
    }

    private void UpdateUnrealizedPnL(double currentPrice)
    {
        if (_state.Position > 0)
        {
            _state.UnrealizedPnL = (currentPrice - _state.AvgPrice) * _state.Position;
        }
        else if (_state.Position < 0)
        {
            _state.UnrealizedPnL = (_state.AvgPrice - currentPrice) * Math.Abs(_state.Position);
        }
        else
        {
            _state.UnrealizedPnL = 0;
        }
    }
}
