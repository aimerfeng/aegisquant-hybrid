using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace AegisQuant.UI.Services;

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

/// <summary>
/// 环境配置服务 - 管理多环境切换和配置
/// Requirements: 11.1, 11.2, 11.3, 11.4
/// </summary>
public class EnvironmentService : INotifyPropertyChanged
{
    private static EnvironmentService? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// 单例实例
    /// </summary>
    public static EnvironmentService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new EnvironmentService();
                }
            }
            return _instance;
        }
    }

    private TradingEnvironment _currentEnvironment = TradingEnvironment.Backtest;
    private bool _isLiveConfirmed;

    /// <summary>
    /// 是否已确认实盘模式
    /// </summary>
    public bool IsLiveConfirmed => _isLiveConfirmed;

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
                OnPropertyChanged(nameof(EnvironmentBrush));
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
    /// 环境状态栏画刷
    /// </summary>
    public SolidColorBrush EnvironmentBrush => new(EnvironmentColor);

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

    /// <summary>
    /// 环境变更事件
    /// </summary>
    public event EventHandler<EnvironmentChangedEventArgs>? EnvironmentChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private EnvironmentService()
    {
        LoadSettings();
    }

    /// <summary>
    /// 切换环境
    /// </summary>
    /// <param name="environment">目标环境</param>
    /// <returns>是否切换成功</returns>
    public bool SetEnvironment(TradingEnvironment environment)
    {
        if (_currentEnvironment == environment)
            return true;

        // 切换到实盘模式需要确认
        if (environment == TradingEnvironment.Live)
        {
            if (!ShowLiveConfirmationDialog())
            {
                return false;
            }
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
    /// <returns>用户是否确认</returns>
    private bool ShowLiveConfirmationDialog()
    {
        var result = MessageBox.Show(
            "⚠️ 警告：您即将切换到实盘交易模式！\n\n" +
            "在实盘模式下：\n" +
            "• 所有订单将被发送到真实交易所\n" +
            "• 可能产生真实的资金损失\n" +
            "• 快进功能将被禁用\n\n" +
            "请确保您已充分了解风险并准备好进行实盘交易。\n\n" +
            "是否确认切换到实盘模式？",
            "实盘模式确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    /// <summary>
    /// 获取当前环境的配置文件路径
    /// </summary>
    /// <returns>配置文件路径</returns>
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

    /// <summary>
    /// 获取配置目录
    /// </summary>
    private static string GetConfigDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AegisQuant",
            "config"
        );
    }

    /// <summary>
    /// 初始化服务
    /// </summary>
    public void Initialize()
    {
        LoadSettings();
    }

    private void SaveSettings()
    {
        try
        {
            var settingsPath = GetSettingsPath();
            var dir = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var lines = new[]
            {
                $"Environment={_currentEnvironment}"
            };
            File.WriteAllLines(settingsPath, lines);
        }
        catch
        {
            // 忽略保存失败
        }
    }

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
        catch
        {
            // 使用默认值
        }
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AegisQuant",
            "environment_settings.txt"
        );
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// 环境变更事件参数
/// </summary>
public class EnvironmentChangedEventArgs : EventArgs
{
    public TradingEnvironment OldEnvironment { get; }
    public TradingEnvironment NewEnvironment { get; }

    public EnvironmentChangedEventArgs(TradingEnvironment oldEnv, TradingEnvironment newEnv)
    {
        OldEnvironment = oldEnv;
        NewEnvironment = newEnv;
    }
}
