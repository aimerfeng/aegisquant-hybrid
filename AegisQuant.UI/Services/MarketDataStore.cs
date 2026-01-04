using AegisQuant.Interop;
using AegisQuant.UI.Services.Interfaces;
using ScottPlot;

namespace AegisQuant.UI.Services;

/// <summary>
/// Market data storage with multi-timeframe OHLC caching.
/// Implements thread-safe access to pre-computed OHLC data for all standard timeframes.
/// 
/// Design Principles:
/// 1. Single Source of Truth: Rust engine loads data, C# caches OHLC
/// 2. Time-Window Aggregation: Handles market gaps (lunch breaks, weekends) correctly
/// 3. Thread Safety: Lock-based synchronization for cache access
/// 
/// Requirements: 3.1, 3.2, 3.4, 3.5, 3.6, 3.7
/// </summary>
public class MarketDataStore : IMarketDataStore
{
    /// <summary>
    /// SoA layout tick storage for memory efficiency.
    /// </summary>
    public TickDataStore RawTicks { get; } = new();
    
    /// <summary>
    /// Lock object for thread-safe cache access.
    /// </summary>
    private readonly object _cacheLock = new();
    
    /// <summary>
    /// Pre-computed OHLC cache for all timeframes.
    /// </summary>
    private readonly Dictionary<string, List<OHLC>> _cache = new();
    
    /// <summary>
    /// Gets the OHLC cache as read-only dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, List<OHLC>> OhlcCache
    {
        get
        {
            lock (_cacheLock)
            {
                // Return a snapshot to prevent external modification
                return new Dictionary<string, List<OHLC>>(_cache);
            }
        }
    }
    
    /// <summary>
    /// Standard timeframes: 1m, 5m, 15m, 30m, 1h, 4h, 1d
    /// </summary>
    public static readonly string[] StandardTimeframes = { "1m", "5m", "15m", "30m", "1h", "4h", "1d" };
    
    /// <summary>
    /// Maps timeframe strings to minutes.
    /// </summary>
    private static readonly Dictionary<string, int> TimeframeMinutes = new()
    {
        { "1m", 1 },
        { "5m", 5 },
        { "15m", 15 },
        { "30m", 30 },
        { "1h", 60 },
        { "4h", 240 },
        { "1d", 1440 }
    };
    
    /// <summary>
    /// Loads data from the Rust engine and builds OHLC cache for all timeframes.
    /// </summary>
    /// <param name="engine">Engine wrapper instance</param>
    /// <param name="config">Data import configuration</param>
    public async Task LoadFromRustEngineAsync(EngineWrapper engine, DataImportConfig config)
    {
        // TODO: In future, call Rust engine to get 1m OHLC data
        // For now, this is a placeholder that will be implemented when FFI is ready
        // var bars1m = await Task.Run(() => engine.GetOhlcData("1m"));
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Loads 1-minute OHLC data and builds cache for all timeframes.
    /// This is the main entry point for populating the cache.
    /// </summary>
    /// <param name="bars1m">1-minute OHLC bars from Rust engine</param>
    public void LoadFromBars(List<OHLC> bars1m)
    {
        if (bars1m == null || bars1m.Count == 0)
            return;
        
        lock (_cacheLock)
        {
            _cache.Clear();
            
            // Store 1m bars directly
            _cache["1m"] = new List<OHLC>(bars1m);
            
            // Resample higher timeframes from 1m (fast, no tick iteration)
            _cache["5m"] = ResampleBars(bars1m, 5);
            _cache["15m"] = ResampleBars(bars1m, 15);
            _cache["30m"] = ResampleBars(bars1m, 30);
            _cache["1h"] = ResampleBars(bars1m, 60);
            _cache["4h"] = ResampleBars(bars1m, 240);
            _cache["1d"] = ResampleBars(bars1m, 1440);
        }
    }
    
    /// <summary>
    /// Gets OHLC data for a specific timeframe.
    /// Returns a copy to prevent external modification.
    /// </summary>
    /// <param name="timeframe">Timeframe string (e.g., "1m", "5m", "1h")</param>
    /// <returns>List of OHLC bars for the timeframe, or empty list if not found</returns>
    public List<OHLC> GetOhlcData(string timeframe)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(timeframe, out var data)
                ? new List<OHLC>(data)  // Return copy to prevent external modification
                : new List<OHLC>();
        }
    }
    
    /// <summary>
    /// Clears all cached data.
    /// </summary>
    public void Clear()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
        RawTicks.Clear();
    }

    /// <summary>
    /// Resamples 1-minute bars to a higher timeframe using time-window based aggregation.
    /// 
    /// CRITICAL: Uses time-window based aggregation, NOT index-based.
    /// This correctly handles market gaps (lunch breaks, weekends, holidays).
    /// 
    /// Example: If there's a 2-hour lunch break, we don't merge bars across the gap.
    /// Each bar belongs to its calculated time period based on its timestamp.
    /// </summary>
    /// <param name="source">Source 1-minute OHLC bars</param>
    /// <param name="periodMinutes">Target period in minutes (e.g., 5, 15, 60)</param>
    /// <returns>Resampled OHLC bars</returns>
    public static List<OHLC> ResampleBars(List<OHLC> source, int periodMinutes)
    {
        if (source == null || source.Count == 0 || periodMinutes <= 0)
            return new List<OHLC>();
        
        var result = new List<OHLC>();
        OHLC? currentBar = null;
        DateTime periodEnd = DateTime.MinValue;
        
        foreach (var bar in source)
        {
            // Calculate the period this bar belongs to (align to period boundary)
            long ticks = bar.DateTime.Ticks;
            long periodTicks = TimeSpan.FromMinutes(periodMinutes).Ticks;
            long periodStartTicks = (ticks / periodTicks) * periodTicks;
            DateTime calculatedEnd = new DateTime(periodStartTicks).AddMinutes(periodMinutes);
            
            if (currentBar == null || calculatedEnd > periodEnd)
            {
                // Start new bar - either first bar or new time period
                if (currentBar != null)
                {
                    result.Add(currentBar.Value);
                }
                
                periodEnd = calculatedEnd;
                currentBar = new OHLC(
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    new DateTime(periodStartTicks),
                    TimeSpan.FromMinutes(periodMinutes)
                );
            }
            else
            {
                // Merge into current bar - same time period
                currentBar = new OHLC(
                    currentBar.Value.Open,                           // Keep original open
                    Math.Max(currentBar.Value.High, bar.High),       // Update high
                    Math.Min(currentBar.Value.Low, bar.Low),         // Update low
                    bar.Close,                                        // Update close to latest
                    currentBar.Value.DateTime,                        // Keep period start time
                    currentBar.Value.TimeSpan                         // Keep timespan
                );
            }
        }
        
        // Don't forget the last bar
        if (currentBar != null)
        {
            result.Add(currentBar.Value);
        }
        
        return result;
    }
    
    /// <summary>
    /// Checks if the cache contains all standard timeframes.
    /// Useful for validation after loading.
    /// </summary>
    /// <returns>True if all standard timeframes are cached</returns>
    public bool HasAllTimeframes()
    {
        lock (_cacheLock)
        {
            foreach (var tf in StandardTimeframes)
            {
                if (!_cache.ContainsKey(tf) || _cache[tf].Count == 0)
                    return false;
            }
            return true;
        }
    }
    
    /// <summary>
    /// Gets the count of bars for a specific timeframe.
    /// </summary>
    /// <param name="timeframe">Timeframe string</param>
    /// <returns>Number of bars, or 0 if timeframe not found</returns>
    public int GetBarCount(string timeframe)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(timeframe, out var data) ? data.Count : 0;
        }
    }
    
    /// <summary>
    /// Converts a timeframe string to minutes.
    /// </summary>
    /// <param name="timeframe">Timeframe string (e.g., "1m", "5m", "1h")</param>
    /// <returns>Minutes, or 0 if invalid timeframe</returns>
    public static int GetTimeframeMinutes(string timeframe)
    {
        return TimeframeMinutes.TryGetValue(timeframe, out var minutes) ? minutes : 0;
    }
}
