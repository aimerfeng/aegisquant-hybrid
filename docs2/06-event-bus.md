# 异步事件总线架构

## 概述

事件总线是解耦组件间通信的核心模块。通过发布-订阅模式，策略可以响应多种事件类型，而不需要直接依赖数据源。

## 问题分析

### 传统同步模式的局限

```rust
// 传统模式：策略直接处理 Tick
impl Strategy for MyStrategy {
    fn on_tick(&mut self, tick: &Tick) -> Option<OrderRequest> {
        // 只能处理 Tick 事件
        // 无法响应定时器、订单状态变化等
    }
}
```

**问题**:
1. **单一事件源**: 只能处理 Tick
2. **紧耦合**: 策略直接依赖数据源
3. **无法扩展**: 添加新事件类型需要修改接口

## 解决方案

### 1. 事件类型定义

```rust
// event_bus.rs
/// 事件类型枚举
#[derive(Debug, Clone)]
pub enum Event {
    /// 行情 Tick
    Tick(Tick),
    
    /// 定时器事件
    Timer {
        id: u64,
        timestamp: i64,
    },
    
    /// 订单状态更新
    OrderUpdate {
        order_id: u64,
        status: OrderStatus,
        filled_quantity: f64,
        fill_price: f64,
    },
    
    /// 账户状态更新
    AccountUpdate(AccountStatus),
    
    /// 交易信号
    Signal {
        symbol: String,
        direction: i32,
        strength: f64,
    },
    
    /// 自定义事件
    Custom {
        event_type: String,
        payload: String,
    },
}

/// 订单状态
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OrderStatus {
    Pending = 0,
    PartiallyFilled = 1,
    Filled = 2,
    Cancelled = 3,
    Rejected = 4,
}
```

### 2. 事件过滤器

```rust
/// 事件过滤器 - 选择性订阅
#[derive(Debug, Clone, Default)]
pub struct EventFilter {
    pub tick: bool,
    pub timer: bool,
    pub order_update: bool,
    pub account_update: bool,
    pub signal: bool,
    pub custom: bool,
}

impl EventFilter {
    /// 订阅所有事件
    pub fn all() -> Self {
        Self {
            tick: true,
            timer: true,
            order_update: true,
            account_update: true,
            signal: true,
            custom: true,
        }
    }
    
    /// 只订阅 Tick
    pub fn tick_only() -> Self {
        Self { tick: true, ..Default::default() }
    }
    
    /// 检查事件是否匹配
    pub fn matches(&self, event: &Event) -> bool {
        match event {
            Event::Tick(_) => self.tick,
            Event::Timer { .. } => self.timer,
            Event::OrderUpdate { .. } => self.order_update,
            Event::AccountUpdate(_) => self.account_update,
            Event::Signal { .. } => self.signal,
            Event::Custom { .. } => self.custom,
        }
    }
}
```

### 3. 事件总线实现

```rust
use crossbeam_channel::{bounded, Sender, Receiver, TrySendError};

/// 事件总线
pub struct EventBus {
    subscribers: Vec<SubscriberEntry>,
    default_capacity: usize,
    events_published: u64,
    events_delivered: u64,
    events_dropped: u64,
}

struct SubscriberEntry {
    id: SubscriptionId,
    sender: Sender<Event>,
    filter: EventFilter,
}

impl EventBus {
    pub fn new(capacity: usize) -> Self {
        Self {
            subscribers: Vec::new(),
            default_capacity: capacity,
            events_published: 0,
            events_delivered: 0,
            events_dropped: 0,
        }
    }
    
    /// 订阅事件
    pub fn subscribe(&mut self, filter: EventFilter) -> Subscription {
        let (sender, receiver) = bounded(self.default_capacity);
        let id = next_subscription_id();
        
        self.subscribers.push(SubscriberEntry {
            id,
            sender,
            filter: filter.clone(),
        });
        
        Subscription { id, receiver, filter }
    }
    
    /// 发布事件 (非阻塞)
    pub fn publish(&mut self, event: Event) -> usize {
        self.events_published += 1;
        let mut delivered = 0;
        
        for subscriber in &self.subscribers {
            if subscriber.filter.matches(&event) {
                match subscriber.sender.try_send(event.clone()) {
                    Ok(()) => {
                        delivered += 1;
                        self.events_delivered += 1;
                    }
                    Err(TrySendError::Full(_)) => {
                        self.events_dropped += 1;
                    }
                    Err(TrySendError::Disconnected(_)) => {
                        // 订阅者已断开
                    }
                }
            }
        }
        
        delivered
    }
    
    /// 取消订阅
    pub fn unsubscribe(&mut self, id: SubscriptionId) -> bool {
        let len = self.subscribers.len();
        self.subscribers.retain(|s| s.id != id);
        self.subscribers.len() < len
    }
}
```

### 4. 订阅句柄

```rust
/// 订阅句柄
pub struct Subscription {
    pub id: SubscriptionId,
    receiver: Receiver<Event>,
    filter: EventFilter,
}

impl Subscription {
    /// 非阻塞接收
    pub fn try_recv(&self) -> Result<Event, TryRecvError> {
        self.receiver.try_recv()
    }
    
    /// 阻塞接收
    pub fn recv(&self) -> Result<Event, RecvError> {
        self.receiver.recv()
    }
    
    /// 带超时接收
    pub fn recv_timeout(&self, timeout: Duration) -> Result<Event, RecvTimeoutError> {
        self.receiver.recv_timeout(timeout)
    }
    
    /// 待处理事件数量
    pub fn len(&self) -> usize {
        self.receiver.len()
    }
}
```

### 5. 定时器管理

```rust
/// 定时器条目
pub struct TimerEntry {
    pub id: TimerId,
    pub interval_ms: u64,
    pub next_trigger_ms: i64,
    pub active: bool,
    pub repeating: bool,
}

/// 定时器管理器
pub struct TimerManager {
    timers: Vec<TimerEntry>,
    current_time_ms: i64,
}

impl TimerManager {
    /// 设置一次性定时器
    pub fn schedule_once(&mut self, delay_ms: u64) -> TimerId {
        let timer = TimerEntry::one_shot(self.current_time_ms + delay_ms as i64);
        let id = timer.id;
        self.timers.push(timer);
        id
    }
    
    /// 设置重复定时器
    pub fn schedule_repeating(&mut self, interval_ms: u64) -> TimerId {
        let timer = TimerEntry::repeating(interval_ms, self.current_time_ms);
        let id = timer.id;
        self.timers.push(timer);
        id
    }
    
    /// 处理定时器，返回触发的事件
    pub fn process(&mut self, current_time_ms: i64) -> Vec<Event> {
        self.current_time_ms = current_time_ms;
        let mut events = Vec::new();
        
        for timer in &mut self.timers {
            if timer.should_fire(current_time_ms) {
                events.push(Event::timer(timer.id, current_time_ms));
                timer.advance();
            }
        }
        
        // 清理已完成的定时器
        self.timers.retain(|t| t.active);
        events
    }
}
```

### 6. 事件驱动策略接口

```rust
/// 事件驱动策略 trait
pub trait EventDrivenStrategy {
    fn on_tick(&mut self, tick: &Tick) -> Option<OrderRequest>;
    fn on_timer(&mut self, id: u64, timestamp: i64);
    fn on_order_update(&mut self, order_id: u64, status: &OrderStatus, 
                       filled_qty: f64, fill_price: f64);
    fn on_account_update(&mut self, status: &AccountStatus);
    
    fn event_filter(&self) -> EventFilter {
        EventFilter::all()
    }
}
```

## FFI 接口

```rust
/// FFI: 订阅事件
#[no_mangle]
pub unsafe extern "C" fn subscribe_event(
    event_bus: *mut EventBus,
    filter_mask: i32,  // 位掩码: bit0=tick, bit1=timer, ...
) -> i64 {
    if event_bus.is_null() { return ERR_NULL_POINTER as i64; }
    
    let filter = EventFilter {
        tick: (filter_mask & 0x01) != 0,
        timer: (filter_mask & 0x02) != 0,
        order_update: (filter_mask & 0x04) != 0,
        account_update: (filter_mask & 0x08) != 0,
        signal: (filter_mask & 0x10) != 0,
        custom: (filter_mask & 0x20) != 0,
    };
    
    let bus = &mut *event_bus;
    let subscription = bus.subscribe(filter);
    subscription.id as i64
}
```

## 使用示例

```rust
// 创建事件总线
let mut bus = EventBus::new(1000);

// 订阅所有事件
let sub_all = bus.subscribe(EventFilter::all());

// 只订阅 Tick 和订单更新
let sub_trading = bus.subscribe(EventFilter {
    tick: true,
    order_update: true,
    ..Default::default()
});

// 发布事件
bus.publish(Event::tick(tick));
bus.publish(Event::order_update(order_id, OrderStatus::Filled, 100.0, 50.0));

// 接收事件
while let Ok(event) = sub_all.try_recv() {
    match event {
        Event::Tick(tick) => strategy.on_tick(&tick),
        Event::OrderUpdate { order_id, status, .. } => {
            strategy.on_order_update(order_id, &status, 0.0, 0.0)
        }
        _ => {}
    }
}
```

## 面试话术

### Q: 为什么选择 crossbeam-channel？

**A**: crossbeam-channel 相比标准库有三个优势：
1. **性能**: 无锁实现，比 `std::sync::mpsc` 快 2-3 倍
2. **功能**: 支持 bounded/unbounded、select、超时
3. **安全**: 完全 safe Rust，无 unsafe 代码

### Q: 如何处理事件积压？

**A**: 使用 bounded channel + try_send：
1. **有界队列**: 限制内存使用
2. **非阻塞发送**: 队列满时丢弃事件
3. **统计监控**: 记录 dropped 事件数量

对于关键事件（如订单更新），可以使用 unbounded channel 或阻塞发送。

### Q: 事件总线和直接调用有什么区别？

**A**: 事件总线提供：
1. **解耦**: 发布者不需要知道订阅者
2. **多播**: 一个事件可以发送给多个订阅者
3. **过滤**: 订阅者只接收感兴趣的事件
4. **异步**: 发布者不等待订阅者处理完成
