using System.Windows;
using AegisQuant.UI.Services;

namespace AegisQuant.UI.Views;

/// <summary>
/// 通知设置窗口
/// Requirements: 17.5, 17.6, 17.7
/// </summary>
public partial class NotificationSettingsWindow : Window
{
    public NotificationSettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
        HistoryDataGrid.ItemsSource = NotificationService.Instance.History;
    }

    private void LoadSettings()
    {
        var service = NotificationService.Instance;
        
        SilentModeEnabled.IsChecked = service.SilentModeEnabled;
        
        // 设置静默时段
        var startHour = (int)service.SilentStart.TotalHours;
        var endHour = (int)service.SilentEnd.TotalHours;
        
        foreach (System.Windows.Controls.ComboBoxItem item in SilentStartHour.Items)
        {
            if (item.Content?.ToString()?.StartsWith($"{startHour:D2}:") == true)
            {
                SilentStartHour.SelectedItem = item;
                break;
            }
        }
        
        foreach (System.Windows.Controls.ComboBoxItem item in SilentEndHour.Items)
        {
            if (item.Content?.ToString()?.StartsWith($"{endHour:D2}:") == true)
            {
                SilentEndHour.SelectedItem = item;
                break;
            }
        }
        
        // 加载通知类型开关
        RiskNotifyEnabled.IsChecked = service.IsTypeEnabled(NotificationType.RiskCircuitBreaker);
        OrderNotifyEnabled.IsChecked = service.IsTypeEnabled(NotificationType.OrderFilled);
        DrawdownNotifyEnabled.IsChecked = service.IsTypeEnabled(NotificationType.DrawdownWarning);
    }

    private void SaveSettings()
    {
        var service = NotificationService.Instance;
        
        service.SilentModeEnabled = SilentModeEnabled.IsChecked == true;
        
        // 解析静默时段
        if (SilentStartHour.SelectedItem is System.Windows.Controls.ComboBoxItem startItem)
        {
            var startStr = startItem.Content?.ToString();
            if (!string.IsNullOrEmpty(startStr) && int.TryParse(startStr.Split(':')[0], out var hour))
            {
                service.SilentStart = TimeSpan.FromHours(hour);
            }
        }
        
        if (SilentEndHour.SelectedItem is System.Windows.Controls.ComboBoxItem endItem)
        {
            var endStr = endItem.Content?.ToString();
            if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr.Split(':')[0], out var hour))
            {
                service.SilentEnd = TimeSpan.FromHours(hour);
            }
        }
        
        // 保存通知类型开关
        service.SetTypeEnabled(NotificationType.RiskCircuitBreaker, RiskNotifyEnabled.IsChecked == true);
        service.SetTypeEnabled(NotificationType.OrderFilled, OrderNotifyEnabled.IsChecked == true);
        service.SetTypeEnabled(NotificationType.DrawdownWarning, DrawdownNotifyEnabled.IsChecked == true);
        
        // 配置钉钉渠道
        if (DingTalkEnabled.IsChecked == true && !string.IsNullOrEmpty(DingTalkWebhook.Text))
        {
            var dingTalk = new DingTalkNotificationChannel();
            dingTalk.Configure(new ChannelConfig
            {
                Channel = NotificationChannel.DingTalk,
                IsEnabled = true,
                WebhookUrl = DingTalkWebhook.Text
            });
            service.RegisterChannel(dingTalk);
        }
        
        // 配置飞书渠道
        if (FeishuEnabled.IsChecked == true && !string.IsNullOrEmpty(FeishuWebhook.Text))
        {
            var feishu = new FeishuNotificationChannel();
            feishu.Configure(new ChannelConfig
            {
                Channel = NotificationChannel.Feishu,
                IsEnabled = true,
                WebhookUrl = FeishuWebhook.Text
            });
            service.RegisterChannel(feishu);
        }
        
        service.SaveSettings();
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        await NotificationService.Instance.SendNotificationAsync(
            NotificationType.Info,
            "测试通知",
            "这是一条测试通知消息"
        );
        
        MessageBox.Show("测试通知已发送", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
