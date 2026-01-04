using ScottPlot;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Models;

namespace AegisQuant.UI.Services;

/// <summary>
/// 交易记录
/// </summary>
public class TradeRecord
{
    public int BarIndex { get; set; }
    public DateTime Time { get; set; }
    public Signal Signal { get; set; }
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
/// </summary>
public class StrategyReplayService
{
    private List<OHLC> _ohlcData = new();
    private List<double> _volumes = new();
    private IStrategy? _strategy;
    private StrategyContext _context = new();
    private ReplayState _state = new();
    
    // 回放控制
    private int _currentIndex = 0;
    private bool _isPlaying = false;
    private CancellationTokenSource? _playbackCts;
    
    // 回放速度（毫秒/K线）
    public int PlaybackSpeed { get; set; } = 500;
    
    // 初始资金
    public double InitialCapital { get; set; } = 100000;
    
    // 每次交易数量
    public double TradeQuantity { get; set; } = 100;

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
    /// 设置数据
    /// </summary>
    public void SetData(List<OHLC> ohlcData, List<double> volumes)
    {
        _ohlcData = ohlcData;
        _volumes = volumes;
        Reset();
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
    /// </summary>
    public ReplayEventArgs? StepForward()
    {
        if (_currentIndex >= _ohlcData.Count)
            return null;

        var bar = _ohlcData[_currentIndex];
        var volume = _currentIndex < _volumes.Count ? _volumes[_currentIndex] : 0;

        // 更新策略上下文
        UpdateContext(bar, volume);

        // 获取策略信号
        TradeRecord? trade = null;
        if (_strategy != null)
        {
            var signal = _strategy.OnBar(_context);
            if (signal != Signal.None)
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
    /// </summary>
    public void SeekTo(int barIndex)
    {
        if (barIndex < 0 || barIndex >= _ohlcData.Count)
            return;

        Reset();
        while (_currentIndex < barIndex)
        {
            StepForward();
        }
    }

    /// <summary>
    /// 开始自动播放
    /// </summary>
    public async Task PlayAsync()
    {
        if (_isPlaying) return;

        _isPlaying = true;
        _playbackCts = new CancellationTokenSource();

        try
        {
            while (_currentIndex < _ohlcData.Count && !_playbackCts.Token.IsCancellationRequested)
            {
                StepForward();
                await Task.Delay(PlaybackSpeed, _playbackCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        finally
        {
            _isPlaying = false;
        }
    }

    /// <summary>
    /// 暂停播放
    /// </summary>
    public void Pause()
    {
        _playbackCts?.Cancel();
        _isPlaying = false;
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
                if (signal != Signal.None)
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

    private TradeRecord ExecuteTrade(Signal signal, OHLC bar)
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
            case Signal.Buy:
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

            case Signal.Sell:
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

            case Signal.CloseLong:
                if (_state.Position > 0)
                {
                    _state.RealizedPnL += (bar.Close - _state.AvgPrice) * _state.Position;
                    _state.Position = 0;
                    _state.AvgPrice = 0;
                    trade.Reason = "平多";
                }
                break;

            case Signal.CloseShort:
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
