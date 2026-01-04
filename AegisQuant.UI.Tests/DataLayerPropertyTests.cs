using Xunit;
using FsCheck;
using FsCheck.Xunit;
using AegisQuant.UI.Services;
using AegisQuant.UI.Services.Interfaces;
using ScottPlot;

namespace AegisQuant.UI.Tests;

/// <summary>
/// Property-based tests for the data layer.
/// Tests Properties 7, 8, and 16 from the design document.
/// </summary>
public class DataLayerPropertyTests
{
    #region Property 7: OHLC Cache Completeness
    
    /// <summary>
    /// Property 7: OHLC Cache Completeness
    /// For any successful data load, the MarketDataStore.OhlcCache SHALL contain 
    /// entries for all standard timeframes: 1m, 5m, 15m, 30m, 1h, 4h, 1d.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Fact]
    public void Property7_OhlcCacheCompleteness_AllTimeframesPresent()
    {
        // Arrange
        var store = new MarketDataStore();
        var bars1m = GenerateSample1mBars(100);
        
        // Act
        store.LoadFromBars(bars1m);
        
        // Assert - all standard timeframes should be present
        var expectedTimeframes = new[] { "1m", "5m", "15m", "30m", "1h", "4h", "1d" };
        foreach (var tf in expectedTimeframes)
        {
            var data = store.GetOhlcData(tf);
            Assert.NotNull(data);
            Assert.True(data.Count > 0 || tf == "4h" || tf == "1d", 
                $"Timeframe {tf} should have data (or be empty for longer timeframes with short input)");
        }
    }
    
    /// <summary>
    /// Property 7: OHLC Cache Completeness - Property-based test
    /// For any non-empty list of 1m bars, loading them SHALL populate all standard timeframes.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property7_OhlcCacheCompleteness_PropertyBased()
    {
        return Prop.ForAll(
            Gen.Choose(60, 1000).ToArbitrary(), // Generate between 60 and 1000 bars
            barCount =>
            {
                var store = new MarketDataStore();
                var bars1m = GenerateSample1mBars(barCount);
                
                store.LoadFromBars(bars1m);
                
                // All standard timeframes should be in the cache
                var cache = store.OhlcCache;
                var expectedTimeframes = new[] { "1m", "5m", "15m", "30m", "1h", "4h", "1d" };
                
                return expectedTimeframes.All(tf => cache.ContainsKey(tf));
            });
    }
    
    #endregion
    
    #region Property 8: Higher Timeframe Aggregation
    
    /// <summary>
    /// Property 8: Higher Timeframe Aggregation
    /// For any timeframe T > 1m, the OHLC bars SHALL be aggregates of consecutive 1m bars.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Fact]
    public void Property8_HigherTimeframeAggregation_5mFrom1m()
    {
        // Arrange - create exactly 10 1m bars (should produce 2 5m bars)
        var bars1m = new List<OHLC>();
        var baseTime = new DateTime(2024, 1, 1, 9, 0, 0);
        
        for (int i = 0; i < 10; i++)
        {
            bars1m.Add(new OHLC(
                100 + i,           // Open
                105 + i,           // High
                95 + i,            // Low
                102 + i,           // Close
                baseTime.AddMinutes(i),
                TimeSpan.FromMinutes(1)
            ));
        }
        
        // Act
        var bars5m = MarketDataStore.ResampleBars(bars1m, 5);
        
        // Assert
        Assert.Equal(2, bars5m.Count);
        
        // First 5m bar should aggregate bars 0-4
        Assert.Equal(100, bars5m[0].Open);  // First bar's open
        Assert.Equal(109, bars5m[0].High);  // Max high from bars 0-4
        Assert.Equal(95, bars5m[0].Low);    // Min low from bars 0-4
        Assert.Equal(106, bars5m[0].Close); // Last bar's close (bar 4)
        
        // Second 5m bar should aggregate bars 5-9
        Assert.Equal(105, bars5m[1].Open);  // Bar 5's open
        Assert.Equal(114, bars5m[1].High);  // Max high from bars 5-9
        Assert.Equal(100, bars5m[1].Low);   // Min low from bars 5-9
        Assert.Equal(111, bars5m[1].Close); // Last bar's close (bar 9)
    }

    /// <summary>
    /// Property 8: Higher Timeframe Aggregation - Property-based test
    /// For any resampling, the high of the aggregated bar SHALL be >= all source highs,
    /// and the low SHALL be <= all source lows.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property8_HigherTimeframeAggregation_HighLowBounds()
    {
        return Prop.ForAll(
            Gen.Choose(10, 100).ToArbitrary(),
            barCount =>
            {
                var bars1m = GenerateSample1mBars(barCount);
                var bars5m = MarketDataStore.ResampleBars(bars1m, 5);
                
                if (bars5m.Count == 0) return true;
                
                // For each 5m bar, verify high/low bounds
                var baseTime = bars1m[0].DateTime;
                
                foreach (var bar5m in bars5m)
                {
                    // Find all 1m bars that belong to this 5m period
                    var periodStart = bar5m.DateTime;
                    var periodEnd = periodStart.AddMinutes(5);
                    
                    var source1mBars = bars1m
                        .Where(b => b.DateTime >= periodStart && b.DateTime < periodEnd)
                        .ToList();
                    
                    if (source1mBars.Count == 0) continue;
                    
                    // High should be max of all source highs
                    var maxHigh = source1mBars.Max(b => b.High);
                    if (Math.Abs(bar5m.High - maxHigh) > 0.0001) return false;
                    
                    // Low should be min of all source lows
                    var minLow = source1mBars.Min(b => b.Low);
                    if (Math.Abs(bar5m.Low - minLow) > 0.0001) return false;
                }
                
                return true;
            });
    }
    
    /// <summary>
    /// Property 8: Higher Timeframe Aggregation - Open/Close preservation
    /// The open of aggregated bar SHALL equal the open of the first source bar,
    /// and the close SHALL equal the close of the last source bar.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property8_HigherTimeframeAggregation_OpenClosePreservation()
    {
        return Prop.ForAll(
            Gen.Choose(15, 100).ToArbitrary(),
            barCount =>
            {
                var bars1m = GenerateSample1mBars(barCount);
                var bars5m = MarketDataStore.ResampleBars(bars1m, 5);
                
                if (bars5m.Count == 0) return true;
                
                foreach (var bar5m in bars5m)
                {
                    var periodStart = bar5m.DateTime;
                    var periodEnd = periodStart.AddMinutes(5);
                    
                    var source1mBars = bars1m
                        .Where(b => b.DateTime >= periodStart && b.DateTime < periodEnd)
                        .OrderBy(b => b.DateTime)
                        .ToList();
                    
                    if (source1mBars.Count == 0) continue;
                    
                    // Open should match first bar's open
                    if (Math.Abs(bar5m.Open - source1mBars.First().Open) > 0.0001) return false;
                    
                    // Close should match last bar's close
                    if (Math.Abs(bar5m.Close - source1mBars.Last().Close) > 0.0001) return false;
                }
                
                return true;
            });
    }
    
    /// <summary>
    /// Property 8: Time-window based aggregation handles market gaps correctly.
    /// Bars across a gap should NOT be merged into the same period.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Fact]
    public void Property8_TimeWindowAggregation_HandlesMarketGaps()
    {
        // Arrange - create bars with a 2-hour gap (simulating lunch break)
        var bars1m = new List<OHLC>();
        var morningStart = new DateTime(2024, 1, 1, 9, 0, 0);
        var afternoonStart = new DateTime(2024, 1, 1, 13, 0, 0); // 2-hour gap
        
        // Morning session: 9:00-9:04 (5 bars)
        for (int i = 0; i < 5; i++)
        {
            bars1m.Add(new OHLC(100, 105, 95, 102, morningStart.AddMinutes(i), TimeSpan.FromMinutes(1)));
        }
        
        // Afternoon session: 13:00-13:04 (5 bars)
        for (int i = 0; i < 5; i++)
        {
            bars1m.Add(new OHLC(200, 205, 195, 202, afternoonStart.AddMinutes(i), TimeSpan.FromMinutes(1)));
        }
        
        // Act
        var bars5m = MarketDataStore.ResampleBars(bars1m, 5);
        
        // Assert - should have 2 separate 5m bars, not merged
        Assert.Equal(2, bars5m.Count);
        Assert.Equal(100, bars5m[0].Open);  // Morning bar
        Assert.Equal(200, bars5m[1].Open);  // Afternoon bar (not merged with morning)
    }
    
    #endregion
    
    #region Property 16: Memory Efficiency for Tick Storage
    
    /// <summary>
    /// Property 16: Memory Efficiency for Tick Storage
    /// For any dataset of 10 million ticks stored in TickDataStore, 
    /// the managed heap allocation SHALL NOT exceed 300MB.
    /// **Validates: Requirements 11.6**
    /// </summary>
    [Fact]
    public void Property16_MemoryEfficiency_CalculatedMemoryWithinBounds()
    {
        // Arrange
        var store = new TickDataStore();
        const int tickCount = 10_000_000;
        
        // Act
        store.Allocate(tickCount);
        
        // Assert - calculated memory should be 24 bytes per tick = 240MB
        var memoryBytes = store.GetMemoryUsageBytes();
        var memoryMB = memoryBytes / (1024.0 * 1024.0);
        
        // Primary requirement: memory must be under 300MB
        Assert.True(memoryMB <= 300, $"Memory usage {memoryMB:F2}MB exceeds 300MB limit");
        
        // Secondary check: memory should be approximately 24 bytes per tick (240MB for 10M ticks)
        // Allow some tolerance for .NET array overhead
        Assert.True(memoryMB >= 200 && memoryMB <= 260, 
            $"Memory usage {memoryMB:F2}MB is outside expected range of 200-260MB");
    }
    
    /// <summary>
    /// Property 16: Memory Efficiency - Property-based test
    /// For any tick count N, memory usage SHALL be exactly 24 * N bytes.
    /// **Validates: Requirements 11.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property16_MemoryEfficiency_LinearScaling()
    {
        return Prop.ForAll(
            Gen.Choose(1000, 100000).ToArbitrary(),
            tickCount =>
            {
                var store = new TickDataStore();
                store.Allocate(tickCount);
                
                var expectedBytes = (long)tickCount * 24;
                var actualBytes = store.GetMemoryUsageBytes();
                
                return actualBytes == expectedBytes;
            });
    }
    
    /// <summary>
    /// Property 16: TickDataStore SoA layout - no boxing/unboxing
    /// Verifies that tick data can be added and retrieved without boxing.
    /// **Validates: Requirements 11.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property16_TickDataStore_RoundTrip()
    {
        return Prop.ForAll(
            Arb.From<long>(),
            Arb.From<double>(),
            Arb.From<double>(),
            (timestamp, price, volume) =>
            {
                // Skip invalid values
                if (double.IsNaN(price) || double.IsInfinity(price) ||
                    double.IsNaN(volume) || double.IsInfinity(volume))
                    return true;
                
                var store = new TickDataStore();
                store.Allocate(1);
                store.Add(timestamp, price, volume);
                
                var tick = store.GetTick(0);
                
                return tick.Timestamp == timestamp &&
                       tick.Price == price &&
                       tick.Volume == volume;
            });
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Generates sample 1-minute OHLC bars for testing.
    /// </summary>
    private static List<OHLC> GenerateSample1mBars(int count)
    {
        var bars = new List<OHLC>();
        var baseTime = new DateTime(2024, 1, 1, 9, 0, 0);
        var random = new System.Random(42); // Fixed seed for reproducibility
        
        double price = 100.0;
        
        for (int i = 0; i < count; i++)
        {
            var change = (random.NextDouble() - 0.5) * 2; // -1 to +1
            var open = price;
            var close = price + change;
            var high = Math.Max(open, close) + random.NextDouble() * 0.5;
            var low = Math.Min(open, close) - random.NextDouble() * 0.5;
            
            bars.Add(new OHLC(
                open,
                high,
                low,
                close,
                baseTime.AddMinutes(i),
                TimeSpan.FromMinutes(1)
            ));
            
            price = close;
        }
        
        return bars;
    }
    
    #endregion
}
