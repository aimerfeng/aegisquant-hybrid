using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AegisQuant.Interop;
using AegisQuant.UI.Services;

namespace AegisQuant.UI.Controls;

/// <summary>
/// 紧急控制面板
/// Requirements: 16.1, 16.2, 16.5
/// </summary>
public partial class EmergencyControlPanel : UserControl
{
    private bool _isHalted;

    /// <summary>
    /// 是否处于紧急停止状态
    /// </summary>
    public bool IsHalted
    {
        get => _isHalted;
        private set
        {
            _isHalted = value;
            UpdateUI();
        }
    }

    /// <summary>
    /// 紧急停止事件
    /// </summary>
    public event EventHandler? EmergencyStopTriggered;

    /// <summary>
    /// 一键清仓事件
    /// </summary>
    public event EventHandler? CloseAllTriggered;

    /// <summary>
    /// 恢复交易事件
    /// </summary>
    public event EventHandler? ResumeTriggered;

    public EmergencyControlPanel()
    {
        InitializeComponent();
        UpdateUI();
    }

    private void EmergencyStopButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要紧急停止所有自动交易吗？\n\n这将立即停止所有策略信号生成。",
            "紧急停止确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            TriggerEmergencyStop();
        }
    }

    private void CloseAllButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要一键清仓吗？\n\n这将平掉所有持仓，可能产生损失。",
            "一键清仓确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            TriggerCloseAll();
        }
    }

    private void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要恢复自动交易吗？",
            "恢复交易确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            TriggerResume();
        }
    }

    private void TriggerEmergencyStop()
    {
        try
        {
            // 调用 Rust 侧紧急停止
            NativeMethods.EmergencyStop();
            
            IsHalted = true;
            
            // 记录审计日志
            AuditLogService.Instance.LogEmergencyStop("用户触发紧急停止");
            
            EmergencyStopTriggered?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"紧急停止失败: {ex.Message}", "错误", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TriggerCloseAll()
    {
        try
        {
            // 记录审计日志
            AuditLogService.Instance.LogOrderAction("一键清仓", "用户触发一键清仓");
            
            CloseAllTriggered?.Invoke(this, EventArgs.Empty);
            
            MessageBox.Show("清仓指令已发送", "提示", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"清仓失败: {ex.Message}", "错误", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TriggerResume()
    {
        try
        {
            // 调用 Rust 侧重置紧急停止
            NativeMethods.ResetEmergencyStop();
            
            IsHalted = false;
            
            // 记录审计日志
            AuditLogService.Instance.Log(AuditActionType.Other, "用户恢复自动交易");
            
            ResumeTriggered?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"恢复失败: {ex.Message}", "错误", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateUI()
    {
        if (_isHalted)
        {
            // 紧急停止状态 - 红色边框和背景
            MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x14, 0x3C));
            MainBorder.BorderThickness = new Thickness(3);
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(0xDC, 0x14, 0x3C));
            StatusText.Text = "⚠ 紧急停止中";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x14, 0x3C));
            
            EmergencyStopButton.IsEnabled = false;
            ResumeButton.Visibility = Visibility.Visible;
        }
        else
        {
            // 正常状态 - 绿色
            MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
            MainBorder.BorderThickness = new Thickness(1);
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            StatusText.Text = "自动交易运行中";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            
            EmergencyStopButton.IsEnabled = true;
            ResumeButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 外部设置停止状态
    /// </summary>
    public void SetHaltedState(bool halted)
    {
        IsHalted = halted;
    }
}
