using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Controls;
using AegisQuant.UI.Models;
using AegisQuant.UI.Services;
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
    private readonly StrategyManagerService _strategyManager;
    private readonly MultiStrategyManagerService _multiStrategyManager;
    private IStrategy? _currentStrategy;
    
    // UI elements (defined here for compatibility)
    private TextBlock? CurrentStrategyNameText => FindName("CurrentStrategyNameText") as TextBlock;
    private TextBlock? CurrentStrategyTypeText => FindName("CurrentStrategyTypeText") as TextBlock;
    private Button? UseBuiltInButton => FindName("UseBuiltInButton") as Button;
    private StrategyListPanel? StrategyListPanelControl => FindName("StrategyListPanel") as StrategyListPanel;
    private CandlestickChartControl? MainChartControlElement => FindName("MainChartControl") as CandlestickChartControl;
    private ChartViewModel _chartViewModel = new();

    public MainWindow()
    {
        InitializeComponent();

        // Get the view model
        _viewModel = DataContext as MainViewModel;

        // Initialize services
        EnvironmentService.Instance.Initialize();
        _strategyManager = new StrategyManagerService();
        _multiStrategyManager = new MultiStrategyManagerService();

        // Wire up the strategy list panel
        if (StrategyListPanelControl != null)
        {
            StrategyListPanelControl.StrategyManager = _multiStrategyManager;
            StrategyListPanelControl.StrategySelected += OnStrategySelected;
        }

        // Subscribe to OHLC data changes
        if (_viewModel != null)
        {
            _viewModel.OnOhlcDataLoaded += OnOhlcDataLoaded;
            _viewModel.EquityCurve.CollectionChanged += EquityCurve_CollectionChanged;
        }
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
        var loaderWindow = new StrategyLoaderWindow(_strategyManager)
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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Clean up resources
        _currentStrategy?.Dispose();
        _multiStrategyManager?.Dispose();
        
        // Unsubscribe from events
        if (StrategyListPanelControl != null)
        {
            StrategyListPanelControl.StrategySelected -= OnStrategySelected;
        }
        
        if (_viewModel != null)
        {
            _viewModel.OnOhlcDataLoaded -= OnOhlcDataLoaded;
            _viewModel.EquityCurve.CollectionChanged -= EquityCurve_CollectionChanged;
            _viewModel.Dispose();
        }
    }
}
