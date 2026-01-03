using System.Windows.Media;
using AegisQuant.UI.Services;

namespace AegisQuant.UI.Models;

/// <summary>
/// 交易方向
/// </summary>
public enum TradeDirection
{
    /// <summary>买入</summary>
    Buy,
    /// <summary>卖出</summary>
    Sell
}

/// <summary>
/// 买卖标记数据模型
/// </summary>
public class TradeMarker
{
    /// <summary>K 线索引</summary>
    public int BarIndex { get; set; }

    /// <summary>成交价格</summary>
    public double Price { get; set; }

    /// <summary>成交数量</summary>
    public double Quantity { get; set; }

    /// <summary>交易方向</summary>
    public TradeDirection Direction { get; set; }

    /// <summary>成交时间</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>订单 ID</summary>
    public string? OrderId { get; set; }

    /// <summary>备注</summary>
    public string? Note { get; set; }

    /// <summary>是否为买入</summary>
    public bool IsBuy => Direction == TradeDirection.Buy;

    /// <summary>是否为卖出</summary>
    public bool IsSell => Direction == TradeDirection.Sell;

    /// <summary>获取标记颜色</summary>
    public Color MarkerColor => IsBuy 
        ? ColorSchemeService.Instance.UpColor 
        : ColorSchemeService.Instance.DownColor;

    /// <summary>获取标记画刷</summary>
    public SolidColorBrush MarkerBrush => new(MarkerColor);

    /// <summary>获取标记符号 (箭头)</summary>
    public string MarkerSymbol => IsBuy ? "▲" : "▼";

    /// <summary>获取标记提示文本</summary>
    public string TooltipText => $"{(IsBuy ? "买入" : "卖出")} {Quantity:N0} @ {Price:F2}\n{Timestamp:yyyy-MM-dd HH:mm:ss}";

    /// <summary>
    /// 创建买入标记
    /// </summary>
    public static TradeMarker CreateBuy(int barIndex, double price, double quantity, DateTime timestamp, string? orderId = null)
    {
        return new TradeMarker
        {
            BarIndex = barIndex,
            Price = price,
            Quantity = quantity,
            Direction = TradeDirection.Buy,
            Timestamp = timestamp,
            OrderId = orderId
        };
    }

    /// <summary>
    /// 创建卖出标记
    /// </summary>
    public static TradeMarker CreateSell(int barIndex, double price, double quantity, DateTime timestamp, string? orderId = null)
    {
        return new TradeMarker
        {
            BarIndex = barIndex,
            Price = price,
            Quantity = quantity,
            Direction = TradeDirection.Sell,
            Timestamp = timestamp,
            OrderId = orderId
        };
    }
}

/// <summary>
/// 买卖标记管理器
/// </summary>
public class TradeMarkerManager
{
    private readonly List<TradeMarker> _markers = new();

    /// <summary>所有标记</summary>
    public IReadOnlyList<TradeMarker> Markers => _markers.AsReadOnly();

    /// <summary>买入标记</summary>
    public IEnumerable<TradeMarker> BuyMarkers => _markers.Where(m => m.IsBuy);

    /// <summary>卖出标记</summary>
    public IEnumerable<TradeMarker> SellMarkers => _markers.Where(m => m.IsSell);

    /// <summary>标记数量</summary>
    public int Count => _markers.Count;

    /// <summary>
    /// 添加标记
    /// </summary>
    public void Add(TradeMarker marker)
    {
        _markers.Add(marker);
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 添加买入标记
    /// </summary>
    public void AddBuy(int barIndex, double price, double quantity, DateTime timestamp, string? orderId = null)
    {
        Add(TradeMarker.CreateBuy(barIndex, price, quantity, timestamp, orderId));
    }

    /// <summary>
    /// 添加卖出标记
    /// </summary>
    public void AddSell(int barIndex, double price, double quantity, DateTime timestamp, string? orderId = null)
    {
        Add(TradeMarker.CreateSell(barIndex, price, quantity, timestamp, orderId));
    }

    /// <summary>
    /// 移除标记
    /// </summary>
    public bool Remove(TradeMarker marker)
    {
        var result = _markers.Remove(marker);
        if (result)
        {
            MarkersChanged?.Invoke(this, EventArgs.Empty);
        }
        return result;
    }

    /// <summary>
    /// 清除所有标记
    /// </summary>
    public void Clear()
    {
        _markers.Clear();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 获取指定 K 线索引的标记
    /// </summary>
    public IEnumerable<TradeMarker> GetMarkersAt(int barIndex)
    {
        return _markers.Where(m => m.BarIndex == barIndex);
    }

    /// <summary>
    /// 获取指定范围内的标记
    /// </summary>
    public IEnumerable<TradeMarker> GetMarkersInRange(int startIndex, int endIndex)
    {
        return _markers.Where(m => m.BarIndex >= startIndex && m.BarIndex <= endIndex);
    }

    /// <summary>
    /// 标记变更事件
    /// </summary>
    public event EventHandler? MarkersChanged;
}
