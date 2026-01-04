using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Controls;
using AegisQuant.UI.Models;
using AegisQuant.UI.Services;
using AegisQuant.UI.Services.Interfaces;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.ViewModels;
using ScottPlot;

namespace AegisQuant.UI.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private readonly IStrategyManagerService _strategyManager;
    private readonly MultiStrategyManagerService _multiStrategyManager;
    private readonly IReplayService? _replayService;
    private readonly IBacktestService? _backtestService;
    private IStrategy? _currentStrategy;
    private CancellationTokenSource? _displayUpdateCts;
    
    // UI elements (defined here for compatibility)
    private StrategyListPanel? StrategyListPanelControl => FindName("StrategyListPanel") as StrategyListPanel;
    private CandlestickChartControl? MainChartControlElement => FindName("MainChartControl") as CandlestickChartControl;
    private ReplayControlPanel? ReplayControlPanelElement => FindName("ReplayControlPanel") as ReplayControlPanel;
    private StatusPanel? StatusPanelElement => FindName("StatusPanel") as StatusPanel;
    private LogPanel? LogPanelElement => FindName("LogPanel") as LogPanel;
    private ChartViewModel _chartViewModel = new();

    public MainWindow(
        MainViewModel viewModel,
        IStrategyManagerService strategyManager,
        MultiStrategyManagerService multiStrategyManager,
        IReplayService? replayService = null,
        IBacktestService? backtestService = null)
    {
        InitializeComponent();

        // Set the view model via DI
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Initialize services via DI
        EnvironmentService.Instance.Initialize();
        _strategyManager = strategyManager;
        _multiStrategyManager = multiStrategyManager;
        _replayService = replayService;
        _backtestService = backtestService;

        // Wire up the strategy list panel
        if (StrategyListPanelControl != null)
        {
            StrategyListPanelControl.StrategyManager = _multiStrategyManager;
            StrategyListPanelControl.StrategySelected += OnStrategySelected;
        }

        // Wire up replay control panel - Requirements: 4.1, 4.2
        WireUpReplayControls();
        
        // Wire up log panel - Requirements: 7.1, 7.2, 7.3, 7.4, 7.5
        WireUpLogPanel();
        
        // Start display update consumer - Requirements: 9.10, 9.11
        StartDisplayUpdateConsumer();

        // Subscribe to OHLC data changes
        if (_viewModel != null)
        {
            _viewModel.OnOhlcDataLoaded += OnOhlcDataLoaded;
            _viewModel.OnReplayStepOccurred += OnReplayStepOccurred;
            _viewModel.EquityCurve.CollectionChanged += EquityCurve_CollectionChanged;
        }
    }

    /// <summary>
    /// Wires up replay control panel events to ViewModel commands.
    /// Requirements: 4.2 - Replay controls integrated with StrategyReplayService
    /// </summary>
    private void WireUpReplayControls()
    {
        if (ReplayControlPanelElement == null || _viewModel == null) return;

        // Wire up events to ViewModel commands
        ReplayControlPanelElement.PlayRequested += (s, e) =>
        {
            if (_viewModel.PlayReplayCommand.CanExecute(null))
            {
                _viewModel.PlayReplayCommand.Execute(null);
                ReplayControlPanelElement.IsPlaying = true;
            }
        };

        ReplayControlPanelElement.PauseRequested += (s, e) =>
        {
            if (_viewModel.PauseReplayCommand.CanExecute(null))
            {
                _viewModel.PauseReplayCommand.Execute(null);
                ReplayControlPanelElement.IsPlaying = false;
            }
        };

        ReplayControlPanelElement.StepForwardRequested += (s, e) =>
        {
            if (_viewModel.StepForwardReplayCommand.CanExecute(null))
            {
                _viewModel.StepForwardReplayCommand.Execute(null);
            }
        };

        ReplayControlPanelElement.StepBackwardRequested += (s, e) =>
        {
            if (_viewModel.StepBackwardReplayCommand.CanExecute(null))
            {
                _viewModel.StepBackwardReplayCommand.Execute(null);
            }
        };

        ReplayControlPanelElement.ResetRequested += (s, e) =>
        {
            if (_viewModel.ResetReplayCommand.CanExecute(null))
            {
                _viewModel.ResetReplayCommand.Execute(null);
                ReplayControlPanelElement.CurrentIndex = 0;
                StatusPanelElement?.Reset();
            }
        };

        ReplayControlPanelElement.SeekRequested += (s, targetIndex) =>
        {
            _viewModel.SeekReplayCommand.Execute(targetIndex);
        };

        ReplayControlPanelElement.SpeedChanged += (s, speed) =>
        {
            _viewModel.PlaybackSpeed = speed;
        };

        // Subscribe to ViewModel property changes to update control panel
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Wires up log panel to receive log events from BacktestService.
    /// Requirements: 7.1 - Display log panel at the bottom
    /// Requirements: 7.2 - Signal log display
    /// Requirements: 7.3 - Log format with timestamp, signal type, price, indicator values
    /// Requirements: 7.4 - Execution log display
    /// Requirements: 7.5 - Log level filtering
    /// </summary>
    private void WireUpLogPanel()
    {
        if (LogPanelElement == null) return;

        // Subscribe to BacktestService log events
        if (_backtestService is BacktestService backtestService)
        {
            // Requirements: 7.2, 7.3 - Signal logging
            backtestService.OnLogReceived += (s, e) =>
            {
                // Use Dispatcher to ensure UI access from correct thread
                Dispatcher.BeginInvoke(() => LogPanelElement.AddLog(e.Level, e.Message));
            };

            // Requirements: 7.2 - Signal logging
            backtestService.OnStrategySignal += (s, e) =>
            {
                Dispatcher.BeginInvoke(() => LogPanelElement.AddSignalLog(e.Signal, e.Price));
            };

            // Requirements: 7.4 - Execution logging
            backtestService.OnExecution += (s, e) =>
            {
                var signal = e.Side == 0 ? Strategy.Signal.Buy : Strategy.Signal.Sell;
                var orderId = $"ORD-{e.Timestamp:X8}";
                Dispatcher.BeginInvoke(() => LogPanelElement.AddExecutionLog(orderId, signal, e.Price, e.Quantity, true));
            };
        }

        // Handle log export events
        LogPanelElement.LogExportRequested += (s, e) =>
        {
            if (e.Success)
            {
                _viewModel?.AddLog(Interop.LogLevel.Info, $"Log exported to {System.IO.Path.GetFileName(e.FilePath)}");
            }
            else
            {
                _viewModel?.AddLog(Interop.LogLevel.Error, $"Failed to export log: {e.ErrorMessage}");
            }
        };

        // Handle log clear events
        LogPanelElement.LogCleared += (s, e) =>
        {
            _viewModel?.ClearLogCommand.Execute(null);
        };
    }

    /// <summary>
    /// Starts the display update consumer that reads from the BacktestService channel.
    /// Requirements: 9.10, 9.11 - Channel-based display updates with Dispatcher throttling
    /// </summary>
    private void StartDisplayUpdateConsumer()
    {
        if (_backtestService is not BacktestService backtestService) return;

        _displayUpdateCts = new CancellationTokenSource();
        var token = _displayUpdateCts.Token;

        // Start background task to consume display updates
        Task.Run(async () =>
        {
            try
            {
                var lastUpdateTime = DateTime.MinValue;
                const int throttleMs = 16; // ~60 FPS throttle

                await foreach (var update in backtestService.DisplayUpdates.ReadAllAsync(token))
                {
                    // Throttle updates to prevent UI flooding (Requirements: 9.11)
                    var now = DateTime.Now;
                    if ((now - lastUpdateTime).TotalMilliseconds < throttleMs && update.Signal == null)
                    {
                        continue; // Skip non-signal updates if too frequent
                    }
                    lastUpdateTime = now;

                    // Dispatch to UI thread (Requirements: 9.11)
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ProcessDisplayUpdate(update);
                    }, System.Windows.Threading.DispatcherPriority.DataBind, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Display update consumer error: {ex.Message}");
            }
        }, token);
    }

    /// <summary>
    /// Processes a display update on the UI thread.
    /// Requirements: 9.10, 9.11 - Update UI from Channel data
    /// </summary>
    private void ProcessDisplayUpdate(DisplayUpdate update)
    {
        // Update status panel
        if (StatusPanelElement != null)
        {
            StatusPanelElement.Equity = update.Status.Equity;
            StatusPanelElement.Position = update.Status.PositionCount;
            // AccountStatus has TotalPnl, not separate Unrealized/Realized
            // For now, show TotalPnl as realized and 0 as unrealized
            StatusPanelElement.UnrealizedPnL = 0;
            StatusPanelElement.RealizedPnL = update.Status.TotalPnl;

            if (update.Timestamp > 0)
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(update.Timestamp / 1_000_000).DateTime;
                StatusPanelElement.CurrentTime = time;
            }

            // Show signal if present
            if (update.Signal.HasValue && update.Signal.Value != Strategy.Signal.None)
            {
                StatusPanelElement.ShowTradeSignal(update.Signal.Value);
            }
        }

        // Update chart with new bar if present
        if (MainChartControlElement != null && update.NewBar.HasValue)
        {
            // Add trade marker if signal present
            if (update.Signal.HasValue && update.Signal.Value != Strategy.Signal.None)
            {
                MainChartControlElement.AddTradeMarker(
                    update.BarIndex,
                    update.Signal.Value,
                    update.NewBar.Value.Close);
            }
        }

        // Update replay control panel position
        if (ReplayControlPanelElement != null)
        {
            ReplayControlPanelElement.CurrentIndex = update.BarIndex;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null) return;

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ReplayCurrentIndex):
                if (ReplayControlPanelElement != null)
                {
                    ReplayControlPanelElement.CurrentIndex = _viewModel.ReplayCurrentIndex;
                }
                break;

            case nameof(MainViewModel.ReplayTotalBars):
                if (ReplayControlPanelElement != null)
                {
                    ReplayControlPanelElement.TotalBars = _viewModel.ReplayTotalBars;
                }
                break;

            case nameof(MainViewModel.ReplayCurrentTime):
                if (ReplayControlPanelElement != null)
                {
                    ReplayControlPanelElement.CurrentTime = _viewModel.ReplayCurrentTime;
                }
                if (StatusPanelElement != null)
                {
                    StatusPanelElement.CurrentTime = _viewModel.ReplayCurrentTime;
                }
                break;

            case nameof(MainViewModel.IsReplayPlaying):
                if (ReplayControlPanelElement != null)
                {
                    ReplayControlPanelElement.IsPlaying = _viewModel.IsReplayPlaying;
                }
                break;

            case nameof(MainViewModel.ReplayState):
                UpdateStatusPanel();
                break;

            case nameof(MainViewModel.ReplayCurrentSignal):
                if (StatusPanelElement != null)
                {
                    StatusPanelElement.CurrentSignal = _viewModel.ReplayCurrentSignal;
                }
                break;
        }
    }

    /// <summary>
    /// Updates the status panel with current replay state.
    /// Requirements: 5.2 - Status panel SHALL update with current account state
    /// </summary>
    private void UpdateStatusPanel()
    {
        if (StatusPanelElement == null || _viewModel?.ReplayState == null) return;

        StatusPanelElement.UpdateFromReplayState(_viewModel.ReplayState, _viewModel.ReplayCurrentTime);
    }

    /// <summary>
    /// Handles replay step events for chart updates.
    /// </summary>
    private void OnReplayStepOccurred(object? sender, ReplayStepEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Update chart with new bar if needed
            if (MainChartControlElement != null && _replayService != null)
            {
                var visibleData = _replayService.GetDataUpToCurrentPosition();
                if (visibleData.Count > 0)
                {
                    MainChartControlElement.SetVisibleRange(visibleData);
                }
            }

            // Update status panel with trade signal flash
            if (e.Trade != null && StatusPanelElement != null)
            {
                StatusPanelElement.ShowTradeSignal(e.Trade.Signal);
            }
        });
    }

    private void OnStrategySelected(object? sender, Strategy.Models.ManagedStrategy? strategy)
    {
        if (strategy == null) return;
        
        // Update the current strategy display when a strategy is selected from the list
        if (CurrentStrategyNameText != null)
            CurrentStrategyNameText.Text = strategy.Strategy.Name;
        if (CurrentStrategyTypeText != null)
            CurrentStrategyTypeText.Text = strategy.Strategy.Type switch
            {
                StrategyType.JsonConfig => "JSON Configuration",
                StrategyType.PythonScript => "Python Script",
                _ => "External"
            };
    }

    /// <summary>
    /// Handles OHLC data loaded event and updates the chart.
    /// </summary>
    private void OnOhlcDataLoaded(object? sender, OhlcDataLoadedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (MainChartControlElement != null && e.OhlcData.Count > 0)
                {
                    // Update the candlestick chart with OHLC data
                    MainChartControlElement.UpdateOhlcData(e.OhlcData);
                    
                    // Update volume data if available
                    if (e.Volumes.Count > 0)
                    {
                        MainChartControlElement.UpdateVolumeData(e.Volumes);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update chart: {ex.Message}");
            }
        });
    }

    private void EquityCurve_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        // Note: With CandlestickChartControl, we don't need to update equity curve here
        // The chart displays OHLC data, not equity curve
        // Equity curve could be displayed in a separate panel if needed
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void LoadStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        // Create a temporary StrategyManagerService for the loader window
        // since it needs the concrete type for file loading
        using var tempManager = new StrategyManagerService();
        var loaderWindow = new StrategyLoaderWindow(tempManager)
        {
            Owner = this
        };

        if (loaderWindow.ShowDialog() == true && loaderWindow.LoadedStrategy != null)
        {
            // Dispose previous strategy if any
            _currentStrategy?.Dispose();
            _currentStrategy = loaderWindow.LoadedStrategy;

            // Update UI
            if (CurrentStrategyNameText != null)
                CurrentStrategyNameText.Text = _currentStrategy.Name;
            if (CurrentStrategyTypeText != null)
                CurrentStrategyTypeText.Text = _currentStrategy.Type switch
                {
                    StrategyType.JsonConfig => "JSON 配置策略",
                    StrategyType.PythonScript => "Python 脚本策略",
                    _ => "外部策略"
                };
            if (UseBuiltInButton != null)
                UseBuiltInButton.Visibility = Visibility.Visible;

            // Notify view model about strategy change
            _viewModel?.SetExternalStrategy(_currentStrategy);
        }
    }

    private void NewStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var editorWindow = new StrategyEditorWindow()
            {
                Owner = this
            };
            editorWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开策略编辑器失败:\n{ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UseBuiltInButton_Click(object sender, RoutedEventArgs e)
    {
        // Dispose external strategy
        _currentStrategy?.Dispose();
        _currentStrategy = null;

        // Reset UI
        if (CurrentStrategyNameText != null)
            CurrentStrategyNameText.Text = FindResource("String.Strategy.BuiltIn") as string ?? "Built-in (DualMA)";
        if (CurrentStrategyTypeText != null)
            CurrentStrategyTypeText.Text = "Built-in";
        if (UseBuiltInButton != null)
            UseBuiltInButton.Visibility = Visibility.Collapsed;

        // Notify view model to use built-in strategy
        _viewModel?.ClearExternalStrategy();
    }

    /// <summary>
    /// Opens the Import Wizard window for data import with column mapping and cleaning.
    /// 修复：确保数据正确传递到 MainViewModel 和 BacktestService
    /// </summary>
    private async void ImportWizardButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var importWindow = new ImportWizardWindow
            {
                Owner = this
            };
            
            if (importWindow.ShowDialog() == true && importWindow.Result != null)
            {
                var config = importWindow.Result;
                _viewModel?.AddLog(Interop.LogLevel.Info, $"开始导入数据: {System.IO.Path.GetFileName(config.FilePath)}");
                
                // 根据文件类型处理数据
                var extension = System.IO.Path.GetExtension(config.FilePath).ToLowerInvariant();
                
                if (extension == ".xlsx" || extension == ".xls")
                {
                    // Excel 文件 - 使用 ExcelDataImportService
                    var excelService = new ExcelDataImportService();
                    var result = await excelService.ImportExcelAsync(config.FilePath);
                    
                    if (result.Success)
                    {
                        if (result.FormatType == ExcelDataImportService.DataFormatType.OHLC && result.OhlcData != null)
                        {
                            // 使用 MainViewModel.OnDataLoaded 作为数据流的中心枢纽
                            _viewModel?.OnDataLoaded(result.OhlcData, result.VolumeData, config.FilePath);
                            _viewModel?.AddLog(Interop.LogLevel.Info, $"成功导入 {result.RowCount} 条 OHLC 数据");
                        }
                        else if (!string.IsNullOrEmpty(result.CsvFilePath))
                        {
                            // Tick 数据 - 加载生成的 CSV 文件
                            await LoadDataFromFileAsync(result.CsvFilePath);
                            _viewModel?.AddLog(Interop.LogLevel.Info, $"成功导入 {result.RowCount} 条 Tick 数据");
                        }
                    }
                    else
                    {
                        MessageBox.Show($"导入失败: {result.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // CSV 文件 - 直接加载
                    await LoadDataFromFileAsync(config.FilePath);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入数据失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            _viewModel?.AddLog(Interop.LogLevel.Error, $"导入失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads data from a file path.
    /// </summary>
    private async Task LoadDataFromFileAsync(string filePath)
    {
        if (_viewModel == null || _backtestService == null) return;
        
        try
        {
            _viewModel.StatusMessage = "正在加载数据...";
            
            // 如果服务处于 Faulted 状态，先重置
            if (_backtestService.State == Services.Interfaces.ServiceState.Faulted)
            {
                _backtestService.Reset();
            }
            
            // 初始化引擎
            var strategyParams = new Interop.StrategyParams
            {
                ShortMaPeriod = _viewModel.ShortMaPeriod,
                LongMaPeriod = _viewModel.LongMaPeriod,
                PositionSize = _viewModel.PositionSize,
                StopLossPct = _viewModel.StopLossPct / 100.0,
                TakeProfitPct = _viewModel.TakeProfitPct / 100.0
            };

            var riskConfig = new Interop.RiskConfig
            {
                MaxOrderRate = _viewModel.MaxOrderRate,
                MaxPositionSize = _viewModel.MaxPositionSize,
                MaxOrderValue = _viewModel.MaxOrderValue,
                MaxDrawdownPct = _viewModel.MaxDrawdownPct / 100.0
            };

            _backtestService.Initialize(strategyParams, riskConfig);
            
            var report = await _backtestService.LoadDataAsync(filePath);
            
            _viewModel.DataFilePath = filePath;
            _viewModel.IsDataLoaded = true;
            _viewModel.DataQualityReport = report;
            _viewModel.StatusMessage = $"已加载 {report.ValidTicks:N0} 条数据 - {System.IO.Path.GetFileName(filePath)}";
            
            // 初始化回放
            _viewModel.InitializeReplay();
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"加载失败: {ex.Message}";
            _viewModel.AddLog(Interop.LogLevel.Error, $"数据加载失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the Audit Log window.
    /// </summary>
    private void AuditLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var auditWindow = new AuditLogWindow
            {
                Owner = this
            };
            auditWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开审计日志失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Opens the Notification Settings window.
    /// </summary>
    private void NotificationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var notificationWindow = new NotificationSettingsWindow
            {
                Owner = this
            };
            notificationWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开通知设置失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Cancel display update consumer
        _displayUpdateCts?.Cancel();
        _displayUpdateCts?.Dispose();
        
        // Clean up resources (only dispose locally-owned resources)
        _currentStrategy?.Dispose();
        
        // Unsubscribe from events
        if (StrategyListPanelControl != null)
        {
            StrategyListPanelControl.StrategySelected -= OnStrategySelected;
        }
        
        if (_viewModel != null)
        {
            _viewModel.OnOhlcDataLoaded -= OnOhlcDataLoaded;
            _viewModel.OnReplayStepOccurred -= OnReplayStepOccurred;
            _viewModel.EquityCurve.CollectionChanged -= EquityCurve_CollectionChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
        }
    }
}
