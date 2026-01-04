using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AegisQuant.UI.Models;
using AegisQuant.UI.Services;
using Microsoft.Win32;

namespace AegisQuant.UI.Views;

/// <summary>
/// Import wizard dialog for CSV data files.
/// Provides preview, column mapping, and date format detection.
/// Implements Requirements 15.1, 15.2, 15.3, 15.6.
/// </summary>
public partial class ImportWizardWindow : Window
{
    private readonly ColumnMappingDetector _detector = new();
    private DataImportConfig _config = new();
    private List<string[]> _previewRows = new();
    private string[] _headers = Array.Empty<string>();
    private ObservableCollection<ColumnMappingViewModel> _columnMappings = new();

    /// <summary>
    /// Gets the resulting import configuration after successful import.
    /// </summary>
    public DataImportConfig? Result { get; private set; }

    /// <summary>
    /// Gets the path to the imported/converted file.
    /// </summary>
    public string? ImportedFilePath => Result?.FilePath;

    public ImportWizardWindow()
    {
        InitializeComponent();
        ColumnMappingPanel.ItemsSource = _columnMappings;
        DateFormatComboBox.SelectedIndex = 0; // Auto detect
    }

    /// <summary>
    /// Opens the wizard with a pre-selected file path.
    /// </summary>
    public ImportWizardWindow(string filePath) : this()
    {
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            _ = LoadPreviewAsync(filePath);
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "所有支持格式 (*.csv;*.xlsx;*.xls)|*.csv;*.xlsx;*.xls|Excel 文件 (*.xlsx;*.xls)|*.xlsx;*.xls|CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            Title = "选择数据文件"
        };

        if (dialog.ShowDialog() == true)
        {
            _ = LoadPreviewAsync(dialog.FileName);
        }
    }

    /// <summary>
    /// Loads preview data from the specified file (CSV or Excel).
    /// Only reads the first 5 rows for preview.
    /// </summary>
    public async Task LoadPreviewAsync(string filePath)
    {
        _config.FilePath = filePath;
        FilePathTextBox.Text = filePath;
        HideError();

        try
        {
            _previewRows.Clear();
            
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            if (extension == ".xlsx" || extension == ".xls")
            {
                // Excel 文件处理
                await LoadExcelPreviewAsync(filePath);
            }
            else
            {
                // CSV 文件处理
                await LoadCsvPreviewAsync(filePath);
            }

            // Auto-detect column mappings
            AutoDetectMappings();

            // Update preview grid
            UpdatePreviewGrid();

            // Enable import button
            ImportButton.IsEnabled = true;
            NoDataText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError($"读取文件错误: {ex.Message}");
            ImportButton.IsEnabled = false;
        }
    }

    private async Task LoadCsvPreviewAsync(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var headerLine = await reader.ReadLineAsync();
        
        if (string.IsNullOrEmpty(headerLine))
        {
            ShowError("文件为空或没有标题行。");
            return;
        }

        _headers = ParseCsvLine(headerLine);

        for (int i = 0; i < 5 && !reader.EndOfStream; i++)
        {
            var line = await reader.ReadLineAsync();
            if (!string.IsNullOrEmpty(line))
                _previewRows.Add(ParseCsvLine(line));
        }
    }

    private async Task LoadExcelPreviewAsync(string filePath)
    {
        await Task.Run(() =>
        {
            var excelService = new ExcelDataImportService();
            var (headers, rows) = excelService.ReadExcelPreview(filePath, 5);
            _headers = headers;
            _previewRows = rows;
        });
        
        if (_headers.Length == 0)
        {
            ShowError("Excel 文件为空或无法读取。");
        }
    }

    private string[] ParseCsvLine(string line)
    {
        // Simple CSV parsing (handles basic cases)
        var result = new List<string>();
        var inQuotes = false;
        var currentField = "";

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField.Trim());
                currentField = "";
            }
            else
            {
                currentField += c;
            }
        }
        result.Add(currentField.Trim());

        return result.ToArray();
    }

    private void AutoDetectMappings()
    {
        _columnMappings.Clear();
        var detectedMappings = _detector.DetectColumnMappings(_headers);

        foreach (var header in _headers)
        {
            var mapping = new ColumnMappingViewModel
            {
                ColumnName = header,
                SelectedMapping = detectedMappings.TryGetValue(header, out var type) 
                    ? type.ToString() 
                    : "Ignore"
            };
            _columnMappings.Add(mapping);
        }

        // Auto-detect date format from first data row
        if (_previewRows.Count > 0)
        {
            var timeColumnIndex = Array.FindIndex(_headers, h => 
                detectedMappings.TryGetValue(h, out var t) && t == ColumnMappingType.Timestamp);
            
            if (timeColumnIndex >= 0 && timeColumnIndex < _previewRows[0].Length)
            {
                var sampleDate = _previewRows[0][timeColumnIndex];
                var detectedFormat = _detector.DetectDateFormat(sampleDate);
                _config.DateFormat = detectedFormat;
                
                // Update UI
                SelectDateFormat(detectedFormat);
                DetectedFormatText.Text = $"(Detected: {detectedFormat})";
            }
        }
    }

    private void SelectDateFormat(string format)
    {
        foreach (ComboBoxItem item in DateFormatComboBox.Items)
        {
            if (item.Tag?.ToString() == format)
            {
                DateFormatComboBox.SelectedItem = item;
                return;
            }
        }
        // Default to auto if not found
        DateFormatComboBox.SelectedIndex = 0;
    }

    private void UpdatePreviewGrid()
    {
        PreviewDataGrid.Columns.Clear();
        PreviewDataGrid.ItemsSource = null;

        if (_headers.Length == 0 || _previewRows.Count == 0)
            return;

        // Create columns dynamically
        for (int i = 0; i < _headers.Length; i++)
        {
            var column = new DataGridTextColumn
            {
                Header = _headers[i],
                Binding = new Binding($"[{i}]")
            };
            PreviewDataGrid.Columns.Add(column);
        }

        // Set data source
        PreviewDataGrid.ItemsSource = _previewRows;
    }

    private void MappingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update config when mapping changes
        UpdateConfigFromMappings();
    }

    private void DateFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DateFormatComboBox.SelectedItem is ComboBoxItem item)
        {
            _config.DateFormat = item.Tag?.ToString() ?? "auto";
        }
    }

    private void UpdateConfigFromMappings()
    {
        _config.IgnoredColumns.Clear();

        for (int i = 0; i < _columnMappings.Count; i++)
        {
            var mapping = _columnMappings[i];
            var columnName = mapping.ColumnName;

            switch (mapping.SelectedMapping)
            {
                case "Timestamp":
                    _config.TimeColumnName = columnName;
                    break;
                case "Open":
                    _config.OpenColumnName = columnName;
                    break;
                case "High":
                    _config.HighColumnName = columnName;
                    break;
                case "Low":
                    _config.LowColumnName = columnName;
                    break;
                case "Close":
                    _config.CloseColumnName = columnName;
                    break;
                case "Volume":
                    _config.VolumeColumnName = columnName;
                    break;
                case "Ignore":
                    _config.IgnoredColumns.Add(columnName);
                    break;
            }
        }
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

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateConfigFromMappings();
        
        // Validate required columns
        if (string.IsNullOrEmpty(_config.TimeColumnName))
        {
            ShowError("Please map a column to Timestamp.");
            return;
        }

        if (string.IsNullOrEmpty(_config.CloseColumnName))
        {
            ShowError("Please map a column to Close price.");
            return;
        }

        Result = _config;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>
/// ViewModel for column mapping in the import wizard.
/// </summary>
public class ColumnMappingViewModel : INotifyPropertyChanged
{
    private string _columnName = string.Empty;
    private string _selectedMapping = "Ignore";

    public string ColumnName
    {
        get => _columnName;
        set
        {
            _columnName = value;
            OnPropertyChanged(nameof(ColumnName));
        }
    }

    public string SelectedMapping
    {
        get => _selectedMapping;
        set
        {
            _selectedMapping = value;
            OnPropertyChanged(nameof(SelectedMapping));
        }
    }

    public List<string> MappingOptions { get; } = new()
    {
        "Ignore",
        "Timestamp",
        "Open",
        "High",
        "Low",
        "Close",
        "Volume"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
