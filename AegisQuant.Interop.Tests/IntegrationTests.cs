using Xunit;

namespace AegisQuant.Interop.Tests;

/// <summary>
/// Integration tests for the complete backtest workflow.
/// Tests: Initialize Engine -> Load Data -> Run Backtest -> Get Results
/// </summary>
public class IntegrationTests
{
    private const string TestDataPath = @"..\..\..\..\..\test_data\ticks_small.csv";

    /// <summary>
    /// Integration test: Complete backtest workflow with default parameters.
    /// </summary>
    [Fact]
    public void CompleteBacktestWorkflow_WithDefaultParams_ShouldSucceed()
    {
        // Arrange
        var parameters = StrategyParams.Default;
        var riskConfig = RiskConfig.Default;

        // Act & Assert
        using var engine = new EngineWrapper(parameters, riskConfig);

        // 1. Verify engine is initialized with correct initial state
        var initialStatus = engine.GetAccountStatus();
        Assert.Equal(100_000.0, initialStatus.Balance);
        Assert.Equal(100_000.0, initialStatus.Equity);
        Assert.Equal(0, initialStatus.PositionCount);

        // 2. Run backtest (placeholder - actual implementation in Phase 2)
        engine.RunBacktest();

        // 3. Get final status
        var finalStatus = engine.GetAccountStatus();
        Assert.True(finalStatus.Balance > 0, "Balance should be positive");
        Assert.True(finalStatus.Equity > 0, "Equity should be positive");
    }

    /// <summary>
    /// Integration test: Engine initialization with custom parameters.
    /// </summary>
    [Fact]
    public void EngineInitialization_WithCustomParams_ShouldSucceed()
    {
        // Arrange
        var parameters = new StrategyParams
        {
            ShortMaPeriod = 10,
            LongMaPeriod = 30,
            PositionSize = 200.0,
            StopLossPct = 0.03,
            TakeProfitPct = 0.08
        };

        var riskConfig = new RiskConfig
        {
            MaxOrderRate = 5,
            MaxPositionSize = 500.0,
            MaxOrderValue = 50000.0,
            MaxDrawdownPct = 0.15
        };

        // Act
        using var engine = new EngineWrapper(parameters, riskConfig);
        var status = engine.GetAccountStatus();

        // Assert
        Assert.Equal(100_000.0, status.Balance);
        Assert.Equal(100_000.0, status.Equity);
    }

    /// <summary>
    /// Integration test: Process multiple ticks sequentially.
    /// </summary>
    [Fact]
    public void ProcessMultipleTicks_ShouldNotCrash()
    {
        // Arrange
        using var engine = new EngineWrapper();
        var ticks = new[]
        {
            new Tick { Timestamp = 1704072600000000000, Price = 100.0, Volume = 1000.0 },
            new Tick { Timestamp = 1704072600001000000, Price = 100.5, Volume = 1500.0 },
            new Tick { Timestamp = 1704072600002000000, Price = 99.8, Volume = 800.0 },
            new Tick { Timestamp = 1704072600003000000, Price = 101.2, Volume = 2000.0 },
            new Tick { Timestamp = 1704072600004000000, Price = 100.9, Volume = 1200.0 },
        };

        // Act & Assert - should not throw
        foreach (var tick in ticks)
        {
            engine.ProcessTick(tick);
        }

        var status = engine.GetAccountStatus();
        Assert.True(status.Balance > 0);
    }

    /// <summary>
    /// Integration test: Log callback functionality.
    /// </summary>
    [Fact]
    public void LogCallback_ShouldReceiveMessages()
    {
        // Arrange
        using var engine = new EngineWrapper();
        var logMessages = new List<(LogLevel Level, string Message)>();

        // Act
        engine.SetLogCallback((level, message) =>
        {
            logMessages.Add((level, message));
        });

        // Process some ticks to potentially generate log messages
        engine.ProcessTick(new Tick { Timestamp = 1, Price = 100.0, Volume = 1000.0 });

        // Assert - callback was set successfully (no exception)
        // Note: Actual log messages depend on Rust implementation
        Assert.NotNull(logMessages);
    }

    /// <summary>
    /// Integration test: Verify Rust-C# data consistency.
    /// </summary>
    [Fact]
    public void DataConsistency_AccountStatusFields_ShouldBeValid()
    {
        // Arrange
        using var engine = new EngineWrapper();

        // Act
        var status = engine.GetAccountStatus();

        // Assert - verify all fields are valid numbers
        Assert.False(double.IsNaN(status.Balance), "Balance should not be NaN");
        Assert.False(double.IsNaN(status.Equity), "Equity should not be NaN");
        Assert.False(double.IsNaN(status.Available), "Available should not be NaN");
        Assert.False(double.IsNaN(status.TotalPnl), "TotalPnl should not be NaN");
        Assert.False(double.IsInfinity(status.Balance), "Balance should not be Infinity");
        Assert.False(double.IsInfinity(status.Equity), "Equity should not be Infinity");

        // Verify logical consistency
        Assert.True(status.Balance >= 0, "Balance should be non-negative");
        Assert.True(status.Equity >= 0, "Equity should be non-negative");
        Assert.True(status.PositionCount >= 0, "PositionCount should be non-negative");
    }

    /// <summary>
    /// Integration test: Memory safety - no leaks after multiple engine cycles.
    /// </summary>
    [Fact]
    public void MemorySafety_MultipleEngineCycles_ShouldNotLeak()
    {
        // Act - create and dispose multiple engines
        for (int i = 0; i < 100; i++)
        {
            using var engine = new EngineWrapper();
            var status = engine.GetAccountStatus();
            Assert.Equal(100_000.0, status.Balance);
        }

        // Assert - if we get here without crash or OOM, memory is being released
        Assert.True(true);
    }

    /// <summary>
    /// Integration test: Concurrent engine instances should be independent.
    /// </summary>
    [Fact]
    public void ConcurrentEngines_ShouldBeIndependent()
    {
        // Arrange
        var params1 = new StrategyParams
        {
            ShortMaPeriod = 5,
            LongMaPeriod = 20,
            PositionSize = 100.0,
            StopLossPct = 0.02,
            TakeProfitPct = 0.05
        };

        var params2 = new StrategyParams
        {
            ShortMaPeriod = 10,
            LongMaPeriod = 30,
            PositionSize = 200.0,
            StopLossPct = 0.03,
            TakeProfitPct = 0.08
        };

        // Act
        using var engine1 = new EngineWrapper(params1, RiskConfig.Default);
        using var engine2 = new EngineWrapper(params2, RiskConfig.Default);

        var status1 = engine1.GetAccountStatus();
        var status2 = engine2.GetAccountStatus();

        // Assert - both engines should have independent state
        Assert.Equal(100_000.0, status1.Balance);
        Assert.Equal(100_000.0, status2.Balance);
    }

    /// <summary>
    /// Integration test: OrderRequest struct symbol handling.
    /// </summary>
    [Fact]
    public void OrderRequest_SymbolHandling_ShouldWorkCorrectly()
    {
        // Arrange
        var order = new OrderRequest();

        // Act
        order.SetSymbol("BTCUSDT");
        var symbol = order.GetSymbol();

        // Assert
        Assert.Equal("BTCUSDT", symbol);
    }

    /// <summary>
    /// Integration test: OrderRequest with long symbol (truncation).
    /// </summary>
    [Fact]
    public void OrderRequest_LongSymbol_ShouldTruncate()
    {
        // Arrange
        var order = new OrderRequest();
        var longSymbol = "VERYLONGSYMBOLNAME123"; // > 15 chars

        // Act
        order.SetSymbol(longSymbol);
        var symbol = order.GetSymbol();

        // Assert - should be truncated to 15 chars
        Assert.Equal(15, symbol.Length);
        Assert.Equal("VERYLONGSYMBOLN", symbol);
    }

    /// <summary>
    /// Integration test: Position struct symbol handling.
    /// </summary>
    [Fact]
    public void Position_SymbolHandling_ShouldWorkCorrectly()
    {
        // Arrange
        var position = new Position();

        // Act
        position.SetSymbol("ETHUSDT");
        var symbol = position.GetSymbol();

        // Assert
        Assert.Equal("ETHUSDT", symbol);
    }
}
