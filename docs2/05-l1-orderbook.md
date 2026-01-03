# L1 订单簿模拟 (Market Microstructure)

## 概述

L1 订单簿模拟是量化回测系统中提升仿真精度的关键模块。通过模拟真实市场的盘口深度和流动性，可以更准确地评估策略在实盘中的表现。

## 问题分析

### 简单模式的局限性

```rust
// 简单模式：假设无限流动性
fn execute_order_simple(order: &OrderRequest, current_price: f64) -> f64 {
    let slippage = 0.001;  // 固定 0.1% 滑点
    if order.direction > 0 {
        current_price * (1.0 + slippage)  // 买入
    } else {
        current_price * (1.0 - slippage)  // 卖出
    }
}
```

**问题**:
1. **无限流动性假设**: 忽略了盘口挂单量限制
2. **固定滑点**: 实际滑点与订单大小相关
3. **无部分成交**: 大单可能无法一次性成交

## 解决方案

### 1. 订单簿数据结构

```rust
// orderbook.rs
/// 单个价格档位
#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct OrderBookLevel {
    pub price: f64,      // 价格
    pub quantity: f64,   // 挂单量
    pub order_count: i32, // 订单数量
}

/// 订单簿快照 (10 档)
#[repr(C)]
pub struct OrderBookSnapshot {
    pub bids: [OrderBookLevel; 10],  // 买盘 (价格降序)
    pub asks: [OrderBookLevel; 10],  // 卖盘 (价格升序)
    pub bid_count: i32,
    pub ask_count: i32,
    pub last_price: f64,
    pub timestamp: i64,
}

impl OrderBookSnapshot {
    /// 获取最优买价
    pub fn best_bid(&self) -> Option<f64> {
        if self.bid_count > 0 { Some(self.bids[0].price) } else { None }
    }
    
    /// 获取最优卖价
    pub fn best_ask(&self) -> Option<f64> {
        if self.ask_count > 0 { Some(self.asks[0].price) } else { None }
    }
    
    /// 计算买卖价差
    pub fn spread(&self) -> Option<f64> {
        match (self.best_bid(), self.best_ask()) {
            (Some(bid), Some(ask)) => Some(ask - bid),
            _ => None,
        }
    }
}
```

### 2. 滑点模型

```rust
// l1_gateway.rs
/// 滑点模型
pub struct SlippageModel {
    pub base_slippage: f64,   // 基础滑点 (如 0.0001 = 1bps)
    pub impact_factor: f64,   // 冲击因子 (每单位数量的额外滑点)
    pub max_slippage: f64,    // 最大滑点上限
}

impl SlippageModel {
    /// 计算滑点
    pub fn calculate(&self, quantity: f64) -> f64 {
        let slippage = self.base_slippage + self.impact_factor * quantity;
        slippage.min(self.max_slippage)
    }
}
```

### 3. L1 撮合引擎

```rust
/// L1 模拟网关
pub struct L1SimulatedGateway {
    orderbook: OrderBookSnapshot,
    slippage_model: SlippageModel,
    fill_ratio: f64,  // 最大成交比例 (如 0.5 = 最多吃掉 50% 挂单)
}

impl L1SimulatedGateway {
    /// 执行订单 - 根据盘口深度计算成交
    pub fn execute_order(&self, order: &OrderRequest) -> FillResult {
        let mut remaining = order.quantity;
        let mut total_cost = 0.0;
        let mut fills = Vec::new();
        
        // 选择对手盘
        let levels = if order.direction > 0 {
            &self.orderbook.asks  // 买单吃卖盘
        } else {
            &self.orderbook.bids  // 卖单吃买盘
        };
        
        // 逐档撮合
        for (level_idx, level) in levels.iter().enumerate() {
            if remaining <= 0.0 || level.quantity <= 0.0 {
                break;
            }
            
            // 计算可成交量 (受 fill_ratio 限制)
            let available = level.quantity * self.fill_ratio;
            let fill_qty = remaining.min(available);
            
            // 计算成交价 (含滑点)
            let slippage = self.slippage_model.calculate(fill_qty);
            let fill_price = if order.direction > 0 {
                level.price * (1.0 + slippage)  // 买入价格上浮
            } else {
                level.price * (1.0 - slippage)  // 卖出价格下浮
            };
            
            fills.push(LevelFill {
                price: fill_price,
                quantity: fill_qty,
                level: level_idx,
            });
            
            total_cost += fill_price * fill_qty;
            remaining -= fill_qty;
        }
        
        FillResult {
            fills,
            unfilled: remaining,
            average_price: total_cost / (order.quantity - remaining),
            filled_quantity: order.quantity - remaining,
        }
    }
}
```

### 4. 盘口统计

```rust
/// 盘口统计信息
#[repr(C)]
pub struct OrderBookStats {
    pub total_bid_volume: f64,   // 买盘总量
    pub total_ask_volume: f64,   // 卖盘总量
    pub spread: f64,             // 买卖价差
    pub spread_bps: f64,         // 价差基点
    pub bid_ask_ratio: f64,      // 买卖比
    pub total_bid_orders: i32,   // 买单数量
    pub total_ask_orders: i32,   // 卖单数量
}

impl OrderBookSnapshot {
    pub fn get_stats(&self) -> OrderBookStats {
        let total_bid_volume: f64 = self.bids[..self.bid_count as usize]
            .iter().map(|l| l.quantity).sum();
        let total_ask_volume: f64 = self.asks[..self.ask_count as usize]
            .iter().map(|l| l.quantity).sum();
        
        OrderBookStats {
            total_bid_volume,
            total_ask_volume,
            spread: self.spread().unwrap_or(0.0),
            spread_bps: spread_bps(
                self.best_bid().unwrap_or(0.0),
                self.best_ask().unwrap_or(0.0)
            ),
            bid_ask_ratio: total_bid_volume / total_ask_volume.max(0.0001),
            // ...
        }
    }
}
```

### 5. 网关模式切换

```rust
/// 网关模式
#[repr(i32)]
pub enum GatewayMode {
    Simple = 0,  // 简单模式：固定滑点
    L1 = 1,      // L1 模式：基于盘口深度
}

static GATEWAY_MODE: AtomicI32 = AtomicI32::new(0);

/// FFI: 设置网关模式
#[no_mangle]
pub extern "C" fn set_gateway_mode(mode: i32) -> i32 {
    GATEWAY_MODE.store(mode, Ordering::SeqCst);
    ERR_SUCCESS
}
```

## 使用示例

```rust
// 创建 L1 网关
let mut gateway = L1SimulatedGateway::new(
    100_000.0,  // 初始资金
    SlippageModel {
        base_slippage: 0.0001,   // 1 bps
        impact_factor: 0.00001, // 每单位额外滑点
        max_slippage: 0.01,     // 最大 1%
    },
    0.0001,  // 手续费率
);

// 设置成交比例
gateway.set_fill_ratio(0.5);  // 最多吃掉 50% 挂单

// 更新盘口
gateway.update_orderbook(orderbook_snapshot);

// 执行订单
let result = gateway.execute_order(&order);
println!("成交量: {}, 均价: {}, 未成交: {}", 
    result.filled_quantity, 
    result.average_price, 
    result.unfilled);
```

## 面试话术

### Q: 为什么需要 L1 订单簿模拟？

**A**: 简单回测假设无限流动性，会高估策略收益。L1 模拟解决三个问题：
1. **流动性限制**: 大单可能无法一次成交
2. **滑点真实化**: 滑点与订单大小相关
3. **部分成交**: 模拟真实的分档成交

### Q: fill_ratio 参数的作用是什么？

**A**: `fill_ratio` 限制每档最大成交比例，原因是：
1. **避免过度乐观**: 不可能吃掉所有挂单
2. **模拟竞争**: 其他交易者也在抢单
3. **保守估计**: 0.5 意味着最多成交 50%

### Q: 滑点模型如何设计？

**A**: 我使用线性滑点模型：`slippage = base + impact * quantity`
- `base_slippage`: 基础滑点，反映市场摩擦
- `impact_factor`: 冲击因子，反映大单对价格的影响
- `max_slippage`: 上限保护，防止极端情况

实际可以根据历史数据拟合更复杂的模型。
