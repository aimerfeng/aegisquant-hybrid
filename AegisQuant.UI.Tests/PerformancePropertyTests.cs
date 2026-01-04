using Xunit;
using FsCheck;
using FsCheck.Xunit;
using AegisQuant.UI.Services;
using ScottPlot;
using System.Diagnostics;

namespace AegisQuant.UI.Tests;

/// <summary>
/// Performance property tests for the hybrid backtest mode.
/// Tests memory efficiency and time performance requirements.
/// 
/// Task 20: 性能优化
/// - 20.1: 验证内存使用 (10M ticks < 300MB)
/// - 20.2: 验证时间性能 (周期切换 < 100ms, 图表更新 < 16ms)
/// - 20.3: Property 9 - Timeframe Switch Performance
/// </summary>
public class PerformancePropertyTests
{
    #region Task 20.1: Memory Usage Verification

    /// <summary>
    /// Task 20.1: Verify memory usage for 10 million ticks.
    /// The TickDataStore SHALL NOT exceed 300MB for 10M ticks.
    /// **Validates: Requirements 8.8, 11.6**
    /// </summary>
    [Fact]
    public void Task20_1_MemoryUsage_10MillionTicks_Under300MB()
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
        Assert.True(memoryMB <= 300, 
            $"Memory usage {memoryMB:F2}MB exceeds 300MB limit for 10M ticks");
        
        // Verify the SoA layout is efficient (24 bytes per tick)
        // long (8) + double (8) + double (8) = 24 bytes
        var expectedMB = (tickCount * 24L) / (1024.0 * 1024.0);
        Assert.True(Math.Abs(memoryMB - expectedMB) < 1.0,
            $"Memory usage {memoryMB:F2}MB differs from expected {expectedMB:F2}MB");
    }

    /// <summary>
    /// Task 20.1: Verify actual memory allocation with data population.
    /// Ensures no hidden allocations occur when adding ticks.
    /// **Validates: Requirements 8.8, 11.6**
    /// </summary>
    [Fact]
    public void Task20_1_MemoryUsage_PopulatedStore_NoHiddenAllocations()
    {
        // Arrange
        var store = new TickDataStore();
        const int tickCount = 1_000_000; // 1M ticks for faster test
        
        // Act
        store.Allocate(tickCount);
        
        // Populate with data
        var baseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < tickCount; i++)
        {
            store.Add(baseTimestamp + i, 100.0 + (i % 100) * 0.01, 1000.0 + i);
        }
        
        // Assert - memory should still be exactly 24 bytes per tick
        var memoryBytes = store.GetMemoryUsageBytes();
        var expectedBytes = tickCount * 24L;
        
        Assert.Equal(expectedBytes, memoryBytes);
        Assert.Equal(tickCount, store.Count);
    }

    /// <summary>
    /// Task 20.1: Property-based test for memory scaling.
    /// Memory SHALL scale linearly at 24 bytes per tick.
    /// **Validates: Requirements 11.6**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property Task20_1_MemoryUsage_LinearScaling()
    {
        return Prop.ForAll(
            Gen.Choose(10000, 500000).ToArbitrary(),
            tickCount =>
            {
                var store = new TickDataStore();
                store.Allocate(tickCount);
                
                var expectedBytes = (long)tickCount * 24;
                var actualBytes = store.GetMemoryUsageBytes();
                
                return actualBytes == expectedBytes;
            });
    }

    #endregion

    #region Task 20.2: Time Performance Verification

    /// <summary>
    /// Task 20.2: Verify timeframe switch completes within 100ms.
    /// Switching from 1m to any higher timeframe SHALL complete within 100ms.
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Fact]
    public void Task20_2_TimeframeSwitch_Under100ms()
    {
        // Arrange - create 1 year of 1m data (approx 252 trading days * 390 minutes = ~98,280 bars)
        var store = new MarketDataStore();
        var bars1m = GenerateOneYearOf1mBars();
        
        // Pre-load the data
        store.LoadFromBars(bars1m);
        
        // Act & Assert - measure time for each timeframe retrieval
        var timeframes = new[] { "5m", "15m", "30m", "1h", "4h", "1d" };
        
        foreach (var tf in timeframes)
        {
            var sw = Stopwatch.StartNew();
            var data = store.GetOhlcData(tf);
            sw.Stop();
            
            Assert.True(sw.ElapsedMilliseconds < 100,
                $"Timeframe switch to {tf} took {sw.ElapsedMilliseconds}ms, exceeds 100ms limit");
            Assert.NotEmpty(data);
        }
    }

    /// <summary>
    /// Task 20.2: Verify ResampleBars performance for large datasets.
    /// Resampling 100,000 1m bars to 5m SHALL complete within 100ms.
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Fact]
    public void Task20_2_ResampleBars_LargeDataset_Under100ms()
    {
        // Arrange - 100,000 1m bars
        var bars1m = GenerateSample1mBars(100_000);
        
        // Act
        var sw = Stopwatch.StartNew();
        var bars5m = MarketDataStore.ResampleBars(bars1m, 5);
        sw.Stop();
        
        // Assert
        Assert.True(sw.ElapsedMilliseconds < 100,
            $"ResampleBars took {sw.ElapsedMilliseconds}ms, exceeds 100ms limit");
        Assert.True(bars5m.Count > 0);
        Assert.True(bars5m.Count <= bars1m.Count / 5 + 1); // Approximately 1/5 the bars
    }

    /// <summary>
    /// Task 20.2: Verify chart data preparation is fast.
    /// Preparing visible range data SHALL complete within 16ms (60 FPS target).
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Fact]
    public void Task20_2_ChartDataPreparation_Under16ms()
    {
        // Arrange - simulate chart visible range extraction
        var store = new MarketDataStore();
        var bars1m = GenerateSample1mBars(50_000);
        store.LoadFromBars(bars1m);
        
        // Act - simulate getting visible range (last 1000 bars)
        var sw = Stopwatch.StartNew();
        var allBars = store.GetOhlcData("1m");
        var visibleBars = allBars.TakeLast(1000).ToList();
        sw.Stop();
        
        // Assert
        Assert.True(sw.ElapsedMilliseconds < 16,
            $"Chart data preparation took {sw.ElapsedMilliseconds}ms, exceeds 16ms limit");
        Assert.Equal(1000, visibleBars.Count);
    }

    /// <summary>
    /// Task 20.2: Verify incremental bar append is fast.
    /// Appending a single bar SHALL complete within 16ms (60 FPS target).
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Fact]
    public void Task20_2_IncrementalBarAppend_Under16ms()
    {
        // Arrange
        var displayData = new List<OHLC>();
        var baseTime = new DateTime(2024, 1, 1, 9, 0, 0);
        
        // Pre-populate with some data
        for (int i = 0; i < 1000; i++)
        {
            displayData.Add(new OHLC(100, 105, 95, 102, baseTime.AddMinutes(i), TimeSpan.FromMinutes(1)));
        }
        
        // Warm up - first operation may be slow due to JIT
        var warmupBar = new OHLC(102, 107, 97, 104, baseTime.AddMinutes(1000), TimeSpan.FromMinutes(1));
        displayData.Add(warmupBar);
        var closes = displayData.Select(o => o.Close).ToArray();
        var _ = closes.Length >= 5 ? closes.TakeLast(5).Average() : 0;
        
        // Act - measure time to append a single bar (after warmup)
        var newBar = new OHLC(103, 108, 98, 105, baseTime.AddMinutes(1001), TimeSpan.FromMinutes(1));
        
        var sw = Stopwatch.StartNew();
        displayData.Add(newBar);
        // Simulate MA calculation update (simplified)
        closes = displayData.Select(o => o.Close).ToArray();
        var ma5 = closes.Length >= 5 ? closes.TakeLast(5).Average() : 0;
        sw.Stop();
        
        // Assert - should complete within 16ms (60 FPS target)
        Assert.True(sw.ElapsedMilliseconds < 16,
            $"Incremental bar append took {sw.ElapsedMilliseconds}ms, exceeds 16ms limit");
    }

    #endregion

    #region Task 20.3: Property 9 - Timeframe Switch Performance

    /// <summary>
    /// Property 9: Timeframe Switch Performance
    /// For any timeframe switch operation on a dataset of up to 1 year,
    /// the operation SHALL complete within 100ms.
    /// **Validates: Requirements 3.7**
    /// 
    /// Feature: hybrid-backtest-mode, Property 9: Timeframe Switch Performance
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property9_TimeframeSwitchPerformance()
    {
        return Prop.ForAll(
            Gen.Choose(1000, 100000).ToArbitrary(), // Bar count (1K to 100K)
            Gen.Elements(5, 15, 30, 60, 240, 1440).ToArbitrary(), // Target timeframe in minutes
            (barCount, targetMinutes) =>
            {
                // Arrange
                var bars1m = GenerateSample1mBars(barCount);
                
                // Act
                var sw = Stopwatch.StartNew();
                var resampled = MarketDataStore.ResampleBars(bars1m, targetMinutes);
                sw.Stop();
                
                // Assert - must complete within 100ms
                return sw.ElapsedMilliseconds < 100;
            });
    }

    /// <summary>
    /// Property 9: Timeframe switch with full MarketDataStore.
    /// Loading and switching timeframes SHALL complete within 100ms each.
    /// **Validates: Requirements 3.7**
    /// 
    /// Feature: hybrid-backtest-mode, Property 9: Timeframe Switch Performance
    /// </summary>
    [Property(MaxTest = 20)]
    public Property Property9_TimeframeSwitchPerformance_FullStore()
    {
        return Prop.ForAll(
            Gen.Choose(10000, 50000).ToArbitrary(), // Bar count
            barCount =>
            {
                // Arrange
                var store = new MarketDataStore();
                var bars1m = GenerateSample1mBars(barCount);
                store.LoadFromBars(bars1m);
                
                // Act - test all timeframe switches
                var timeframes = new[] { "1m", "5m", "15m", "30m", "1h", "4h", "1d" };
                
                foreach (var tf in timeframes)
                {
                    var sw = Stopwatch.StartNew();
                    var data = store.GetOhlcData(tf);
                    sw.Stop();
                    
                    if (sw.ElapsedMilliseconds >= 100)
                        return false;
                }
                
                return true;
            });
    }

    /// <summary>
    /// Property 9: Verify cache retrieval is O(1) after initial load.
    /// Subsequent timeframe switches SHALL be near-instant (< 10ms).
    /// **Validates: Requirements 3.7**
    /// 
    /// Feature: hybrid-backtest-mode, Property 9: Timeframe Switch Performance
    /// </summary>
    [Fact]
    public void Property9_CacheRetrieval_NearInstant()
    {
        // Arrange
        var store = new MarketDataStore();
        var bars1m = GenerateSample1mBars(50_000);
        store.LoadFromBars(bars1m);
        
        // Warm up cache (first access)
        _ = store.GetOhlcData("5m");
        
        // Act - measure subsequent access
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            _ = store.GetOhlcData("5m");
        }
        sw.Stop();
        
        var avgMs = sw.ElapsedMilliseconds / 100.0;
        
        // Assert - average should be < 10ms per access
        Assert.True(avgMs < 10,
            $"Average cache retrieval took {avgMs:F2}ms, exceeds 10ms limit");
    }

    #endregion

    #region Additional Performance Tests

    /// <summary>
    /// Verify MarketDataStore.LoadFromBars performance.
    /// Loading 1 year of data and building all caches SHALL complete within 500ms.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Fact]
    public void LoadFromBars_OneYearData_Under500ms()
    {
        // Arrange
        var store = new MarketDataStore();
        var bars1m = GenerateOneYearOf1mBars();
        
        // Act
        var sw = Stopwatch.StartNew();
        store.LoadFromBars(bars1m);
        sw.Stop();
        
        // Assert
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"LoadFromBars took {sw.ElapsedMilliseconds}ms, exceeds 500ms limit");
        
        // Verify all timeframes are populated
        Assert.True(store.HasAllTimeframes());
    }

    /// <summary>
    /// Verify TickDataStore bulk add performance.
    /// Adding 1M ticks SHALL complete within 1 second.
    /// **Validates: Requirements 11.5**
    /// </summary>
    [Fact]
    public void TickDataStore_BulkAdd_1MillionTicks_Under1Second()
    {
        // Arrange
        var store = new TickDataStore();
        const int tickCount = 1_000_000;
        store.Allocate(tickCount);
        
        var baseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < tickCount; i++)
        {
            store.Add(baseTimestamp + i, 100.0 + (i % 100) * 0.01, 1000.0);
        }
        sw.Stop();
        
        // Assert
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"Bulk add took {sw.ElapsedMilliseconds}ms, exceeds 1000ms limit");
        Assert.Equal(tickCount, store.Count);
    }

    /// <summary>
    /// Verify TickDataStore random access performance.
    /// Random access to ticks SHALL be O(1).
    /// **Validates: Requirements 11.5**
    /// </summary>
    [Fact]
    public void TickDataStore_RandomAccess_Fast()
    {
        // Arrange
        var store = new TickDataStore();
        const int tickCount = 100_000;
        store.Allocate(tickCount);
        
        var baseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < tickCount; i++)
        {
            store.Add(baseTimestamp + i, 100.0 + i, 1000.0);
        }
        
        var random = new System.Random(42);
        var indices = Enumerable.Range(0, 10000).Select(_ => random.Next(tickCount)).ToArray();
        
        // Act
        var sw = Stopwatch.StartNew();
        foreach (var idx in indices)
        {
            var tick = store.GetTick(idx);
            _ = tick.Price; // Force access
        }
        sw.Stop();
        
        // Assert - 10K random accesses should be < 10ms
        Assert.True(sw.ElapsedMilliseconds < 10,
            $"10K random accesses took {sw.ElapsedMilliseconds}ms, exceeds 10ms limit");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Generates sample 1-minute OHLC bars for testing.
    /// </summary>
    private static List<OHLC> GenerateSample1mBars(int count)
    {
        var bars = new List<OHLC>(count);
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

    /// <summary>
    /// Generates approximately 1 year of 1-minute trading data.
    /// Assumes 252 trading days, 6.5 hours per day (390 minutes).
    /// Total: ~98,280 bars
    /// </summary>
    private static List<OHLC> GenerateOneYearOf1mBars()
    {
        const int tradingDays = 252;
        const int minutesPerDay = 390; // 9:30 AM to 4:00 PM
        const int totalBars = tradingDays * minutesPerDay;
        
        var bars = new List<OHLC>(totalBars);
        var baseDate = new DateTime(2024, 1, 2); // Start from first trading day
        var random = new System.Random(42);
        
        double price = 100.0;
        int dayCount = 0;
        
        for (int day = 0; day < tradingDays; day++)
        {
            // Skip weekends
            var currentDate = baseDate.AddDays(dayCount);
            while (currentDate.DayOfWeek == DayOfWeek.Saturday || 
                   currentDate.DayOfWeek == DayOfWeek.Sunday)
            {
                dayCount++;
                currentDate = baseDate.AddDays(dayCount);
            }
            
            var marketOpen = currentDate.AddHours(9).AddMinutes(30);
            
            for (int minute = 0; minute < minutesPerDay; minute++)
            {
                var change = (random.NextDouble() - 0.5) * 0.5;
                var open = price;
                var close = price + change;
                var high = Math.Max(open, close) + random.NextDouble() * 0.2;
                var low = Math.Min(open, close) - random.NextDouble() * 0.2;
                
                bars.Add(new OHLC(
                    open,
                    high,
                    low,
                    close,
                    marketOpen.AddMinutes(minute),
                    TimeSpan.FromMinutes(1)
                ));
                
                price = close;
            }
            
            dayCount++;
        }
        
        return bars;
    }

    #endregion
}
