using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AegisQuant.Interop;
using AegisQuant.UI.Services;
using System.Windows.Media;

namespace AegisQuant.UI.ViewModels;

/// <summary>
/// 盘口档位数据模型
/// </summary>
public partial class OrderBookLevelViewModel : ObservableObject
{
    /// <summary>档位序号 (1-5)</summary>
    [ObservableProperty]
    private int _level;

    /// <summary>档位名称 (如 "买一", "卖五")</summary>
    [ObservableProperty]
    private string _levelName = string.Empty;

    /// <summary>价格</summary>
    [ObservableProperty]
    private double _price;

    /// <summary>数量</summary>
    [ObservableProperty]
    private double _quantity;

    /// <summary>订单数</summary>
    [ObservableProperty]
    private int _orderCount;

    /// <summary>数量占比 (0-1)，用于显示量能背景条</summary>
    [ObservableProperty]
    private double _quantityRatio;

    /// <summary>是否为买盘</summary>
    [ObservableProperty]
    private bool _isBid;

    /// <summary>
    /// 从 Rust 结构体更新数据
    /// </summary>
    public void UpdateFrom(OrderBookLevel level, double maxQuantity)
    {
        Price = level.Price;
        Quantity = level.Quantity;
        OrderCount = level.OrderCount;
        QuantityRatio = maxQuantity > 0 ? Math.Min(1.0, level.Quantity / maxQuantity) : 0;
    }
}

/// <summary>
/// 五档盘口 ViewModel
/// </summary>
public partial class OrderBookViewModel : ObservableObject
{
    /// <summary>买盘档位 (买一到买五)</summary>
    [ObservableProperty]
    private ObservableCollection<OrderBookLevelViewModel> _bids = new();

    /// <summary>卖盘档位 (卖五到卖一，从上到下显示)</summary>
    [ObservableProperty]
    private ObservableCollection<OrderBookLevelViewModel> _asks = new();

    /// <summary>最新价</summary>
    [ObservableProperty]
    private double _lastPrice;

    /// <summary>参考价格 (昨收价)</summary>
    [ObservableProperty]
    private double _referencePrice;

    /// <summary>价格变化</summary>
    [ObservableProperty]
    private double _priceChange;

    /// <summary>价格变化百分比</summary>
    [ObservableProperty]
    private double _priceChangePercent;

    /// <summary>买盘总量</summary>
    [ObservableProperty]
    private double _totalBidVolume;

    /// <summary>卖盘总量</summary>
    [ObservableProperty]
    private double _totalAskVolume;

    /// <summary>买卖比</summary>
    [ObservableProperty]
    private double _bidAskRatio;

    /// <summary>价差</summary>
    [ObservableProperty]
    private double _spread;

    /// <summary>价差基点</summary>
    [ObservableProperty]
    private double _spreadBps;

    /// <summary>时间戳</summary>
    [ObservableProperty]
    private long _timestamp;

    /// <summary>价格变化颜色</summary>
    public SolidColorBrush PriceChangeColor => ColorSchemeService.Instance.GetPriceChangeBrush(PriceChange);

    public OrderBookViewModel()
    {
        InitializeLevels();
    }

    /// <summary>
    /// 初始化档位
    /// </summary>
    private void InitializeLevels()
    {
        // 初始化买盘 (买一到买五)
        for (int i = 1; i <= 5; i++)
        {
            Bids.Add(new OrderBookLevelViewModel
            {
                Level = i,
                LevelName = $"买{GetChineseNumber(i)}",
                IsBid = true
            });
        }

        // 初始化卖盘 (卖五到卖一，从上到下)
        for (int i = 5; i >= 1; i--)
        {
            Asks.Add(new OrderBookLevelViewModel
            {
                Level = i,
                LevelName = $"卖{GetChineseNumber(i)}",
                IsBid = false
            });
        }
    }

    /// <summary>
    /// 从 Rust 快照更新数据
    /// </summary>
    public void UpdateFromSnapshot(OrderBookSnapshot snapshot)
    {
        var bids = snapshot.GetBids();
        var asks = snapshot.GetAsks();

        // 计算最大数量用于量能条
        double maxQuantity = 0;
        foreach (var bid in bids)
        {
            maxQuantity = Math.Max(maxQuantity, bid.Quantity);
        }
        foreach (var ask in asks)
        {
            maxQuantity = Math.Max(maxQuantity, ask.Quantity);
        }

        // 更新买盘
        for (int i = 0; i < Bids.Count && i < bids.Length; i++)
        {
            Bids[i].UpdateFrom(bids[i], maxQuantity);
        }

        // 更新卖盘 (注意顺序：Asks 集合是从卖五到卖一)
        for (int i = 0; i < Asks.Count && i < asks.Length; i++)
        {
            // Asks[0] = 卖五, Asks[4] = 卖一
            // asks[0] = 卖一 (最优卖价)
            int askIndex = Asks.Count - 1 - i;
            if (askIndex >= 0 && i < asks.Length)
            {
                Asks[askIndex].UpdateFrom(asks[i], maxQuantity);
            }
        }

        // 更新最新价
        LastPrice = snapshot.LastPrice;
        Timestamp = snapshot.Timestamp;

        // 计算价格变化
        if (ReferencePrice > 0)
        {
            PriceChange = LastPrice - ReferencePrice;
            PriceChangePercent = PriceChange / ReferencePrice;
        }

        // 计算统计数据
        CalculateStats(bids, asks);

        // 通知颜色属性变更
        OnPropertyChanged(nameof(PriceChangeColor));
    }

    /// <summary>
    /// 从统计数据更新
    /// </summary>
    public void UpdateFromStats(OrderBookStats stats)
    {
        TotalBidVolume = stats.TotalBidVolume;
        TotalAskVolume = stats.TotalAskVolume;
        Spread = stats.Spread;
        SpreadBps = stats.SpreadBps;
        BidAskRatio = stats.BidAskRatio;
    }

    /// <summary>
    /// 计算统计数据
    /// </summary>
    private void CalculateStats(OrderBookLevel[] bids, OrderBookLevel[] asks)
    {
        TotalBidVolume = bids.Sum(b => b.Quantity);
        TotalAskVolume = asks.Sum(a => a.Quantity);

        if (bids.Length > 0 && asks.Length > 0)
        {
            Spread = asks[0].Price - bids[0].Price;
            if (bids[0].Price > 0)
            {
                SpreadBps = (Spread / bids[0].Price) * 10000;
            }
        }

        if (TotalAskVolume > 0)
        {
            BidAskRatio = TotalBidVolume / TotalAskVolume;
        }
    }

    /// <summary>
    /// 设置参考价格 (昨收价)
    /// </summary>
    public void SetReferencePrice(double price)
    {
        ReferencePrice = price;
        if (ReferencePrice > 0 && LastPrice > 0)
        {
            PriceChange = LastPrice - ReferencePrice;
            PriceChangePercent = PriceChange / ReferencePrice;
            OnPropertyChanged(nameof(PriceChangeColor));
        }
    }

    /// <summary>
    /// 重置数据
    /// </summary>
    public void Reset()
    {
        foreach (var bid in Bids)
        {
            bid.Price = 0;
            bid.Quantity = 0;
            bid.OrderCount = 0;
            bid.QuantityRatio = 0;
        }

        foreach (var ask in Asks)
        {
            ask.Price = 0;
            ask.Quantity = 0;
            ask.OrderCount = 0;
            ask.QuantityRatio = 0;
        }

        LastPrice = 0;
        PriceChange = 0;
        PriceChangePercent = 0;
        TotalBidVolume = 0;
        TotalAskVolume = 0;
        Spread = 0;
        SpreadBps = 0;
        BidAskRatio = 0;
    }

    /// <summary>
    /// 获取中文数字
    /// </summary>
    private static string GetChineseNumber(int n)
    {
        return n switch
        {
            1 => "一",
            2 => "二",
            3 => "三",
            4 => "四",
            5 => "五",
            6 => "六",
            7 => "七",
            8 => "八",
            9 => "九",
            10 => "十",
            _ => n.ToString()
        };
    }
}
