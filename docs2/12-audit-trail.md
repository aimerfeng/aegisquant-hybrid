# 审计日志详解

## 概述

审计日志是金融系统合规的核心要求，记录所有人为操作以便追溯和审查。本文档详细说明如何实现完整的审计日志系统，包括操作记录、查询和导出功能。

## 问题分析

### 缺乏审计的风险

1. **无法追溯**: 出问题时不知道谁做了什么
2. **合规风险**: 无法满足监管审计要求
3. **责任不清**: 多人操作时无法定责
4. **安全隐患**: 无法发现异常操作

### 设计目标

- 记录所有人为操作 (按钮点击、参数修改等)
- 只追加模式，防止篡改
- 支持按时间、类型、用户查询
- 支持导出为 JSON 格式


## 解决方案

### 审计操作类型

```csharp
/// <summary>
/// 审计操作类型
/// </summary>
public enum AuditActionType
{
    ButtonClick,        // 按钮点击
    ParameterChange,    // 参数修改
    EnvironmentChange,  // 环境切换
    Login,              // 登录
    Logout,             // 登出
    OrderAction,        // 订单操作
    EmergencyStop,      // 紧急停止
    ConfigChange,       // 配置变更
    DataLoad,           // 数据加载
    BacktestStart,      // 回测启动
    BacktestStop,       // 回测停止
    Other               // 其他
}
```

### 审计日志条目

```csharp
/// <summary>
/// 审计日志条目
/// </summary>
public class AuditLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Username { get; set; } = "System";
    public AuditActionType ActionType { get; set; }
    public string Details { get; set; } = string.Empty;
    public string? OldValue { get; set; }  // 参数修改时使用
    public string? NewValue { get; set; }  // 参数修改时使用
    public string SessionId { get; set; } = string.Empty;

    public override string ToString()
    {
        var valueChange = !string.IsNullOrEmpty(OldValue) || !string.IsNullOrEmpty(NewValue)
            ? $" [{OldValue} -> {NewValue}]"
            : string.Empty;
        return $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {Username} | {ActionTypeDisplay} | {Details}{valueChange}";
    }
}
```

### 审计服务实现

```csharp
// AuditLogService.cs
public class AuditLogService : INotifyPropertyChanged
{
    private readonly ObservableCollection<AuditLogEntry> _recentLogs = new();
    private string _currentUsername = "Anonymous";
    private string _sessionId;
    private const int MaxRecentLogs = 1000;
    private static readonly object _fileLock = new();

    /// <summary>
    /// 记录按钮点击事件
    /// </summary>
    public void LogButtonClick(string buttonName, string? additionalInfo = null)
    {
        var details = string.IsNullOrEmpty(additionalInfo)
            ? $"点击按钮: {buttonName}"
            : $"点击按钮: {buttonName} - {additionalInfo}";
        Log(AuditActionType.ButtonClick, details);
    }

    /// <summary>
    /// 记录参数修改事件
    /// </summary>
    public void LogParameterChange(string parameterName, object? oldValue, object? newValue)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.Now,
            Username = _currentUsername,
            ActionType = AuditActionType.ParameterChange,
            Details = $"修改参数: {parameterName}",
            OldValue = oldValue?.ToString() ?? "(null)",
            NewValue = newValue?.ToString() ?? "(null)",
            SessionId = _sessionId
        };
        WriteLog(entry);
    }

    /// <summary>
    /// 记录紧急停止
    /// </summary>
    public void LogEmergencyStop(string reason)
    {
        Log(AuditActionType.EmergencyStop, $"紧急停止: {reason}");
    }
}
```

### 只追加写入

```csharp
/// <summary>
/// 写入日志 (只追加模式)
/// </summary>
private void WriteLog(AuditLogEntry entry)
{
    // 添加到内存列表
    _recentLogs.Add(entry);
    while (_recentLogs.Count > MaxRecentLogs)
        _recentLogs.RemoveAt(0);

    // 写入文件 (只追加模式)
    lock (_fileLock)
    {
        try
        {
            var logPath = GetCurrentLogPath();
            var logLine = entry.ToString();
            File.AppendAllText(logPath, logLine + Environment.NewLine);
        }
        catch { }
    }
}

private static string GetCurrentLogPath()
{
    return Path.Combine(GetLogDirectory(), $"audit_{DateTime.Today:yyyyMMdd}.log");
}
```

### 日志查询

```csharp
/// <summary>
/// 查询审计日志
/// </summary>
public List<AuditLogEntry> QueryLogs(
    DateTime? startTime = null,
    DateTime? endTime = null,
    AuditActionType? actionType = null,
    string? username = null)
{
    var query = _recentLogs.AsEnumerable();

    if (startTime.HasValue)
        query = query.Where(e => e.Timestamp >= startTime.Value);

    if (endTime.HasValue)
        query = query.Where(e => e.Timestamp <= endTime.Value);

    if (actionType.HasValue)
        query = query.Where(e => e.ActionType == actionType.Value);

    if (!string.IsNullOrEmpty(username))
        query = query.Where(e => e.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

    return query.OrderByDescending(e => e.Timestamp).ToList();
}
```

### JSON 导出

```csharp
/// <summary>
/// 导出日志到 JSON 文件
/// </summary>
public void ExportToJson(string filePath, IEnumerable<AuditLogEntry> logs)
{
    var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions
    {
        WriteIndented = true
    });
    File.WriteAllText(filePath, json);
}
```

## 使用示例

```csharp
// 记录按钮点击
AuditLogService.Instance.LogButtonClick("启动回测", "BTCUSDT 策略");

// 记录参数修改
AuditLogService.Instance.LogParameterChange("止损比例", 0.02, 0.03);

// 记录紧急停止
AuditLogService.Instance.LogEmergencyStop("用户手动触发");

// 查询今日所有紧急停止记录
var emergencyLogs = AuditLogService.Instance.QueryLogs(
    startTime: DateTime.Today,
    actionType: AuditActionType.EmergencyStop);

// 导出日志
AuditLogService.Instance.ExportToJson("audit_export.json", emergencyLogs);
```

## 面试话术

### Q: 为什么使用只追加模式？

**A**: 防篡改是审计日志的核心要求：
1. **不可修改**: 历史记录不能被修改或删除
2. **完整性**: 保证日志的完整性和可信度
3. **合规要求**: 金融监管要求日志不可篡改

### Q: 如何保证日志的线程安全？

**A**: 使用文件锁：
```csharp
lock (_fileLock)
{
    File.AppendAllText(logPath, logLine);
}
```
内存列表使用 `ObservableCollection`，UI 绑定自动处理线程同步。

### Q: 日志文件如何管理？

**A**: 按日期分割：
- 每天一个文件: `audit_20240101.log`
- 便于按日期查询和归档
- 可配置保留天数，自动清理旧日志
