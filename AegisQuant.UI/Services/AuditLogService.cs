using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AegisQuant.UI.Services;

/// <summary>
/// 审计操作类型
/// </summary>
public enum AuditActionType
{
    /// <summary>按钮点击</summary>
    ButtonClick,
    /// <summary>参数修改</summary>
    ParameterChange,
    /// <summary>环境切换</summary>
    EnvironmentChange,
    /// <summary>登录</summary>
    Login,
    /// <summary>登出</summary>
    Logout,
    /// <summary>订单操作</summary>
    OrderAction,
    /// <summary>紧急停止</summary>
    EmergencyStop,
    /// <summary>配置变更</summary>
    ConfigChange,
    /// <summary>数据加载</summary>
    DataLoad,
    /// <summary>回测启动</summary>
    BacktestStart,
    /// <summary>回测停止</summary>
    BacktestStop,
    /// <summary>其他</summary>
    Other
}

/// <summary>
/// 审计日志条目
/// </summary>
public class AuditLogEntry
{
    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>操作者用户名</summary>
    public string Username { get; set; } = "System";
    
    /// <summary>操作类型</summary>
    public AuditActionType ActionType { get; set; }
    
    /// <summary>操作详情</summary>
    public string Details { get; set; } = string.Empty;
    
    /// <summary>修改前的值 (参数修改时使用)</summary>
    public string? OldValue { get; set; }
    
    /// <summary>修改后的值 (参数修改时使用)</summary>
    public string? NewValue { get; set; }
    
    /// <summary>客户端 IP (可选)</summary>
    public string? ClientIp { get; set; }
    
    /// <summary>会话 ID</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>格式化的时间戳</summary>
    public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");

    /// <summary>操作类型显示名称</summary>
    public string ActionTypeDisplay => ActionType switch
    {
        AuditActionType.ButtonClick => "按钮点击",
        AuditActionType.ParameterChange => "参数修改",
        AuditActionType.EnvironmentChange => "环境切换",
        AuditActionType.Login => "登录",
        AuditActionType.Logout => "登出",
        AuditActionType.OrderAction => "订单操作",
        AuditActionType.EmergencyStop => "紧急停止",
        AuditActionType.ConfigChange => "配置变更",
        AuditActionType.DataLoad => "数据加载",
        AuditActionType.BacktestStart => "回测启动",
        AuditActionType.BacktestStop => "回测停止",
        _ => "其他"
    };

    public override string ToString()
    {
        var valueChange = !string.IsNullOrEmpty(OldValue) || !string.IsNullOrEmpty(NewValue)
            ? $" [{OldValue} -> {NewValue}]"
            : string.Empty;
        return $"{FormattedTimestamp} | {Username} | {ActionTypeDisplay} | {Details}{valueChange}";
    }
}

/// <summary>
/// 审计日志服务 - 记录所有人为操作
/// Requirements: 12.1, 12.2, 12.3, 12.4, 12.6
/// </summary>
public class AuditLogService : INotifyPropertyChanged
{
    private static AuditLogService? _instance;
    private static readonly object _lock = new();
    private static readonly object _fileLock = new();

    /// <summary>
    /// 单例实例
    /// </summary>
    public static AuditLogService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new AuditLogService();
                }
            }
            return _instance;
        }
    }

    private readonly ObservableCollection<AuditLogEntry> _recentLogs = new();
    private string _currentUsername = "Anonymous";
    private string _sessionId;
    private const int MaxRecentLogs = 1000;

    /// <summary>
    /// 最近的审计日志 (内存中保留最近 1000 条)
    /// </summary>
    public ObservableCollection<AuditLogEntry> RecentLogs => _recentLogs;

    /// <summary>
    /// 当前用户名
    /// </summary>
    public string CurrentUsername
    {
        get => _currentUsername;
        set
        {
            if (_currentUsername != value)
            {
                _currentUsername = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private AuditLogService()
    {
        _sessionId = Guid.NewGuid().ToString("N")[..8];
        EnsureLogDirectory();
    }

    /// <summary>
    /// 记录按钮点击事件
    /// </summary>
    /// <param name="buttonName">按钮名称</param>
    /// <param name="additionalInfo">附加信息</param>
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
    /// <param name="parameterName">参数名称</param>
    /// <param name="oldValue">旧值</param>
    /// <param name="newValue">新值</param>
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
    /// 记录环境切换事件
    /// </summary>
    /// <param name="oldEnv">旧环境</param>
    /// <param name="newEnv">新环境</param>
    public void LogEnvironmentChange(TradingEnvironment oldEnv, TradingEnvironment newEnv)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.Now,
            Username = _currentUsername,
            ActionType = AuditActionType.EnvironmentChange,
            Details = "切换交易环境",
            OldValue = oldEnv.ToString(),
            NewValue = newEnv.ToString(),
            SessionId = _sessionId
        };

        WriteLog(entry);
    }

    /// <summary>
    /// 记录登录事件
    /// </summary>
    /// <param name="username">用户名</param>
    /// <param name="success">是否成功</param>
    public void LogLogin(string username, bool success)
    {
        _currentUsername = success ? username : "Anonymous";
        _sessionId = Guid.NewGuid().ToString("N")[..8];

        var details = success
            ? $"用户 {username} 登录成功"
            : $"用户 {username} 登录失败";

        Log(AuditActionType.Login, details);
    }

    /// <summary>
    /// 记录登出事件
    /// </summary>
    public void LogLogout()
    {
        Log(AuditActionType.Logout, $"用户 {_currentUsername} 登出");
        _currentUsername = "Anonymous";
    }

    /// <summary>
    /// 记录订单操作
    /// </summary>
    /// <param name="action">操作类型 (下单/撤单/修改)</param>
    /// <param name="orderDetails">订单详情</param>
    public void LogOrderAction(string action, string orderDetails)
    {
        Log(AuditActionType.OrderAction, $"{action}: {orderDetails}");
    }

    /// <summary>
    /// 记录紧急停止
    /// </summary>
    /// <param name="reason">原因</param>
    public void LogEmergencyStop(string reason)
    {
        Log(AuditActionType.EmergencyStop, $"紧急停止: {reason}");
    }

    /// <summary>
    /// 记录回测启动
    /// </summary>
    /// <param name="dataFile">数据文件</param>
    /// <param name="parameters">策略参数</param>
    public void LogBacktestStart(string dataFile, string parameters)
    {
        Log(AuditActionType.BacktestStart, $"启动回测 - 数据: {dataFile}, 参数: {parameters}");
    }

    /// <summary>
    /// 记录回测停止
    /// </summary>
    /// <param name="reason">停止原因</param>
    public void LogBacktestStop(string reason)
    {
        Log(AuditActionType.BacktestStop, $"停止回测: {reason}");
    }

    /// <summary>
    /// 通用日志记录
    /// </summary>
    /// <param name="actionType">操作类型</param>
    /// <param name="details">详情</param>
    public void Log(AuditActionType actionType, string details)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.Now,
            Username = _currentUsername,
            ActionType = actionType,
            Details = details,
            SessionId = _sessionId
        };

        WriteLog(entry);
    }

    /// <summary>
    /// 写入日志 (只追加模式)
    /// </summary>
    private void WriteLog(AuditLogEntry entry)
    {
        // 添加到内存列表
        _recentLogs.Add(entry);
        while (_recentLogs.Count > MaxRecentLogs)
        {
            _recentLogs.RemoveAt(0);
        }

        // 写入文件 (只追加模式)
        lock (_fileLock)
        {
            try
            {
                var logPath = GetCurrentLogPath();
                var logLine = entry.ToString();
                File.AppendAllText(logPath, logLine + Environment.NewLine);
            }
            catch
            {
                // 忽略写入失败
            }
        }
    }

    /// <summary>
    /// 查询审计日志
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="actionType">操作类型 (null 表示全部)</param>
    /// <param name="username">用户名 (null 表示全部)</param>
    /// <returns>符合条件的日志列表</returns>
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

    /// <summary>
    /// 从文件加载历史日志
    /// </summary>
    /// <param name="date">日期</param>
    /// <returns>日志列表</returns>
    public List<AuditLogEntry> LoadLogsFromFile(DateTime date)
    {
        var logs = new List<AuditLogEntry>();
        var logPath = GetLogPath(date);

        if (!File.Exists(logPath))
            return logs;

        try
        {
            var lines = File.ReadAllLines(logPath);
            foreach (var line in lines)
            {
                var entry = ParseLogLine(line);
                if (entry != null)
                    logs.Add(entry);
            }
        }
        catch
        {
            // 忽略读取失败
        }

        return logs;
    }

    /// <summary>
    /// 导出日志到 JSON 文件
    /// </summary>
    /// <param name="filePath">导出路径</param>
    /// <param name="logs">日志列表</param>
    public void ExportToJson(string filePath, IEnumerable<AuditLogEntry> logs)
    {
        var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(filePath, json);
    }

    private static AuditLogEntry? ParseLogLine(string line)
    {
        try
        {
            var parts = line.Split(" | ");
            if (parts.Length < 4)
                return null;

            var entry = new AuditLogEntry
            {
                Timestamp = DateTime.Parse(parts[0]),
                Username = parts[1],
                Details = parts[3]
            };

            // 解析操作类型
            entry.ActionType = parts[2] switch
            {
                "按钮点击" => AuditActionType.ButtonClick,
                "参数修改" => AuditActionType.ParameterChange,
                "环境切换" => AuditActionType.EnvironmentChange,
                "登录" => AuditActionType.Login,
                "登出" => AuditActionType.Logout,
                "订单操作" => AuditActionType.OrderAction,
                "紧急停止" => AuditActionType.EmergencyStop,
                "配置变更" => AuditActionType.ConfigChange,
                "数据加载" => AuditActionType.DataLoad,
                "回测启动" => AuditActionType.BacktestStart,
                "回测停止" => AuditActionType.BacktestStop,
                _ => AuditActionType.Other
            };

            // 解析值变更
            if (entry.Details.Contains(" [") && entry.Details.EndsWith("]"))
            {
                var valueStart = entry.Details.LastIndexOf(" [");
                var valueChange = entry.Details[(valueStart + 2)..^1];
                entry.Details = entry.Details[..valueStart];

                var arrow = valueChange.IndexOf(" -> ");
                if (arrow > 0)
                {
                    entry.OldValue = valueChange[..arrow];
                    entry.NewValue = valueChange[(arrow + 4)..];
                }
            }

            return entry;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureLogDirectory()
    {
        var dir = GetLogDirectory();
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string GetLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AegisQuant",
            "audit"
        );
    }

    private static string GetCurrentLogPath()
    {
        return GetLogPath(DateTime.Today);
    }

    private static string GetLogPath(DateTime date)
    {
        return Path.Combine(GetLogDirectory(), $"audit_{date:yyyyMMdd}.log");
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
