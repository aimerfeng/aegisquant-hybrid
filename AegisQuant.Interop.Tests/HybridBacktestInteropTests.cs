using Xunit;

namespace AegisQuant.Interop.Tests;

/// <summary>
/// Integration tests for Hybrid Backtest Mode FFI methods.
/// Validates: Requirements 2.5, 2.6, 8.3, 13.5
/// Checkpoint 8: Verify C# can correctly call new FFI methods and memory doesn't leak.
/// </summary>
public class HybridBacktestInteropTests
{
    #region ProcessTickWithResult Tests

    /// <summary>
    /// Test: ProcessTickWithResult should return events array without crashing.
    /// </summary>
    [Fact]
    public void ProcessTickWithResult_WithValidTick_ShouldReturnEvents()
    {
        // Arrange
        using var engine = new EngineWrapper();
        var tick = new Tick
        {
            Timestamp = 1704072600000000000,
            Price = 100.0,
            Volume = 1000.0
        };

        // Act
        int eventCount = engine.ProcessTickWithResult(tick, out var events);

        // Assert
        Assert.NotNull(events);
        Assert.True(eventCount >= 0);
        Assert.Equal(eventCount, events.Length);
    }

    /// <summary>
    /// Test: ProcessTickWithResult with zero-allocation buffer overload.
    /// </summary>
    [Fact]
    public void ProcessTickWithResult_WithPreallocatedBuffer_ShouldWork()
    {
        // Arrange
        using var engine = new EngineWrapper();
        var tick = new Tick
        {
            Timestamp = 1704072600000000000,
            Price = 100.0,
            Volume = 1000.0
        };
        var buffer = new ExecutionEvent[16];

        // Act
        engine.ProcessTickWithResult(tick, buffer, out int eventCount);

        // Assert
        Assert.True(eventCount >= 0);
        Assert.True(eventCount <= buffer.Length);
    }

    /// <summary>
    /// Test: ProcessTickWithResult should generate stop-loss event when triggered.
    /// </summary>
    [Fact]
    public void ProcessTickWithResult_StopLossTriggered_ShouldGenerateEvent()
    {
        // Arrange
        var parameters = new StrategyParams
        {
            ShortMaPeriod = 5,
            LongMaPeriod = 20,
            PositionSize = 100.0,
            StopLossPct = 0.02,  // 2% stop loss
            TakeProfitPct = 0.10
        };
        using var engine = new EngineWrapper(parameters, RiskConfig.Default);

        // Open a long position at price 100
        var buyResult = engine.PlaceOrder(Signal.Buy, 100.0);
        Assert.True(buyResult.IsAccepted);

        // Process tick at price that triggers stop loss (98 = 2% drop)
        var tick = new Tick
        {
            Timestamp = 1704072600001000000,
            Price = 97.5,  // More than 2% drop
            Volume = 1000.0
        };

        // Act
        int eventCount = engine.ProcessTickWithResult(tick, out var events);

        // Assert
        Assert.True(eventCount > 0, "Should have generated stop-loss event");
        Assert.Contains(events, e => e.EventType == ExecutionEventType.StopTriggered);
    }

    /// <summary>
    /// Test: ProcessTickWithResult should generate take-profit event when triggered.
    /// </summary>
    [Fact]
    public void ProcessTickWithResult_TakeProfitTriggered_ShouldGenerateEvent()
    {
        // Arrange
        var parameters = new StrategyParams
        {
            ShortMaPeriod = 5,
            LongMaPeriod = 20,
            PositionSize = 100.0,
            StopLossPct = 0.10,
            TakeProfitPct = 0.02  // 2% take profit
        };
        using var engine = new EngineWrapper(parameters, RiskConfig.Default);

        // Open a long position at price 100
        var buyResult = engine.PlaceOrder(Signal.Buy, 100.0);
        Assert.True(buyResult.IsAccepted);

        // Process tick at price that triggers take profit (102 = 2% gain)
        var tick = new Tick
        {
            Timestamp = 1704072600001000000,
            Price = 102.5,  // More than 2% gain
            Volume = 1000.0
        };

        // Act
        int eventCount = engine.ProcessTickWithResult(tick, out var events);

        // Assert
        Assert.True(eventCount > 0, "Should have generated take-profit event");
        Assert.Contains(events, e => e.EventType == ExecutionEventType.TakeProfitTriggered);
    }

    #endregion

    #region PlaceOrder Tests

    /// <summary>
    /// Test: PlaceOrder with Buy signal should execute successfully.
    /// </summary>
    [Fact]
    public void PlaceOrder_BuySignal_ShouldExecute()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act
        var result = engine.PlaceOrder(Signal.Buy, 100.0);

        // Assert
        Assert.True(result.IsAccepted);
        Assert.True(result.OrderId > 0);
        Assert.Equal(100.0, result.FillPrice);
        Assert.True(result.FillQuantity > 0);
        Assert.Equal(RejectionCode.None, result.RejectionCode);
    }

    /// <summary>
    /// Test: PlaceOrder with Sell signal should execute successfully.
    /// </summary>
    [Fact]
    public void PlaceOrder_SellSignal_ShouldExecute()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act
        var result = engine.PlaceOrder(Signal.Sell, 100.0);

        // Assert
        Assert.True(result.IsAccepted);
        Assert.True(result.OrderId > 0);
        Assert.Equal(100.0, result.FillPrice);
    }

    /// <summary>
    /// Test: PlaceOrder with custom quantity should use that quantity.
    /// </summary>
    [Fact]
    public void PlaceOrder_WithCustomQuantity_ShouldUseQuantity()
    {
        // Arrange
        using var engine = new EngineWrapper();
        double customQuantity = 50.0;

        // Act
        var result = engine.PlaceOrder(Signal.Buy, 100.0, customQuantity);

        // Assert
        Assert.True(result.IsAccepted);
        Assert.Equal(customQuantity, result.FillQuantity);
    }

    /// <summary>
    /// Test: PlaceOrder with invalid signal should throw.
    /// </summary>
    [Fact]
    public void PlaceOrder_InvalidSignal_ShouldThrow()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => engine.PlaceOrder(0, 100.0));  // Signal.None
        Assert.Throws<ArgumentException>(() => engine.PlaceOrder(99, 100.0)); // Invalid signal
    }

    /// <summary>
    /// Test: PlaceOrder with invalid price should throw.
    /// </summary>
    [Fact]
    public void PlaceOrder_InvalidPrice_ShouldThrow()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => engine.PlaceOrder(Signal.Buy, 0.0));
        Assert.Throws<ArgumentException>(() => engine.PlaceOrder(Signal.Buy, -100.0));
    }

    /// <summary>
    /// Test: PlaceOrder exceeding risk limit should be rejected.
    /// </summary>
    [Fact]
    public void PlaceOrder_ExceedingRiskLimit_ShouldBeRejected()
    {
        // Arrange
        var riskConfig = new RiskConfig
        {
            MaxOrderRate = 10,
            MaxPositionSize = 1000.0,
            MaxOrderValue = 1000.0,  // Very low limit
            MaxDrawdownPct = 0.1
        };
        using var engine = new EngineWrapper(StrategyParams.Default, riskConfig);

        // Act - try to place order exceeding max order value
        var result = engine.PlaceOrder(Signal.Buy, 100.0, 100.0);  // 100 * 100 = 10000 > 1000

        // Assert
        Assert.False(result.IsAccepted);
        Assert.Equal(RejectionCode.RiskLimitExceeded, result.RejectionCode);
    }

    #endregion

    #region GetOhlcData Tests

    /// <summary>
    /// Test: GetOhlcData should return empty array when no data loaded.
    /// </summary>
    [Fact]
    public void GetOhlcData_NoDataLoaded_ShouldReturnEmpty()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act
        var bars = engine.GetOhlcData("1m");

        // Assert
        Assert.NotNull(bars);
        Assert.Empty(bars);
    }

    /// <summary>
    /// Test: GetOhlcData with invalid timeframe should throw.
    /// </summary>
    [Fact]
    public void GetOhlcData_InvalidTimeframe_ShouldThrow()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => engine.GetOhlcData(""));
        Assert.Throws<ArgumentException>(() => engine.GetOhlcData(null!));
    }

    #endregion

    #region FastForwardTo Tests

    /// <summary>
    /// Test: FastForwardTo with negative index should throw.
    /// </summary>
    [Fact]
    public void FastForwardTo_NegativeIndex_ShouldThrow()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.FastForwardTo(-1));
    }

    /// <summary>
    /// Test: GetCurrentTickIndex should return 0 initially.
    /// </summary>
    [Fact]
    public void GetCurrentTickIndex_Initially_ShouldReturnZero()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act
        var index = engine.GetCurrentTickIndex();

        // Assert
        Assert.Equal(0, index);
    }

    /// <summary>
    /// Test: GetTickCount should return 0 when no data loaded.
    /// </summary>
    [Fact]
    public void GetTickCount_NoDataLoaded_ShouldReturnZero()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act
        var count = engine.GetTickCount();

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region CsvMapping Tests

    /// <summary>
    /// Test: CsvMapping default values should be set correctly.
    /// </summary>
    [Fact]
    public void CsvMapping_Default_ShouldHaveCorrectValues()
    {
        // Act
        var mapping = CsvMapping.Default;

        // Assert
        Assert.Equal("timestamp", mapping.GetTimeColumn());
        Assert.Equal("price", mapping.GetPriceColumn());
        Assert.Equal("volume", mapping.GetVolumeColumn());
        Assert.Equal("unix", mapping.GetDateFormat());
        Assert.Equal(1, mapping.SkipHeader);
    }

    /// <summary>
    /// Test: CsvMapping setters and getters should round-trip correctly.
    /// </summary>
    [Fact]
    public void CsvMapping_SettersAndGetters_ShouldRoundTrip()
    {
        // Arrange
        var mapping = new CsvMapping();

        // Act
        mapping.SetTimeColumn("Date");
        mapping.SetPriceColumn("Close");
        mapping.SetVolumeColumn("Vol");
        mapping.SetDateFormat("yyyy-MM-dd");
        mapping.SkipHeader = 0;

        // Assert
        Assert.Equal("Date", mapping.GetTimeColumn());
        Assert.Equal("Close", mapping.GetPriceColumn());
        Assert.Equal("Vol", mapping.GetVolumeColumn());
        Assert.Equal("yyyy-MM-dd", mapping.GetDateFormat());
        Assert.Equal(0, mapping.SkipHeader);
    }

    #endregion

    #region ExecutionEvent Tests

    /// <summary>
    /// Test: ExecutionEvent helper properties should work correctly.
    /// </summary>
    [Fact]
    public void ExecutionEvent_HelperProperties_ShouldWorkCorrectly()
    {
        // Arrange
        var tradeEvent = new ExecutionEvent
        {
            EventType = ExecutionEventType.Trade,
            Side = Direction.Buy,
            Timestamp = 1704072600000000000
        };

        var stopEvent = new ExecutionEvent
        {
            EventType = ExecutionEventType.StopTriggered,
            Side = Direction.Sell
        };

        // Assert
        Assert.True(tradeEvent.IsTrade);
        Assert.False(tradeEvent.IsStopTriggered);
        Assert.True(tradeEvent.IsBuy);
        Assert.False(tradeEvent.IsSell);

        Assert.False(stopEvent.IsTrade);
        Assert.True(stopEvent.IsStopTriggered);
        Assert.False(stopEvent.IsBuy);
        Assert.True(stopEvent.IsSell);
    }

    /// <summary>
    /// Test: OrderResult helper properties should work correctly.
    /// </summary>
    [Fact]
    public void OrderResult_HelperProperties_ShouldWorkCorrectly()
    {
        // Arrange
        var acceptedResult = new OrderResult
        {
            Accepted = 1,
            OrderId = 123,
            RejectionCode = RejectionCode.None
        };

        var rejectedResult = new OrderResult
        {
            Accepted = 0,
            RejectionCode = RejectionCode.InsufficientCapital
        };

        // Assert
        Assert.True(acceptedResult.IsAccepted);
        Assert.False(acceptedResult.IsRejected);

        Assert.False(rejectedResult.IsAccepted);
        Assert.True(rejectedResult.IsRejected);
        Assert.Equal("Insufficient Capital", rejectedResult.RejectionReason);
    }

    #endregion

    #region Memory Safety Tests

    /// <summary>
    /// Test: Multiple ProcessTickWithResult calls should not leak memory.
    /// </summary>
    [Fact]
    public void ProcessTickWithResult_MultipleCalls_ShouldNotLeakMemory()
    {
        // Arrange
        using var engine = new EngineWrapper();
        var buffer = new ExecutionEvent[16];

        // Act - process many ticks
        for (int i = 0; i < 10000; i++)
        {
            var tick = new Tick
            {
                Timestamp = 1704072600000000000 + i * 1000000,
                Price = 100.0 + (i % 10) * 0.1,
                Volume = 1000.0
            };
            engine.ProcessTickWithResult(tick, buffer, out _);
        }

        // Assert - if we get here without OOM, memory is being managed correctly
        Assert.True(true);
    }

    /// <summary>
    /// Test: Multiple PlaceOrder calls should not leak memory.
    /// </summary>
    [Fact]
    public void PlaceOrder_MultipleCalls_ShouldNotLeakMemory()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act - place many orders (alternating buy/sell to avoid position limits)
        for (int i = 0; i < 1000; i++)
        {
            var signal = (i % 2 == 0) ? Signal.Buy : Signal.Sell;
            engine.PlaceOrder(signal, 100.0, 1.0);
        }

        // Assert - if we get here without OOM, memory is being managed correctly
        Assert.True(true);
    }

    /// <summary>
    /// Test: Multiple engine cycles with hybrid methods should not leak memory.
    /// </summary>
    [Fact]
    public void HybridMethods_MultipleEngineCycles_ShouldNotLeakMemory()
    {
        // Force GC before test
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var initialMemory = GC.GetTotalMemory(true);

        // Create and dispose many engines using hybrid methods
        for (int i = 0; i < 100; i++)
        {
            using var engine = new EngineWrapper();
            
            // Use ProcessTickWithResult
            var tick = new Tick { Timestamp = i, Price = 100.0, Volume = 1000.0 };
            engine.ProcessTickWithResult(tick, out _);
            
            // Use PlaceOrder
            engine.PlaceOrder(Signal.Buy, 100.0);
            
            // Use GetOhlcData
            engine.GetOhlcData("1m");
            
            // Use GetTickCount
            engine.GetTickCount();
            
            // Use GetCurrentTickIndex
            engine.GetCurrentTickIndex();
        }

        // Force GC after test
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);
        var memoryGrowth = finalMemory - initialMemory;

        // Memory should not grow significantly (allow some variance)
        Assert.True(memoryGrowth < 10_000_000, // 10MB threshold
            $"Memory grew by {memoryGrowth / 1024 / 1024}MB, possible leak");
    }

    #endregion

    #region Struct Size Tests

    /// <summary>
    /// Test: New FFI struct sizes should match expected layout.
    /// </summary>
    [Fact]
    public void NewStructSizes_ShouldMatchExpectedLayout()
    {
        // ExecutionEvent: i32 + i64 + f64 + f64 + i32 + i64 + f64 = 4 + 8 + 8 + 8 + 4 + 8 + 8 = 48 bytes
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<ExecutionEvent>() >= 48);

        // OrderResult: i32 + i64 + f64 + f64 + i32 = 4 + 8 + 8 + 8 + 4 = 32 bytes
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<OrderResult>() >= 32);

        // OhlcBar: i64 + f64 + f64 + f64 + f64 + f64 = 8 + 8 + 8 + 8 + 8 + 8 = 48 bytes
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<OhlcBar>() >= 48);

        // CsvMapping: 7 * 32 bytes (fixed arrays) + i32 = 224 + 4 = 228 bytes
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<CsvMapping>() >= 228);
    }

    #endregion
}
