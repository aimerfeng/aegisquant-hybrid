using System;
using System.Collections.Generic;
using AegisQuant.Interop;
using ScottPlot;

namespace AegisQuant.UI.Strategy;

/// <summary>
/// Position information for strategy context.
/// </summary>
public class PositionInfo
{
    /// <summary>Current position quantity (positive = long, negative = short, 0 = flat)</summary>
    public double Quantity { get; set; }

    /// <summary>Average entry price</summary>
    public double AveragePrice { get; set; }

    /// <summary>Unrealized P&L</summary>
    public double UnrealizedPnL { get; set; }

    /// <summary>Whether currently holding a position</summary>
    public bool HasPosition => Math.Abs(Quantity) > 0.0001;

    /// <summary>Whether position is long</summary>
    public bool IsLong => Quantity > 0.0001;

    /// <summary>Whether position is short</summary>
    public bool IsShort => Quantity < -0.0001;
}

/// <summary>
/// Tick data for strategy processing.
/// Wraps the native Tick struct with additional OHLC fields.
/// </summary>
public class TickData
{
    /// <summary>Timestamp in nanoseconds (matches native Tick)</summary>
    public long Timestamp { get; set; }

    /// <summary>Price</summary>
    public double Price { get; set; }

    /// <summary>Volume</summary>
    public double Volume { get; set; }

    /// <summary>High price (for OHLC data)</summary>
    public double? High { get; set; }

    /// <summary>Low price (for OHLC data)</summary>
    public double? Low { get; set; }

    /// <summary>Open price (for OHLC data)</summary>
    public double? Open { get; set; }

    /// <summary>Close price (for OHLC data)</summary>
    public double? Close { get; set; }

    /// <summary>
    /// Creates TickData from native Tick struct.
    /// </summary>
    public static TickData FromNative(AegisQuant.Interop.Tick tick)
    {
        return new TickData
        {
            Timestamp = tick.Timestamp,
            Price = tick.Price,
            Volume = tick.Volume,
            // For tick data, OHLC are all the same as Price
            Open = tick.Price,
            High = tick.Price,
            Low = tick.Price,
            Close = tick.Price
        };
    }

    /// <summary>
    /// Creates TickData from OHLC bar.
    /// </summary>
    public static TickData FromOhlc(OHLC ohlc, double volume = 0)
    {
        return new TickData
        {
            Timestamp = new DateTimeOffset(ohlc.DateTime).ToUnixTimeMilliseconds(),
            Price = ohlc.Close,
            Volume = volume,
            Open = ohlc.Open,
            High = ohlc.High,
            Low = ohlc.Low,
            Close = ohlc.Close
        };
    }

    /// <summary>
    /// Converts to native Tick struct.
    /// </summary>
    public AegisQuant.Interop.Tick ToNative()
    {
        return new AegisQuant.Interop.Tick
        {
            Timestamp = Timestamp,
            Price = Price,
            Volume = Volume
        };
    }
}

/// <summary>
/// Strategy execution context providing market data, indicators, and account info.
/// </summary>
public class StrategyContext
{
    private readonly List<TickData> _priceHistory;
    private readonly List<OHLC> _ohlcHistory;
    private readonly IndicatorService _indicators;

    /// <summary>
    /// Creates a new strategy context.
    /// </summary>
    public StrategyContext()
    {
        _priceHistory = new List<TickData>();
        _ohlcHistory = new List<OHLC>();
        _indicators = new IndicatorService(_priceHistory);
        Position = new PositionInfo();
        Account = new AccountStatus();
    }

    /// <summary>
    /// Gets the current tick data.
    /// </summary>
    public TickData CurrentTick { get; private set; } = new();

    /// <summary>
    /// Gets the current OHLC bar (if available).
    /// </summary>
    public OHLC? CurrentBar => _ohlcHistory.Count > 0 ? _ohlcHistory[^1] : null;

    /// <summary>
    /// Gets the price history (read-only).
    /// </summary>
    public IReadOnlyList<TickData> PriceHistory => _priceHistory;

    /// <summary>
    /// Gets the OHLC history (read-only).
    /// </summary>
    public IReadOnlyList<OHLC> OhlcHistory => _ohlcHistory;

    /// <summary>
    /// Gets the indicator service for calculating technical indicators.
    /// </summary>
    public IndicatorService Indicators => _indicators;

    /// <summary>
    /// Gets the current position information.
    /// </summary>
    public PositionInfo Position { get; set; }

    /// <summary>
    /// Gets the current account status (from native engine).
    /// </summary>
    public AccountStatus Account { get; set; }

    /// <summary>
    /// Convenience property for current price.
    /// </summary>
    public double Price => CurrentTick.Price;

    /// <summary>
    /// Convenience property for current volume.
    /// </summary>
    public double Volume => CurrentTick.Volume;

    /// <summary>
    /// Convenience property for current timestamp.
    /// </summary>
    public long Timestamp => CurrentTick.Timestamp;

    /// <summary>
    /// Number of ticks in history.
    /// </summary>
    public int TickCount => _priceHistory.Count;

    /// <summary>
    /// Number of OHLC bars in history.
    /// </summary>
    public int BarCount => _ohlcHistory.Count;

    /// <summary>
    /// Gets closing prices as array (for indicator calculations).
    /// </summary>
    public double[] Closes => _ohlcHistory.Select(o => o.Close).ToArray();

    /// <summary>
    /// Gets high prices as array.
    /// </summary>
    public double[] Highs => _ohlcHistory.Select(o => o.High).ToArray();

    /// <summary>
    /// Gets low prices as array.
    /// </summary>
    public double[] Lows => _ohlcHistory.Select(o => o.Low).ToArray();

    /// <summary>
    /// Gets open prices as array.
    /// </summary>
    public double[] Opens => _ohlcHistory.Select(o => o.Open).ToArray();

    /// <summary>
    /// Updates the context with a new tick (from TickData).
    /// </summary>
    /// <param name="tick">New tick data</param>
    public void UpdateTick(TickData tick)
    {
        CurrentTick = tick;
        _priceHistory.Add(tick);
        _indicators.InvalidateCache();
    }

    /// <summary>
    /// Updates the context with a new native Tick.
    /// </summary>
    /// <param name="tick">Native tick from engine</param>
    public void UpdateTick(AegisQuant.Interop.Tick tick)
    {
        UpdateTick(TickData.FromNative(tick));
    }

    /// <summary>
    /// Adds an OHLC bar to history.
    /// </summary>
    public void AddOhlc(OHLC ohlc)
    {
        _ohlcHistory.Add(ohlc);
    }

    /// <summary>
    /// Updates the context with account status from native engine.
    /// </summary>
    /// <param name="status">Account status from engine</param>
    public void UpdateAccount(AccountStatus status)
    {
        Account = status;
        // Note: AccountStatus doesn't have Position field directly
        // Position tracking is handled separately
    }

    /// <summary>
    /// Updates position info from native Position struct.
    /// </summary>
    /// <param name="position">Position from engine</param>
    public void UpdatePosition(Position position)
    {
        Position.Quantity = position.Quantity;
        Position.AveragePrice = position.AveragePrice;
        Position.UnrealizedPnL = position.UnrealizedPnl;
    }

    /// <summary>
    /// Resets the context state.
    /// </summary>
    public void Reset()
    {
        _priceHistory.Clear();
        _ohlcHistory.Clear();
        _indicators.InvalidateCache();
        CurrentTick = new TickData();
        Position = new PositionInfo();
    }
}
