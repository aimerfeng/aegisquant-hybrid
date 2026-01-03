# 多环境配置详解

## 概述

量化交易系统需要支持多种运行环境：回测、模拟盘、实盘。本文档详细说明如何实现环境切换、配置隔离和安全防护机制。

## 问题分析

### 单一环境的风险

1. **配置混淆**: 回测配置误用于实盘
2. **误操作风险**: 开发时意外连接实盘
3. **数据污染**: 测试数据混入生产数据
4. **缺乏警示**: 用户不清楚当前环境

### 设计目标

- 三种环境: 回测 (Backtest)、模拟盘 (PaperTrading)、实盘 (Live)
- 环境状态栏颜色区分
- 实盘切换二次确认
- 配置文件隔离


## 解决方案

### 环境枚举定义

```csharp
/// <summary>
/// 运行环境枚举
/// </summary>
public enum TradingEnvironment
{
    /// <summary>回测模式 - 使用历史数据进行策略测试</summary>
    Backtest,
    /// <summary>模拟盘模式 - 使用实时数据但不实际下单</summary>
    PaperTrading,
    /// <summary>实盘模式 - 真实交易环境</summary>
    Live
}
```

### 环境服务实现

```csharp
// EnvironmentService.cs
public class EnvironmentService : INotifyPropertyChanged
{
    private static EnvironmentService? _instance;
    private TradingEnvironment _currentEnvironment = TradingEnvironment.Backtest;
    private bool _isLiveConfirmed;

    /// <summary>
    /// 当前运行环境
    /// </summary>
    public TradingEnvironment CurrentEnvironment
    {
        get => _currentEnvironment;
        private set
        {
            if (_currentEnvironment != value)
            {
                _currentEnvironment = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EnvironmentColor));
                OnPropertyChanged(nameof(EnvironmentDisplayName));
                OnPropertyChanged(nameof(IsLiveMode));
                OnPropertyChanged(nameof(IsFastForwardEnabled));
            }
        }
    }

    /// <summary>
    /// 是否为实盘模式
    /// </summary>
    public bool IsLiveMode => _currentEnvironment == TradingEnvironment.Live;

    /// <summary>
    /// 是否允许快进功能 (实盘模式下禁用)
    /// </summary>
    public bool IsFastForwardEnabled => _currentEnvironment != TradingEnvironment.Live;
}
```

### 环境颜色标识

```csharp
/// <summary>
/// 环境状态栏颜色
/// Backtest=蓝色, PaperTrading=黄色, Live=红色
/// </summary>
public Color EnvironmentColor => _currentEnvironment switch
{
    TradingEnvironment.Backtest => Color.FromRgb(0x00, 0x7A, 0xCC),      // 蓝色
    TradingEnvironment.PaperTrading => Color.FromRgb(0xFF, 0xA5, 0x00),  // 橙黄色
    TradingEnvironment.Live => Color.FromRgb(0xDC, 0x14, 0x3C),          // 红色
    _ => Color.FromRgb(0x00, 0x7A, 0xCC)
};

/// <summary>
/// 环境显示名称
/// </summary>
public string EnvironmentDisplayName => _currentEnvironment switch
{
    TradingEnvironment.Backtest => "回测模式",
    TradingEnvironment.PaperTrading => "模拟盘",
    TradingEnvironment.Live => "⚠ 实盘交易",
    _ => "未知"
};
```

### 实盘切换确认

```csharp
/// <summary>
/// 切换环境
/// </summary>
public bool SetEnvironment(TradingEnvironment environment)
{
    if (_currentEnvironment == environment)
        return true;

    // 切换到实盘模式需要确认
    if (environment == TradingEnvironment.Live)
    {
        if (!ShowLiveConfirmationDialog())
            return false;
        _isLiveConfirmed = true;
    }
    else
    {
        _isLiveConfirmed = false;
    }

    var oldEnvironment = _currentEnvironment;
    CurrentEnvironment = environment;
    SaveSettings();

    EnvironmentChanged?.Invoke(this, new EnvironmentChangedEventArgs(oldEnvironment, environment));
    return true;
}

/// <summary>
/// 显示实盘模式确认对话框
/// </summary>
private bool ShowLiveConfirmationDialog()
{
    var result = MessageBox.Show(
        "⚠️ 警告：您即将切换到实盘交易模式！\n\n" +
        "在实盘模式下：\n" +
        "• 所有订单将被发送到真实交易所\n" +
        "• 可能产生真实的资金损失\n" +
        "• 快进功能将被禁用\n\n" +
        "是否确认切换到实盘模式？",
        "实盘模式确认",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning,
        MessageBoxResult.No);

    return result == MessageBoxResult.Yes;
}
```

### 配置文件隔离

```csharp
/// <summary>
/// 获取当前环境的配置文件路径
/// </summary>
public string GetConfigFilePath()
{
    var configName = _currentEnvironment switch
    {
        TradingEnvironment.Backtest => "config.dev.json",
        TradingEnvironment.PaperTrading => "config.uat.json",
        TradingEnvironment.Live => "config.prod.json",
        _ => "config.dev.json"
    };

    return Path.Combine(GetConfigDirectory(), configName);
}
```

### 启动安全保护

```csharp
private void LoadSettings()
{
    try
    {
        var settingsPath = GetSettingsPath();
        if (File.Exists(settingsPath))
        {
            var lines = File.ReadAllLines(settingsPath);
            foreach (var line in lines)
            {
                if (line.StartsWith("Environment="))
                {
                    var value = line["Environment=".Length..];
                    if (Enum.TryParse<TradingEnvironment>(value, out var env))
                    {
                        // 安全起见，启动时不自动恢复到实盘模式
                        _currentEnvironment = env == TradingEnvironment.Live 
                            ? TradingEnvironment.Backtest 
                            : env;
                    }
                }
            }
        }
    }
    catch { }
}
```

## 使用示例

```csharp
// 切换到模拟盘
EnvironmentService.Instance.SetEnvironment(TradingEnvironment.PaperTrading);

// 检查是否为实盘
if (EnvironmentService.Instance.IsLiveMode)
{
    // 实盘特殊处理
}

// 获取当前环境配置文件
var configPath = EnvironmentService.Instance.GetConfigFilePath();

// 监听环境变更
EnvironmentService.Instance.EnvironmentChanged += (s, e) =>
{
    Console.WriteLine($"环境从 {e.OldEnvironment} 切换到 {e.NewEnvironment}");
};
```

## 面试话术

### Q: 为什么需要多环境配置？

**A**: 三个原因：
1. **安全隔离**: 防止测试配置误用于实盘
2. **数据隔离**: 不同环境使用不同数据源
3. **行为差异**: 实盘禁用快进等危险功能

### Q: 实盘切换为什么需要二次确认？

**A**: 这是金融系统的标准做法：
1. **防误操作**: 避免意外切换到实盘
2. **风险提示**: 明确告知用户风险
3. **审计需求**: 记录用户确认行为

### Q: 为什么启动时不恢复实盘模式？

**A**: 安全考虑：
- 用户可能忘记上次是实盘模式
- 程序崩溃后重启可能导致意外交易
- 强制用户每次手动确认进入实盘
