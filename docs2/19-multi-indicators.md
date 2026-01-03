# 多指标叠加系统 (三段式布局)

## 概述

专业的量化终端采用"三段式"图表布局：主图 (K线+均线+布林带) + 成交量副图 + 指标副图 (MACD/KDJ)。本文档说明如何实现这种布局。

## 布局结构

```
┌─────────────────────────────────────────────────────────────┐
│                      主图 (60% 高度)                         │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  K 线图                                              │   │
│  │  + MA5 (黄色)                                        │   │
│  │  + MA10 (紫色)                                       │   │
│  │  + MA20 (绿色)                                       │   │
│  │  + MA60 (白色)                                       │   │
│  │  + 布林带 (上轨/中轨/下轨)                            │   │
│  └─────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│                    成交量副图 (20% 高度)                     │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  VOL 柱状图 (红涨绿跌)                                │   │
│  │  + 成交量均线                                        │   │
│  └─────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│                    指标副图 (20% 高度)                       │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  MACD: DIF (白) + DEA (黄) + 柱状图 (红绿)            │   │
│  │  或 KDJ: K (白) + D (黄) + J (紫)                     │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

## Rust 侧指标计算

### 1. 集成 ta crate

```toml
# Cargo.toml
[dependencies]
ta = "0.5"  # Technical Analysis library
```

### 2. 指标计算模块

```rust
// indicators.rs
use ta::indicators::{
    SimpleMovingAverage,
    ExponentialMovingAverage,
    BollingerBands,
    MovingAverageConvergenceDivergence,
    RelativeStrengthIndex,
};
use ta::Next;

/// 指标结果结构体 (FFI 导出)
#[repr(C)]
pub struct IndicatorResult {
    pub ma5: f64,
    pub ma10: f64,
    pub ma20: f64,
    pub ma60: f64,
    pub boll_upper: f64,
    pub boll_middle: f64,
    pub boll_lower: f64,
    pub macd_dif: f64,
    pub macd_dea: f64,
    pub macd_histogram: f64,
    pub rsi: f64,
}

/// 指标计算器
pub struct IndicatorCalculator {
    ma5: SimpleMovingAverage,
    ma10: SimpleMovingAverage,
    ma20: SimpleMovingAverage,
    ma60: SimpleMovingAverage,
    boll: BollingerBands,
    macd: MovingAverageConvergenceDivergence,
    rsi: RelativeStrengthIndex,
}

impl IndicatorCalculator {
    pub fn new() -> Self {
        Self {
            ma5: SimpleMovingAverage::new(5).unwrap(),
            ma10: SimpleMovingAverage::new(10).unwrap(),
            ma20: SimpleMovingAverage::new(20).unwrap(),
            ma60: SimpleMovingAverage::new(60).unwrap(),
            boll: BollingerBands::new(20, 2.0).unwrap(),
            macd: MovingAverageConvergenceDivergence::new(12, 26, 9).unwrap(),
            rsi: RelativeStrengthIndex::new(14).unwrap(),
        }
    }
    
    pub fn update(&mut self, close: f64) -> IndicatorResult {
        let ma5 = self.ma5.next(close);
        let ma10 = self.ma10.next(close);
        let ma20 = self.ma20.next(close);
        let ma60 = self.ma60.next(close);
        
        let boll = self.boll.next(close);
        let macd = self.macd.next(close);
        let rsi = self.rsi.next(close);
        
        IndicatorResult {
            ma5,
            ma10,
            ma20,
            ma60,
            boll_upper: boll.upper,
            boll_middle: boll.average,
            boll_lower: boll.lower,
            macd_dif: macd.macd,
            macd_dea: macd.signal,
            macd_histogram: macd.histogram,
            rsi,
        }
    }
}
```

### 3. FFI 导出

```rust
// ffi.rs
/// 批量计算指标
/// 
/// # Safety
/// - prices 必须是有效的 f64 数组指针
/// - results 必须是有效的 IndicatorResult 数组指针
/// - 两个数组长度必须等于 count
#[no_mangle]
pub unsafe extern "C" fn calculate_indicators(
    prices: *const f64,
    count: i32,
    results: *mut IndicatorResult,
) -> i32 {
    if prices.is_null() || results.is_null() || count <= 0 {
        return ERR_NULL_POINTER;
    }
    
    let prices_slice = std::slice::from_raw_parts(prices, count as usize);
    let results_slice = std::slice::from_raw_parts_mut(results, count as usize);
    
    let mut calculator = IndicatorCalculator::new();
    
    for (i, &price) in prices_slice.iter().enumerate() {
        results_slice[i] = calculator.update(price);
    }
    
    ERR_SUCCESS
}
```

## C# 侧图表实现

### 1. 三段式布局 XAML

```xml
<!-- MainWindow.xaml -->
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="6*"/>  <!-- 主图 60% -->
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="2*"/>  <!-- 成交量 20% -->
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="2*"/>  <!-- 指标 20% -->
    </Grid.RowDefinitions>
    
    <!-- 主图: K线 + 均线 + 布林带 -->
    <WpfPlot x:Name="MainChart" Grid.Row="0"/>
    
    <GridSplitter Grid.Row="1" Height="5" HorizontalAlignment="Stretch"/>
    
    <!-- 成交量副图 -->
    <WpfPlot x:Name="VolumeChart" Grid.Row="2"/>
    
    <GridSplitter Grid.Row="3" Height="5" HorizontalAlignment="Stretch"/>
    
    <!-- 指标副图 -->
    <WpfPlot x:Name="IndicatorChart" Grid.Row="4"/>
</Grid>

<!-- 指标选择面板 -->
<StackPanel Orientation="Horizontal" DockPanel.Dock="Top">
    <CheckBox Content="MA5" IsChecked="{Binding ShowMA5}" Foreground="Yellow"/>
    <CheckBox Content="MA10" IsChecked="{Binding ShowMA10}" Foreground="Purple"/>
    <CheckBox Content="MA20" IsChecked="{Binding ShowMA20}" Foreground="Green"/>
    <CheckBox Content="MA60" IsChecked="{Binding ShowMA60}" Foreground="White"/>
    <CheckBox Content="BOLL" IsChecked="{Binding ShowBoll}"/>
    <ComboBox SelectedItem="{Binding SelectedIndicator}">
        <ComboBoxItem Content="MACD"/>
        <ComboBoxItem Content="KDJ"/>
        <ComboBoxItem Content="RSI"/>
    </ComboBox>
</StackPanel>
```

### 2. 图表绑定 ViewModel

```csharp
// ViewModels/ChartViewModel.cs
public partial class ChartViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _showMA5 = true;
    
    [ObservableProperty]
    private bool _showMA10 = true;
    
    [ObservableProperty]
    private bool _showMA20 = true;
    
    [ObservableProperty]
    private bool _showMA60 = false;
    
    [ObservableProperty]
    private bool _showBoll = true;
    
    [ObservableProperty]
    private string _selectedIndicator = "MACD";
    
    private ScottPlot.WpfPlot _mainChart;
    private ScottPlot.WpfPlot _volumeChart;
    private ScottPlot.WpfPlot _indicatorChart;
    
    // 数据
    private List<OHLC> _ohlcData = new();
    private List<double> _volumes = new();
    private List<IndicatorResult> _indicators = new();
    
    public void Initialize(WpfPlot main, WpfPlot volume, WpfPlot indicator)
    {
        _mainChart = main;
        _volumeChart = volume;
        _indicatorChart = indicator;
        
        // 同步 X 轴
        _mainChart.Plot.Axes.Link(_volumeChart.Plot.Axes);
        _mainChart.Plot.Axes.Link(_indicatorChart.Plot.Axes);
    }
    
    public void UpdateData(OHLC[] ohlc, double[] volumes, IndicatorResult[] indicators)
    {
        _ohlcData = ohlc.ToList();
        _volumes = volumes.ToList();
        _indicators = indicators.ToList();
        
        RefreshCharts();
    }
    
    private void RefreshCharts()
    {
        RefreshMainChart();
        RefreshVolumeChart();
        RefreshIndicatorChart();
    }
    
    private void RefreshMainChart()
    {
        _mainChart.Plot.Clear();
        
        // K 线图
        var candlestick = _mainChart.Plot.Add.Candlestick(_ohlcData.ToArray());
        candlestick.RisingColor = GetUpColor();
        candlestick.FallingColor = GetDownColor();
        
        // 均线
        if (ShowMA5)
            AddMovingAverage(_mainChart.Plot, _indicators.Select(i => i.ma5), Colors.Yellow, "MA5");
        if (ShowMA10)
            AddMovingAverage(_mainChart.Plot, _indicators.Select(i => i.ma10), Colors.Purple, "MA10");
        if (ShowMA20)
            AddMovingAverage(_mainChart.Plot, _indicators.Select(i => i.ma20), Colors.Green, "MA20");
        if (ShowMA60)
            AddMovingAverage(_mainChart.Plot, _indicators.Select(i => i.ma60), Colors.White, "MA60");
        
        // 布林带
        if (ShowBoll)
        {
            AddMovingAverage(_mainChart.Plot, _indicators.Select(i => i.boll_upper), Colors.Gray, "BOLL Upper");
            AddMovingAverage(_mainChart.Plot, _indicators.Select(i => i.boll_middle), Colors.Orange, "BOLL Middle");
            AddMovingAverage(_mainChart.Plot, _indicators.Select(i => i.boll_lower), Colors.Gray, "BOLL Lower");
        }
        
        _mainChart.Refresh();
    }
    
    private void RefreshVolumeChart()
    {
        _volumeChart.Plot.Clear();
        
        // 成交量柱状图
        for (int i = 0; i < _volumes.Count; i++)
        {
            var bar = _volumeChart.Plot.Add.Bar(i, _volumes[i]);
            bool isUp = i > 0 && _ohlcData[i].Close >= _ohlcData[i].Open;
            bar.FillColor = isUp ? GetUpColor() : GetDownColor();
        }
        
        _volumeChart.Refresh();
    }
    
    private void RefreshIndicatorChart()
    {
        _indicatorChart.Plot.Clear();
        
        switch (SelectedIndicator)
        {
            case "MACD":
                DrawMACD();
                break;
            case "KDJ":
                DrawKDJ();
                break;
            case "RSI":
                DrawRSI();
                break;
        }
        
        _indicatorChart.Refresh();
    }
    
    private void DrawMACD()
    {
        // DIF 线 (白色)
        var dif = _indicators.Select(i => i.macd_dif).ToArray();
        _indicatorChart.Plot.Add.Signal(dif, color: Colors.White);
        
        // DEA 线 (黄色)
        var dea = _indicators.Select(i => i.macd_dea).ToArray();
        _indicatorChart.Plot.Add.Signal(dea, color: Colors.Yellow);
        
        // 柱状图 (红绿)
        for (int i = 0; i < _indicators.Count; i++)
        {
            var hist = _indicators[i].macd_histogram;
            var bar = _indicatorChart.Plot.Add.Bar(i, hist);
            bar.FillColor = hist >= 0 ? GetUpColor() : GetDownColor();
        }
    }
    
    private void AddMovingAverage(Plot plot, IEnumerable<double> values, Color color, string label)
    {
        var signal = plot.Add.Signal(values.ToArray());
        signal.Color = ScottPlot.Color.FromColor(System.Drawing.Color.FromArgb(color.R, color.G, color.B));
        signal.Label = label;
    }
    
    private ScottPlot.Color GetUpColor()
    {
        var c = ColorSchemeService.Instance.UpColor;
        return ScottPlot.Color.FromColor(System.Drawing.Color.FromArgb(c.R, c.G, c.B));
    }
    
    private ScottPlot.Color GetDownColor()
    {
        var c = ColorSchemeService.Instance.DownColor;
        return ScottPlot.Color.FromColor(System.Drawing.Color.FromArgb(c.R, c.G, c.B));
    }
}
```

### 3. 指标选择响应

```csharp
partial void OnShowMA5Changed(bool value) => RefreshMainChart();
partial void OnShowMA10Changed(bool value) => RefreshMainChart();
partial void OnShowMA20Changed(bool value) => RefreshMainChart();
partial void OnShowMA60Changed(bool value) => RefreshMainChart();
partial void OnShowBollChanged(bool value) => RefreshMainChart();
partial void OnSelectedIndicatorChanged(string value) => RefreshIndicatorChart();
```

## 面试话术

### Q: 为什么指标计算放在 Rust 侧？

**A**: 三个原因：
1. **性能**: Rust 计算比 C# 快 5-10 倍
2. **复用**: 回测和实盘使用同一套计算逻辑
3. **一致性**: 避免 C# 和 Rust 计算结果不一致

我使用 `ta` crate，它是 Rust 生态中成熟的技术分析库，支持 50+ 种指标。

### Q: 如何实现三个图表的 X 轴同步？

**A**: ScottPlot 提供了 `Axes.Link()` 方法：
```csharp
_mainChart.Plot.Axes.Link(_volumeChart.Plot.Axes);
```
这样当用户在主图上缩放或平移时，成交量图和指标图会同步移动。

### Q: 布林带的参数是什么？

**A**: 标准布林带参数是 (20, 2)：
- 20: 中轨是 20 日移动平均线
- 2: 上下轨是中轨 ± 2 倍标准差

这个参数可以配置，但 (20, 2) 是最常用的设置。
