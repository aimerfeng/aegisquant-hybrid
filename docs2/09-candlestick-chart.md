# K 线图表详解

## 概述

专业级 K 线图表是量化交易系统的核心可视化组件。本文档详细说明如何使用 ScottPlot 实现三段式 K 线图表，包括主图 (K 线 + 均线 + 布林带)、成交量图和 MACD 指标图。

## 问题分析

### 传统图表库的局限

1. **性能问题**: 大数据量下渲染卡顿
2. **交互不足**: 缺少十字光标联动
3. **定制困难**: 难以实现 A 股配色
4. **指标叠加**: 多指标显示复杂

### 设计目标

- 三段式布局: K 线主图 + 成交量 + MACD
- 十字光标三图联动
- 支持多种技术指标叠加
- A 股红涨绿跌配色
- 买卖点标记显示

## 解决方案

### 三段式图表架构

```
┌─────────────────────────────────────────┐
│           主图 (K线 + 均线 + 布林带)      │  60%
├─────────────────────────────────────────┤
│              成交量图                    │  20%
├─────────────────────────────────────────┤
│           MACD 指标图                    │  20%
└─────────────────────────────────────────┘
```

### 核心代码实现

```csharp
// CandlestickChartControl.xaml.cs
public partial class CandlestickChartControl : UserControl
{
    private Crosshair? _mainCrosshair;
    private Crosshair? _volumeCrosshair;
    private Crosshair? _macdCrosshair;

    // 数据存储
    private List<OHLC> _ohlcData = new();
    private List<double> _volumes = new();
    private List<double> _ma5 = new();
    private List<double> _ma10 = new();
    private List<double> _macdDif = new();
    private List<double> _macdDea = new();
    private List<double> _macdHistogram = new();

    // 买卖标记
    private List<(int index, double price, bool isBuy)> _tradeMarkers = new();

    /// <summary>
    /// 初始化图表
    /// </summary>
    private void InitializeCharts()
    {
        var colorService = ColorSchemeService.Instance;

        // 主图设置 - 暗色主题
        ConfigureChart(MainChart.Plot, "K线图");
        ConfigureChart(VolumeChart.Plot, "成交量");
        ConfigureChart(MacdChart.Plot, "MACD");

        // 添加十字光标
        _mainCrosshair = MainChart.Plot.Add.Crosshair(0, 0);
        _mainCrosshair.IsVisible = false;
        
        _volumeCrosshair = VolumeChart.Plot.Add.Crosshair(0, 0);
        _volumeCrosshair.IsVisible = false;
        
        _macdCrosshair = MacdChart.Plot.Add.Crosshair(0, 0);
        _macdCrosshair.IsVisible = false;
    }

    /// <summary>
    /// 配置图表通用设置
    /// </summary>
    private void ConfigureChart(Plot plot, string title)
    {
        // 暗色主题
        plot.Style.Background(
            figure: ScottPlot.Color.FromHex("#1E1E1E"),
            data: ScottPlot.Color.FromHex("#252525"));
        
        plot.Style.ColorAxes(ScottPlot.Color.FromHex("#A0A0A0"));
        plot.Style.ColorGrids(ScottPlot.Color.FromHex("#333333"));
        plot.Layout.Frameless();
    }
}
```

### 十字光标联动

```csharp
/// <summary>
/// 主图鼠标移动 - 联动更新所有图表的十字光标
/// </summary>
private void OnMainChartMouseMove(object sender, MouseEventArgs e)
{
    var position = e.GetPosition(MainChart);
    var pixel = new Pixel((float)position.X, (float)position.Y);
    var coordinates = MainChart.Plot.GetCoordinates(pixel);

    // 更新三个图表的十字光标位置
    UpdateCrosshairs(coordinates.X, coordinates.Y);
    UpdateCrosshairInfo((int)coordinates.X);
    
    CrosshairInfo.Visibility = Visibility.Visible;
}

/// <summary>
/// 更新所有十字光标位置
/// </summary>
private void UpdateCrosshairs(double x, double y)
{
    if (_mainCrosshair != null)
    {
        _mainCrosshair.IsVisible = true;
        _mainCrosshair.Position = new Coordinates(x, y);
    }
    
    // 成交量和 MACD 图只同步 X 轴
    if (_volumeCrosshair != null)
    {
        _volumeCrosshair.IsVisible = true;
        _volumeCrosshair.Position = new Coordinates(x, 0);
    }
    
    if (_macdCrosshair != null)
    {
        _macdCrosshair.IsVisible = true;
        _macdCrosshair.Position = new Coordinates(x, 0);
    }
    
    RefreshAllCharts();
}
```

### K 线渲染与配色

```csharp
/// <summary>
/// 渲染主图 - K 线 + 均线 + 布林带 + 买卖标记
/// </summary>
private void RenderMainChart()
{
    MainChart.Plot.Clear();
    if (_ohlcData.Count == 0) return;

    var colorService = ColorSchemeService.Instance;

    // 添加 K 线 - 使用 A 股配色
    var candlestick = MainChart.Plot.Add.Candlestick(_ohlcData);
    candlestick.RisingColor = ScottPlot.Color.FromColor(
        System.Drawing.Color.FromArgb(
            colorService.UpColor.R, 
            colorService.UpColor.G, 
            colorService.UpColor.B));  // 红色上涨
    candlestick.FallingColor = ScottPlot.Color.FromColor(
        System.Drawing.Color.FromArgb(
            colorService.DownColor.R, 
            colorService.DownColor.G, 
            colorService.DownColor.B));  // 绿色下跌

    // 添加均线
    if (_ma5.Count > 0)
    {
        var signal = MainChart.Plot.Add.Signal(_ma5.ToArray());
        signal.Color = ScottPlot.Color.FromHex("#FFFF00"); // 黄色 MA5
        signal.LineWidth = 1;
    }

    // 添加买卖标记
    foreach (var (index, price, isBuy) in _tradeMarkers)
    {
        var marker = MainChart.Plot.Add.Marker(index, price);
        marker.Shape = isBuy ? MarkerShape.TriUp : MarkerShape.TriDown;
        marker.Size = 10;
        marker.Color = isBuy 
            ? ScottPlot.Color.FromHex("#FF4444")   // 买入红色
            : ScottPlot.Color.FromHex("#44FF44"); // 卖出绿色
    }

    MainChart.Plot.Axes.AutoScale();
    MainChart.Refresh();
}
```

### MACD 指标渲染

```csharp
/// <summary>
/// 渲染 MACD 图 - DIF + DEA + 柱状图
/// </summary>
private void RenderMacdChart()
{
    MacdChart.Plot.Clear();
    if (_macdDif.Count == 0) return;

    var colorService = ColorSchemeService.Instance;

    // DIF 线 (黄色)
    var difSignal = MacdChart.Plot.Add.Signal(_macdDif.ToArray());
    difSignal.Color = ScottPlot.Color.FromHex("#FFFF00");
    difSignal.LineWidth = 1;

    // DEA 线 (青色)
    var deaSignal = MacdChart.Plot.Add.Signal(_macdDea.ToArray());
    deaSignal.Color = ScottPlot.Color.FromHex("#00FFFF");
    deaSignal.LineWidth = 1;

    // MACD 柱状图 - 红绿配色
    for (int i = 0; i < _macdHistogram.Count; i++)
    {
        var bar = MacdChart.Plot.Add.Bar(new[] { _macdHistogram[i] });
        bar.Position = i;
        bar.Color = _macdHistogram[i] >= 0 
            ? ScottPlot.Color.FromHex("#FF4444")   // 正值红色
            : ScottPlot.Color.FromHex("#44FF44"); // 负值绿色
    }

    // 添加零线
    var zeroLine = MacdChart.Plot.Add.HorizontalLine(0);
    zeroLine.Color = ScottPlot.Color.FromHex("#666666");
    zeroLine.LineWidth = 1;

    MacdChart.Plot.Axes.AutoScale();
    MacdChart.Refresh();
}
```

### 信息面板更新

```csharp
/// <summary>
/// 更新十字光标信息面板
/// </summary>
private void UpdateCrosshairInfo(int index)
{
    if (index < 0 || index >= _ohlcData.Count) return;

    var ohlc = _ohlcData[index];
    
    CrosshairTime.Text = ohlc.DateTime.ToString("yyyy-MM-dd HH:mm");
    CrosshairOpen.Text = ohlc.Open.ToString("F2");
    CrosshairHigh.Text = ohlc.High.ToString("F2");
    CrosshairLow.Text = ohlc.Low.ToString("F2");
    CrosshairClose.Text = ohlc.Close.ToString("F2");

    // 根据涨跌设置颜色
    var colorService = ColorSchemeService.Instance;
    var brush = colorService.GetPriceChangeBrush(ohlc.Close - ohlc.Open);
    CrosshairClose.Foreground = brush;
}
```

## 使用示例

```csharp
// 更新 K 线数据
var ohlcData = new List<OHLC>
{
    new OHLC(100, 105, 98, 103, DateTime.Now.AddDays(-2), TimeSpan.FromDays(1)),
    new OHLC(103, 108, 101, 106, DateTime.Now.AddDays(-1), TimeSpan.FromDays(1)),
    new OHLC(106, 110, 104, 109, DateTime.Now, TimeSpan.FromDays(1)),
};
chartControl.UpdateOhlcData(ohlcData);

// 更新成交量
chartControl.UpdateVolumeData(new List<double> { 1000000, 1200000, 1500000 });

// 添加买卖标记
chartControl.AddTradeMarker(1, 106, isBuy: true);  // 买入点
chartControl.AddTradeMarker(2, 109, isBuy: false); // 卖出点
```

## 面试话术

### Q: 为什么选择 ScottPlot 而不是其他图表库？

**A**: ScottPlot 有三个优势：
1. **高性能**: 基于 SkiaSharp，支持百万级数据点渲染
2. **金融友好**: 内置 Candlestick、OHLC 等金融图表类型
3. **高度可定制**: 可以自定义颜色、样式、交互行为

相比 LiveCharts 更轻量，相比 OxyPlot 金融支持更好。

### Q: 十字光标联动是如何实现的？

**A**: 核心是共享 X 坐标：
1. 任一图表的鼠标移动事件触发时，获取当前坐标
2. 将 X 坐标同步到其他两个图表的 Crosshair
3. 调用 Refresh() 重绘所有图表

关键点是三个图表的 X 轴刻度必须一致，这样同一个 X 值对应同一根 K 线。

### Q: 如何处理大数据量的性能问题？

**A**: 三个策略：
1. **数据裁剪**: 只渲染可视区域内的数据
2. **降采样**: 缩小视图时合并 K 线
3. **异步加载**: 滚动时异步加载历史数据

ScottPlot 的 Signal 类型内部已经做了优化，百万级数据点也能流畅渲染。

### Q: 买卖标记是如何实现的？

**A**: 使用 ScottPlot 的 Marker 功能：
- 买入用向上三角形 (TriUp)，红色
- 卖出用向下三角形 (TriDown)，绿色
- 位置由 K 线索引和价格决定

标记数据单独存储，每次重绘时叠加到 K 线图上。
