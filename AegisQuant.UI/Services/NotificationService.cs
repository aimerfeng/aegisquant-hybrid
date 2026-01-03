using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AegisQuant.UI.Services;

/// <summary>
/// 通知类型
/// </summary>
public enum NotificationType
{
    /// <summary>风控熔断</summary>
    RiskCircuitBreaker,
    /// <summary>订单成交</summary>
    OrderFilled,
    /// <summary>回撤预警</summary>
    DrawdownWarning,
    /// <summary>系统错误</summary>
    SystemError,
    /// <summary>紧急停止</summary>
    EmergencyStop,
    /// <summary>一般信息</summary>
    Info
}

/// <summary>
/// 通知渠道类型
/// </summary>
public enum NotificationChannel
{
    /// <summary>应用内通知</summary>
    InApp,
    /// <summary>钉钉</summary>
    DingTalk,
    /// <summary>飞书</summary>
    Feishu,
    /// <summary>Telegram</summary>
    Telegram,
    /// <summary>邮件</summary>
    Email
}

/// <summary>
/// 通知记录
/// </summary>
public class NotificationRecord
{
    public DateTime Timestamp { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsSent { get; set; }
    public string? Error { get; set; }

    public string TypeDisplay => Type switch
    {
        NotificationType.RiskCircuitBreaker => "风控熔断",
        NotificationType.OrderFilled => "订单成交",
        NotificationType.DrawdownWarning => "回撤预警",
        NotificationType.SystemError => "系统错误",
        NotificationType.EmergencyStop => "紧急停止",
        _ => "信息"
    };

    public string FormattedTime => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>
/// 通知渠道配置
/// </summary>
public class ChannelConfig
{
    public NotificationChannel Channel { get; set; }
    public bool IsEnabled { get; set; }
    public string? WebhookUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? Secret { get; set; }
    public Dictionary<string, string> ExtraParams { get; set; } = new();
}

/// <summary>
/// 通知渠道接口
/// </summary>
public interface INotificationChannel
{
    NotificationChannel ChannelType { get; }
    Task<bool> SendAsync(string title, string message, NotificationType type);
    void Configure(ChannelConfig config);
}

/// <summary>
/// 消息通知服务
/// Requirements: 17.1, 17.2, 17.3, 17.4
/// </summary>
public class NotificationService : INotifyPropertyChanged, IDisposable
{
    private static NotificationService? _instance;
    private static readonly object _lock = new();

    public static NotificationService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new NotificationService();
                }
            }
            return _instance;
        }
    }

    private readonly ObservableCollection<NotificationRecord> _history = new();
    private readonly Dictionary<NotificationChannel, INotificationChannel> _channels = new();
    private readonly Dictionary<NotificationType, bool> _typeEnabled = new();
    private TimeSpan _silentStart = TimeSpan.FromHours(22);
    private TimeSpan _silentEnd = TimeSpan.FromHours(8);
    private bool _silentModeEnabled;
    private bool _disposed;

    public ObservableCollection<NotificationRecord> History => _history;

    public bool SilentModeEnabled
    {
        get => _silentModeEnabled;
        set { _silentModeEnabled = value; OnPropertyChanged(); }
    }

    public TimeSpan SilentStart
    {
        get => _silentStart;
        set { _silentStart = value; OnPropertyChanged(); }
    }

    public TimeSpan SilentEnd
    {
        get => _silentEnd;
        set { _silentEnd = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private NotificationService()
    {
        // 默认启用所有通知类型
        foreach (NotificationType type in Enum.GetValues<NotificationType>())
        {
            _typeEnabled[type] = true;
        }

        // 注册内置渠道
        RegisterChannel(new InAppNotificationChannel());
        
        LoadSettings();
    }

    public void RegisterChannel(INotificationChannel channel)
    {
        _channels[channel.ChannelType] = channel;
    }

    public void ConfigureChannel(ChannelConfig config)
    {
        if (_channels.TryGetValue(config.Channel, out var channel))
        {
            channel.Configure(config);
        }
    }

    public void SetTypeEnabled(NotificationType type, bool enabled)
    {
        _typeEnabled[type] = enabled;
    }

    public bool IsTypeEnabled(NotificationType type)
    {
        return _typeEnabled.GetValueOrDefault(type, true);
    }

    /// <summary>
    /// 发送风控熔断通知
    /// </summary>
    public async Task NotifyRiskCircuitBreaker(string reason)
    {
        await SendNotificationAsync(
            NotificationType.RiskCircuitBreaker,
            "⚠️ 风控熔断",
            $"风控系统已触发熔断\n原因: {reason}\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        );
    }

    /// <summary>
    /// 发送订单成交通知
    /// </summary>
    public async Task NotifyOrderFilled(string symbol, string direction, double quantity, double price)
    {
        await SendNotificationAsync(
            NotificationType.OrderFilled,
            "📈 订单成交",
            $"标的: {symbol}\n方向: {direction}\n数量: {quantity}\n价格: {price:F2}\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        );
    }

    /// <summary>
    /// 发送回撤预警通知
    /// </summary>
    public async Task NotifyDrawdownWarning(double currentDrawdown, double threshold)
    {
        await SendNotificationAsync(
            NotificationType.DrawdownWarning,
            "⚠️ 回撤预警",
            $"当前回撤: {currentDrawdown:F2}%\n预警阈值: {threshold:F2}%\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        );
    }

    /// <summary>
    /// 发送紧急停止通知
    /// </summary>
    public async Task NotifyEmergencyStop(string reason)
    {
        await SendNotificationAsync(
            NotificationType.EmergencyStop,
            "🚨 紧急停止",
            $"系统已紧急停止\n原因: {reason}\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        );
    }

    /// <summary>
    /// 发送通知
    /// </summary>
    public async Task SendNotificationAsync(NotificationType type, string title, string message)
    {
        // 检查是否启用该类型
        if (!IsTypeEnabled(type))
            return;

        // 检查静默时段
        if (IsInSilentPeriod() && type != NotificationType.EmergencyStop)
            return;

        var record = new NotificationRecord
        {
            Timestamp = DateTime.Now,
            Type = type,
            Title = title,
            Message = message
        };

        try
        {
            // 发送到所有启用的渠道
            foreach (var channel in _channels.Values)
            {
                try
                {
                    await channel.SendAsync(title, message, type);
                }
                catch
                {
                    // 单个渠道失败不影响其他渠道
                }
            }
            record.IsSent = true;
        }
        catch (Exception ex)
        {
            record.Error = ex.Message;
        }

        // 添加到历史记录
        _history.Insert(0, record);
        while (_history.Count > 1000)
        {
            _history.RemoveAt(_history.Count - 1);
        }
    }

    private bool IsInSilentPeriod()
    {
        if (!_silentModeEnabled)
            return false;

        var now = DateTime.Now.TimeOfDay;
        
        if (_silentStart < _silentEnd)
        {
            return now >= _silentStart && now < _silentEnd;
        }
        else
        {
            return now >= _silentStart || now < _silentEnd;
        }
    }

    private void LoadSettings()
    {
        try
        {
            var path = GetSettingsPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<NotificationSettings>(json);
                if (settings != null)
                {
                    _silentModeEnabled = settings.SilentModeEnabled;
                    _silentStart = TimeSpan.FromHours(settings.SilentStartHour);
                    _silentEnd = TimeSpan.FromHours(settings.SilentEndHour);
                }
            }
        }
        catch { }
    }

    public void SaveSettings()
    {
        try
        {
            var settings = new NotificationSettings
            {
                SilentModeEnabled = _silentModeEnabled,
                SilentStartHour = _silentStart.TotalHours,
                SilentEndHour = _silentEnd.TotalHours
            };
            var json = JsonSerializer.Serialize(settings);
            var path = GetSettingsPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, json);
        }
        catch { }
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AegisQuant", "notification_settings.json"
        );
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            SaveSettings();
            _disposed = true;
        }
    }
}

internal class NotificationSettings
{
    public bool SilentModeEnabled { get; set; }
    public double SilentStartHour { get; set; }
    public double SilentEndHour { get; set; }
}

/// <summary>
/// 应用内通知渠道
/// </summary>
internal class InAppNotificationChannel : INotificationChannel
{
    public NotificationChannel ChannelType => NotificationChannel.InApp;

    public void Configure(ChannelConfig config) { }

    public Task<bool> SendAsync(string title, string message, NotificationType type)
    {
        // 应用内通知已通过 History 记录
        return Task.FromResult(true);
    }
}

/// <summary>
/// 钉钉通知渠道
/// </summary>
public class DingTalkNotificationChannel : INotificationChannel
{
    private string? _webhookUrl;
    private readonly HttpClient _httpClient = new();

    public NotificationChannel ChannelType => NotificationChannel.DingTalk;

    public void Configure(ChannelConfig config)
    {
        _webhookUrl = config.WebhookUrl;
    }

    public async Task<bool> SendAsync(string title, string message, NotificationType type)
    {
        if (string.IsNullOrEmpty(_webhookUrl))
            return false;

        var payload = new
        {
            msgtype = "text",
            text = new { content = $"【{title}】\n{message}" }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_webhookUrl, content);
        return response.IsSuccessStatusCode;
    }
}

/// <summary>
/// 飞书通知渠道
/// </summary>
public class FeishuNotificationChannel : INotificationChannel
{
    private string? _webhookUrl;
    private readonly HttpClient _httpClient = new();

    public NotificationChannel ChannelType => NotificationChannel.Feishu;

    public void Configure(ChannelConfig config)
    {
        _webhookUrl = config.WebhookUrl;
    }

    public async Task<bool> SendAsync(string title, string message, NotificationType type)
    {
        if (string.IsNullOrEmpty(_webhookUrl))
            return false;

        var payload = new
        {
            msg_type = "text",
            content = new { text = $"【{title}】\n{message}" }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_webhookUrl, content);
        return response.IsSuccessStatusCode;
    }
}
