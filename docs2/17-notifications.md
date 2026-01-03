# 消息通知详解

## 概述

消息通知是交易系统的重要组成部分，用于在关键事件发生时及时通知交易员。本文档详细说明如何实现多渠道通知系统，包括应用内通知、钉钉、飞书等。

## 问题分析

### 无通知的风险

1. **错过关键事件**: 风控熔断、大额成交等
2. **响应延迟**: 无法及时处理异常
3. **监控盲区**: 离开电脑时无法获知状态
4. **信息过载**: 所有消息同等对待

### 设计目标

- 支持多种通知类型 (风控、成交、预警等)
- 支持多渠道推送 (应用内、钉钉、飞书)
- 支持静默时段设置
- 支持按类型启用/禁用


## 解决方案

### 通知类型定义

```csharp
/// <summary>
/// 通知类型
/// </summary>
public enum NotificationType
{
    RiskCircuitBreaker,  // 风控熔断
    OrderFilled,         // 订单成交
    DrawdownWarning,     // 回撤预警
    SystemError,         // 系统错误
    EmergencyStop,       // 紧急停止
    Info                 // 一般信息
}

/// <summary>
/// 通知渠道类型
/// </summary>
public enum NotificationChannel
{
    InApp,      // 应用内通知
    DingTalk,   // 钉钉
    Feishu,     // 飞书
    Telegram,   // Telegram
    Email       // 邮件
}
```

### 通知服务实现

```csharp
// NotificationService.cs
public class NotificationService : INotifyPropertyChanged, IDisposable
{
    private readonly ObservableCollection<NotificationRecord> _history = new();
    private readonly Dictionary<NotificationChannel, INotificationChannel> _channels = new();
    private readonly Dictionary<NotificationType, bool> _typeEnabled = new();
    private TimeSpan _silentStart = TimeSpan.FromHours(22);
    private TimeSpan _silentEnd = TimeSpan.FromHours(8);
    private bool _silentModeEnabled;

    public ObservableCollection<NotificationRecord> History => _history;

    /// <summary>
    /// 发送通知
    /// </summary>
    public async Task SendNotificationAsync(NotificationType type, string title, string message)
    {
        // 检查是否启用该类型
        if (!IsTypeEnabled(type))
            return;

        // 检查静默时段 (紧急停止除外)
        if (IsInSilentPeriod() && type != NotificationType.EmergencyStop)
            return;

        var record = new NotificationRecord
        {
            Timestamp = DateTime.Now,
            Type = type,
            Title = title,
            Message = message
        };

        // 发送到所有启用的渠道
        foreach (var channel in _channels.Values)
        {
            try
            {
                await channel.SendAsync(title, message, type);
            }
            catch { }
        }

        // 添加到历史记录
        _history.Insert(0, record);
        while (_history.Count > 1000)
            _history.RemoveAt(_history.Count - 1);
    }
}
```

### 静默时段检查

```csharp
private bool IsInSilentPeriod()
{
    if (!_silentModeEnabled)
        return false;

    var now = DateTime.Now.TimeOfDay;
    
    // 处理跨午夜的情况
    if (_silentStart < _silentEnd)
    {
        return now >= _silentStart && now < _silentEnd;
    }
    else
    {
        return now >= _silentStart || now < _silentEnd;
    }
}
```

### 渠道接口

```csharp
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
/// 渠道配置
/// </summary>
public class ChannelConfig
{
    public NotificationChannel Channel { get; set; }
    public bool IsEnabled { get; set; }
    public string? WebhookUrl { get; set; }
    public string? ApiKey { get; set; }
    public Dictionary<string, string> ExtraParams { get; set; } = new();
}
```

### 钉钉渠道实现

```csharp
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
```

### 飞书渠道实现

```csharp
/// <summary>
/// 飞书通知渠道
/// </summary>
public class FeishuNotificationChannel : INotificationChannel
{
    private string? _webhookUrl;
    private readonly HttpClient _httpClient = new();

    public NotificationChannel ChannelType => NotificationChannel.Feishu;

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
```

### 便捷通知方法

```csharp
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
        $"标的: {symbol}\n方向: {direction}\n数量: {quantity}\n价格: {price:F2}"
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
```

## 使用示例

```csharp
// 配置钉钉渠道
NotificationService.Instance.ConfigureChannel(new ChannelConfig
{
    Channel = NotificationChannel.DingTalk,
    IsEnabled = true,
    WebhookUrl = "https://oapi.dingtalk.com/robot/send?access_token=xxx"
});

// 设置静默时段 (22:00 - 08:00)
NotificationService.Instance.SilentModeEnabled = true;
NotificationService.Instance.SilentStart = TimeSpan.FromHours(22);
NotificationService.Instance.SilentEnd = TimeSpan.FromHours(8);

// 禁用成交通知
NotificationService.Instance.SetTypeEnabled(NotificationType.OrderFilled, false);

// 发送通知
await NotificationService.Instance.NotifyRiskCircuitBreaker("日内亏损超过 5%");
await NotificationService.Instance.NotifyOrderFilled("BTCUSDT", "买入", 0.5, 42000);
```

## 面试话术

### Q: 为什么紧急停止不受静默时段限制？

**A**: 安全优先：
1. **紧急事件**: 紧急停止是最高优先级事件
2. **及时响应**: 必须立即通知交易员
3. **风险控制**: 不能因为静默而错过关键警报

### Q: 如何处理通知发送失败？

**A**: 容错设计：
1. **独立发送**: 单个渠道失败不影响其他渠道
2. **记录历史**: 所有通知都记录到历史列表
3. **重试机制**: 可以实现指数退避重试

### Q: 为什么使用 Webhook 而不是 SDK？

**A**: 简单可靠：
1. **无依赖**: 不需要引入第三方 SDK
2. **通用性**: HTTP POST 适用于所有平台
3. **易维护**: 只需要维护 URL 配置
