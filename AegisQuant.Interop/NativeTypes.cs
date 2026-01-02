using System.Runtime.InteropServices;

namespace AegisQuant.Interop;

/// <summary>
/// Tick data representing a single market data point.
/// Matches Rust repr(C) Tick struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Tick
{
    /// <summary>Unix timestamp in nanoseconds</summary>
    public long Timestamp;
    /// <summary>Price (f64 for performance)</summary>
    public double Price;
    /// <summary>Volume</summary>
    public double Volume;
}

/// <summary>
/// Order request structure for submitting orders.
/// Matches Rust repr(C) OrderRequest struct.
/// </summary>
/// <remarks>
/// - Symbol is a fixed-size array (null-terminated UTF-8)
/// - Direction: 1 = Buy, -1 = Sell
/// - OrderType: 0 = Market, 1 = Limit
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct OrderRequest
{
    /// <summary>Symbol as fixed-size byte array (null-terminated)</summary>
    public fixed byte Symbol[16];
    /// <summary>Order quantity</summary>
    public double Quantity;
    /// <summary>Direction: 1 = Buy, -1 = Sell</summary>
    public int Direction;
    /// <summary>Order type: 0 = Market, 1 = Limit</summary>
    public int OrderType;
    /// <summary>Limit price (ignored for Market orders)</summary>
    public double LimitPrice;

    /// <summary>
    /// Sets the symbol from a string.
    /// </summary>
    public void SetSymbol(string symbol)
    {
        fixed (byte* ptr = Symbol)
        {
            // Clear the buffer first
            for (int i = 0; i < 16; i++)
                ptr[i] = 0;

            // Copy symbol bytes (max 15 chars + null terminator)
            var bytes = System.Text.Encoding.UTF8.GetBytes(symbol);
            int len = Math.Min(bytes.Length, 15);
            for (int i = 0; i < len; i++)
                ptr[i] = bytes[i];
        }
    }

    /// <summary>
    /// Gets the symbol as a string.
    /// </summary>
    public readonly string GetSymbol()
    {
        fixed (byte* ptr = Symbol)
        {
            int len = 0;
            while (len < 16 && ptr[len] != 0)
                len++;
            return System.Text.Encoding.UTF8.GetString(ptr, len);
        }
    }
}


/// <summary>
/// Position structure representing a held position.
/// Matches Rust repr(C) Position struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Position
{
    /// <summary>Symbol as fixed-size byte array (null-terminated)</summary>
    public fixed byte Symbol[16];
    /// <summary>Position quantity (positive = long, negative = short)</summary>
    public double Quantity;
    /// <summary>Average entry price</summary>
    public double AveragePrice;
    /// <summary>Unrealized profit/loss</summary>
    public double UnrealizedPnl;
    /// <summary>Realized profit/loss</summary>
    public double RealizedPnl;

    /// <summary>
    /// Sets the symbol from a string.
    /// </summary>
    public void SetSymbol(string symbol)
    {
        fixed (byte* ptr = Symbol)
        {
            for (int i = 0; i < 16; i++)
                ptr[i] = 0;

            var bytes = System.Text.Encoding.UTF8.GetBytes(symbol);
            int len = Math.Min(bytes.Length, 15);
            for (int i = 0; i < len; i++)
                ptr[i] = bytes[i];
        }
    }

    /// <summary>
    /// Gets the symbol as a string.
    /// </summary>
    public readonly string GetSymbol()
    {
        fixed (byte* ptr = Symbol)
        {
            int len = 0;
            while (len < 16 && ptr[len] != 0)
                len++;
            return System.Text.Encoding.UTF8.GetString(ptr, len);
        }
    }
}

/// <summary>
/// Account status structure.
/// Matches Rust repr(C) AccountStatus struct.
/// </summary>
/// <remarks>
/// Internal calculations use Decimal for precision, exported as f64.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct AccountStatus
{
    /// <summary>Account balance</summary>
    public double Balance;
    /// <summary>Net equity = balance + unrealized_pnl</summary>
    public double Equity;
    /// <summary>Available funds for trading</summary>
    public double Available;
    /// <summary>Number of open positions</summary>
    public int PositionCount;
    /// <summary>Total profit/loss</summary>
    public double TotalPnl;
}

/// <summary>
/// Strategy parameters for the dual moving average strategy.
/// Matches Rust repr(C) StrategyParams struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct StrategyParams
{
    /// <summary>Short moving average period</summary>
    public int ShortMaPeriod;
    /// <summary>Long moving average period</summary>
    public int LongMaPeriod;
    /// <summary>Position size per trade</summary>
    public double PositionSize;
    /// <summary>Stop loss percentage (e.g., 0.02 = 2%)</summary>
    public double StopLossPct;
    /// <summary>Take profit percentage (e.g., 0.05 = 5%)</summary>
    public double TakeProfitPct;

    /// <summary>
    /// Creates default strategy parameters.
    /// </summary>
    public static StrategyParams Default => new()
    {
        ShortMaPeriod = 5,
        LongMaPeriod = 20,
        PositionSize = 100.0,
        StopLossPct = 0.02,
        TakeProfitPct = 0.05
    };
}


/// <summary>
/// Risk configuration parameters.
/// Matches Rust repr(C) RiskConfig struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RiskConfig
{
    /// <summary>Maximum orders per second</summary>
    public int MaxOrderRate;
    /// <summary>Maximum position size</summary>
    public double MaxPositionSize;
    /// <summary>Maximum single order value</summary>
    public double MaxOrderValue;
    /// <summary>Maximum drawdown percentage (e.g., 0.1 = 10%)</summary>
    public double MaxDrawdownPct;

    /// <summary>
    /// Creates default risk configuration.
    /// </summary>
    public static RiskConfig Default => new()
    {
        MaxOrderRate = 10,
        MaxPositionSize = 1000.0,
        MaxOrderValue = 100000.0,
        MaxDrawdownPct = 0.1
    };
}

/// <summary>
/// Data quality report from data cleansing.
/// Matches Rust repr(C) DataQualityReport struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DataQualityReport
{
    /// <summary>Total number of ticks processed</summary>
    public long TotalTicks;
    /// <summary>Number of valid ticks</summary>
    public long ValidTicks;
    /// <summary>Number of invalid ticks (price &lt;= 0 or volume &lt; 0)</summary>
    public long InvalidTicks;
    /// <summary>Number of anomaly ticks (price jumps &gt; 10%)</summary>
    public long AnomalyTicks;
    /// <summary>First timestamp in the dataset</summary>
    public long FirstTimestamp;
    /// <summary>Last timestamp in the dataset</summary>
    public long LastTimestamp;
}

/// <summary>
/// Backtest result structure.
/// Matches Rust repr(C) BacktestResult struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BacktestResult
{
    /// <summary>Final equity value</summary>
    public double FinalEquity;
    /// <summary>Total return percentage</summary>
    public double TotalReturnPct;
    /// <summary>Maximum drawdown percentage</summary>
    public double MaxDrawdownPct;
    /// <summary>Sharpe ratio</summary>
    public double SharpeRatio;
    /// <summary>Total number of trades</summary>
    public int TotalTrades;
    /// <summary>Number of winning trades</summary>
    public int WinningTrades;
    /// <summary>Number of losing trades</summary>
    public int LosingTrades;
}

/// <summary>
/// Direction constants for orders.
/// </summary>
public static class Direction
{
    public const int Buy = 1;
    public const int Sell = -1;
}

/// <summary>
/// Order type constants.
/// </summary>
public static class OrderType
{
    public const int Market = 0;
    public const int Limit = 1;
}
