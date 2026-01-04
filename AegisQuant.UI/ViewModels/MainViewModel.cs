using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegisQuant.Interop;
using AegisQuant.UI.Models;
using AegisQuant.UI.Services.Interfaces;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Strategy.Models;
using ScottPlot;

namespace AegisQuant.UI.ViewModels;

/// <summary>
/// Log entry for display in the UI.
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;

    public string LevelString => Level.ToString().ToUpper();
    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");
}

/// <summary>
/// Main view model for the application.
/// Implements MVVM pattern with CommunityToolkit.Mvvm.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IBacktestService _backtestService;
    private readonly IReplayService? _replayService;
    private bool _disposed;

    #region Observable Properties

    /// <summary>
    /// Equity curve data points for charting.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<double> _equityCurve = new();

    /// <summary>
    /// Current account status from the engine.
    /// </summary>
    [ObservableProperty]
    private AccountStatus _currentStatus;

    /// <summary>
    /// Backtest progress (0-100).
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// Whether a backtest is currently running.
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// Whether data has been loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isDataLoaded;

    /// <summary>
    /// Path to the loaded data file.
    /// </summary>
    [ObservableProperty]
    private string _dataFilePath = string.Empty;

    /// <summary>
    /// Status message displayed in the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Ready";

    /// <summary>
    /// Data quality report from the last load.
    /// </summary>
    [ObservableProperty]
    private DataQualityReport? _dataQualityReport;

    /// <summary>
    /// Log entries for display.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = new();

    /// <summary>
    /// Selected log level filter.
    /// </summary>
    [ObservableProperty]
    private LogLevel _selectedLogLevel = LogLevel.Info;

    /// <summary>
    /// OHLC data for chart display.
    /// </summary>
    [ObservableProperty]
    private List<OHLC>? _ohlcData;

    /// <summary>
    /// Volume data for chart display.
    /// </summary>
    [ObservableProperty]
    private List<double>? _volumeData;

    /// <summary>
    /// Available strategies for selection.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<StrategyInfo> _availableStrategies = new();

    /// <summary>
    /// Currently selected strategy.
    /// </summary>
    [ObservableProperty]
    private StrategyInfo? _selectedStrategy;

    #endregion

    #region Replay Properties - Requirements: 4.2, 4.3, 4.4, 4.5

    /// <summary>
    /// Whether replay is currently playing.
    /// </summary>
    [ObservableProperty]
    private bool _isReplayPlaying;

    /// <summary>
    /// Current replay index.
    /// </summary>
    [ObservableProperty]
    private int _replayCurrentIndex;

    /// <summary>
    /// Total number of bars for replay.
    /// </summary>
    [ObservableProperty]
    private int _replayTotalBars;

    /// <summary>
    /// Current replay timestamp.
    /// </summary>
    [ObservableProperty]
    private DateTime _replayCurrentTime;

    /// <summary>
    /// Current replay signal.
    /// </summary>
    [ObservableProperty]
    private Strategy.Signal _replayCurrentSignal = Strategy.Signal.None;

    /// <summary>
    /// Replay state info for status panel.
    /// </summary>
    [ObservableProperty]
    private ReplayStateInfo? _replayState;

    /// <summary>
    /// Playback speed in milliseconds per bar.
    /// </summary>
    [ObservableProperty]
    private int _playbackSpeed = 500;

    #endregion

    #region Strategy Parameters

    [ObservableProperty]
    private int _shortMaPeriod = 5;

    [ObservableProperty]
    private int _longMaPeriod = 20;

    [ObservableProperty]
    private double _positionSize = 100.0;

    [ObservableProperty]
    private double _stopLossPct = 2.0;

    [ObservableProperty]
    private double _takeProfitPct = 5.0;

    #endregion

    #region Risk Configuration

    [ObservableProperty]
    private int _maxOrderRate = 10;

    [ObservableProperty]
    private double _maxPositionSize = 1000.0;

    [ObservableProperty]
    private double _maxOrderValue = 100000.0;

    [ObservableProperty]
    private double _maxDrawdownPct = 10.0;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Total return percentage.
    /// </summary>
    public double TotalReturnPct => CurrentStatus.Equity > 0
        ? ((CurrentStatus.Equity - 100000) / 100000) * 100
        : 0;

    /// <summary>
    /// Maximum drawdown (tracked separately).
    /// </summary>
    [ObservableProperty]
    private double _maxDrawdown;

    /// <summary>
    /// Peak equity for drawdown calculation.
    /// </summary>
    private double _peakEquity;

    /// <summary>
    /// External strategy loaded from file.
    /// </summary>
    private IStrategy? _externalStrategy;

    /// <summary>
    /// Name of the current strategy.
    /// </summary>
    [ObservableProperty]
    private string _currentStrategyName = "Built-in (DualMA)";

    /// <summary>
    /// Event raised when OHLC data is loaded and ready for chart display.
    /// </summary>
    public event EventHandler<OhlcDataLoadedEventArgs>? OnOhlcDataLoaded;

    #endregion

    public MainViewModel(IBacktestService backtestService, IReplayService? replayService = null)
    {
        _backtestService = backtestService;
        _replayService = replayService;

        // Subscribe to service events
        _backtestService.OnStatusUpdated += OnStatusUpdated;
        _backtestService.OnLogReceived += OnLogReceived;
        _backtestService.OnBacktestCompleted += OnBacktestCompleted;
        _backtestService.OnOhlcDataLoaded += OnOhlcDataLoadedHandler;

        // Subscribe to replay service events if available
        if (_replayService != null)
        {
            _replayService.OnReplayStep += OnReplayStep;
            _replayService.OnReplayCompleted += OnReplayCompleted;
            _replayService.OnTradeSignal += OnReplayTradeSignal;
        }

        // Initialize available strategies
        InitializeStrategies();

        // Initialize with default parameters
        InitializeEngine();
    }

    /// <summary>
    /// Initializes the available strategies list.
    /// </summary>
    private void InitializeStrategies()
    {
        // Add built-in strategy
        var builtInStrategy = new StrategyInfo
        {
            Name = "Built-in (DualMA)",
            Description = "Dual Moving Average crossover strategy implemented in Rust",
            Type = StrategyType.BuiltIn,
            Version = "1.0"
        };
        AvailableStrategies.Add(builtInStrategy);

        // Set default selection
        SelectedStrategy = builtInStrategy;
    }

    private void InitializeEngine()
    {
        try
        {
            var strategyParams = new StrategyParams
            {
                ShortMaPeriod = ShortMaPeriod,
                LongMaPeriod = LongMaPeriod,
                PositionSize = PositionSize,
                StopLossPct = StopLossPct / 100.0,
                TakeProfitPct = TakeProfitPct / 100.0
            };

            var riskConfig = new RiskConfig
            {
                MaxOrderRate = MaxOrderRate,
                MaxPositionSize = MaxPositionSize,
                MaxOrderValue = MaxOrderValue,
                MaxDrawdownPct = MaxDrawdownPct / 100.0
            };

            _backtestService.Initialize(strategyParams, riskConfig);
            StatusMessage = "Engine initialized";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to initialize engine: {ex.Message}";
            AddLog(LogLevel.Error, $"Engine initialization failed: {ex.Message}");
        }
    }

    #region Commands

    /// <summary>
    /// Command to load data from a file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadData))]
    private async Task LoadDataAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "所有支持格式 (*.csv;*.parquet;*.xlsx;*.xls)|*.csv;*.parquet;*.xlsx;*.xls|Excel 文件 (*.xlsx;*.xls)|*.xlsx;*.xls|CSV 文件 (*.csv)|*.csv|Parquet 文件 (*.parquet)|*.parquet|所有文件 (*.*)|*.*",
            Title = "选择数据文件"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                StatusMessage = "正在加载数据...";
                var filePath = dialog.FileName;
                var extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

                // 如果是 Excel 文件，先转换
                if (extension == ".xlsx" || extension == ".xls")
                {
                    AddLog(LogLevel.Info, $"正在导入 Excel 文件: {System.IO.Path.GetFileName(filePath)}");
                    var excelService = new Services.ExcelDataImportService();
                    var importResult = await excelService.ImportExcelAsync(filePath);

                    if (!importResult.Success)
                    {
                        throw new Exception(importResult.ErrorMessage);
                    }

                    AddLog(LogLevel.Info, $"Excel 导入成功: {importResult.RowCount} 行数据，格式: {importResult.DetectedFormat}");

                    // 如果是 OHLC 格式，直接使用解析的数据
                    if (importResult.FormatType == Services.ExcelDataImportService.DataFormatType.OHLC 
                        && importResult.OhlcData != null)
                    {
                        DataFilePath = dialog.FileName;
                        OhlcData = importResult.OhlcData;
                        VolumeData = importResult.VolumeData;
                        IsDataLoaded = true;

                        // 创建模拟的数据质量报告
                        DataQualityReport = new DataQualityReport
                        {
                            TotalTicks = importResult.RowCount,
                            ValidTicks = importResult.RowCount,
                            InvalidTicks = 0,
                            AnomalyTicks = 0,
                            FirstTimestamp = new DateTimeOffset(importResult.OhlcData.First().DateTime).ToUnixTimeMilliseconds(),
                            LastTimestamp = new DateTimeOffset(importResult.OhlcData.Last().DateTime).ToUnixTimeMilliseconds()
                        };

                        // 触发 OHLC 数据加载事件
                        OnOhlcDataLoaded?.Invoke(this, new OhlcDataLoadedEventArgs(importResult.OhlcData, importResult.VolumeData!));

                        StatusMessage = $"已加载 {importResult.RowCount} 条 K 线数据 - {System.IO.Path.GetFileName(dialog.FileName)}";
                        return;
                    }

                    filePath = importResult.CsvFilePath!;
                }

                DataFilePath = dialog.FileName; // 保存原始文件路径用于显示

                var report = await _backtestService.LoadDataAsync(filePath);

                DataQualityReport = report;
                IsDataLoaded = true;
                StatusMessage = $"已加载 {report.ValidTicks:N0} 条数据 - {System.IO.Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
                AddLog(LogLevel.Error, $"数据加载失败: {ex.Message}");
                IsDataLoaded = false;
            }
        }
    }

    private bool CanLoadData() => !IsRunning;

    /// <summary>
    /// Command to start the backtest.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartBacktest))]
    private async Task StartBacktestAsync()
    {
        try
        {
            // Re-initialize engine with current parameters
            InitializeEngine();

            // Reload data if needed
            if (!string.IsNullOrEmpty(DataFilePath))
            {
                await _backtestService.LoadDataAsync(DataFilePath);
            }

            // Clear previous results
            EquityCurve.Clear();
            _peakEquity = 0;
            MaxDrawdown = 0;
            Progress = 0;

            IsRunning = true;
            StatusMessage = "Running backtest...";

            await _backtestService.RunBacktestAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Backtest failed: {ex.Message}";
            AddLog(LogLevel.Error, $"Backtest error: {ex.Message}");
            IsRunning = false;
        }
    }

    private bool CanStartBacktest() => IsDataLoaded && !IsRunning;

    /// <summary>
    /// Command to stop the running backtest.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopBacktest))]
    private void StopBacktest()
    {
        _backtestService.StopBacktest();
        StatusMessage = "Stopping backtest...";
    }

    private bool CanStopBacktest() => IsRunning;

    /// <summary>
    /// Command to clear the log.
    /// </summary>
    [RelayCommand]
    private void ClearLog()
    {
        LogEntries.Clear();
    }

    /// <summary>
    /// Command to open the optimization window.
    /// </summary>
    [RelayCommand]
    private void OpenOptimization()
    {
        var window = new Views.OptimizationWindow();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    /// <summary>
    /// Command to export log to file.
    /// </summary>
    [RelayCommand]
    private void ExportLog()
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
                var lines = LogEntries.Select(e => $"{e.FormattedTime} [{e.LevelString}] {e.Message}");
                System.IO.File.WriteAllLines(dialog.FileName, lines);
                StatusMessage = $"Log exported to {System.IO.Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to export log: {ex.Message}";
            }
        }
    }

    #endregion

    #region Replay Commands - Requirements: 4.2, 4.3, 4.4, 4.5

    /// <summary>
    /// Command to start replay playback.
    /// Requirements: 4.3 - Play SHALL advance through bars at configurable speed
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPlayReplay))]
    private async Task PlayReplayAsync()
    {
        if (_replayService == null || !IsDataLoaded) return;

        try
        {
            IsReplayPlaying = true;
            _replayService.PlaybackSpeed = PlaybackSpeed;
            await _replayService.PlayAsync();
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"Replay error: {ex.Message}");
        }
        finally
        {
            IsReplayPlaying = false;
        }
    }

    private bool CanPlayReplay() => IsDataLoaded && !IsReplayPlaying && !IsRunning;

    /// <summary>
    /// Command to pause replay playback.
    /// Requirements: 4.4 - Pause SHALL stop advancing
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPauseReplay))]
    private void PauseReplay()
    {
        if (_replayService == null) return;

        _replayService.Pause();
        IsReplayPlaying = false;
    }

    private bool CanPauseReplay() => IsReplayPlaying;

    /// <summary>
    /// Command to step forward one bar.
    /// Requirements: 4.5 - StepForward SHALL advance exactly one bar
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStepReplay))]
    private void StepForwardReplay()
    {
        if (_replayService == null) return;

        var result = _replayService.StepForward();
        if (result != null)
        {
            UpdateReplayState(result);
        }
    }

    /// <summary>
    /// Command to step backward one bar.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStepReplay))]
    private void StepBackwardReplay()
    {
        if (_replayService == null) return;

        _replayService.StepBackward();
        ReplayCurrentIndex = _replayService.CurrentIndex;
        ReplayState = _replayService.State;
    }

    private bool CanStepReplay() => IsDataLoaded && !IsReplayPlaying && !IsRunning;

    /// <summary>
    /// Command to reset replay to beginning.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResetReplay))]
    private void ResetReplay()
    {
        if (_replayService == null) return;

        _replayService.Reset();
        ReplayCurrentIndex = 0;
        ReplayState = _replayService.State;
        ReplayCurrentSignal = Strategy.Signal.None;
        StatusMessage = "Replay reset";
    }

    private bool CanResetReplay() => IsDataLoaded && !IsRunning;

    /// <summary>
    /// Command to seek to a specific position.
    /// Requirements: 4.8 - Time slider SHALL allow seeking to any position
    /// </summary>
    [RelayCommand]
    private void SeekReplay(int targetIndex)
    {
        if (_replayService == null) return;

        _replayService.SeekTo(targetIndex);
        ReplayCurrentIndex = _replayService.CurrentIndex;
        ReplayState = _replayService.State;
    }

    /// <summary>
    /// Initializes replay with current OHLC data.
    /// </summary>
    public void InitializeReplay()
    {
        if (_replayService == null || OhlcData == null || OhlcData.Count == 0) return;

        var volumes = VolumeData ?? new List<double>(new double[OhlcData.Count]);
        _replayService.SetData(OhlcData, volumes);
        ReplayTotalBars = _replayService.TotalBars;
        ReplayCurrentIndex = 0;
        ReplayState = _replayService.State;

        // Notify command state changes
        PlayReplayCommand.NotifyCanExecuteChanged();
        PauseReplayCommand.NotifyCanExecuteChanged();
        StepForwardReplayCommand.NotifyCanExecuteChanged();
        StepBackwardReplayCommand.NotifyCanExecuteChanged();
        ResetReplayCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Sets the strategy for replay.
    /// </summary>
    public void SetReplayStrategy(IStrategy strategy)
    {
        _replayService?.SetStrategy(strategy);
    }

    private void UpdateReplayState(ReplayStepEventArgs e)
    {
        ReplayCurrentIndex = e.BarIndex + 1; // +1 because index is 0-based but display is 1-based
        ReplayState = e.State;
        ReplayCurrentTime = e.CurrentBar.DateTime;

        if (e.Trade != null)
        {
            ReplayCurrentSignal = e.Trade.Signal;
            AddLog(LogLevel.Info, $"[{e.Trade.Signal}] @ {e.Trade.Price:F2} - {e.Trade.Reason}");
        }
        else
        {
            ReplayCurrentSignal = Strategy.Signal.None;
        }
    }

    /// <summary>
    /// Event raised when replay step occurs.
    /// </summary>
    public event EventHandler<ReplayStepEventArgs>? OnReplayStepOccurred;

    /// <summary>
    /// Sets an external strategy for backtesting.
    /// </summary>
    public void SetExternalStrategy(IStrategy strategy)
    {
        _externalStrategy = strategy;
        CurrentStrategyName = strategy.Name;
        _backtestService.SetExternalStrategy(strategy);
        AddLog(LogLevel.Info, $"Loaded external strategy: {strategy.Name}");
        StatusMessage = $"Strategy loaded: {strategy.Name}";

        // Add to available strategies if not already present
        var existingStrategy = AvailableStrategies.FirstOrDefault(s => s.Name == strategy.Name);
        if (existingStrategy == null)
        {
            var newStrategyInfo = new StrategyInfo
            {
                Name = strategy.Name,
                Description = strategy.Description,
                Type = strategy.Type
            };
            AvailableStrategies.Add(newStrategyInfo);
            SelectedStrategy = newStrategyInfo;
        }
        else
        {
            SelectedStrategy = existingStrategy;
        }
    }

    /// <summary>
    /// Clears the external strategy and reverts to built-in.
    /// </summary>
    public void ClearExternalStrategy()
    {
        _externalStrategy = null;
        CurrentStrategyName = "Built-in (DualMA)";
        _backtestService.ClearExternalStrategy();
        AddLog(LogLevel.Info, "Reverted to built-in strategy");
        StatusMessage = "Using built-in strategy";
    }

    #endregion

    #region Event Handlers

    private void OnStatusUpdated(object? sender, StatusUpdatedEventArgs e)
    {
        // Update on UI thread
        Application.Current?.Dispatcher.Invoke(() =>
        {
            CurrentStatus = e.Status;
            Progress = e.Progress;

            // Update equity curve
            EquityCurve.Add(e.Status.Equity);

            // Update max drawdown
            if (e.Status.Equity > _peakEquity)
            {
                _peakEquity = e.Status.Equity;
            }

            if (_peakEquity > 0)
            {
                var drawdown = (_peakEquity - e.Status.Equity) / _peakEquity * 100;
                if (drawdown > MaxDrawdown)
                {
                    MaxDrawdown = drawdown;
                }
            }

            OnPropertyChanged(nameof(TotalReturnPct));
        });
    }

    private void OnLogReceived(object? sender, LogReceivedEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            AddLog(e.Level, e.Message);
        });
    }

    private void OnBacktestCompleted(object? sender, BacktestCompletedEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsRunning = false;
            Progress = 100;
            CurrentStatus = e.FinalStatus;

            // Add final equity point
            EquityCurve.Add(e.FinalStatus.Equity);

            if (e.Success)
            {
                StatusMessage = $"Backtest completed in {e.Duration.TotalSeconds:F2}s. Final equity: {e.FinalStatus.Equity:F2}";
            }
            else
            {
                StatusMessage = $"Backtest stopped: {e.ErrorMessage}";
            }

            OnPropertyChanged(nameof(TotalReturnPct));

            // Update command states
            LoadDataCommand.NotifyCanExecuteChanged();
            StartBacktestCommand.NotifyCanExecuteChanged();
            StopBacktestCommand.NotifyCanExecuteChanged();
        });
    }

    /// <summary>
    /// Adds a log entry to the log panel.
    /// </summary>
    public void AddLog(LogLevel level, string message)
    {
        // Filter by selected log level
        if (level >= SelectedLogLevel)
        {
            LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            });

            // Keep log size manageable
            while (LogEntries.Count > 1000)
            {
                LogEntries.RemoveAt(0);
            }
        }
    }

    private void OnOhlcDataLoadedHandler(object? sender, OhlcDataLoadedEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            OhlcData = e.OhlcData;
            VolumeData = e.Volumes;
            
            // Initialize replay with loaded data
            InitializeReplay();
            
            // Forward the event to the View
            OnOhlcDataLoaded?.Invoke(this, e);
        });
    }

    #endregion

    #region Replay Event Handlers

    private void OnReplayStep(object? sender, ReplayStepEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            UpdateReplayState(e);
            OnReplayStepOccurred?.Invoke(this, e);
        });
    }

    private void OnReplayCompleted(object? sender, ReplayStateInfo e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsReplayPlaying = false;
            ReplayState = e;
            StatusMessage = $"Replay completed. Final equity: {e.Equity:C2}";
            AddLog(LogLevel.Info, $"Replay completed with {e.Trades.Count} trades");
        });
    }

    private void OnReplayTradeSignal(object? sender, ReplayTradeRecord e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ReplayCurrentSignal = e.Signal;
        });
    }

    #endregion

    #region Property Change Handlers

    partial void OnIsRunningChanged(bool value)
    {
        LoadDataCommand.NotifyCanExecuteChanged();
        StartBacktestCommand.NotifyCanExecuteChanged();
        StopBacktestCommand.NotifyCanExecuteChanged();
        PlayReplayCommand.NotifyCanExecuteChanged();
        PauseReplayCommand.NotifyCanExecuteChanged();
        StepForwardReplayCommand.NotifyCanExecuteChanged();
        StepBackwardReplayCommand.NotifyCanExecuteChanged();
        ResetReplayCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDataLoadedChanged(bool value)
    {
        StartBacktestCommand.NotifyCanExecuteChanged();
        PlayReplayCommand.NotifyCanExecuteChanged();
        StepForwardReplayCommand.NotifyCanExecuteChanged();
        StepBackwardReplayCommand.NotifyCanExecuteChanged();
        ResetReplayCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsReplayPlayingChanged(bool value)
    {
        PlayReplayCommand.NotifyCanExecuteChanged();
        PauseReplayCommand.NotifyCanExecuteChanged();
        StepForwardReplayCommand.NotifyCanExecuteChanged();
        StepBackwardReplayCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLogLevelChanged(LogLevel value)
    {
        // Could filter existing logs here if needed
    }

    partial void OnSelectedStrategyChanged(StrategyInfo? value)
    {
        if (value == null) return;

        CurrentStrategyName = value.Name;

        if (value.Type == StrategyType.BuiltIn)
        {
            // Use built-in Rust strategy
            _backtestService.UseBuiltInStrategy();
            AddLog(LogLevel.Info, "Switched to built-in DualMA strategy");
        }
        else if (!string.IsNullOrEmpty(value.FilePath))
        {
            // Load external strategy
            _ = LoadExternalStrategyAsync(value.FilePath);
        }
    }

    private async Task LoadExternalStrategyAsync(string filePath)
    {
        try
        {
            await _backtestService.LoadExternalStrategyAsync(filePath);
            AddLog(LogLevel.Info, $"Loaded external strategy from: {filePath}");
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"Failed to load strategy: {ex.Message}");
            // Revert to built-in
            SelectedStrategy = AvailableStrategies.FirstOrDefault(s => s.Type == StrategyType.BuiltIn);
        }
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _backtestService.OnStatusUpdated -= OnStatusUpdated;
            _backtestService.OnLogReceived -= OnLogReceived;
            _backtestService.OnBacktestCompleted -= OnBacktestCompleted;
            _backtestService.OnOhlcDataLoaded -= OnOhlcDataLoadedHandler;
            
            // Unsubscribe from replay service events
            if (_replayService != null)
            {
                _replayService.OnReplayStep -= OnReplayStep;
                _replayService.OnReplayCompleted -= OnReplayCompleted;
                _replayService.OnTradeSignal -= OnReplayTradeSignal;
            }
            
            // Note: Don't dispose _backtestService as it's managed by DI container
            _disposed = true;
        }
    }
}
