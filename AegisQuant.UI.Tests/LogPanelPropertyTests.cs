using System.IO;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using AegisQuant.UI.Controls;
using AegisQuant.UI.Services;
using AegisQuant.Interop;
using StrategySignal = AegisQuant.UI.Strategy.Signal;

namespace AegisQuant.UI.Tests;

/// <summary>
/// Property-based tests for Log Panel functionality.
/// Validates Property 15 from the design document.
/// **Feature: hybrid-backtest-mode, Property 15: Log Entry Completeness**
/// **Validates: Requirements 7.2, 7.3, 7.4**
/// </summary>
public class LogPanelPropertyTests
{
    /// <summary>
    /// Property 15: Log Entry Completeness - Signal Log
    /// *For any* signal or execution event, the log panel SHALL contain an entry 
    /// with timestamp, signal type, and price.
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property15_SignalLogEntry_ContainsRequiredFields()
    {
        return Prop.ForAll(
            Arb.From<StrategySignal>().Filter(s => s != StrategySignal.None),
            Arb.From<double>().Filter(p => p > 0 && !double.IsNaN(p) && !double.IsInfinity(p)),
            (signal, price) =>
            {
                // Arrange
                var loggingService = new LoggingService();
                
                // Act
                loggingService.LogSignal(signal, price);
                
                // Assert
                var entry = loggingService.LogEntries.LastOrDefault();
                
                // Requirements 7.2, 7.3: Log SHALL contain timestamp, signal type, price
                return entry != null
                    && entry.Timestamp != default
                    && entry.Signal == signal
                    && entry.Price == price
                    && entry.FormattedTime != null
                    && entry.FormattedMessage.Contains(signal.ToString())
                    && entry.FormattedMessage.Contains(price.ToString("F2"));
            });
    }

    /// <summary>
    /// Property 15: Log Entry Completeness - Execution Log
    /// *For any* execution event, the log panel SHALL contain an entry 
    /// with timestamp, signal type, price, and execution details.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property15_ExecutionLogEntry_ContainsRequiredFields()
    {
        // Combine orderId and signal into a tuple to stay within 4 parameter limit
        var combinedArb = Arb.From(
            from orderId in Arb.Generate<string>().Where(s => !string.IsNullOrEmpty(s))
            from signal in Arb.Generate<StrategySignal>().Where(s => s != StrategySignal.None)
            select (orderId, signal));
        
        var priceArb = Arb.From<double>().Filter(p => p > 0 && !double.IsNaN(p) && !double.IsInfinity(p));
        var quantityArb = Arb.From<double>().Filter(q => q > 0 && !double.IsNaN(q) && !double.IsInfinity(q));
        
        return Prop.ForAll(
            combinedArb,
            priceArb,
            quantityArb,
            (combined, price, quantity) =>
            {
                var (orderId, signal) = combined;
                
                // Test both accepted and rejected cases
                var loggingService = new LoggingService();
                
                // Act - test accepted
                loggingService.LogExecution(orderId, signal, price, quantity, true);
                var entryAccepted = loggingService.LogEntries.LastOrDefault();
                
                // Act - test rejected
                loggingService.LogExecution(orderId, signal, price, quantity, false);
                var entryRejected = loggingService.LogEntries.LastOrDefault();
                
                // Requirements 7.4: Execution log SHALL contain timestamp, signal, price, quantity, acceptance status
                return entryAccepted != null
                    && entryAccepted.Timestamp != default
                    && entryAccepted.Signal == signal
                    && entryAccepted.Price == price
                    && entryAccepted.Quantity == quantity
                    && entryAccepted.OrderId == orderId
                    && entryAccepted.IsAccepted == true
                    && entryAccepted.FormattedTime != null
                    && entryRejected != null
                    && entryRejected.IsAccepted == false;
            });
    }

    /// <summary>
    /// Property 15: Log Entry Completeness - Basic Log
    /// *For any* log message, the entry SHALL contain timestamp, level, and message.
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property15_BasicLogEntry_ContainsRequiredFields()
    {
        return Prop.ForAll(
            Arb.From<LogLevel>(),
            Arb.From<string>().Filter(s => !string.IsNullOrEmpty(s)),
            (level, message) =>
            {
                // Arrange
                var loggingService = new LoggingService();
                
                // Act
                loggingService.Log(level, message);
                
                // Assert
                var entry = loggingService.LogEntries.LastOrDefault();
                
                return entry != null
                    && entry.Timestamp != default
                    && entry.Level == level
                    && entry.Message == message
                    && entry.FormattedTime != null
                    && entry.LevelString == level.ToString().ToUpper();
            });
    }

    /// <summary>
    /// Property 15: Log Entry Completeness - Indicator Values
    /// *For any* signal with indicator values, the log SHALL include indicator values.
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Fact]
    public void Property15_SignalLogWithIndicators_ContainsIndicatorValues()
    {
        // Arrange
        var loggingService = new LoggingService();
        var indicators = new Dictionary<string, double>
        {
            { "MA5", 100.5 },
            { "MA20", 98.3 },
            { "RSI", 65.2 }
        };
        
        // Act
        loggingService.LogSignal(StrategySignal.Buy, 100.0, indicators);
        
        // Assert
        var entry = loggingService.LogEntries.LastOrDefault();
        Assert.NotNull(entry);
        Assert.NotNull(entry.IndicatorValues);
        Assert.Equal(3, entry.IndicatorValues.Count);
        Assert.Equal(100.5, entry.IndicatorValues["MA5"]);
        Assert.Equal(98.3, entry.IndicatorValues["MA20"]);
        Assert.Equal(65.2, entry.IndicatorValues["RSI"]);
        
        // Verify formatted message contains indicator values
        Assert.Contains("MA5", entry.FormattedMessage);
        Assert.Contains("MA20", entry.FormattedMessage);
        Assert.Contains("RSI", entry.FormattedMessage);
    }

    /// <summary>
    /// Log entries are ordered by timestamp.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property LogEntries_AreOrderedByTimestamp()
    {
        return Prop.ForAll(
            Arb.From<int>().Filter(n => n > 0 && n <= 100),
            count =>
            {
                // Arrange
                var loggingService = new LoggingService();
                
                // Act - Add multiple log entries
                for (int i = 0; i < count; i++)
                {
                    loggingService.Log(LogLevel.Info, $"Message {i}");
                }
                
                // Assert - Entries should be in order
                var entries = loggingService.LogEntries.ToList();
                for (int i = 1; i < entries.Count; i++)
                {
                    if (entries[i].Timestamp < entries[i - 1].Timestamp)
                    {
                        return false;
                    }
                }
                
                return entries.Count == count;
            });
    }

    /// <summary>
    /// Clear removes all log entries.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property Clear_RemovesAllEntries()
    {
        return Prop.ForAll(
            Arb.From<int>().Filter(n => n > 0 && n <= 100),
            count =>
            {
                // Arrange
                var loggingService = new LoggingService();
                for (int i = 0; i < count; i++)
                {
                    loggingService.Log(LogLevel.Info, $"Message {i}");
                }
                
                // Act
                loggingService.Clear();
                
                // Assert
                return loggingService.LogEntries.Count == 0;
            });
    }

    /// <summary>
    /// Log level string is correctly formatted.
    /// </summary>
    [Theory]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Info, "INFO")]
    [InlineData(LogLevel.Warn, "WARN")]
    [InlineData(LogLevel.Error, "ERROR")]
    public void LogLevelString_IsCorrectlyFormatted(LogLevel level, string expected)
    {
        // Arrange
        var loggingService = new LoggingService();
        
        // Act
        loggingService.Log(level, "Test message");
        
        // Assert
        var entry = loggingService.LogEntries.LastOrDefault();
        Assert.NotNull(entry);
        Assert.Equal(expected, entry.LevelString);
    }

    /// <summary>
    /// Formatted time is in correct format (HH:mm:ss.fff).
    /// </summary>
    [Fact]
    public void FormattedTime_IsInCorrectFormat()
    {
        // Arrange
        var loggingService = new LoggingService();
        
        // Act
        loggingService.Log(LogLevel.Info, "Test message");
        
        // Assert
        var entry = loggingService.LogEntries.LastOrDefault();
        Assert.NotNull(entry);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3}$", entry.FormattedTime);
    }

    /// <summary>
    /// Signal log formatted message contains all required information.
    /// </summary>
    [Theory]
    [InlineData(StrategySignal.Buy, 100.50)]
    [InlineData(StrategySignal.Sell, 99.25)]
    public void SignalLog_FormattedMessage_ContainsRequiredInfo(StrategySignal signal, double price)
    {
        // Arrange
        var loggingService = new LoggingService();
        
        // Act
        loggingService.LogSignal(signal, price);
        
        // Assert
        var entry = loggingService.LogEntries.LastOrDefault();
        Assert.NotNull(entry);
        Assert.Contains("Signal", entry.FormattedMessage);
        Assert.Contains(signal.ToString(), entry.FormattedMessage);
        Assert.Contains("Price", entry.FormattedMessage);
    }

    /// <summary>
    /// Execution log formatted message contains acceptance status.
    /// </summary>
    [Theory]
    [InlineData(true, "Accepted")]
    [InlineData(false, "Rejected")]
    public void ExecutionLog_FormattedMessage_ContainsAcceptanceStatus(bool isAccepted, string expectedStatus)
    {
        // Arrange
        var loggingService = new LoggingService();
        
        // Act
        loggingService.LogExecution("ORD-001", StrategySignal.Buy, 100.0, 10.0, isAccepted);
        
        // Assert
        var entry = loggingService.LogEntries.LastOrDefault();
        Assert.NotNull(entry);
        Assert.Contains(expectedStatus, entry.FormattedMessage);
    }

    /// <summary>
    /// LogEntryAdded event is raised when entry is added.
    /// </summary>
    [Fact]
    public void LogEntryAdded_EventIsRaised()
    {
        // Arrange
        var loggingService = new LoggingService();
        ExtendedLogEntry? receivedEntry = null;
        loggingService.LogEntryAdded += (s, e) => receivedEntry = e;
        
        // Act
        loggingService.Log(LogLevel.Info, "Test message");
        
        // Assert
        Assert.NotNull(receivedEntry);
        Assert.Equal("Test message", receivedEntry.Message);
    }

    /// <summary>
    /// Export creates file with correct content.
    /// </summary>
    [Fact]
    public async Task Export_CreatesFileWithCorrectContent()
    {
        // Arrange
        var loggingService = new LoggingService();
        loggingService.Log(LogLevel.Info, "Test message 1");
        loggingService.Log(LogLevel.Warn, "Test message 2");
        
        var tempFile = Path.GetTempFileName();
        
        try
        {
            // Act
            await loggingService.ExportAsync(tempFile);
            
            // Assert
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("Test message 1", content);
            Assert.Contains("Test message 2", content);
            Assert.Contains("[INFO]", content);
            Assert.Contains("[WARN]", content);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
