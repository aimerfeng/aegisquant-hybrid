using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Controls;
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

        // Set up chart
        SetupChart();

        // Subscribe to equity curve changes
        if (_viewModel != null)
        {
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

    private void SetupChart()
    {
        // Configure the chart appearance
        EquityChart.Plot.Title("Equity Curve");
        EquityChart.Plot.XLabel("Tick");
        EquityChart.Plot.YLabel("Equity ($)");

        // Set initial axis limits
        EquityChart.Plot.Axes.SetLimitsX(0, 100);
        EquityChart.Plot.Axes.SetLimitsY(90000, 110000);

        EquityChart.Refresh();
    }

    private void EquityCurve_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        // Update chart on UI thread
        Dispatcher.Invoke(() =>
        {
            try
            {
                // Clear existing plots
                EquityChart.Plot.Clear();

                if (_viewModel.EquityCurve.Count > 0)
                {
                    // Create data arrays
                    var xs = Enumerable.Range(0, _viewModel.EquityCurve.Count)
                        .Select(i => (double)i)
                        .ToArray();
                    var ys = _viewModel.EquityCurve.ToArray();

                    // Add the equity curve
                    var scatter = EquityChart.Plot.Add.Scatter(xs, ys);
                    scatter.LineWidth = 2;
                    scatter.MarkerSize = 0;
                    scatter.Color = Colors.Blue;

                    // Auto-scale axes
                    EquityChart.Plot.Axes.AutoScale();
                }

                // Refresh the chart
                EquityChart.Refresh();
            }
            catch
            {
                // Ignore chart update errors
            }
        });
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
                    StrategyType.JsonConfig => "JSON Configuration",
                    StrategyType.PythonScript => "Python Script",
                    _ => "External"
                };
            if (UseBuiltInButton != null)
                UseBuiltInButton.Visibility = Visibility.Visible;

            // Notify view model about strategy change
            _viewModel?.SetExternalStrategy(_currentStrategy);
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
            _viewModel.EquityCurve.CollectionChanged -= EquityCurve_CollectionChanged;
            _viewModel.Dispose();
        }
    }
}
