using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Services;

namespace AegisQuant.UI.Views;

/// <summary>
/// 审计日志查看窗口
/// Requirements: 12.5
/// </summary>
public partial class AuditLogWindow : Window
{
    private DateTime? _startDate;
    private DateTime? _endDate;
    private AuditActionType? _selectedActionType;
    private List<AuditLogEntry> _currentLogs = new();

    public AuditLogWindow()
    {
        InitializeComponent();

        // 默认显示今天的日志
        StartDatePicker.SelectedDate = DateTime.Today;
        EndDatePicker.SelectedDate = DateTime.Today;

        // 加载初始数据
        LoadLogs();
    }

    private void LoadLogs()
    {
        _startDate = StartDatePicker.SelectedDate;
        _endDate = EndDatePicker.SelectedDate?.AddDays(1).AddSeconds(-1); // 包含结束日期的全天

        _currentLogs = AuditLogService.Instance.QueryLogs(
            _startDate,
            _endDate,
            _selectedActionType,
            null
        );

        LogDataGrid.ItemsSource = _currentLogs;
        RecordCountText.Text = $"共 {_currentLogs.Count} 条记录";
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        LoadLogs();
    }

    private void ActionTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionTypeFilter.SelectedItem is ComboBoxItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            if (string.IsNullOrEmpty(tag))
            {
                _selectedActionType = null;
            }
            else if (Enum.TryParse<AuditActionType>(tag, out var actionType))
            {
                _selectedActionType = actionType;
            }
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadLogs();
    }

    private void ExportJsonButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = "导出审计日志",
            FileName = $"audit_log_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                AuditLogService.Instance.ExportToJson(dialog.FileName, _currentLogs);
                MessageBox.Show($"已导出 {_currentLogs.Count} 条记录到:\n{dialog.FileName}", 
                    "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", 
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            Title = "导出审计日志",
            FileName = $"audit_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var lines = new List<string>
                {
                    "时间,用户,操作类型,详情,旧值,新值"
                };

                foreach (var log in _currentLogs)
                {
                    var line = $"\"{log.FormattedTimestamp}\",\"{log.Username}\",\"{log.ActionTypeDisplay}\",\"{EscapeCsv(log.Details)}\",\"{EscapeCsv(log.OldValue)}\",\"{EscapeCsv(log.NewValue)}\"";
                    lines.Add(line);
                }

                File.WriteAllLines(dialog.FileName, lines, System.Text.Encoding.UTF8);
                MessageBox.Show($"已导出 {_currentLogs.Count} 条记录到:\n{dialog.FileName}", 
                    "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", 
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace("\"", "\"\"");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
