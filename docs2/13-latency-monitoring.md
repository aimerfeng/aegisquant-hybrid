# 延迟监控详解

## 概述

延迟监控是高频交易系统的关键性能指标。本文档详细说明如何实现纳秒级延迟追踪，包括 Rust 侧的原子操作实现和 C# 侧的实时监控服务。

## 问题分析

### 延迟监控的挑战

1. **精度要求高**: 需要纳秒级精度
2. **开销要求低**: 监控本身不能影响性能
3. **线程安全**: 多线程环境下的并发访问
4. **统计需求**: 需要 P50/P95/P99 等百分位数

### 设计目标

- 纳秒级精度延迟记录
- 原子操作保证线程安全
- 支持采样率控制
- 提供完整统计指标


## 解决方案

### Rust 侧延迟追踪器

```rust
// latency.rs
const MAX_SAMPLES: usize = 10000;

#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct LatencyStats {
    pub min_ns: u64,
    pub max_ns: u64,
    pub avg_ns: u64,
    pub p50_ns: u64,
    pub p95_ns: u64,
    pub p99_ns: u64,
    pub sample_count: u64,
    pub last_ns: u64,
}

pub struct LatencyTracker {
    min_ns: AtomicU64,
    max_ns: AtomicU64,
    sum_ns: AtomicU64,
    count: AtomicUsize,
    last_ns: AtomicU64,
    samples: Vec<AtomicU64>,  // 环形缓冲区
    write_index: AtomicUsize,
    enabled: AtomicBool,
    sample_rate: AtomicUsize,
    sample_counter: AtomicUsize,
}
```

### 原子操作记录延迟

```rust
/// 记录一次延迟测量
pub fn record(&self, latency_ns: u64) {
    if !self.enabled.load(Ordering::Relaxed) {
        return;
    }

    // 检查采样率
    let counter = self.sample_counter.fetch_add(1, Ordering::Relaxed);
    let rate = self.sample_rate.load(Ordering::Relaxed);
    if rate > 1 && !counter.is_multiple_of(rate) {
        return;
    }

    // 原子更新最小值 (CAS 循环)
    let mut current_min = self.min_ns.load(Ordering::Relaxed);
    while latency_ns < current_min {
        match self.min_ns.compare_exchange_weak(
            current_min, latency_ns,
            Ordering::Relaxed, Ordering::Relaxed,
        ) {
            Ok(_) => break,
            Err(x) => current_min = x,
        }
    }

    // 原子更新最大值
    let mut current_max = self.max_ns.load(Ordering::Relaxed);
    while latency_ns > current_max {
        match self.max_ns.compare_exchange_weak(
            current_max, latency_ns,
            Ordering::Relaxed, Ordering::Relaxed,
        ) {
            Ok(_) => break,
            Err(x) => current_max = x,
        }
    }

    // 更新累计值
    self.sum_ns.fetch_add(latency_ns, Ordering::Relaxed);
    self.count.fetch_add(1, Ordering::Relaxed);
    self.last_ns.store(latency_ns, Ordering::Relaxed);

    // 存入环形缓冲区
    let index = self.write_index.fetch_add(1, Ordering::Relaxed) % MAX_SAMPLES;
    self.samples[index].store(latency_ns, Ordering::Relaxed);
}
```

### 百分位数计算

```rust
/// 获取延迟统计
pub fn get_stats(&self) -> LatencyStats {
    let count = self.count.load(Ordering::Relaxed);
    if count == 0 {
        return LatencyStats::default();
    }

    // 收集样本并排序
    let sample_count = count.min(MAX_SAMPLES);
    let mut sorted_samples: Vec<u64> = self.samples[..sample_count]
        .iter()
        .map(|s| s.load(Ordering::Relaxed))
        .filter(|&s| s > 0)
        .collect();
    sorted_samples.sort_unstable();

    // 计算百分位数
    let len = sorted_samples.len();
    let p50_idx = len * 50 / 100;
    let p95_idx = len * 95 / 100;
    let p99_idx = len * 99 / 100;

    LatencyStats {
        min_ns: self.min_ns.load(Ordering::Relaxed),
        max_ns: self.max_ns.load(Ordering::Relaxed),
        avg_ns: self.sum_ns.load(Ordering::Relaxed) / count as u64,
        p50_ns: sorted_samples.get(p50_idx).copied().unwrap_or(0),
        p95_ns: sorted_samples.get(p95_idx).copied().unwrap_or(0),
        p99_ns: sorted_samples.get(p99_idx).copied().unwrap_or(0),
        sample_count: count as u64,
        last_ns: self.last_ns.load(Ordering::Relaxed),
    }
}
```

### RAII 延迟测量

```rust
/// RAII 延迟测量守卫
pub struct LatencyGuard {
    start: Instant,
}

impl LatencyGuard {
    pub fn new() -> Self {
        Self { start: Instant::now() }
    }
}

impl Drop for LatencyGuard {
    fn drop(&mut self) {
        let elapsed = self.start.elapsed();
        GLOBAL_TRACKER.record(elapsed.as_nanos() as u64);
    }
}

// 使用示例
fn process_tick() {
    let _guard = LatencyGuard::new();
    // ... 处理逻辑
    // guard 析构时自动记录延迟
}
```

### C# 监控服务

```csharp
// LatencyMonitorService.cs
public class LatencyMonitorService : INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherTimer _updateTimer;
    private LatencyStats _currentStats;
    private double _warningThresholdUs = 1000.0;
    private bool _isWarning;

    public LatencyStats CurrentStats
    {
        get => _currentStats;
        private set
        {
            _currentStats = value;
            OnPropertyChanged();
            CheckWarningThreshold();
        }
    }

    public string LastLatencyDisplay => _currentStats.SampleCount > 0 
        ? _currentStats.LastFormatted : "--";

    public event EventHandler<LatencyWarningEventArgs>? LatencyWarning;

    public void Start()
    {
        if (!_updateTimer.IsEnabled)
            _updateTimer.Start();
    }

    public void RefreshStats()
    {
        unsafe
        {
            LatencyStats stats;
            if (NativeMethods.GetLatencyStats(&stats) == 0)
            {
                CurrentStats = stats;
                StatsUpdated?.Invoke(this, stats);
            }
        }
    }

    private void CheckWarningThreshold()
    {
        IsWarning = _currentStats.SampleCount > 0 && 
                    _currentStats.LastUs > _warningThresholdUs;
    }
}
```

### FFI 接口

```rust
#[no_mangle]
pub unsafe extern "C" fn get_latency_stats_ffi(stats: *mut LatencyStats) -> i32 {
    if stats.is_null() { return -1; }
    *stats = get_latency_stats();
    0
}

#[no_mangle]
pub extern "C" fn reset_latency_stats_ffi() -> i32 {
    reset_latency_stats();
    0
}

#[no_mangle]
pub extern "C" fn set_latency_sample_rate_ffi(rate: i32) -> i32 {
    set_latency_sample_rate(rate.max(1) as usize);
    0
}
```

## 使用示例

```csharp
// 启动监控
LatencyMonitorService.Instance.Start();

// 设置预警阈值 (1ms)
LatencyMonitorService.Instance.WarningThresholdUs = 1000.0;

// 监听预警事件
LatencyMonitorService.Instance.LatencyWarning += (s, e) =>
{
    Console.WriteLine($"延迟预警: {e.Stats.LastUs}us");
};

// 重置统计
LatencyMonitorService.Instance.Reset();

// 设置采样率 (每 10 次记录 1 次)
LatencyMonitorService.Instance.SetSampleRate(10);
```

## 面试话术

### Q: 为什么使用原子操作而不是锁？

**A**: 性能考虑：
1. **无锁**: 原子操作不会阻塞线程
2. **低开销**: CAS 操作比互斥锁快一个数量级
3. **无死锁**: 不存在死锁风险

对于延迟监控这种高频操作，锁的开销会影响测量精度。

### Q: 为什么使用环形缓冲区？

**A**: 内存效率：
1. **固定大小**: 不会无限增长
2. **O(1) 写入**: 直接覆盖旧数据
3. **近期数据**: 百分位数基于最近样本计算

### Q: 采样率有什么作用？

**A**: 平衡精度和开销：
- `rate=1`: 每次都记录，最精确但开销最大
- `rate=10`: 每 10 次记录 1 次，降低开销
- 高频场景下可以提高采样率减少开销
