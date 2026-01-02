using System.Diagnostics;
using Xunit;

namespace AegisQuant.Interop.Tests;

/// <summary>
/// End-to-end integration tests for the complete backtest workflow.
/// Tests: Initialize Engine -> Load Data -> Run Backtest -> Get Results
/// Validates: Requirements 8.5, 8.6
/// </summary>
public class EndToEndIntegrationTests
{
    private const string TestDataPath = @"..\..\..\..\..\test_data\ticks_small.csv";

    #region Complete Backtest Workflow Tests

    /// <summary>
    /// E2E Test: Complete backtest workflow from initialization to results.
    /// </summary>
    [Fact]
    public void E2E_CompleteBacktestWorkflow_ShouldProduceValidResults()
    {
        // Arrange
        var parameters = new StrategyParams
        {
            ShortMaPeriod = 5,
            LongMaPeriod = 20,
            PositionSize = 100.0,
            StopLossPct = 0.02,
            TakeProfitPct = 0.05
        };
        var riskConfig = RiskConfig.Default;

        // Act
        using var engine = new EngineWrapper(parameters, riskConfig);

        // Step 1: Verify initial state
        var initialStatus = engine.GetAccountStatus();
        Assert.Equal(100_000.0, initialStatus.Balance);
        Assert.Equal(100_000.0, initialStatus.Equity);
        Assert.Equal(0, initialStatus.PositionCount);

        // Step 2: Run backtest
        engine.RunBacktest();

        // Step 3: Verify final state
        var finalStatus = engine.GetAccountStatus();
        Assert.True(finalStatus.Balance > 0, "Balance should be positive after backtest");
        Assert.True(finalStatus.Equity > 0, "Equity should be positive after backtest");
        Assert.False(double.IsNaN(finalStatus.TotalPnl), "TotalPnl should not be NaN");
    }

    /// <summary>
    /// E2E Test: Multiple sequential backtests with different parameters.
    /// </summary>
    [Fact]
    public void E2E_MultipleSequentialBacktests_ShouldBeIndependent()
    {
        var paramSets = new[]
        {
            new StrategyParams { ShortMaPeriod = 5, LongMaPeriod = 20, PositionSize = 100.0, StopLossPct = 0.02, TakeProfitPct = 0.05 },
            new StrategyParams { ShortMaPeriod = 10, LongMaPeriod = 30, PositionSize = 200.0, StopLossPct = 0.03, TakeProfitPct = 0.08 },
            new StrategyParams { ShortMaPeriod = 3, LongMaPeriod = 15, PositionSize = 50.0, StopLossPct = 0.01, TakeProfitPct = 0.03 },
        };

        var results = new List<AccountStatus>();

        foreach (var parameters in paramSets)
        {
            using var engine = new EngineWrapper(parameters, RiskConfig.Default);

            // Verify fresh initial state for each engine
            var initialStatus = engine.GetAccountStatus();
            Assert.Equal(100_000.0, initialStatus.Balance);

            engine.RunBacktest();
            results.Add(engine.GetAccountStatus());
        }

        // All results should be valid
        foreach (var result in results)
        {
            Assert.True(result.Balance > 0);
            Assert.True(result.Equity > 0);
            Assert.False(double.IsNaN(result.Balance));
            Assert.False(double.IsNaN(result.Equity));
        }
    }

    /// <summary>
    /// E2E Test: Process ticks manually and verify state updates.
    /// </summary>
    [Fact]
    public void E2E_ManualTickProcessing_ShouldUpdateState()
    {
        // Arrange
        using var engine = new EngineWrapper();
        var initialStatus = engine.GetAccountStatus();

        // Generate a series of ticks
        var ticks = GenerateTestTicks(100);

        // Act - Process each tick
        foreach (var tick in ticks)
        {
            engine.ProcessTick(tick);
        }

        // Assert
        var finalStatus = engine.GetAccountStatus();
        Assert.True(finalStatus.Balance > 0);
        Assert.True(finalStatus.Equity > 0);
    }

    #endregion

    #region Rust-C# Data Consistency Tests

    /// <summary>
    /// Data Consistency Test: AccountStatus fields should be logically consistent.
    /// </summary>
    [Fact]
    public void DataConsistency_AccountStatus_ShouldBeLogicallyConsistent()
    {
        using var engine = new EngineWrapper();

        // Process some ticks to potentially change state
        var ticks = GenerateTestTicks(50);
        foreach (var tick in ticks)
        {
            engine.ProcessTick(tick);
        }

        var status = engine.GetAccountStatus();

        // Verify logical consistency
        Assert.True(status.Balance >= 0, "Balance should be non-negative");
        Assert.True(status.Equity >= 0, "Equity should be non-negative");
        Assert.True(status.Available >= 0, "Available should be non-negative");
        Assert.True(status.PositionCount >= 0, "PositionCount should be non-negative");

        // Available should not exceed Balance
        Assert.True(status.Available <= status.Balance + 0.01,
            "Available should not exceed Balance");
    }

    /// <summary>
    /// Data Consistency Test: Tick data should be processed without corruption.
    /// </summary>
    [Fact]
    public void DataConsistency_TickProcessing_ShouldNotCorruptData()
    {
        using var engine = new EngineWrapper();

        // Test with specific known values
        var testTicks = new[]
        {
            new Tick { Timestamp = 1704072600000000000, Price = 100.0, Volume = 1000.0 },
            new Tick { Timestamp = 1704072600001000000, Price = 100.5, Volume = 1500.0 },
            new Tick { Timestamp = 1704072600002000000, Price = 99.8, Volume = 800.0 },
        };

        // Process ticks - should not throw
        foreach (var tick in testTicks)
        {
            engine.ProcessTick(tick);
        }

        // Verify state is still valid
        var status = engine.GetAccountStatus();
        Assert.False(double.IsNaN(status.Balance));
        Assert.False(double.IsInfinity(status.Balance));
    }

    /// <summary>
    /// Data Consistency Test: StrategyParams should be correctly passed to Rust.
    /// </summary>
    [Fact]
    public void DataConsistency_StrategyParams_ShouldBeCorrectlyPassed()
    {
        var parameters = new StrategyParams
        {
            ShortMaPeriod = 7,
            LongMaPeriod = 21,
            PositionSize = 150.0,
            StopLossPct = 0.025,
            TakeProfitPct = 0.075
        };

        // Engine should initialize without error with these params
        using var engine = new EngineWrapper(parameters, RiskConfig.Default);

        // Verify engine is functional
        var status = engine.GetAccountStatus();
        Assert.Equal(100_000.0, status.Balance);
    }

    /// <summary>
    /// Data Consistency Test: RiskConfig should be correctly passed to Rust.
    /// </summary>
    [Fact]
    public void DataConsistency_RiskConfig_ShouldBeCorrectlyPassed()
    {
        var riskConfig = new RiskConfig
        {
            MaxOrderRate = 5,
            MaxPositionSize = 500.0,
            MaxOrderValue = 50000.0,
            MaxDrawdownPct = 0.15
        };

        // Engine should initialize without error with these params
        using var engine = new EngineWrapper(StrategyParams.Default, riskConfig);

        // Verify engine is functional
        var status = engine.GetAccountStatus();
        Assert.Equal(100_000.0, status.Balance);
    }

    /// <summary>
    /// Data Consistency Test: OrderRequest symbol handling across FFI boundary.
    /// </summary>
    [Fact]
    public void DataConsistency_OrderRequestSymbol_ShouldRoundTrip()
    {
        var testSymbols = new[] { "BTC", "ETHUSDT", "BTCUSDT", "A", "VERYLONGSYMBOL" };

        foreach (var symbol in testSymbols)
        {
            var order = new OrderRequest();
            order.SetSymbol(symbol);
            var retrieved = order.GetSymbol();

            // Symbol should be preserved (truncated to 15 chars max)
            var expected = symbol.Length > 15 ? symbol[..15] : symbol;
            Assert.Equal(expected, retrieved);
        }
    }

    /// <summary>
    /// Data Consistency Test: Position symbol handling across FFI boundary.
    /// </summary>
    [Fact]
    public void DataConsistency_PositionSymbol_ShouldRoundTrip()
    {
        var testSymbols = new[] { "BTC", "ETHUSDT", "BTCUSDT", "A" };

        foreach (var symbol in testSymbols)
        {
            var position = new Position();
            position.SetSymbol(symbol);
            var retrieved = position.GetSymbol();

            Assert.Equal(symbol, retrieved);
        }
    }

    #endregion

    #region Memory Safety Tests

    /// <summary>
    /// Memory Safety Test: Multiple engine creation/disposal cycles should not leak memory.
    /// </summary>
    [Fact]
    public void MemorySafety_MultipleEngineCycles_ShouldNotLeak()
    {
        // Force GC before test
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var initialMemory = GC.GetTotalMemory(true);

        // Create and dispose many engines
        for (int i = 0; i < 1000; i++)
        {
            using var engine = new EngineWrapper();
            var status = engine.GetAccountStatus();
            Assert.Equal(100_000.0, status.Balance);
        }

        // Force GC after test
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);

        // Memory should not grow significantly (allow some variance)
        // This is a basic check - for production, use dotMemory or similar
        var memoryGrowth = finalMemory - initialMemory;
        Assert.True(memoryGrowth < 50_000_000, // 50MB threshold
            $"Memory grew by {memoryGrowth / 1024 / 1024}MB, possible leak");
    }

    /// <summary>
    /// Memory Safety Test: Rapid engine creation/disposal should be stable.
    /// </summary>
    [Fact]
    public void MemorySafety_RapidCreateDispose_ShouldBeStable()
    {
        var sw = Stopwatch.StartNew();
        var iterations = 100;

        for (int i = 0; i < iterations; i++)
        {
            using var engine = new EngineWrapper();
            engine.ProcessTick(new Tick { Timestamp = i, Price = 100.0, Volume = 1000.0 });
            var status = engine.GetAccountStatus();
            Assert.True(status.Balance > 0);
        }

        sw.Stop();

        // Should complete in reasonable time (< 10 seconds for 100 iterations)
        Assert.True(sw.ElapsedMilliseconds < 10000,
            $"Rapid create/dispose took {sw.ElapsedMilliseconds}ms, may indicate issues");
    }

    /// <summary>
    /// Memory Safety Test: Dispose should be idempotent.
    /// </summary>
    [Fact]
    public void MemorySafety_DisposeIdempotent_ShouldNotCrash()
    {
        var engine = new EngineWrapper();

        // Multiple dispose calls should be safe
        engine.Dispose();
        engine.Dispose();
        engine.Dispose();

        // Should reach here without crash
        Assert.True(true);
    }

    /// <summary>
    /// Memory Safety Test: Operations after dispose should throw ObjectDisposedException.
    /// </summary>
    [Fact]
    public void MemorySafety_OperationsAfterDispose_ShouldThrow()
    {
        var engine = new EngineWrapper();
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.GetAccountStatus());
        Assert.Throws<ObjectDisposedException>(() =>
            engine.ProcessTick(new Tick { Timestamp = 0, Price = 100.0, Volume = 1000.0 }));
        Assert.Throws<ObjectDisposedException>(() => engine.RunBacktest());
    }

    /// <summary>
    /// Memory Safety Test: Concurrent engine instances should be independent.
    /// </summary>
    [Fact]
    public void MemorySafety_ConcurrentEngines_ShouldBeIndependent()
    {
        const int engineCount = 10;
        var engines = new EngineWrapper[engineCount];
        var statuses = new AccountStatus[engineCount];

        try
        {
            // Create multiple engines
            for (int i = 0; i < engineCount; i++)
            {
                engines[i] = new EngineWrapper();
            }

            // Process different number of ticks on each
            for (int i = 0; i < engineCount; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    engines[i].ProcessTick(new Tick
                    {
                        Timestamp = j,
                        Price = 100.0 + j,
                        Volume = 1000.0
                    });
                }
            }

            // Get statuses
            for (int i = 0; i < engineCount; i++)
            {
                statuses[i] = engines[i].GetAccountStatus();
            }

            // All should have valid, independent state
            for (int i = 0; i < engineCount; i++)
            {
                Assert.True(statuses[i].Balance > 0);
                Assert.False(double.IsNaN(statuses[i].Balance));
            }
        }
        finally
        {
            // Cleanup
            foreach (var engine in engines)
            {
                engine?.Dispose();
            }
        }
    }

    /// <summary>
    /// Memory Safety Test: Log callback should not cause memory issues.
    /// </summary>
    [Fact]
    public void MemorySafety_LogCallback_ShouldNotLeak()
    {
        var logMessages = new List<string>();

        for (int i = 0; i < 100; i++)
        {
            using var engine = new EngineWrapper();

            engine.SetLogCallback((level, message) =>
            {
                logMessages.Add(message);
            });

            engine.ProcessTick(new Tick { Timestamp = i, Price = 100.0, Volume = 1000.0 });
        }

        // Should complete without crash
        Assert.True(true);
    }

    #endregion

    #region Helper Methods

    private static Tick[] GenerateTestTicks(int count)
    {
        var ticks = new Tick[count];
        var baseTimestamp = 1704072600000000000L; // 2024-01-01 00:00:00 UTC in nanoseconds
        var basePrice = 100.0;

        for (int i = 0; i < count; i++)
        {
            // Create a price pattern that oscillates
            var priceOffset = Math.Sin(i * 0.1) * 5.0;

            ticks[i] = new Tick
            {
                Timestamp = baseTimestamp + (i * 1000000), // 1ms apart
                Price = basePrice + priceOffset,
                Volume = 1000.0 + (i % 10) * 100
            };
        }

        return ticks;
    }

    #endregion
}
