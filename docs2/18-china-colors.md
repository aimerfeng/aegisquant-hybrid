# A 股配色规范 (红涨绿跌)

## 概述

A 股市场使用与国际市场相反的配色习惯：红色表示上涨，绿色表示下跌。本文档说明如何在 WPF 应用中实现符合 A 股习惯的配色系统。

## 配色对比

| 场景 | A 股 (中国) | 国际 (美股/港股) |
|------|-------------|------------------|
| 上涨 | 红色 #FF3333 | 绿色 #00CC00 |
| 下跌 | 绿色 #00CC00 | 红色 #FF3333 |
| 平盘 | 白色/灰色 | 白色/灰色 |

## 实现方案

### 1. App.xaml 全局资源定义

```xml
<!-- App.xaml -->
<Application x:Class="AegisQuant.UI.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <!-- A 股配色方案 (默认) -->
            <ResourceDictionary x:Key="ChinaColorScheme">
                <!-- 涨跌颜色 -->
                <SolidColorBrush x:Key="UpBrush" Color="#FF3333"/>
                <SolidColorBrush x:Key="DownBrush" Color="#00CC00"/>
                <SolidColorBrush x:Key="FlatBrush" Color="#AAAAAA"/>
                
                <!-- 买卖颜色 (A股: 买=红, 卖=绿) -->
                <SolidColorBrush x:Key="BuyBrush" Color="#FF3333"/>
                <SolidColorBrush x:Key="SellBrush" Color="#00CC00"/>
                
                <!-- 盈亏颜色 -->
                <SolidColorBrush x:Key="ProfitBrush" Color="#FF3333"/>
                <SolidColorBrush x:Key="LossBrush" Color="#00CC00"/>
                
                <!-- 盘口颜色 -->
                <SolidColorBrush x:Key="BidBrush" Color="#FF3333"/>
                <SolidColorBrush x:Key="AskBrush" Color="#00CC00"/>
            </ResourceDictionary>
            
            <!-- 国际配色方案 -->
            <ResourceDictionary x:Key="InternationalColorScheme">
                <SolidColorBrush x:Key="UpBrush" Color="#00CC00"/>
                <SolidColorBrush x:Key="DownBrush" Color="#FF3333"/>
                <SolidColorBrush x:Key="FlatBrush" Color="#AAAAAA"/>
                
                <SolidColorBrush x:Key="BuyBrush" Color="#00CC00"/>
                <SolidColorBrush x:Key="SellBrush" Color="#FF3333"/>
                
                <SolidColorBrush x:Key="ProfitBrush" Color="#00CC00"/>
                <SolidColorBrush x:Key="LossBrush" Color="#FF3333"/>
                
                <SolidColorBrush x:Key="BidBrush" Color="#00CC00"/>
                <SolidColorBrush x:Key="AskBrush" Color="#FF3333"/>
            </ResourceDictionary>
            
            <!-- 默认使用 A 股配色 -->
            <ResourceDictionary Source="ChinaColorScheme"/>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

### 2. 配色服务类

```csharp
// Services/ColorSchemeService.cs
public enum ColorScheme
{
    China,        // 红涨绿跌
    International // 绿涨红跌
}

public class ColorSchemeService : INotifyPropertyChanged
{
    private static ColorSchemeService? _instance;
    public static ColorSchemeService Instance => _instance ??= new ColorSchemeService();
    
    private ColorScheme _currentScheme = ColorScheme.China;
    
    public ColorScheme CurrentScheme
    {
        get => _currentScheme;
        set
        {
            if (_currentScheme != value)
            {
                _currentScheme = value;
                ApplyColorScheme(value);
                OnPropertyChanged();
            }
        }
    }
    
    public Color UpColor => CurrentScheme == ColorScheme.China 
        ? Color.FromRgb(0xFF, 0x33, 0x33)  // 红
        : Color.FromRgb(0x00, 0xCC, 0x00); // 绿
    
    public Color DownColor => CurrentScheme == ColorScheme.China 
        ? Color.FromRgb(0x00, 0xCC, 0x00)  // 绿
        : Color.FromRgb(0xFF, 0x33, 0x33); // 红
    
    public Color FlatColor => Color.FromRgb(0xAA, 0xAA, 0xAA);
    
    /// <summary>
    /// 根据价格变化获取颜色
    /// </summary>
    public Color GetPriceChangeColor(double change)
    {
        if (change > 0) return UpColor;
        if (change < 0) return DownColor;
        return FlatColor;
    }
    
    /// <summary>
    /// 根据盈亏获取颜色
    /// </summary>
    public Color GetPnlColor(double pnl)
    {
        if (pnl > 0) return UpColor;
        if (pnl < 0) return DownColor;
        return FlatColor;
    }
    
    private void ApplyColorScheme(ColorScheme scheme)
    {
        var app = Application.Current;
        var schemeKey = scheme == ColorScheme.China 
            ? "ChinaColorScheme" 
            : "InternationalColorScheme";
        
        // 切换资源字典
        var newScheme = app.Resources[schemeKey] as ResourceDictionary;
        if (newScheme != null)
        {
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(newScheme);
        }
        
        // 保存用户偏好
        Properties.Settings.Default.ColorScheme = scheme.ToString();
        Properties.Settings.Default.Save();
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
```

### 3. ScottPlot K 线图配色

```csharp
// ViewModels/ChartViewModel.cs
public void UpdateCandlestickColors()
{
    var colorService = ColorSchemeService.Instance;
    
    // ScottPlot 5.0 K 线配色
    var upColor = ScottPlot.Color.FromColor(
        System.Drawing.Color.FromArgb(
            colorService.UpColor.R,
            colorService.UpColor.G,
            colorService.UpColor.B));
    
    var downColor = ScottPlot.Color.FromColor(
        System.Drawing.Color.FromArgb(
            colorService.DownColor.R,
            colorService.DownColor.G,
            colorService.DownColor.B));
    
    // 设置 K 线颜色
    _candlestickPlot.RisingColor = upColor;
    _candlestickPlot.FallingColor = downColor;
    
    // 设置成交量柱颜色
    foreach (var bar in _volumeBars)
    {
        bar.FillColor = bar.Value > 0 ? upColor : downColor;
    }
    
    _plot.Refresh();
}
```

### 4. 盈亏显示绑定

```xml
<!-- MainWindow.xaml -->
<TextBlock Text="{Binding TotalPnl, StringFormat='{}{0:+#,##0.00;-#,##0.00;0.00}'}"
           Foreground="{Binding TotalPnl, Converter={StaticResource PnlColorConverter}}"/>
```

```csharp
// Converters/PnlColorConverter.cs
public class PnlColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double pnl)
        {
            var color = ColorSchemeService.Instance.GetPnlColor(pnl);
            return new SolidColorBrush(color);
        }
        return Brushes.Gray;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

### 5. 买卖标记箭头

```csharp
// 在 K 线图上添加买卖标记
public void AddTradeMarker(DateTime time, double price, bool isBuy)
{
    var colorService = ColorSchemeService.Instance;
    var color = isBuy ? colorService.UpColor : colorService.DownColor;
    
    // ScottPlot 标记
    var marker = _plot.Add.Marker(
        time.ToOADate(),
        price,
        isBuy ? MarkerShape.TriangleUp : MarkerShape.TriangleDown);
    
    marker.Color = ScottPlot.Color.FromColor(
        System.Drawing.Color.FromArgb(color.R, color.G, color.B));
    marker.Size = 10;
    
    // 添加标签
    var label = _plot.Add.Text(
        isBuy ? "买" : "卖",
        time.ToOADate(),
        price + (isBuy ? -5 : 5));
    label.Color = marker.Color;
}
```

### 6. 设置界面

```xml
<!-- SettingsWindow.xaml -->
<GroupBox Header="配色方案">
    <StackPanel>
        <RadioButton Content="A 股模式 (红涨绿跌)" 
                     IsChecked="{Binding IsChinaScheme}"
                     GroupName="ColorScheme"/>
        <RadioButton Content="国际模式 (绿涨红跌)" 
                     IsChecked="{Binding IsInternationalScheme}"
                     GroupName="ColorScheme"/>
        
        <!-- 预览 -->
        <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
            <TextBlock Text="上涨: " />
            <Rectangle Width="20" Height="20" 
                       Fill="{DynamicResource UpBrush}"/>
            <TextBlock Text="  下跌: " />
            <Rectangle Width="20" Height="20" 
                       Fill="{DynamicResource DownBrush}"/>
        </StackPanel>
    </StackPanel>
</GroupBox>
```

## 面试话术

### Q: 为什么 A 股用红涨绿跌？

**A**: 这是文化差异：
- 中国文化中红色代表喜庆、好运，所以用红色表示赚钱
- 西方文化中红色代表危险、警告，所以用红色表示亏损

作为量化系统，必须支持用户习惯。我实现了配色方案切换功能，默认 A 股模式，也支持国际模式。

### Q: 如何实现全局配色切换？

**A**: 我使用了 WPF 的动态资源机制：
1. 在 `App.xaml` 中定义两套配色资源字典
2. 使用 `DynamicResource` 绑定颜色
3. 切换时替换 `MergedDictionaries`

这样所有使用 `DynamicResource` 的控件会自动更新颜色，无需手动刷新。

### Q: ScottPlot 如何自定义 K 线颜色？

**A**: ScottPlot 5.0 的 Candlestick 类有 `RisingColor` 和 `FallingColor` 属性：
- `RisingColor`: 收盘 > 开盘时的颜色
- `FallingColor`: 收盘 < 开盘时的颜色

我在配色切换时更新这两个属性，然后调用 `Refresh()` 重绘图表。
