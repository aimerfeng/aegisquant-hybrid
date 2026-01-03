using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace AegisQuant.UI.Services;

/// <summary>
/// 配色方案枚举
/// </summary>
public enum ColorScheme
{
    /// <summary>A股配色 (红涨绿跌)</summary>
    China,
    /// <summary>国际配色 (绿涨红跌)</summary>
    International
}

/// <summary>
/// 主题模式枚举
/// </summary>
public enum ThemeMode
{
    /// <summary>浅色主题</summary>
    Light,
    /// <summary>暗色主题</summary>
    Dark
}

/// <summary>
/// 配色方案服务 - 支持运行时热切换主题和配色
/// </summary>
public class ColorSchemeService : INotifyPropertyChanged
{
    private static ColorSchemeService? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// 单例实例
    /// </summary>
    public static ColorSchemeService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ColorSchemeService();
                }
            }
            return _instance;
        }
    }

    private ColorScheme _currentScheme = ColorScheme.China;
    private ThemeMode _currentTheme = ThemeMode.Dark;

    /// <summary>
    /// 当前配色方案
    /// </summary>
    public ColorScheme CurrentScheme
    {
        get => _currentScheme;
        private set
        {
            if (_currentScheme != value)
            {
                _currentScheme = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 当前主题模式
    /// </summary>
    public ThemeMode CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 上涨颜色 (A股=红, 国际=绿)
    /// </summary>
    public Color UpColor => _currentScheme == ColorScheme.China
        ? Color.FromRgb(0xFF, 0x33, 0x33)   // 红
        : Color.FromRgb(0x00, 0xCC, 0x00);  // 绿

    /// <summary>
    /// 下跌颜色 (A股=绿, 国际=红)
    /// </summary>
    public Color DownColor => _currentScheme == ColorScheme.China
        ? Color.FromRgb(0x00, 0xCC, 0x00)   // 绿
        : Color.FromRgb(0xFF, 0x33, 0x33);  // 红

    /// <summary>
    /// 平盘颜色
    /// </summary>
    public Color FlatColor => Color.FromRgb(0xAA, 0xAA, 0xAA);

    /// <summary>
    /// 买盘颜色 (与上涨颜色相同)
    /// </summary>
    public Color BidColor => UpColor;

    /// <summary>
    /// 卖盘颜色 (与下跌颜色相同)
    /// </summary>
    public Color AskColor => DownColor;

    /// <summary>
    /// 上涨画刷
    /// </summary>
    public SolidColorBrush UpBrush => new(UpColor);

    /// <summary>
    /// 下跌画刷
    /// </summary>
    public SolidColorBrush DownBrush => new(DownColor);

    /// <summary>
    /// 平盘画刷
    /// </summary>
    public SolidColorBrush FlatBrush => new(FlatColor);

    /// <summary>
    /// 买盘画刷
    /// </summary>
    public SolidColorBrush BidBrush => new(BidColor);

    /// <summary>
    /// 卖盘画刷
    /// </summary>
    public SolidColorBrush AskBrush => new(AskColor);

    /// <summary>
    /// 主题变更事件
    /// </summary>
    public event EventHandler? ThemeChanged;

    /// <summary>
    /// 配色方案变更事件
    /// </summary>
    public event EventHandler? SchemeChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private ColorSchemeService()
    {
        // 从设置加载
        LoadSettings();
    }

    /// <summary>
    /// 设置配色方案
    /// </summary>
    /// <param name="scheme">配色方案</param>
    public void SetScheme(ColorScheme scheme)
    {
        if (_currentScheme == scheme) return;

        CurrentScheme = scheme;
        ApplyTheme();
        SaveSettings();

        // 通知颜色属性变更
        OnPropertyChanged(nameof(UpColor));
        OnPropertyChanged(nameof(DownColor));
        OnPropertyChanged(nameof(BidColor));
        OnPropertyChanged(nameof(AskColor));
        OnPropertyChanged(nameof(UpBrush));
        OnPropertyChanged(nameof(DownBrush));
        OnPropertyChanged(nameof(BidBrush));
        OnPropertyChanged(nameof(AskBrush));

        SchemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 设置主题模式
    /// </summary>
    /// <param name="theme">主题模式</param>
    public void SetTheme(ThemeMode theme)
    {
        if (_currentTheme == theme) return;

        CurrentTheme = theme;
        ApplyTheme();
        SaveSettings();

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 切换主题 (Light <-> Dark)
    /// </summary>
    public void ToggleTheme()
    {
        SetTheme(_currentTheme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
    }

    /// <summary>
    /// 切换配色方案 (China <-> International)
    /// </summary>
    public void ToggleScheme()
    {
        SetScheme(_currentScheme == ColorScheme.China ? ColorScheme.International : ColorScheme.China);
    }

    /// <summary>
    /// 应用主题到应用程序资源
    /// </summary>
    private void ApplyTheme()
    {
        var app = Application.Current;
        if (app == null) return;

        var mergedDicts = app.Resources.MergedDictionaries;

        // 移除旧的主题资源
        ResourceDictionary? oldTheme = null;
        foreach (var dict in mergedDicts)
        {
            if (dict.Source?.ToString().Contains("Theme.xaml") == true)
            {
                oldTheme = dict;
                break;
            }
        }

        if (oldTheme != null)
        {
            mergedDicts.Remove(oldTheme);
        }

        // 加载新主题
        var themeUri = _currentTheme == ThemeMode.Dark
            ? new Uri("Resources/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Resources/LightTheme.xaml", UriKind.Relative);

        try
        {
            var newTheme = new ResourceDictionary { Source = themeUri };

            // 注入配色方案 (覆盖主题中的默认颜色)
            newTheme["UpColor"] = UpColor;
            newTheme["DownColor"] = DownColor;
            newTheme["BidColor"] = BidColor;
            newTheme["AskColor"] = AskColor;
            newTheme["UpBrush"] = new SolidColorBrush(UpColor);
            newTheme["DownBrush"] = new SolidColorBrush(DownColor);
            newTheme["BidBrush"] = new SolidColorBrush(BidColor);
            newTheme["AskBrush"] = new SolidColorBrush(AskColor);

            mergedDicts.Add(newTheme);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load theme: {ex.Message}");
        }
    }

    /// <summary>
    /// 初始化服务 (在应用启动时调用)
    /// </summary>
    public void Initialize()
    {
        LoadSettings();
        ApplyTheme();
    }

    /// <summary>
    /// 根据价格变化获取颜色
    /// </summary>
    /// <param name="change">价格变化 (正=涨, 负=跌, 0=平)</param>
    /// <returns>对应的颜色</returns>
    public Color GetPriceChangeColor(double change)
    {
        if (change > 0) return UpColor;
        if (change < 0) return DownColor;
        return FlatColor;
    }

    /// <summary>
    /// 根据价格变化获取画刷
    /// </summary>
    /// <param name="change">价格变化 (正=涨, 负=跌, 0=平)</param>
    /// <returns>对应的画刷</returns>
    public SolidColorBrush GetPriceChangeBrush(double change)
    {
        return new SolidColorBrush(GetPriceChangeColor(change));
    }

    /// <summary>
    /// 根据当前价格和参考价格获取颜色
    /// </summary>
    /// <param name="currentPrice">当前价格</param>
    /// <param name="referencePrice">参考价格 (如昨收价)</param>
    /// <returns>对应的颜色</returns>
    public Color GetPriceColor(double currentPrice, double referencePrice)
    {
        return GetPriceChangeColor(currentPrice - referencePrice);
    }

    /// <summary>
    /// 根据当前价格和参考价格获取画刷
    /// </summary>
    /// <param name="currentPrice">当前价格</param>
    /// <param name="referencePrice">参考价格 (如昨收价)</param>
    /// <returns>对应的画刷</returns>
    public SolidColorBrush GetPriceBrush(double currentPrice, double referencePrice)
    {
        return new SolidColorBrush(GetPriceColor(currentPrice, referencePrice));
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
                $"Theme={_currentTheme}",
                $"ColorScheme={_currentScheme}"
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
                    if (line.StartsWith("Theme="))
                    {
                        var value = line["Theme=".Length..];
                        if (Enum.TryParse<ThemeMode>(value, out var theme))
                        {
                            _currentTheme = theme;
                        }
                    }
                    else if (line.StartsWith("ColorScheme="))
                    {
                        var value = line["ColorScheme=".Length..];
                        if (Enum.TryParse<ColorScheme>(value, out var scheme))
                        {
                            _currentScheme = scheme;
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
            "theme_settings.txt"
        );
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
