using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Services;
using AegisQuant.UI.ViewModels;
using ScottPlot;

namespace AegisQuant.UI.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Get the view model
        _viewModel = DataContext as MainViewModel;

        // Initialize environment service
        EnvironmentService.Instance.Initialize();

        // Set up chart
        SetupChart();

        // Subscribe to equity curve changes
        if (_viewModel != null)
        {
            _viewModel.EquityCurve.CollectionChanged += EquityCurve_CollectionChanged;
        }

        // Sync environment selector with current environment
        SyncEnvironmentSelector();
    }

    private void SyncEnvironmentSelector()
    {
        var currentEnv = EnvironmentService.Instance.CurrentEnvironment;
        foreach (ComboBoxItem item in EnvironmentSelector.Items)
        {
            if (item.Tag?.ToString() == currentEnv.ToString())
            {
                EnvironmentSelector.SelectedItem = item;
                break;
            }
        }
    }

    private void EnvironmentSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentSelector.SelectedItem is ComboBoxItem selectedItem)
        {
            var envTag = selectedItem.Tag?.ToString();
            if (Enum.TryParse<TradingEnvironment>(envTag, out var environment))
            {
                var success = EnvironmentService.Instance.SetEnvironment(environment);
                if (!success)
                {
                    // 用户取消了切换，恢复选择
                    SyncEnvironmentSelector();
                }
            }
        }
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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Clean up resources
        if (_viewModel != null)
        {
            _viewModel.EquityCurve.CollectionChanged -= EquityCurve_CollectionChanged;
            _viewModel.Dispose();
        }
    }
}
