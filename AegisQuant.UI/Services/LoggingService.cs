using System.Collections.ObjectModel;
using AegisQuant.Interop;
using AegisQuant.UI.Controls;
using StrategySignal = AegisQuant.UI.Strategy.Signal;

namespace AegisQuant.UI.Services;

/// <summary>
/// Centralized logging service for the application.
/// Requirements: 7.2, 7.3, 7.4 - Signal and execution logging
/// </summary>
public interface ILoggingService
{
    /// <summary>
    /// Gets all log entries.
    /// </summary>
    ObservableCollection<ExtendedLogEntry> LogEntries { get; }
    
    /// <summary>
    /// Adds a basic log entry.
    /// </summary>
    void Log(LogLevel level, string message);
    
    /// <summary>
    /// Adds a signal log entry.
    /// Requirements: 7.2 - WHEN a strategy generates a signal, THE log panel SHALL display the signal details
    /// Requirements: 7.3 - Log format SHALL include timestamp, signal type, price, indicator values
    /// </summary>
    void LogSignal(StrategySignal signal, double price, Dictionary<string, double>? indicatorValues = null);
    
    /// <summary>
    /// Adds an execution log entry.
    /// Requirements: 7.4 - WHEN an order is executed, THE log panel SHALL display execution details
    /// </summary>
    void LogExecution(string orderId, StrategySignal signal, double price, double quantity, bool isAccepted);
    
    /// <summary>
    /// Clears all log entries.
    /// </summary>
    void Clear();
    
    /// <summary>
    /// Exports log entries to a file.
    /// </summary>
    Task ExportAsync(string filePath);
    
    /// <summary>
    /// Event raised when a new log entry is added.
    /// </summary>
    event EventHandler<ExtendedLogEntry>? LogEntryAdded;
}

/// <summary>
/// Implementation of the logging service.
/// </summary>
public class LoggingService : ILoggingService
{
    private readonly ObservableCollection<ExtendedLogEntry> _logEntries = new();
    private readonly object _lock = new();
    private const int MaxLogEntries = 10000;

    /// <inheritdoc/>
    public ObservableCollection<ExtendedLogEntry> LogEntries => _logEntries;

    /// <inheritdoc/>
    public event EventHandler<ExtendedLogEntry>? LogEntryAdded;

    /// <inheritdoc/>
    public void Log(LogLevel level, string message)
    {
        var entry = new ExtendedLogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };
        
        AddEntry(entry);
    }

    /// <inheritdoc/>
    public void LogSignal(StrategySignal signal, double price, Dictionary<string, double>? indicatorValues = null)
    {
        var entry = new ExtendedLogEntry
        {
            Timestamp = DateTime.Now,
            Level = LogLevel.Info,
            Message = $"Signal Generated: {signal}",
            Signal = signal,
            Price = price,
            IndicatorValues = indicatorValues
        };
        
        AddEntry(entry);
    }

    /// <inheritdoc/>
    public void LogExecution(string orderId, StrategySignal signal, double price, double quantity, bool isAccepted)
    {
        var entry = new ExtendedLogEntry
        {
            Timestamp = DateTime.Now,
            Level = LogLevel.Info,
            Message = isAccepted ? "Order Executed" : "Order Rejected",
            Signal = signal,
            Price = price,
            OrderId = orderId,
            Quantity = quantity,
            IsAccepted = isAccepted
        };
        
        AddEntry(entry);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (_lock)
        {
            _logEntries.Clear();
        }
    }

    /// <inheritdoc/>
    public async Task ExportAsync(string filePath)
    {
        List<ExtendedLogEntry> entries;
        lock (_lock)
        {
            entries = _logEntries.ToList();
        }
        
        var lines = entries.Select(e => $"{e.FormattedTime} [{e.LevelString}] {e.FormattedMessage}");
        await System.IO.File.WriteAllLinesAsync(filePath, lines);
    }

    private void AddEntry(ExtendedLogEntry entry)
    {
        lock (_lock)
        {
            _logEntries.Add(entry);
            
            // Trim old entries to prevent memory issues
            while (_logEntries.Count > MaxLogEntries)
            {
                _logEntries.RemoveAt(0);
            }
        }
        
        LogEntryAdded?.Invoke(this, entry);
    }
}
