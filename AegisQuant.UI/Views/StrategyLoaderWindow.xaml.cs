using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using AegisQuant.UI.Services;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Strategy.Loaders;
using AegisQuant.UI.Strategy.Models;
using Microsoft.Win32;

namespace AegisQuant.UI.Views;

/// <summary>
/// Strategy loader dialog window.
/// Allows users to select and preview strategy files before loading.
/// </summary>
public partial class StrategyLoaderWindow : Window
{
    private readonly StrategyManagerService _strategyManager;
    private StrategyInfo? _selectedStrategyInfo;
    private string? _selectedFilePath;

    /// <summary>
    /// Gets the loaded strategy after successful load.
    /// </summary>
    public IStrategy? LoadedStrategy { get; private set; }

    public StrategyLoaderWindow(StrategyManagerService strategyManager)
    {
        InitializeComponent();
        _strategyManager = strategyManager;

        // Load recent strategies
        RecentStrategiesListBox.ItemsSource = _strategyManager.RecentStrategies;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Strategy Files (*.json;*.py)|*.json;*.py|JSON Strategies (*.json)|*.json|Python Strategies (*.py)|*.py|All Files (*.*)|*.*",
            Title = "Select Strategy File"
        };

        if (dialog.ShowDialog() == true)
        {
            SelectFile(dialog.FileName);
        }
    }

    private void RecentStrategiesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RecentStrategiesListBox.SelectedItem is StrategyInfo info)
        {
            if (File.Exists(info.FilePath))
            {
                SelectFile(info.FilePath);
            }
            else
            {
                ShowError("File not found: " + info.FilePath);
            }
        }
    }

    private async void SelectFile(string filePath)
    {
        _selectedFilePath = filePath;
        FilePathTextBox.Text = filePath;
        HideError();

        try
        {
            // Get strategy info without fully loading
            var info = await _strategyManager.GetStrategyInfoAsync(filePath);

            if (info != null)
            {
                _selectedStrategyInfo = info;
                ShowPreview(info);
                LoadButton.IsEnabled = true;
            }
            else
            {
                // Try to load and validate
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension == ".json")
                {
                    var loader = new JsonStrategyLoader();
                    var strategy = loader.LoadFromFile(filePath);
                    
                    _selectedStrategyInfo = new StrategyInfo
                    {
                        Name = strategy.Name,
                        Description = strategy.Description,
                        FilePath = filePath,
                        Type = strategy.Type,
                        Parameters = strategy.Parameters.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new ParameterInfo { Name = kvp.Key, CurrentValue = kvp.Value })
                    };
                    
                    ShowPreview(_selectedStrategyInfo);
                    LoadButton.IsEnabled = true;
                    strategy.Dispose();
                }
                else
                {
                    // Python or other - show basic info
                    _selectedStrategyInfo = new StrategyInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(filePath),
                        Description = "Python strategy",
                        FilePath = filePath,
                        Type = StrategyType.PythonScript
                    };
                    ShowPreview(_selectedStrategyInfo);
                    LoadButton.IsEnabled = true;
                }
            }
        }
        catch (StrategyLoadException ex)
        {
            ShowError($"Invalid strategy file (line {ex.LineNumber}): {ex.Message}");
            LoadButton.IsEnabled = false;
            HidePreview();
        }
        catch (Exception ex)
        {
            ShowError($"Error reading file: {ex.Message}");
            LoadButton.IsEnabled = false;
            HidePreview();
        }
    }

    private void ShowPreview(StrategyInfo info)
    {
        PreviewPanel.Visibility = Visibility.Visible;
        NoPreviewText.Visibility = Visibility.Collapsed;

        StrategyNameText.Text = info.Name;
        StrategyTypeText.Text = info.Type switch
        {
            StrategyType.JsonConfig => "JSON Configuration",
            StrategyType.PythonScript => "Python Script",
            StrategyType.BuiltIn => "Built-in",
            _ => "Unknown"
        };
        StrategyDescriptionText.Text = string.IsNullOrEmpty(info.Description) 
            ? "(No description)" 
            : info.Description;

        // Show parameters
        var paramList = info.Parameters
            .Select(kvp => new KeyValuePair<string, string>(
                kvp.Key, 
                kvp.Value.EffectiveValue?.ToString() ?? ""))
            .ToList();
        
        ParametersItemsControl.ItemsSource = paramList;
    }

    private void HidePreview()
    {
        PreviewPanel.Visibility = Visibility.Collapsed;
        NoPreviewText.Visibility = Visibility.Visible;
        _selectedStrategyInfo = null;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorBorder.Visibility = Visibility.Collapsed;
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath))
            return;

        try
        {
            LoadButton.IsEnabled = false;
            LoadButton.Content = "Loading...";

            LoadedStrategy = await _strategyManager.LoadFromFileAsync(_selectedFilePath);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load strategy: {ex.Message}");
            LoadButton.IsEnabled = true;
            LoadButton.Content = FindResource("String.Strategy.Load") ?? "Load";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
