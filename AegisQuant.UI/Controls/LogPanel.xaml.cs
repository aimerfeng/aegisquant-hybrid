using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using AegisQuant.Interop;

namespace AegisQuant.UI.Controls;

/// <summary>
/// Extended log entry with additional fields for signal and execution logging.
/// Requirements: 7.2, 7.3, 7.4 - Log format with timestamp, signal type, price, indicator values
/// </summary>
public class ExtendedLogEntry : INotifyPropertyChanged
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    
    // Signal-specific fields (Requirements: 7.2)
    public Strategy.Signal? Signal { get; set; }
    public double? Price { get; set; }
    public Dictionary<string, double>? IndicatorValues { get; set; }
    
    // Execution-specific fields (Requirements: 7.4)
    public string? OrderId { get; set; }
    public double? Quantity { get; set; }
    public bool? IsAccepted { get; set; }
    
    public string LevelString => Level.ToString().ToUpper();
    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");
    
    /// <summary>
    /// Creates a formatted message including all relevant details.
    /// Requirements: 7.3 - Log format SHALL include timestamp, signal type, price, indicator values
    /// </summary>
    public string FormattedMessage
    {
        get
        {
            var parts = new List<string> { Message };
            
            if (Signal.HasValue && Signal.Value != Strategy.Signal.None)
            {
                parts.Add($"Signal: {Signal.Value}");
            }
            
            if (Price.HasValue)
            {
                parts.Add($"Price: {Price.Value:F2}");
            }
            
            if (IndicatorValues != null && IndicatorValues.Count > 0)
            {
                var indicators = string.Join(", ", IndicatorValues.Select(kv => $"{kv.Key}={kv.Value:F2}"));
                parts.Add($"[{indicators}]");
            }
            
            if (OrderId != null)
            {
                parts.Add($"OrderId: {OrderId}");
            }
            
            if (Quantity.HasValue)
            {
                parts.Add($"Qty: {Quantity.Value:F2}");
            }
            
            if (IsAccepted.HasValue)
            {
                parts.Add(IsAccepted.Value ? "Accepted" : "Rejected");
            }
            
            return string.Join(" | ", parts);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Log panel control with level filtering and auto-scroll.
/// Requirements: 7.1 - Display log panel at the bottom
/// Requirements: 7.5 - Support filtering by log level
/// Requirements: 7.6 - Auto-scroll to show latest entries
/// </summary>
public partial class LogPanel : UserControl, INotifyPropertyChanged
{
    private ObservableCollection<ExtendedLogEntry> _allLogEntries = new();
    private ObservableCollection<ExtendedLogEntry> _filteredLogEntries = new();
    private string _selectedFilter = "Info";
    private const int MaxLogEntries = 1000;

    /// <summary>
    /// Gets the filtered log entries for display.
    /// </summary>
    public ObservableCollection<ExtendedLogEntry> FilteredLogEntries
    {
        get => _filteredLogEntries;
        private set
        {
            _filteredLogEntries = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the selected log level filter.
    /// </summary>
    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (_selectedFilter != value)
            {
                _selectedFilter = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// Event raised when log entries are cleared.
    /// </summary>
    public event EventHandler? LogCleared;

    /// <summary>
    /// Event raised when log export is requested.
    /// </summary>
    public event EventHandler<LogExportEventArgs>? LogExportRequested;

    public LogPanel()
    {
        InitializeComponent();
        DataContext = this;
        
        _allLogEntries.CollectionChanged += AllLogEntries_CollectionChanged;
    }

    /// <summary>
    /// Adds a basic log entry.
    /// </summary>
    public void AddLog(LogLevel level, string message)
    {
        var entry = new ExtendedLogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };
        
        AddLogEntry(entry);
    }

    /// <summary>
    /// Adds a signal log entry.
    /// Requirements: 7.2 - WHEN a strategy generates a signal, THE log panel SHALL display the signal details
    /// </summary>
    public void AddSignalLog(Strategy.Signal signal, double price, Dictionary<string, double>? indicatorValues = null)
    {
        var entry = new ExtendedLogEntry
        {
            Timestamp = DateTime.Now,
            Level = LogLevel.Info, // Use custom Signal level
            Message = $"Signal Generated: {signal}",
            Signal = signal,
            Price = price,
            IndicatorValues = indicatorValues
        };
        
        // Override level string for signal entries
        AddLogEntry(entry);
    }

    /// <summary>
    /// Adds an execution log entry.
    /// Requirements: 7.4 - WHEN an order is executed, THE log panel SHALL display execution details
    /// </summary>
    public void AddExecutionLog(string orderId, Strategy.Signal signal, double price, double quantity, bool isAccepted)
    {
        var entry = new ExtendedLogEntry
        {
            Timestamp = DateTime.Now,
            Level = LogLevel.Info, // Use custom Trade level
            Message = isAccepted ? "Order Executed" : "Order Rejected",
            Signal = signal,
            Price = price,
            OrderId = orderId,
            Quantity = quantity,
            IsAccepted = isAccepted
        };
        
        AddLogEntry(entry);
    }

    /// <summary>
    /// Adds an extended log entry with full details.
    /// </summary>
    public void AddLogEntry(ExtendedLogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            _allLogEntries.Add(entry);
            
            // Trim old entries to prevent memory issues
            while (_allLogEntries.Count > MaxLogEntries)
            {
                _allLogEntries.RemoveAt(0);
            }
        });
    }

    /// <summary>
    /// Clears all log entries.
    /// </summary>
    public void Clear()
    {
        Dispatcher.Invoke(() =>
        {
            _allLogEntries.Clear();
            _filteredLogEntries.Clear();
        });
        
        LogCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets all log entries for export.
    /// </summary>
    public IEnumerable<ExtendedLogEntry> GetAllEntries() => _allLogEntries.ToList();

    /// <summary>
    /// Gets filtered log entries for export.
    /// </summary>
    public IEnumerable<ExtendedLogEntry> GetFilteredEntries() => _filteredLogEntries.ToList();

    private void AllLogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (ExtendedLogEntry entry in e.NewItems)
            {
                if (ShouldShowEntry(entry))
                {
                    Dispatcher.Invoke(() =>
                    {
                        _filteredLogEntries.Add(entry);
                        
                        // Auto-scroll if enabled
                        if (AutoScrollCheckBox?.IsChecked == true && LogListView.Items.Count > 0)
                        {
                            LogListView.ScrollIntoView(LogListView.Items[^1]);
                        }
                    });
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ApplyFilter();
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (ExtendedLogEntry entry in e.OldItems)
            {
                Dispatcher.Invoke(() => _filteredLogEntries.Remove(entry));
            }
        }
    }

    private bool ShouldShowEntry(ExtendedLogEntry entry)
    {
        if (_selectedFilter == "All")
            return true;
            
        // Map filter string to LogLevel
        return _selectedFilter switch
        {
            "Debug" => entry.Level >= LogLevel.Debug,
            "Info" => entry.Level >= LogLevel.Info,
            "Warn" => entry.Level >= LogLevel.Warn,
            "Error" => entry.Level >= LogLevel.Error,
            "Trade" => entry.IsAccepted.HasValue, // Trade entries have IsAccepted
            "Signal" => entry.Signal.HasValue && entry.Signal.Value != Strategy.Signal.None,
            _ => true
        };
    }

    private void ApplyFilter()
    {
        Dispatcher.Invoke(() =>
        {
            _filteredLogEntries.Clear();
            
            foreach (var entry in _allLogEntries)
            {
                if (ShouldShowEntry(entry))
                {
                    _filteredLogEntries.Add(entry);
                }
            }
        });
    }

    private void LevelFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string filter)
        {
            SelectedFilter = filter;
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        Clear();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            Title = "Export Log",
            FileName = $"aegisquant_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var entries = GetFilteredEntries();
                var lines = entries.Select(e => $"{e.FormattedTime} [{e.LevelString}] {e.FormattedMessage}");
                System.IO.File.WriteAllLines(dialog.FileName, lines);
                
                LogExportRequested?.Invoke(this, new LogExportEventArgs(dialog.FileName, true));
            }
            catch (Exception ex)
            {
                LogExportRequested?.Invoke(this, new LogExportEventArgs(dialog.FileName, false, ex.Message));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Event arguments for log export operations.
/// </summary>
public class LogExportEventArgs : EventArgs
{
    public string FilePath { get; }
    public bool Success { get; }
    public string? ErrorMessage { get; }

    public LogExportEventArgs(string filePath, bool success, string? errorMessage = null)
    {
        FilePath = filePath;
        Success = success;
        ErrorMessage = errorMessage;
    }
}
