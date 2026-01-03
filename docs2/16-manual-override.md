# 手动干预详解

## 概述

手动干预是交易系统的安全阀，允许交易员在紧急情况下接管自动交易。本文档详细说明如何实现紧急停止、一键清仓和手动下单功能。

## 问题分析

### 纯自动交易的风险

1. **失控风险**: 策略异常时无法干预
2. **黑天鹅事件**: 极端行情下需要人工判断
3. **系统故障**: 数据源异常时需要紧急停止
4. **合规要求**: 监管要求必须有人工干预能力

### 设计目标

- 一键紧急停止所有自动交易
- 一键清仓平掉所有持仓
- 手动下单覆盖自动策略
- 所有操作记录审计日志


## 解决方案

### Rust 侧紧急停止

```rust
// emergency.rs
use std::sync::atomic::{AtomicBool, Ordering};

/// 全局紧急停止标志
static EMERGENCY_HALT: AtomicBool = AtomicBool::new(false);

/// 检查是否处于紧急停止状态
#[inline]
pub fn is_halted() -> bool {
    EMERGENCY_HALT.load(Ordering::SeqCst)
}

/// 激活紧急停止
pub fn activate_emergency_stop() {
    EMERGENCY_HALT.store(true, Ordering::SeqCst);
    log(LogLevel::Error, "EMERGENCY STOP ACTIVATED - All trading halted");
}

/// 重置紧急停止
pub fn reset_emergency_stop() {
    EMERGENCY_HALT.store(false, Ordering::SeqCst);
    log(LogLevel::Info, "Emergency stop reset - Trading can resume");
}

/// 检查操作是否应被阻止
pub fn check_halt() -> EngineResult<()> {
    if is_halted() {
        Err(EngineError::risk_rejected("Emergency halt is active"))
    } else {
        Ok(())
    }
}
```

### 一键清仓

```rust
/// 生成平仓订单
pub fn generate_close_all_orders(positions: &[Position]) -> Vec<OrderRequest> {
    let mut orders = Vec::new();

    for position in positions {
        if position.quantity.abs() > 0.0 {
            // 多头平仓卖出，空头平仓买入
            let direction = if position.quantity > 0.0 { -1 } else { 1 };

            let mut order = OrderRequest::with_symbol(position.symbol_str());
            order.quantity = position.quantity.abs();
            order.direction = direction;
            order.order_type = 0; // 市价单
            order.limit_price = 0.0;

            orders.push(order);
        }
    }

    if !orders.is_empty() {
        log(LogLevel::Warn, 
            &format!("Close all positions: {} orders generated", orders.len()));
    }

    orders
}
```

### C# 紧急控制面板

```csharp
// EmergencyControlPanel.xaml.cs
public partial class EmergencyControlPanel : UserControl
{
    private bool _isHalted;

    public event EventHandler? EmergencyStopTriggered;
    public event EventHandler? CloseAllTriggered;
    public event EventHandler? ResumeTriggered;

    private void EmergencyStopButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要紧急停止所有自动交易吗？\n\n这将立即停止所有策略信号生成。",
            "紧急停止确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
            TriggerEmergencyStop();
    }

    private void TriggerEmergencyStop()
    {
        // 调用 Rust 侧紧急停止
        NativeMethods.EmergencyStop();
        
        IsHalted = true;
        
        // 记录审计日志
        AuditLogService.Instance.LogEmergencyStop("用户触发紧急停止");
        
        EmergencyStopTriggered?.Invoke(this, EventArgs.Empty);
    }

    private void TriggerCloseAll()
    {
        // 记录审计日志
        AuditLogService.Instance.LogOrderAction("一键清仓", "用户触发一键清仓");
        
        CloseAllTriggered?.Invoke(this, EventArgs.Empty);
        
        MessageBox.Show("清仓指令已发送", "提示", 
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void TriggerResume()
    {
        // 调用 Rust 侧重置紧急停止
        NativeMethods.ResetEmergencyStop();
        
        IsHalted = false;
        
        // 记录审计日志
        AuditLogService.Instance.Log(AuditActionType.Other, "用户恢复自动交易");
        
        ResumeTriggered?.Invoke(this, EventArgs.Empty);
    }
}
```

### 手动下单面板

```csharp
// ManualOrderPanel.xaml.cs
public partial class ManualOrderPanel : UserControl
{
    public event EventHandler<ManualOrderEventArgs>? OrderRequested;

    private void SubmitOrder(bool isBuy)
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
        order.OrderType = isLimitOrder ? OrderType.Limit : OrderType.Market;
        order.LimitPrice = limitPrice;

        // 记录审计日志
        var orderDetails = $"{symbol} {(isBuy ? "买入" : "卖出")} {quantity} @ {(isLimitOrder ? limitPrice.ToString("F2") : "市价")}";
        AuditLogService.Instance.LogOrderAction("手动下单", orderDetails);

        // 触发事件
        OrderRequested?.Invoke(this, new ManualOrderEventArgs(order));

        ShowStatus($"订单已提交: {orderDetails}", false);
    }
}
```

### UI 状态更新

```csharp
private void UpdateUI()
{
    if (_isHalted)
    {
        // 紧急停止状态 - 红色边框和背景
        MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x14, 0x3C));
        MainBorder.BorderThickness = new Thickness(3);
        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(0xDC, 0x14, 0x3C));
        StatusText.Text = "⚠ 紧急停止中";
        
        EmergencyStopButton.IsEnabled = false;
        ResumeButton.Visibility = Visibility.Visible;
    }
    else
    {
        // 正常状态 - 绿色
        MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        StatusText.Text = "自动交易运行中";
        
        EmergencyStopButton.IsEnabled = true;
        ResumeButton.Visibility = Visibility.Collapsed;
    }
}
```

## 使用示例

```csharp
// 紧急停止
emergencyPanel.EmergencyStopTriggered += (s, e) =>
{
    // 停止策略引擎
    engine.Stop();
};

// 一键清仓
emergencyPanel.CloseAllTriggered += (s, e) =>
{
    var positions = engine.GetPositions();
    foreach (var pos in positions)
    {
        engine.ClosePosition(pos.Symbol);
    }
};

// 手动下单
manualOrderPanel.OrderRequested += (s, e) =>
{
    engine.SubmitOrder(e.Order);
};
```

## 面试话术

### Q: 为什么使用 SeqCst 内存序？

**A**: 紧急停止需要最强一致性：
1. **立即可见**: 所有线程立即看到停止标志
2. **顺序一致**: 保证操作顺序
3. **安全优先**: 性能损失可接受

### Q: 一键清仓为什么用市价单？

**A**: 紧急情况下速度优先：
1. **立即成交**: 不等待价格匹配
2. **确保平仓**: 避免限价单挂单不成交
3. **风险控制**: 快速降低风险敞口

### Q: 手动下单如何与自动策略协调？

**A**: 两种模式：
1. **覆盖模式**: 手动订单优先，暂停自动策略
2. **并行模式**: 手动订单独立执行，不影响自动策略

实际项目中通常使用覆盖模式，手动干预时暂停自动交易。
