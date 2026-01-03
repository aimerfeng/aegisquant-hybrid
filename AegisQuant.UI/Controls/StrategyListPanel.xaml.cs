using System;
using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Services;
using AegisQuant.UI.Strategy.Models;
using AegisQuant.UI.Views;

namespace AegisQuant.UI.Controls;

/// <summary>
/// Strategy list panel for managing multiple strategies.
/// </summary>
public partial class StrategyListPanel : UserControl
{
    private MultiStrategyManagerService? _strategyManager;

    public StrategyListPanel()
    {
        InitializeComponent();
        AggregationModeComboBox.SelectedIndex = 0;
        UpdateEmptyState();
    }

    /// <summary>
    /// Gets or sets the strategy manager service.
    /// </summary>
    public MultiStrategyManagerService? StrategyManager
    {
        get => _strategyManager;
        set
        {
            if (_strategyManager != null)
            {
                _strategyManager.StrategyAdded -= OnStrategyAdded;
                _strategyManager.StrategyRemoved -= OnStrategyRemoved;
            }

            _strategyManager = value;
            DataContext = _strategyManager;

            if (_strategyManager != null)
            {
                _strategyManager.StrategyAdded += OnStrategyAdded;
                _strategyManager.StrategyRemoved += OnStrategyRemoved;
                
                // Set aggregation mode from combo box
                if (AggregationModeComboBox.SelectedItem is ComboBoxItem item && 
                    item.Tag is string mode)
                {
                    _strategyManager.AggregationMode = Enum.Parse<SignalAggregationMode>(mode);
                }
            }

            UpdateEmptyState();
        }
    }

    /// <summary>
    /// Event raised when a strategy is selected.
    /// </summary>
    public event EventHandler<ManagedStrategy?>? StrategySelected;

    /// <summary>
    /// Event raised when the user wants to create a new strategy.
    /// </summary>
    public event EventHandler? NewStrategyRequested;

    private void OnStrategyAdded(object? sender, ManagedStrategy e)
    {
        Dispatcher.Invoke(UpdateEmptyState);
    }

    private void OnStrategyRemoved(object? sender, ManagedStrategy e)
    {
        Dispatcher.Invoke(UpdateEmptyState);
    }

    private void UpdateEmptyState()
    {
        var hasStrategies = _strategyManager?.Strategies.Count > 0;
        EmptyStateText.Visibility = hasStrategies ? Visibility.Collapsed : Visibility.Visible;
        StrategyListBox.Visibility = hasStrategies ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strategyManager == null) return;

        var loaderWindow = new StrategyLoaderWindow(new StrategyManagerService())
        {
            Owner = Window.GetWindow(this)
        };

        if (loaderWindow.ShowDialog() == true && loaderWindow.LoadedStrategy != null)
        {
            try
            {
                // Get the file path from the loaded strategy info
                var filePath = loaderWindow.LoadedStrategy.Name; // This is a simplification
                
                // For now, we'll add from the strategy info
                var info = new StrategyInfo
                {
                    Name = loaderWindow.LoadedStrategy.Name,
                    Description = loaderWindow.LoadedStrategy.Description,
                    Type = loaderWindow.LoadedStrategy.Type,
                    FilePath = ""
                };

                var managed = new ManagedStrategy(loaderWindow.LoadedStrategy, info);
                _strategyManager.Strategies.Add(managed);
                UpdateEmptyState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加策略失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void NewStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        NewStrategyRequested?.Invoke(this, EventArgs.Empty);
        
        // Open strategy editor window
        var editorWindow = new StrategyEditorWindow
        {
            Owner = Window.GetWindow(this)
        };

        if (editorWindow.ShowDialog() == true && editorWindow.CreatedStrategy != null)
        {
            try
            {
                var info = new StrategyInfo
                {
                    Name = editorWindow.CreatedStrategy.Name,
                    Description = editorWindow.CreatedStrategy.Description,
                    Type = editorWindow.CreatedStrategy.Type,
                    FilePath = editorWindow.SavedFilePath ?? ""
                };

                var managed = new ManagedStrategy(editorWindow.CreatedStrategy, info);
                _strategyManager?.Strategies.Add(managed);
                UpdateEmptyState();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建策略失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void RemoveStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string strategyId && _strategyManager != null)
        {
            var result = MessageBox.Show("确定要移除此策略吗?", "确认移除",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _strategyManager.RemoveStrategy(strategyId);
                UpdateEmptyState();
            }
        }
    }

    private void StrategyListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = StrategyListBox.SelectedItem as ManagedStrategy;
        StrategySelected?.Invoke(this, selected);
    }

    private void AggregationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_strategyManager == null) return;

        if (AggregationModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string mode)
        {
            _strategyManager.AggregationMode = Enum.Parse<SignalAggregationMode>(mode);
        }
    }
}
