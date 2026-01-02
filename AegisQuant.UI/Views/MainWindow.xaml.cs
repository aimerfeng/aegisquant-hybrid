using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
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

        // Set up chart
        SetupChart();

        // Subscribe to equity curve changes
        if (_viewModel != null)
        {
            _viewModel.EquityCurve.CollectionChanged += EquityCurve_CollectionChanged;
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
