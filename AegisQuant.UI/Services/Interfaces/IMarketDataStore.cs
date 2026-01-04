using AegisQuant.Interop;
using ScottPlot;

namespace AegisQuant.UI.Services.Interfaces;

/// <summary>
/// Configuration for data import with column mapping.
/// </summary>
public class DataImportConfig
{
    /// <summary>
    /// Path to the data file.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Name of the timestamp column.
    /// </summary>
    public string TimeColumnName { get; set; } = "timestamp";
    
    /// <summary>
    /// Name of the price column.
    /// </summary>
    public string PriceColumnName { get; set; } = "price";
    
    /// <summary>
    /// Name of the volume column.
    /// </summary>
    public string VolumeColumnName { get; set; } = "volume";
    
    /// <summary>
    /// Date format string (e.g., "yyyy-MM-dd", "unix").
    /// </summary>
    public string DateFormat { get; set; } = "unix";
    
    /// <summary>
    /// Whether to skip the first row (header).
    /// </summary>
    public bool SkipFirstRow { get; set; } = true;
}

/// <summary>
/// Interface for market data storage with multi-timeframe caching.
/// Requirements: 3.1, 3.2, 3.4, 3.5, 3.6, 3.7
/// </summary>
public interface IMarketDataStore
{
    /// <summary>
    /// Gets the raw tick data store (SoA layout).
    /// </summary>
    Services.TickDataStore RawTicks { get; }
    
    /// <summary>
    /// Gets the pre-computed OHLC cache for all timeframes.
    /// Thread-safe read access.
    /// </summary>
    IReadOnlyDictionary<string, List<OHLC>> OhlcCache { get; }
    
    /// <summary>
    /// Standard timeframes supported by the cache.
    /// </summary>
    static readonly string[] StandardTimeframes = { "1m", "5m", "15m", "30m", "1h", "4h", "1d" };
    
    /// <summary>
    /// Loads data from the Rust engine asynchronously.
    /// </summary>
    /// <param name="engine">Engine wrapper instance</param>
    /// <param name="config">Data import configuration</param>
    Task LoadFromRustEngineAsync(EngineWrapper engine, DataImportConfig config);
    
    /// <summary>
    /// Gets OHLC data for a specific timeframe.
    /// Returns a copy to prevent external modification.
    /// </summary>
    /// <param name="timeframe">Timeframe string (e.g., "1m", "5m", "1h")</param>
    /// <returns>List of OHLC bars for the timeframe</returns>
    List<OHLC> GetOhlcData(string timeframe);
    
    /// <summary>
    /// Clears all cached data.
    /// </summary>
    void Clear();
}
