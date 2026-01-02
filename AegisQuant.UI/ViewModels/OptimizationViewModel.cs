using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegisQuant.Interop;
using AegisQuant.UI.Models;

namespace AegisQuant.UI.ViewModels;

/// <summary>
/// Represents a single optimization result.
/// </summary>
public class OptimizationResult
{
    public int ShortMaPeriod { get; set; }
    public int LongMaPeriod { get; set; }
    public double PositionSize { get; set; }
    public double FinalEquity { get; set; }
    public double TotalReturnPct { get; set; }
    public double MaxDrawdownPct { get; set; }
    public double SharpeRatio { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades * 100 : 0;
}

/// <summary>
/// ViewModel for parameter optimization.
/// </summary>
public partial class OptimizationViewModel : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    #region Parameter Ranges

    [ObservableProperty]
    private int _shortMaMin = 3;

    [ObservableProperty]
    private int _shortMaMax = 10;

    [ObservableProperty]
    private int _shortMaStep = 1;

    [ObservableProperty]
    private int _longMaMin = 15;

    [ObservableProperty]
    private int _longMaMax = 50;

    [ObservableProperty]
    private int _longMaStep = 5;

    [ObservableProperty]
    private double _positionSizeMin = 50;

    [ObservableProperty]
    private double _positionSizeMax = 200;

    [ObservableProperty]
    private double _positionSizeStep = 50;

    #endregion

    #region State

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _dataFilePath = string.Empty;

    [ObservableProperty]
    private int _totalCombinations;

    [ObservableProperty]
    private int _completedCombinations;

    [ObservableProperty]
    private ObservableCollection<OptimizationResult> _results = new();

    #endregion

    public OptimizationViewModel()
    {
        CalculateTotalCombinations();
    }

    partial void OnShortMaMinChanged(int value) => CalculateTotalCombinations();
    partial void OnShortMaMaxChanged(int value) => CalculateTotalCombinations();
    partial void OnShortMaStepChanged(int value) => CalculateTotalCombinations();
    partial void OnLongMaMinChanged(int value) => CalculateTotalCombinations();
    partial void OnLongMaMaxChanged(int value) => CalculateTotalCombinations();
    partial void OnLongMaStepChanged(int value) => CalculateTotalCombinations();
    partial void OnPositionSizeMinChanged(double value) => CalculateTotalCombinations();
    partial void OnPositionSizeMaxChanged(double value) => CalculateTotalCombinations();
    partial void OnPositionSizeStepChanged(double value) => CalculateTotalCombinations();

    private void CalculateTotalCombinations()
    {
        var shortMaCount = Math.Max(1, (ShortMaMax - ShortMaMin) / Math.Max(1, ShortMaStep) + 1);
        var longMaCount = Math.Max(1, (LongMaMax - LongMaMin) / Math.Max(1, LongMaStep) + 1);
        var positionCount = Math.Max(1, (int)((PositionSizeMax - PositionSizeMin) / Math.Max(1, PositionSizeStep) + 1));
        TotalCombinations = shortMaCount * longMaCount * positionCount;
    }

    [RelayCommand(CanExecute = nameof(CanSelectFile))]
    private void SelectFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Data Files (*.csv;*.parquet)|*.csv;*.parquet|All Files (*.*)|*.*",
            Title = "Select Data File for Optimization"
        };

        if (dialog.ShowDialog() == true)
        {
            DataFilePath = dialog.FileName;
            StatusMessage = $"Selected: {System.IO.Path.GetFileName(dialog.FileName)}";
        }
    }

    private bool CanSelectFile() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStartOptimization))]
    private async Task StartOptimizationAsync()
    {
        if (string.IsNullOrEmpty(DataFilePath))
        {
            StatusMessage = "Please select a data file first";
            return;
        }

        IsRunning = true;
        Results.Clear();
        CompletedCombinations = 0;
        Progress = 0;
        _cancellationTokenSource = new CancellationTokenSource();

        StatusMessage = "Running optimization...";

        try
        {
            await Task.Run(async () =>
            {
                var riskConfig = RiskConfig.Default;

                // Generate all parameter combinations
                for (int shortMa = ShortMaMin; shortMa <= ShortMaMax; shortMa += ShortMaStep)
                {
                    for (int longMa = LongMaMin; longMa <= LongMaMax; longMa += LongMaStep)
                    {
                        // Skip invalid combinations
                        if (shortMa >= longMa) continue;

                        for (double posSize = PositionSizeMin; posSize <= PositionSizeMax; posSize += PositionSizeStep)
                        {
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                                return;

                            var strategyParams = new StrategyParams
                            {
                                ShortMaPeriod = shortMa,
                                LongMaPeriod = longMa,
                                PositionSize = posSize,
                                StopLossPct = 0.02,
                                TakeProfitPct = 0.05
                            };

                            try
                            {
                                // Run backtest with these parameters
                                using var engine = new EngineWrapper(strategyParams, riskConfig);
                                var report = engine.LoadData(DataFilePath);
                                engine.RunBacktest();
                                var status = engine.GetAccountStatus();

                                // Calculate metrics
                                var result = new OptimizationResult
                                {
                                    ShortMaPeriod = shortMa,
                                    LongMaPeriod = longMa,
                                    PositionSize = posSize,
                                    FinalEquity = status.Equity,
                                    TotalReturnPct = ((status.Equity - 100000) / 100000) * 100,
                                    MaxDrawdownPct = 0, // Would need to track during backtest
                                    SharpeRatio = 0, // Would need returns series
                                    TotalTrades = 0,
                                    WinningTrades = 0
                                };

                                // Add result on UI thread
                                Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    Results.Add(result);
                                    CompletedCombinations++;
                                    Progress = (double)CompletedCombinations / TotalCombinations * 100;
                                });
                            }
                            catch
                            {
                                // Skip failed combinations
                                Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    CompletedCombinations++;
                                    Progress = (double)CompletedCombinations / TotalCombinations * 100;
                                });
                            }

                            // Small delay to prevent UI freeze
                            await Task.Delay(10);
                        }
                    }
                }
            }, _cancellationTokenSource.Token);

            // Sort results by total return
            var sortedResults = Results.OrderByDescending(r => r.TotalReturnPct).ToList();
            Results.Clear();
            foreach (var result in sortedResults)
            {
                Results.Add(result);
            }

            StatusMessage = $"Optimization complete. {Results.Count} combinations tested.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Optimization cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Optimization failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private bool CanStartOptimization() => !IsRunning && !string.IsNullOrEmpty(DataFilePath);

    [RelayCommand(CanExecute = nameof(CanStopOptimization))]
    private void StopOptimization()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Stopping optimization...";
    }

    private bool CanStopOptimization() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        SelectFileCommand.NotifyCanExecuteChanged();
        StartOptimizationCommand.NotifyCanExecuteChanged();
        StopOptimizationCommand.NotifyCanExecuteChanged();
    }

    partial void OnDataFilePathChanged(string value)
    {
        StartOptimizationCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _disposed = true;
        }
    }
}
