using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace AegisQuant.Interop.Tests;

/// <summary>
/// Property-based tests for FFI safety and Dispose pattern correctness.
/// Feature: aegisquant-hybrid, Property 2: FFI Safety - Error Codes Instead of Panics
/// Validates: Requirements 4.4, 4.5, 8.6
/// </summary>
public class FfiSafetyTests
{
    /// <summary>
    /// Property: For any valid StrategyParams and RiskConfig, creating and disposing
    /// an EngineWrapper should not throw exceptions and should properly release resources.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EngineWrapper_CreateAndDispose_ShouldNotThrow()
    {
        return Prop.ForAll(
            Arb.From<int>().Filter(x => x > 0 && x < 100),  // ShortMaPeriod
            Arb.From<int>().Filter(x => x > 0 && x < 200),  // LongMaPeriod
            (shortMa, longMa) =>
            {
                // Ensure short < long for valid MA strategy
                var actualShort = Math.Min(shortMa, longMa);
                var actualLong = Math.Max(shortMa, longMa);
                if (actualShort == actualLong) actualLong++;

                var parameters = new StrategyParams
                {
                    ShortMaPeriod = actualShort,
                    LongMaPeriod = actualLong,
                    PositionSize = 100.0,
                    StopLossPct = 0.02,
                    TakeProfitPct = 0.05
                };

                var riskConfig = RiskConfig.Default;

                // Create and dispose should work without throwing
                using var engine = new EngineWrapper(parameters, riskConfig);
                return true;
            });
    }

    /// <summary>
    /// Property: For any EngineWrapper, calling Dispose multiple times should be safe
    /// and not throw exceptions (idempotent dispose).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EngineWrapper_MultipleDispose_ShouldBeSafe()
    {
        return Prop.ForAll(
            Arb.From<int>().Filter(x => x >= 1 && x <= 10),  // Number of dispose calls
            disposeCount =>
            {
                var engine = new EngineWrapper();

                // Multiple dispose calls should be safe
                for (int i = 0; i < disposeCount; i++)
                {
                    engine.Dispose();
                }

                return true;
            });
    }

    /// <summary>
    /// Property: After disposing an EngineWrapper, any operation should throw
    /// ObjectDisposedException.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EngineWrapper_AfterDispose_OperationsShouldThrow()
    {
        return Prop.ForAll(
            Arb.From<int>().Filter(x => x >= 0 && x < 3),  // Operation selector
            operationIndex =>
            {
                var engine = new EngineWrapper();
                engine.Dispose();

                try
                {
                    switch (operationIndex)
                    {
                        case 0:
                            engine.GetAccountStatus();
                            break;
                        case 1:
                            engine.ProcessTick(new Tick { Timestamp = 0, Price = 100.0, Volume = 1000.0 });
                            break;
                        case 2:
                            _ = engine.Handle;
                            break;
                    }
                    return false; // Should have thrown
                }
                catch (ObjectDisposedException)
                {
                    return true; // Expected exception
                }
            });
    }


    /// <summary>
    /// Property: For any valid Tick data (positive price, non-negative volume),
    /// ProcessTick should not crash the engine.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ProcessTick_WithValidData_ShouldNotCrash()
    {
        return Prop.ForAll(
            Arb.From<long>(),  // Timestamp
            Arb.From<double>().Filter(x => x > 0 && x < 1_000_000 && !double.IsNaN(x) && !double.IsInfinity(x)),  // Price
            Arb.From<double>().Filter(x => x >= 0 && x < 1_000_000 && !double.IsNaN(x) && !double.IsInfinity(x)),  // Volume
            (timestamp, price, volume) =>
            {
                using var engine = new EngineWrapper();
                var tick = new Tick
                {
                    Timestamp = timestamp,
                    Price = price,
                    Volume = volume
                };

                try
                {
                    engine.ProcessTick(tick);
                    return true;
                }
                catch (EngineException)
                {
                    // Engine exceptions are acceptable (e.g., no data loaded)
                    return true;
                }
            });
    }

    /// <summary>
    /// Property: GetAccountStatus should always return valid data without crashing.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetAccountStatus_ShouldReturnValidData()
    {
        return Prop.ForAll(
            Arb.From<int>().Filter(x => x > 0 && x < 50),  // ShortMaPeriod
            shortMa =>
            {
                var parameters = new StrategyParams
                {
                    ShortMaPeriod = shortMa,
                    LongMaPeriod = shortMa + 10,
                    PositionSize = 100.0,
                    StopLossPct = 0.02,
                    TakeProfitPct = 0.05
                };

                using var engine = new EngineWrapper(parameters, RiskConfig.Default);
                var status = engine.GetAccountStatus();

                // Verify returned data is valid (not NaN or Infinity)
                return !double.IsNaN(status.Balance) &&
                       !double.IsNaN(status.Equity) &&
                       !double.IsNaN(status.Available) &&
                       !double.IsNaN(status.TotalPnl) &&
                       !double.IsInfinity(status.Balance) &&
                       !double.IsInfinity(status.Equity);
            });
    }

    /// <summary>
    /// Property: EngineHandle should correctly report IsInvalid after disposal.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EngineHandle_AfterDispose_ShouldBeInvalid()
    {
        return Prop.ForAll(
            Arb.From<bool>(),  // Dummy parameter to run multiple times
            _ =>
            {
                var engine = new EngineWrapper();
                var handle = engine.Handle;

                // Before dispose, handle should be valid
                bool validBefore = !handle.IsInvalid;

                engine.Dispose();

                // After dispose, handle should be invalid (closed)
                bool invalidAfter = handle.IsClosed;

                return validBefore && invalidAfter;
            });
    }

    /// <summary>
    /// Property: ErrorHandler.CheckResult should throw appropriate exceptions for error codes.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ErrorHandler_CheckResult_ShouldThrowForErrors()
    {
        var errorCodes = new[]
        {
            ErrorCodes.NullPointer,
            ErrorCodes.InvalidParam,
            ErrorCodes.EngineNotInit,
            ErrorCodes.RiskRejected,
            ErrorCodes.DataLoadFailed,
            ErrorCodes.InvalidData,
            ErrorCodes.InsufficientCapital,
            ErrorCodes.ThrottleExceeded,
            ErrorCodes.PositionLimit,
            ErrorCodes.InternalPanic
        };

        return Prop.ForAll(
            Gen.Elements(errorCodes).ToArbitrary(),
            errorCode =>
            {
                try
                {
                    ErrorHandler.CheckResult(errorCode, "TestOperation");
                    return false; // Should have thrown
                }
                catch (Exception ex)
                {
                    // Verify appropriate exception type
                    return errorCode switch
                    {
                        ErrorCodes.NullPointer => ex is ArgumentNullException,
                        ErrorCodes.InvalidParam => ex is ArgumentException,
                        ErrorCodes.EngineNotInit => ex is InvalidOperationException,
                        ErrorCodes.RiskRejected => ex is RiskRejectedException,
                        ErrorCodes.DataLoadFailed => ex is DataLoadException,
                        ErrorCodes.InvalidData => ex is InvalidDataException,
                        ErrorCodes.InsufficientCapital => ex is InsufficientCapitalException,
                        ErrorCodes.ThrottleExceeded => ex is ThrottleExceededException,
                        ErrorCodes.PositionLimit => ex is PositionLimitException,
                        ErrorCodes.InternalPanic => ex is EngineException,
                        _ => ex is EngineException
                    };
                }
            });
    }

    /// <summary>
    /// Property: ErrorHandler.CheckResult should not throw for success code.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ErrorHandler_CheckResult_ShouldNotThrowForSuccess()
    {
        return Prop.ForAll(
            Arb.From<string>().Filter(s => !string.IsNullOrEmpty(s)),
            operation =>
            {
                try
                {
                    ErrorHandler.CheckResult(ErrorCodes.Success, operation);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
    }

    /// <summary>
    /// Unit test: Verify struct sizes match expected C layout.
    /// This is a sanity check for FFI compatibility.
    /// </summary>
    [Fact]
    public void StructSizes_ShouldMatchExpectedLayout()
    {
        // Tick: i64 + f64 + f64 = 8 + 8 + 8 = 24 bytes
        Assert.Equal(24, System.Runtime.InteropServices.Marshal.SizeOf<Tick>());

        // AccountStatus: f64 + f64 + f64 + i32 + f64 = 8 + 8 + 8 + 4 + 8 = 36 bytes
        // Note: May have padding, so check >= 36
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<AccountStatus>() >= 36);

        // StrategyParams: i32 + i32 + f64 + f64 + f64 = 4 + 4 + 8 + 8 + 8 = 32 bytes
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<StrategyParams>() >= 32);

        // RiskConfig: i32 + f64 + f64 + f64 = 4 + 8 + 8 + 8 = 28 bytes (may have padding)
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<RiskConfig>() >= 28);
    }
}
