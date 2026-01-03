# 策略预热机制

## 概述

预热机制确保技术指标在有足够历史数据后才开始生成交易信号。这避免了指标在数据不足时产生的虚假信号。

## 问题分析

### 无预热的问题

```rust
// 问题：MA60 需要 60 个数据点才有效
fn calculate_ma60(&self, prices: &[f64]) -> f64 {
    if prices.len() < 60 {
        return prices.iter().sum::<f64>() / prices.len() as f64;  // ⚠️ 不准确！
    }
    prices[prices.len()-60..].iter().sum::<f64>() / 60.0
}
```

**问题**:
1. **指标不准确**: 数据不足时计算结果失真
2. **虚假信号**: 可能产生错误的交易信号
3. **回测失真**: 前期交易结果不可靠

## 解决方案

### 1. 预热管理器

```rust
// warmup.rs
/// 预热管理器
pub struct WarmupManager {
    warmup_bars: usize,           // 预热所需 bar 数
    current_bar: usize,           // 当前 bar 计数
    is_warmed_up: bool,           // 是否完成预热
    warmup_complete_timestamp: Option<i64>,  // 预热完成时间戳
}

impl WarmupManager {
    /// 创建预热管理器
    pub fn new(warmup_bars: i32) -> Self {
        let warmup_bars = warmup_bars.max(0) as usize;
        Self {
            warmup_bars,
            current_bar: 0,
            is_warmed_up: warmup_bars == 0,
            warmup_complete_timestamp: if warmup_bars == 0 { Some(0) } else { None },
        }
    }
    
    /// 处理新 bar，返回是否已完成预热
    pub fn tick(&mut self, timestamp: i64) -> bool {
        if self.is_warmed_up {
            return true;
        }
        
        self.current_bar += 1;
        
        if self.current_bar >= self.warmup_bars {
            self.is_warmed_up = true;
            self.warmup_complete_timestamp = Some(timestamp);
            log_info!("Warmup complete at bar {}", self.current_bar);
        }
        
        self.is_warmed_up
    }
    
    /// 检查是否完成预热
    pub fn is_warmed_up(&self) -> bool {
        self.is_warmed_up
    }
    
    /// 获取剩余预热 bar 数
    pub fn remaining_bars(&self) -> usize {
        if self.is_warmed_up {
            0
        } else {
            self.warmup_bars.saturating_sub(self.current_bar)
        }
    }
    
    /// 重置预热状态
    pub fn reset(&mut self) {
        self.current_bar = 0;
        self.is_warmed_up = self.warmup_bars == 0;
        self.warmup_complete_timestamp = if self.warmup_bars == 0 { Some(0) } else { None };
    }
}
```

### 2. 策略参数扩展

```rust
/// 策略参数 (含预热配置)
#[repr(C)]
pub struct StrategyParams {
    pub short_ma_period: i32,
    pub long_ma_period: i32,
    pub position_size: f64,
    pub stop_loss_pct: f64,
    pub take_profit_pct: f64,
    pub warmup_bars: i32,  // 新增：预热期
}

impl Default for StrategyParams {
    fn default() -> Self {
        Self {
            short_ma_period: 5,
            long_ma_period: 20,
            position_size: 100.0,
            stop_loss_pct: 0.02,
            take_profit_pct: 0.05,
            warmup_bars: 60,  // 默认 60 bar 预热
        }
    }
}
```

### 3. 引擎集成

```rust
impl BacktestEngine {
    pub fn new(params: StrategyParams, risk_config: RiskConfig) -> Self {
        Self {
            warmup_manager: WarmupManager::new(params.warmup_bars),
            // ...
        }
    }
    
    pub fn process_tick(&mut self, tick: &Tick) -> Result<Option<OrderRequest>, EngineError> {
        // 更新指标 (预热期内也要更新)
        self.update_indicators(tick);
        
        // 检查预热状态
        if !self.warmup_manager.tick(tick.timestamp) {
            // 预热期内：只更新指标，不生成信号
            return Ok(None);
        }
        
        // 预热完成：正常处理
        self.generate_signal(tick)
    }
}
```

### 4. 回测结果报告

```rust
/// 回测结果 (含预热信息)
#[repr(C)]
pub struct BacktestResult {
    pub total_trades: i32,
    pub winning_trades: i32,
    pub total_pnl: f64,
    pub max_drawdown: f64,
    pub sharpe_ratio: f64,
    pub actual_start_bar: i32,    // 新增：实际交易开始 bar
    pub warmup_bars: i32,         // 新增：预热 bar 数
    pub actual_start_timestamp: i64,  // 新增：实际交易开始时间
}
```

### 5. 预热感知策略 trait

```rust
/// 预热感知策略 trait
pub trait WarmupAware {
    fn warmup_manager(&self) -> &WarmupManager;
    fn warmup_manager_mut(&mut self) -> &mut WarmupManager;
    
    fn is_warmed_up(&self) -> bool {
        self.warmup_manager().is_warmed_up()
    }
    
    fn warmup_tick(&mut self, timestamp: i64) -> bool {
        self.warmup_manager_mut().tick(timestamp)
    }
}
```

## FFI 接口

```rust
/// FFI: 检查预热是否完成
#[no_mangle]
pub unsafe extern "C" fn is_warmup_complete(manager: *const WarmupManager) -> i32 {
    if manager.is_null() { return 0; }
    if (*manager).is_warmed_up() { 1 } else { 0 }
}

/// FFI: 获取当前 bar 计数
#[no_mangle]
pub unsafe extern "C" fn get_warmup_current_bar(manager: *const WarmupManager) -> i32 {
    if manager.is_null() { return 0; }
    (*manager).current_bar() as i32
}

/// FFI: 获取剩余预热 bar 数
#[no_mangle]
pub unsafe extern "C" fn get_warmup_remaining_bars(manager: *const WarmupManager) -> i32 {
    if manager.is_null() { return 0; }
    (*manager).remaining_bars() as i32
}
```

## 使用示例

```rust
// 创建带预热的策略参数
let params = StrategyParams {
    short_ma_period: 5,
    long_ma_period: 20,
    warmup_bars: 60,  // 预热 60 bar
    ..Default::default()
};

// 创建引擎
let mut engine = BacktestEngine::new(params, risk_config);

// 处理数据
for tick in ticks {
    match engine.process_tick(&tick) {
        Ok(Some(order)) => {
            // 预热完成后才会有订单
            println!("Order: {:?}", order);
        }
        Ok(None) => {
            // 预热期内或无信号
        }
        Err(e) => {
            println!("Error: {}", e);
        }
    }
}

// 获取结果
let result = engine.get_result();
println!("实际交易开始于 bar {}", result.actual_start_bar);
```

## 预热期计算建议

| 指标 | 建议预热期 |
|------|-----------|
| MA5 | 5 bars |
| MA20 | 20 bars |
| MA60 | 60 bars |
| MACD(12,26,9) | 35 bars |
| Bollinger(20,2) | 20 bars |
| RSI(14) | 14 bars |

**通用规则**: `warmup_bars = max(所有指标周期) + 缓冲`

## 面试话术

### Q: 为什么需要预热机制？

**A**: 技术指标需要历史数据才能准确计算。例如：
- MA60 需要 60 个数据点
- MACD 需要 35 个数据点 (26 + 9)

没有预热，前期指标值不准确，会产生虚假信号，导致回测结果失真。

### Q: 预热期内做什么？

**A**: 预热期内：
1. **更新指标**: 继续计算指标值
2. **不生成信号**: 不产生交易信号
3. **不执行订单**: 引擎不处理任何订单

这确保了指标在有足够数据后才开始影响交易决策。

### Q: 如何确定预热期长度？

**A**: 取所有使用指标的最大周期，再加一些缓冲：
```
warmup_bars = max(MA周期, MACD周期, ...) + buffer
```

例如使用 MA60 和 MACD(12,26,9)，预热期应该是 `max(60, 35) + 5 = 65` bars。
