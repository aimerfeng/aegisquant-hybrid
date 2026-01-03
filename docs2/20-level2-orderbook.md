# 五档盘口与深度图 (Level 2 OrderBook)

## 概述

五档盘口是 A 股交易员的核心工具，显示买一到买五和卖一到卖五的挂单情况。本文档说明如何实现盘口显示和深度图可视化。

## 数据结构

### Rust 侧定义

```rust
// types.rs

/// 单档盘口数据
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct OrderBookLevel {
    pub price: f64,
    pub quantity: f64,
    pub order_count: i32,  // 该价位的订单数量
}

/// 五档盘口快照 (实际存储 10 档)
#[repr(C)]
pub struct OrderBookSnapshot {
    pub bids: [OrderBookLevel; 10],  // 买盘 (价格从高到低)
    pub asks: [OrderBookLevel; 10],  // 卖盘 (价格从低到高)
    pub bid_count: i32,              // 实际买盘档数
    pub ask_count: i32,              // 实际卖盘档数
    pub last_price: f64,             // 最新成交价
    pub timestamp: i64,              // 时间戳
}

/// 盘口统计
#[repr(C)]
pub struct OrderBookStats {
    pub total_bid_volume: f64,   // 买盘总量
    pub total_ask_volume: f64,   // 卖盘总量
    pub bid_ask_ratio: f64,      // 买卖比 (>1 买盘强)
    pub spread: f64,             // 买卖价差
    pub spread_bps: f64,         // 价差基点
}

impl OrderBookSnapshot {
    pub fn get_stats(&self) -> OrderBookStats {
        let total_bid: f64 = self.bids[..self.bid_count as usize]
            .iter()
            .map(|l| l.quantity)
            .sum();
        
        let total_ask: f64 = self.asks[..self.ask_count as usize]
            .iter()
            .map(|l| l.quantity)
            .sum();
        
        let spread = if self.ask_count > 0 && self.bid_count > 0 {
            self.asks[0].price - self.bids[0].price
        } else {
            0.0
        };
        
        let mid_price = if self.ask_count > 0 && self.bid_count > 0 {
            (self.asks[0].price + self.bids[0].price) / 2.0
        } else {
            self.last_price
        };
        
        OrderBookStats {
            total_bid_volume: total_bid,
            total_ask_volume: total_ask,
            bid_ask_ratio: if total_ask > 0.0 { total_bid / total_ask } else { 0.0 },
            spread,
            spread_bps: if mid_price > 0.0 { spread / mid_price * 10000.0 } else { 0.0 },
        }
    }
}
```

### FFI 导出

```rust
// ffi.rs

/// 获取当前盘口快照
#[no_mangle]
pub unsafe extern "C" fn get_orderbook(
    engine: *mut BacktestEngine,
    snapshot: *mut OrderBookSnapshot,
) -> i32 {
    if engine.is_null() || snapshot.is_null() {
        return ERR_NULL_POINTER;
    }
    
    let engine = &*engine;
    *snapshot = engine.get_current_orderbook();
    ERR_SUCCESS
}

/// 获取盘口统计
#[no_mangle]
pub unsafe extern "C" fn get_orderbook_stats(
    engine: *mut BacktestEngine,
    stats: *mut OrderBookStats,
) -> i32 {
    if engine.is_null() || stats.is_null() {
        return ERR_NULL_POINTER;
    }
    
    let engine = &*engine;
    let snapshot = engine.get_current_orderbook();
    *stats = snapshot.get_stats();
    ERR_SUCCESS
}
```

## C# 侧实现

### 1. 数据结构

```csharp
// NativeTypes.cs
[StructLayout(LayoutKind.Sequential)]
public struct OrderBookLevel
{
    public double Price;
    public double Quantity;
    public int OrderCount;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct OrderBookSnapshot
{
    public fixed byte BidsData[10 * 24];  // 10 * sizeof(OrderBookLevel)
    public fixed byte AsksData[10 * 24];
    public int BidCount;
    public int AskCount;
    public double LastPrice;
    public long Timestamp;
    
    public OrderBookLevel[] Bids
    {
        get
        {
            var result = new OrderBookLevel[BidCount];
            fixed (byte* ptr = BidsData)
            {
                for (int i = 0; i < BidCount; i++)
                {
                    result[i] = ((OrderBookLevel*)ptr)[i];
                }
            }
            return result;
        }
    }
    
    public OrderBookLevel[] Asks
    {
        get
        {
            var result = new OrderBookLevel[AskCount];
            fixed (byte* ptr = AsksData)
            {
                for (int i = 0; i < AskCount; i++)
                {
                    result[i] = ((OrderBookLevel*)ptr)[i];
                }
            }
            return result;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct OrderBookStats
{
    public double TotalBidVolume;
    public double TotalAskVolume;
    public double BidAskRatio;
    public double Spread;
    public double SpreadBps;
}
```

### 2. 盘口显示控件

```xml
<!-- Controls/OrderBookControl.xaml -->
<UserControl x:Class="AegisQuant.UI.Controls.OrderBookControl">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <!-- 卖盘标题 -->
        <Grid Grid.Row="0" Background="#1A1A2E">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Text="卖价" Foreground="Gray" HorizontalAlignment="Center"/>
            <TextBlock Text="卖量" Foreground="Gray" HorizontalAlignment="Center" Grid.Column="1"/>
            <TextBlock Text="档位" Foreground="Gray" HorizontalAlignment="Center" Grid.Column="2"/>
        </Grid>
        
        <!-- 卖盘列表 (卖五到卖一，从上到下) -->
        <ItemsControl Grid.Row="1" ItemsSource="{Binding Asks}" 
                      ItemTemplate="{StaticResource AskTemplate}"/>
        
        <!-- 最新价 -->
        <Border Grid.Row="2" Background="#2A2A4E" Padding="5">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                <TextBlock Text="{Binding LastPrice, StringFormat='{}{0:F2}'}" 
                           FontSize="18" FontWeight="Bold"
                           Foreground="{Binding PriceChangeColor}"/>
                <TextBlock Text="{Binding PriceChange, StringFormat=' ({0:+0.00%;-0.00%;0.00%})'}"
                           Foreground="{Binding PriceChangeColor}"/>
            </StackPanel>
        </Border>
        
        <!-- 买盘列表 (买一到买五，从上到下) -->
        <ItemsControl Grid.Row="3" ItemsSource="{Binding Bids}"
                      ItemTemplate="{StaticResource BidTemplate}"/>
        
        <!-- 买卖统计 -->
        <Grid Grid.Row="4" Background="#1A1A2E">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <StackPanel>
                <TextBlock Text="买盘总量" Foreground="Gray" FontSize="10"/>
                <TextBlock Text="{Binding TotalBidVolume, StringFormat='{}{0:N0}'}" 
                           Foreground="{DynamicResource BidBrush}"/>
            </StackPanel>
            <StackPanel Grid.Column="1">
                <TextBlock Text="卖盘总量" Foreground="Gray" FontSize="10"/>
                <TextBlock Text="{Binding TotalAskVolume, StringFormat='{}{0:N0}'}" 
                           Foreground="{DynamicResource AskBrush}"/>
            </StackPanel>
        </Grid>
    </Grid>
    
    <UserControl.Resources>
        <!-- 买盘模板 -->
        <DataTemplate x:Key="BidTemplate">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <!-- 价格背景条 (显示挂单量占比) -->
                <Rectangle Fill="{DynamicResource BidBrush}" Opacity="0.3"
                           HorizontalAlignment="Right"
                           Width="{Binding QuantityRatio, Converter={StaticResource RatioToWidthConverter}}"/>
                <TextBlock Text="{Binding Price, StringFormat='{}{0:F2}'}" 
                           Foreground="{DynamicResource BidBrush}"
                           HorizontalAlignment="Right"/>
                <TextBlock Text="{Binding Quantity, StringFormat='{}{0:N0}'}" 
                           Foreground="White" Grid.Column="1"
                           HorizontalAlignment="Right"/>
                <TextBlock Text="{Binding Level}" Foreground="Gray" Grid.Column="2"
                           HorizontalAlignment="Center"/>
            </Grid>
        </DataTemplate>
        
        <!-- 卖盘模板 -->
        <DataTemplate x:Key="AskTemplate">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="40"/>
                </Grid.ColumnDefinitions>
                <Rectangle Fill="{DynamicResource AskBrush}" Opacity="0.3"
                           HorizontalAlignment="Left"
                           Width="{Binding QuantityRatio, Converter={StaticResource RatioToWidthConverter}}"/>
                <TextBlock Text="{Binding Price, StringFormat='{}{0:F2}'}" 
                           Foreground="{DynamicResource AskBrush}"
                           HorizontalAlignment="Right"/>
                <TextBlock Text="{Binding Quantity, StringFormat='{}{0:N0}'}" 
                           Foreground="White" Grid.Column="1"
                           HorizontalAlignment="Right"/>
                <TextBlock Text="{Binding Level}" Foreground="Gray" Grid.Column="2"
                           HorizontalAlignment="Center"/>
            </Grid>
        </DataTemplate>
    </UserControl.Resources>
</UserControl>
```

### 3. 盘口 ViewModel

```csharp
// ViewModels/OrderBookViewModel.cs
public partial class OrderBookViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<OrderBookLevelVM> _bids = new();
    
    [ObservableProperty]
    private ObservableCollection<OrderBookLevelVM> _asks = new();
    
    [ObservableProperty]
    private double _lastPrice;
    
    [ObservableProperty]
    private double _priceChange;
    
    [ObservableProperty]
    private double _totalBidVolume;
    
    [ObservableProperty]
    private double _totalAskVolume;
    
    [ObservableProperty]
    private double _bidAskRatio;
    
    public Brush PriceChangeColor => ColorSchemeService.Instance
        .GetPriceChangeColor(PriceChange).ToBrush();
    
    public void Update(OrderBookSnapshot snapshot, OrderBookStats stats)
    {
        // 更新卖盘 (卖五到卖一，从上到下显示)
        var asks = snapshot.Asks.Take(5).Reverse().ToList();
        UpdateLevels(Asks, asks, false, stats.TotalAskVolume);
        
        // 更新买盘 (买一到买五，从上到下显示)
        var bids = snapshot.Bids.Take(5).ToList();
        UpdateLevels(Bids, bids, true, stats.TotalBidVolume);
        
        LastPrice = snapshot.LastPrice;
        TotalBidVolume = stats.TotalBidVolume;
        TotalAskVolume = stats.TotalAskVolume;
        BidAskRatio = stats.BidAskRatio;
        
        OnPropertyChanged(nameof(PriceChangeColor));
    }
    
    private void UpdateLevels(
        ObservableCollection<OrderBookLevelVM> collection,
        List<OrderBookLevel> levels,
        bool isBid,
        double totalVolume)
    {
        // 确保集合大小匹配
        while (collection.Count < 5)
            collection.Add(new OrderBookLevelVM());
        while (collection.Count > 5)
            collection.RemoveAt(collection.Count - 1);
        
        for (int i = 0; i < 5; i++)
        {
            if (i < levels.Count)
            {
                collection[i].Price = levels[i].Price;
                collection[i].Quantity = levels[i].Quantity;
                collection[i].Level = isBid ? $"买{i + 1}" : $"卖{5 - i}";
                collection[i].QuantityRatio = totalVolume > 0 
                    ? levels[i].Quantity / totalVolume 
                    : 0;
            }
            else
            {
                collection[i].Price = 0;
                collection[i].Quantity = 0;
                collection[i].Level = isBid ? $"买{i + 1}" : $"卖{5 - i}";
                collection[i].QuantityRatio = 0;
            }
        }
    }
}

public partial class OrderBookLevelVM : ObservableObject
{
    [ObservableProperty] private double _price;
    [ObservableProperty] private double _quantity;
    [ObservableProperty] private string _level = "";
    [ObservableProperty] private double _quantityRatio;
}
```

### 4. 深度图 (Depth Chart)

```csharp
// ViewModels/DepthChartViewModel.cs
public void DrawDepthChart(WpfPlot plot, OrderBookSnapshot snapshot)
{
    plot.Plot.Clear();
    
    var bids = snapshot.Bids.Take(snapshot.BidCount).ToList();
    var asks = snapshot.Asks.Take(snapshot.AskCount).ToList();
    
    // 计算累计量
    var bidPrices = new List<double>();
    var bidCumulative = new List<double>();
    double cumBid = 0;
    foreach (var level in bids)
    {
        cumBid += level.Quantity;
        bidPrices.Add(level.Price);
        bidCumulative.Add(cumBid);
    }
    
    var askPrices = new List<double>();
    var askCumulative = new List<double>();
    double cumAsk = 0;
    foreach (var level in asks)
    {
        cumAsk += level.Quantity;
        askPrices.Add(level.Price);
        askCumulative.Add(cumAsk);
    }
    
    // 绘制买盘深度 (绿色填充)
    if (bidPrices.Count > 0)
    {
        var bidFill = plot.Plot.Add.FillY(
            bidPrices.ToArray(),
            bidCumulative.ToArray(),
            Enumerable.Repeat(0.0, bidPrices.Count).ToArray());
        bidFill.FillColor = GetBidColor().WithAlpha(0.5);
        bidFill.LineColor = GetBidColor();
    }
    
    // 绘制卖盘深度 (红色填充)
    if (askPrices.Count > 0)
    {
        var askFill = plot.Plot.Add.FillY(
            askPrices.ToArray(),
            askCumulative.ToArray(),
            Enumerable.Repeat(0.0, askPrices.Count).ToArray());
        askFill.FillColor = GetAskColor().WithAlpha(0.5);
        askFill.LineColor = GetAskColor();
    }
    
    // 标记中间价
    var midPrice = (bids[0].Price + asks[0].Price) / 2;
    var midLine = plot.Plot.Add.VerticalLine(midPrice);
    midLine.Color = ScottPlot.Colors.White;
    midLine.LineStyle.Pattern = LinePattern.Dashed;
    
    plot.Refresh();
}
```

## 面试话术

### Q: 五档盘口有什么用？

**A**: 五档盘口是判断短期供需的核心工具：
1. **买卖力量对比**: 买盘总量 vs 卖盘总量
2. **支撑/阻力位**: 大单挂单的价位
3. **流动性判断**: 价差大说明流动性差
4. **市场情绪**: 买卖比 > 1 说明买盘强势

### Q: 深度图怎么看？

**A**: 深度图是盘口的可视化：
- X 轴是价格，Y 轴是累计挂单量
- 左边绿色是买盘，右边红色是卖盘
- 陡峭的"墙"表示该价位有大单
- 两边面积差异反映买卖力量对比

### Q: 回测中如何模拟盘口？

**A**: 我实现了两种模式：
1. **简单模式**: 只有最新价，假设无限流动性
2. **L1 模式**: 根据历史成交量估算盘口深度

L1 模式会根据订单大小计算滑点：如果订单量超过盘口挂单量，超出部分会以更差的价格成交。
