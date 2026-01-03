using System.Windows;
using System.Windows.Controls;
using AegisQuant.Interop;
using AegisQuant.UI.Services;

namespace AegisQuant.UI.Controls;

/// <summary>
/// 手动下单面板
/// Requirements: 16.3, 16.4
/// </summary>
public partial class ManualOrderPanel : UserControl
{
    /// <summary>
    /// 下单请求事件
    /// </summary>
    public event EventHandler<ManualOrderEventArgs>? OrderRequested;

    public ManualOrderPanel()
    {
        InitializeComponent();
    }

    private void BuyButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitOrder(true);
    }

    private void SellButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitOrder(false);
    }

    private void SubmitOrder(bool isBuy)
    {
        try
        {
            // 验证输入
            var symbol = SymbolTextBox.Text.Trim();
            if (string.IsNullOrEmpty(symbol))
            {
                ShowStatus("请输入标的代码", true);
                return;
            }

            if (!double.TryParse(QuantityTextBox.Text, out var quantity) || quantity <= 0)
            {
                ShowStatus("请输入有效的数量", true);
                return;
            }

            var isLimitOrder = OrderTypeCombo.SelectedIndex == 1;
            double limitPrice = 0;

            if (isLimitOrder)
            {
                if (!double.TryParse(PriceTextBox.Text, out limitPrice) || limitPrice <= 0)
                {
                    ShowStatus("请输入有效的限价", true);
                    return;
                }
            }

            // 创建订单请求
            var order = new OrderRequest();
            order.SetSymbol(symbol);
            order.Quantity = quantity;
            order.Direction = isBuy ? Direction.Buy : Direction.Sell;
            order.OrderType = isLimitOrder ? Interop.OrderType.Limit : Interop.OrderType.Market;
            order.LimitPrice = limitPrice;

            // 记录审计日志
            var orderDetails = $"{symbol} {(isBuy ? "买入" : "卖出")} {quantity} @ {(isLimitOrder ? limitPrice.ToString("F2") : "市价")}";
            AuditLogService.Instance.LogOrderAction("手动下单", orderDetails);

            // 触发事件
            OrderRequested?.Invoke(this, new ManualOrderEventArgs(order));

            ShowStatus($"订单已提交: {orderDetails}", false);
        }
        catch (Exception ex)
        {
            ShowStatus($"下单失败: {ex.Message}", true);
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError 
            ? System.Windows.Media.Brushes.Red 
            : System.Windows.Media.Brushes.Green;
    }
}

/// <summary>
/// 手动下单事件参数
/// </summary>
public class ManualOrderEventArgs : EventArgs
{
    public OrderRequest Order { get; }

    public ManualOrderEventArgs(OrderRequest order)
    {
        Order = order;
    }
}
