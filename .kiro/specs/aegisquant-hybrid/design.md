# Design Document: AegisQuant-Hybrid

## Overview

AegisQuant-Hybrid 是一个高性能量化回测与交易系统，采用 Rust + C# 混合架构。系统分为三层：

1. **Rust Core Engine** - 编译为 cdylib (.dll/.so)，负责核心计算
2. **C# Interop Layer** - P/Invoke 桥接层，封装 FFI 调用
3. **C# UI Client** - WPF/Avalonia 图形界面

设计目标：
- 热路径延迟 < 1μs (栈内存 + 预分配)
- 支持 GB 级数据加载 (Polars)
- 跨 FFI 边界零 panic
- 回测/实盘代码复用 (Gateway 抽象)

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        C# UI Client                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │ MainWindow  │  │ ChartView   │  │ ParameterOptimizer      │  │
│  │ (MVVM)      │  │ (ScottPlot) │  │ ViewModel               │  │
│  └──────┬──────┘  └──────┬──────┘  └───────────┬─────────────┘  │
│         │                │                      │                │
│         └────────────────┼──────────────────────┘                │
│                          │                                       │
│  ┌───────────────────────▼───────────────────────────────────┐  │
│  │              C# Interop Layer (NativeMethods)              │  │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────────┐    │  │
│  │  │ EngineHandle│  │ StructMaps  │  │ ErrorHandler    │    │  │
│  │  │ (SafeHandle)│  │ (Sequential)│  │ (Exception)     │    │  │
│  │  └─────────────┘  └─────────────┘  └─────────────────┘    │  │
│  └───────────────────────┬───────────────────────────────────┘  │
└──────────────────────────┼──────────────────────────────────────┘
                           │ P/Invoke (extern "C")
┌──────────────────────────▼──────────────────────────────────────┐
│                     Rust Core Engine (cdylib)                    │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    FFI Layer (ffi.rs)                    │    │
│  │  init_engine | process_tick | get_account_status | ...   │    │
│  └─────────────────────────┬───────────────────────────────┘    │
│                            │                                     │
│  ┌─────────────┬───────────┼───────────┬─────────────────────┐  │
│  │             │           │           │                     │  │
│  ▼             ▼           ▼           ▼                     ▼  │
│ ┌───────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌───────────────┐ │
│ │Engine │ │Strategy │ │RiskMgr  │ │Gateway  │ │DataLoader     │ │
│ │       │ │(DualMA) │ │         │ │(trait)  │ │(Polars)       │ │
│ └───┬───┘ └────┬────┘ └────┬────┘ └────┬────┘ └───────────────┘ │
│     │          │           │           │                        │
│     └──────────┴───────────┴───────────┘                        │
│                            │                                     │
│  ┌─────────────────────────▼───────────────────────────────┐    │
│  │                   Core Types (types.rs)                  │    │
│  │  Tick | OrderRequest | AccountStatus | Position | ...    │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

## Components and Interfaces

### 1. Rust Core Types (types.rs)

```rust
// repr(C) 确保与 C# StructLayout.Sequential 兼容
#[repr(C)]
pub struct Tick {
    pub timestamp: i64,      // Unix timestamp (nanoseconds)
    pub price: f64,          // 价格 (f64 for performance)
    pub volume: f64,         // 成交量
}

#[repr(C)]
pub struct OrderRequest {
    pub symbol: [u8; 16],    // 固定长度字符数组 (null-terminated)
    pub quantity: f64,       // 数量
    pub direction: i32,      // 1 = Buy, -1 = Sell
    pub order_type: i32,     // 0 = Market, 1 = Limit
    pub limit_price: f64,    // 限价单价格 (Market 单忽略)
}

#[repr(C)]
pub struct Position {
    pub symbol: [u8; 16],
    pub quantity: f64,
    pub average_price: f64,
    pub unrealized_pnl: f64,
    pub realized_pnl: f64,
}

#[repr(C)]
pub struct AccountStatus {
    pub balance: f64,        // 账户余额 (内部用 Decimal 计算，导出时转 f64)
    pub equity: f64,         // 净值 = balance + unrealized_pnl
    pub available: f64,      // 可用资金
    pub position_count: i32, // 持仓数量
    pub total_pnl: f64,      // 总盈亏
}

#[repr(C)]
pub struct DataQualityReport {
    pub total_ticks: i64,
    pub valid_ticks: i64,
    pub invalid_ticks: i64,
    pub anomaly_ticks: i64,
    pub first_timestamp: i64,
    pub last_timestamp: i64,
}

#[repr(C)]
pub struct StrategyParams {
    pub short_ma_period: i32,
    pub long_ma_period: i32,
    pub position_size: f64,
    pub stop_loss_pct: f64,
    pub take_profit_pct: f64,
}

#[repr(C)]
pub struct RiskConfig {
    pub max_order_rate: i32,     // 每秒最大下单次数
    pub max_position_size: f64,  // 最大持仓量
    pub max_order_value: f64,    // 单笔最大金额
    pub max_drawdown_pct: f64,   // 最大回撤比例
}
```

### 2. Rust FFI Layer (ffi.rs)

```rust
// Error codes
pub const ERR_SUCCESS: i32 = 0;
pub const ERR_NULL_POINTER: i32 = -1;
pub const ERR_INVALID_PARAM: i32 = -2;
pub const ERR_ENGINE_NOT_INIT: i32 = -3;
pub const ERR_RISK_REJECTED: i32 = -4;
pub const ERR_DATA_LOAD_FAILED: i32 = -5;
pub const ERR_INVALID_DATA: i32 = -6;

/// 初始化引擎，返回引擎指针
/// # Safety
/// 调用者必须在使用完毕后调用 free_engine 释放内存
#[no_mangle]
pub unsafe extern "C" fn init_engine(
    params: *const StrategyParams,
    risk_config: *const RiskConfig,
) -> *mut BacktestEngine;

/// 从文件加载数据 (Polars)
/// # Safety
/// file_path 必须是有效的 null-terminated UTF-8 字符串
#[no_mangle]
pub unsafe extern "C" fn load_data_from_file(
    engine: *mut BacktestEngine,
    file_path: *const c_char,
    report: *mut DataQualityReport,
) -> i32;

/// 处理单个 Tick
/// # Safety
/// engine 和 tick 指针必须有效
#[no_mangle]
pub unsafe extern "C" fn process_tick(
    engine: *mut BacktestEngine,
    tick: *const Tick,
) -> i32;

/// 获取账户状态
/// # Safety
/// engine 和 status 指针必须有效
#[no_mangle]
pub unsafe extern "C" fn get_account_status(
    engine: *mut BacktestEngine,
    status: *mut AccountStatus,
) -> i32;

/// 运行完整回测
/// # Safety
/// engine 指针必须有效
#[no_mangle]
pub unsafe extern "C" fn run_backtest(engine: *mut BacktestEngine) -> i32;

/// 参数扫描 (Rayon 并行)
/// # Safety
/// 所有指针必须有效，results 数组大小必须 >= param_count
#[no_mangle]
pub unsafe extern "C" fn run_parameter_sweep(
    base_params: *const StrategyParams,
    risk_config: *const RiskConfig,
    file_path: *const c_char,
    param_variations: *const StrategyParams,
    param_count: i32,
    results: *mut BacktestResult,
) -> i32;

/// 设置日志回调
/// # Safety
/// callback 函数指针必须有效且线程安全
#[no_mangle]
pub unsafe extern "C" fn set_log_callback(
    callback: extern "C" fn(level: i32, message: *const c_char),
) -> i32;

/// 释放引擎内存
/// # Safety
/// engine 必须是 init_engine 返回的有效指针，且只能调用一次
#[no_mangle]
pub unsafe extern "C" fn free_engine(engine: *mut BacktestEngine);
```

### 3. Rust Engine Core (engine.rs)

```rust
pub struct BacktestEngine {
    // 账户状态 (使用 Decimal 内部计算)
    balance: Decimal,
    positions: HashMap<String, PositionInternal>,
    
    // 策略状态
    strategy: Box<dyn Strategy>,
    params: StrategyParams,
    
    // 风控
    risk_manager: RiskManager,
    
    // 数据
    data: Option<DataFrame>,  // Polars DataFrame
    current_index: usize,
    
    // Gateway
    gateway: Box<dyn Gateway>,
    
    // 日志
    logger: Logger,
    
    // 性能优化：预分配缓冲区
    price_buffer: Vec<f64>,   // 用于 MA 计算
    equity_curve: Vec<f64>,   // 净值曲线
}

impl BacktestEngine {
    pub fn new(params: StrategyParams, risk_config: RiskConfig) -> Self;
    pub fn load_data(&mut self, path: &str) -> Result<DataQualityReport, EngineError>;
    pub fn process_tick(&mut self, tick: &Tick) -> Result<Option<OrderRequest>, EngineError>;
    pub fn run(&mut self) -> Result<BacktestResult, EngineError>;
    pub fn get_account_status(&self) -> AccountStatus;
}
```

### 4. Rust Gateway Trait (gateway.rs)

```rust
pub trait Gateway: Send + Sync {
    fn submit_order(&mut self, order: &OrderRequest) -> Result<OrderId, GatewayError>;
    fn cancel_order(&mut self, order_id: OrderId) -> Result<(), GatewayError>;
    fn query_position(&self, symbol: &str) -> Option<Position>;
    fn query_account(&self) -> AccountStatus;
    fn on_fill(&mut self, callback: Box<dyn Fn(Fill) + Send>);
}

pub struct SimulatedGateway {
    // 模拟撮合
    slippage: f64,
    commission_rate: f64,
    current_price: f64,
}

impl Gateway for SimulatedGateway {
    // 立即成交，模拟滑点和手续费
}

// 预留实盘接口
pub trait LiveGateway: Gateway {
    fn connect(&mut self) -> Result<(), GatewayError>;
    fn disconnect(&mut self);
    fn is_connected(&self) -> bool;
}
```

### 5. Rust Risk Manager (risk.rs)

```rust
pub struct RiskManager {
    config: RiskConfig,
    order_timestamps: VecDeque<Instant>,  // 流控时间窗口
    peak_equity: f64,                      // 最高净值 (回撤计算)
}

impl RiskManager {
    pub fn check(&mut self, order: &OrderRequest, account: &AccountStatus) -> Result<(), RiskError> {
        self.check_capital(order, account)?;
        self.check_throttle()?;
        self.check_position_limit(order, account)?;
        self.check_drawdown(account)?;
        Ok(())
    }
    
    fn check_capital(&self, order: &OrderRequest, account: &AccountStatus) -> Result<(), RiskError>;
    fn check_throttle(&mut self) -> Result<(), RiskError>;
    fn check_position_limit(&self, order: &OrderRequest, account: &AccountStatus) -> Result<(), RiskError>;
    fn check_drawdown(&self, account: &AccountStatus) -> Result<(), RiskError>;
}
```

### 6. C# Interop Structs (NativeTypes.cs)

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct Tick
{
    public long Timestamp;
    public double Price;
    public double Volume;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct OrderRequest
{
    public fixed byte Symbol[16];
    public double Quantity;
    public int Direction;
    public int OrderType;
    public double LimitPrice;
}

[StructLayout(LayoutKind.Sequential)]
public struct AccountStatus
{
    public double Balance;
    public double Equity;
    public double Available;
    public int PositionCount;
    public double TotalPnl;
}

[StructLayout(LayoutKind.Sequential)]
public struct StrategyParams
{
    public int ShortMaPeriod;
    public int LongMaPeriod;
    public double PositionSize;
    public double StopLossPct;
    public double TakeProfitPct;
}
```

### 7. C# Native Methods (NativeMethods.cs)

```csharp
internal static partial class NativeMethods
{
    private const string DllName = "aegisquant_core";

    [LibraryImport(DllName)]
    public static unsafe partial IntPtr init_engine(
        StrategyParams* parameters,
        RiskConfig* riskConfig);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial int load_data_from_file(
        IntPtr engine,
        string filePath,
        DataQualityReport* report);

    [LibraryImport(DllName)]
    public static unsafe partial int process_tick(
        IntPtr engine,
        Tick* tick);

    [LibraryImport(DllName)]
    public static unsafe partial int get_account_status(
        IntPtr engine,
        AccountStatus* status);

    [LibraryImport(DllName)]
    public static partial void free_engine(IntPtr engine);
}
```

### 8. C# Engine Wrapper (EngineWrapper.cs)

```csharp
// 日志回调委托定义
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void LogCallback(int level, IntPtr message);

public sealed class EngineWrapper : IDisposable
{
    private EngineHandle? _handle;
    private bool _disposed;
    
    // CRITICAL: 保持 delegate 引用，防止 GC 回收导致回调时 crash
    // 当 Rust 调用回调时，如果 delegate 已被 GC，会导致访问已释放内存
    private LogCallback? _logCallbackKeepAlive;

    public EngineWrapper(StrategyParams parameters, RiskConfig riskConfig)
    {
        unsafe
        {
            // SAFETY: 传递栈上的结构体指针，Rust 会复制数据
            IntPtr ptr = NativeMethods.init_engine(&parameters, &riskConfig);
            if (ptr == IntPtr.Zero)
                throw new EngineException("Failed to initialize engine");
            _handle = new EngineHandle(ptr);
        }
    }

    /// <summary>
    /// 设置日志回调。回调会在 Rust 线程中调用。
    /// </summary>
    /// <param name="callback">日志处理函数</param>
    public void SetLogCallback(Action<int, string> callback)
    {
        ThrowIfDisposed();
        
        // 创建 native callback 并保持引用
        _logCallbackKeepAlive = (level, messagePtr) =>
        {
            string message = Marshal.PtrToStringUTF8(messagePtr) ?? "";
            callback(level, message);
        };
        
        unsafe
        {
            int result = NativeMethods.set_log_callback(
                Marshal.GetFunctionPointerForDelegate(_logCallbackKeepAlive));
            CheckResult(result);
        }
    }

    public DataQualityReport LoadData(string filePath)
    {
        ThrowIfDisposed();
        unsafe
        {
            DataQualityReport report;
            // SAFETY: report 是栈上变量，Rust 写入后立即使用
            int result = NativeMethods.load_data_from_file(
                _handle!.DangerousGetHandle(), filePath, &report);
            CheckResult(result);
            return report;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _handle?.Dispose();
            _logCallbackKeepAlive = null;  // 释放回调引用
            _disposed = true;
        }
    }
}
```

### 9. FFI 类型安全注意事项

**回调函数生命周期管理：**
- C# delegate 传给 Rust 后，必须在 C# 侧保持引用
- 如果 delegate 被 GC 回收，Rust 回调时会访问已释放内存导致 crash
- 解决方案：将 delegate 存为类成员变量 `_logCallbackKeepAlive`

**结构体对齐与类型映射：**
- Rust `bool` = 1 byte，C# `bool` 默认 = 4 bytes
- 解决方案：跨 FFI 边界使用 `i32` 代替 `bool` 和 `enum`
- `Direction`: 1 = Buy, -1 = Sell (i32)
- `OrderType`: 0 = Market, 1 = Limit (i32)
- 避免直接传递 Rust enum，始终使用整数常量

**字符串处理：**
- Rust 字符串使用 `*const c_char` (null-terminated UTF-8)
- C# 使用 `StringMarshalling.Utf8` 或手动 `Marshal.PtrToStringUTF8`
- 固定长度字符数组 (`[u8; 16]`) 对应 C# `fixed byte[16]`
```

## Data Models

### Rust Internal Data Flow

```
CSV/Parquet File
       │
       ▼ (Polars Lazy API)
┌──────────────────┐
│   DataFrame      │  ← 列式存储，零拷贝
│  - timestamp     │
│  - price         │
│  - volume        │
└────────┬─────────┘
         │
         ▼ (Data Cleansing)
┌──────────────────┐
│ Validated Ticks  │  ← 过滤无效数据
│ - price > 0      │
│ - volume >= 0    │
│ - no anomalies   │
└────────┬─────────┘
         │
         ▼ (Strategy Processing)
┌──────────────────┐
│ Price Buffer     │  ← 预分配 Vec<f64>
│ (Ring Buffer)    │     用于 MA 计算
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Signal Generator │  ← 双均线交叉检测
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Risk Manager     │  ← 风控检查
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Gateway          │  ← 订单执行
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Account Update   │  ← Decimal 精度计算
│ Equity Curve     │
└──────────────────┘
```

### C# MVVM Data Flow

```
┌─────────────────────────────────────────────────────────────┐
│                         View Layer                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ MainWindow  │  │ ChartControl│  │ ParameterPanel      │  │
│  │ .xaml       │  │ (ScottPlot) │  │ .xaml               │  │
│  └──────┬──────┘  └──────┬──────┘  └──────────┬──────────┘  │
│         │                │                     │             │
│         └────────────────┼─────────────────────┘             │
│                          │ Data Binding                      │
└──────────────────────────┼──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                      ViewModel Layer                         │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              MainViewModel                           │    │
│  │  - EquityCurve: ObservableCollection<double>        │    │
│  │  - CurrentStatus: AccountStatus                      │    │
│  │  - Progress: double                                  │    │
│  │  - LoadDataCommand: ICommand                         │    │
│  │  - StartBacktestCommand: ICommand                    │    │
│  └─────────────────────────┬───────────────────────────┘    │
└────────────────────────────┼────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────┐
│                       Model Layer                            │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              BacktestService                         │    │
│  │  - EngineWrapper (IDisposable)                       │    │
│  │  - RunBacktestAsync()                                │    │
│  │  - OnStatusUpdated event                             │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```



## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

Based on the prework analysis, the following correctness properties have been identified for property-based testing:

### Property 1: FFI Struct Memory Layout Round-Trip

*For any* valid Tick, OrderRequest, AccountStatus, or Position struct created in Rust, serializing to bytes and deserializing in C# (and vice versa) should produce an equivalent struct with identical field values.

**Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 4.1**

This property ensures that the `repr(C)` layout in Rust exactly matches `StructLayout.Sequential` in C#. We generate random struct instances, convert to raw bytes, and verify field-by-field equality after cross-language reconstruction.

### Property 2: FFI Safety - Error Codes Instead of Panics

*For any* invalid input to FFI functions (null pointers, invalid engine handles, malformed data), the Rust engine should return a non-zero error code and never panic or crash.

**Validates: Requirements 2.5, 2.6, 8.1, 8.2, 8.3**

This property is critical for system stability. We generate random invalid inputs (null pointers, garbage data, out-of-range values) and verify that all FFI calls return appropriate error codes without causing undefined behavior.

### Property 3: Dual Moving Average Strategy Signal Correctness

*For any* sequence of price ticks, the dual MA strategy should generate a BUY signal if and only if the short MA crosses above the long MA, and a SELL signal if and only if the short MA crosses below the long MA.

**Validates: Requirements 3.3, 3.4**

We generate random price sequences, compute expected MA values and crossover points independently, then verify the strategy produces signals at exactly those points.

### Property 4: Risk Manager Order Validation

*For any* OrderRequest and AccountStatus combination:
- If `available_balance < order_value`, the order should be rejected with capital error
- If `orders_per_second > max_rate`, the order should be rejected with throttle error  
- If `position_quantity + order_quantity > max_position`, the order should be rejected with position limit error

**Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**

We generate random order/account combinations spanning valid and invalid scenarios, verifying the risk manager correctly accepts or rejects each case with the appropriate error code.

### Property 5: Data Cleansing Filters Invalid Ticks

*For any* Tick data:
- If `price <= 0` or `volume < 0`, the tick should be marked invalid
- If `abs(price_change) > 10%` from previous tick, the tick should be flagged as anomaly
- If `timestamp <= previous_timestamp`, the tick should be rejected as out-of-order
- Valid tick count + invalid tick count + anomaly count should equal total tick count

**Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5**

We generate random tick sequences including edge cases (negative prices, zero volume, time reversals, price spikes) and verify the data quality report accurately reflects the cleansing results.

### Property 6: Equity Calculation Precision and Consistency

*For any* sequence of trades and price updates:
- `equity = balance + sum(unrealized_pnl for all positions)`
- `unrealized_pnl = (current_price - average_price) * quantity`
- Monetary calculations using Decimal should not lose precision (no floating-point drift)

**Validates: Requirements 3.5, 3.7, 4.6, 7.5**

We generate random trade sequences, compute expected equity independently using exact arithmetic, and verify the engine's equity matches within acceptable tolerance (for f64 export) or exactly (for Decimal internals).

### Property 7: Multi-Engine Thread Safety

*For any* set of N independent engine instances running in parallel (via Rayon), each engine should produce the same results as if run sequentially, with no data races or corrupted state.

**Validates: Requirements 12.2, 12.8**

We create multiple engines with identical parameters and data, run them concurrently, and verify all produce identical results. We also run with different parameters and verify results are independent.

### Property 8: Order Routing Through Gateway

*For any* order generated by the strategy, the order should be routed through the Gateway trait's `submit_order` method before affecting account state.

**Validates: Requirements 14.4**

We implement a mock Gateway that records all orders, run the backtest, and verify every order that affected account state was first submitted through the Gateway.

## Error Handling

### Rust Error Handling Strategy

```rust
// 所有 FFI 函数使用 Result 内部处理，对外返回 error code
pub fn process_tick_internal(
    engine: &mut BacktestEngine,
    tick: &Tick,
) -> Result<(), EngineError> {
    // 数据验证
    if tick.price <= 0.0 {
        return Err(EngineError::InvalidData("price must be positive"));
    }
    
    // 业务逻辑...
    Ok(())
}

// FFI 包装层捕获所有错误
#[no_mangle]
pub unsafe extern "C" fn process_tick(
    engine: *mut BacktestEngine,
    tick: *const Tick,
) -> i32 {
    // 指针验证
    if engine.is_null() || tick.is_null() {
        return ERR_NULL_POINTER;
    }
    
    // catch_unwind 防止 panic 跨 FFI 边界
    let result = std::panic::catch_unwind(|| {
        let engine = &mut *engine;
        let tick = &*tick;
        process_tick_internal(engine, tick)
    });
    
    match result {
        Ok(Ok(())) => ERR_SUCCESS,
        Ok(Err(e)) => e.to_error_code(),
        Err(_) => ERR_INTERNAL_PANIC,  // panic 被捕获
    }
}
```

### Error Code Definitions

| Code | Name | Description |
|------|------|-------------|
| 0 | SUCCESS | 操作成功 |
| -1 | NULL_POINTER | 空指针参数 |
| -2 | INVALID_PARAM | 无效参数值 |
| -3 | ENGINE_NOT_INIT | 引擎未初始化 |
| -4 | RISK_REJECTED | 风控拒绝 |
| -5 | DATA_LOAD_FAILED | 数据加载失败 |
| -6 | INVALID_DATA | 无效数据 |
| -7 | INSUFFICIENT_CAPITAL | 资金不足 |
| -8 | THROTTLE_EXCEEDED | 超过流控限制 |
| -9 | POSITION_LIMIT | 超过持仓限制 |
| -10 | FILE_NOT_FOUND | 文件不存在 |
| -99 | INTERNAL_PANIC | 内部 panic (不应发生) |

### C# Exception Mapping

```csharp
public static class ErrorHandler
{
    public static void CheckResult(int errorCode, string operation)
    {
        if (errorCode == 0) return;
        
        throw errorCode switch
        {
            -1 => new ArgumentNullException(operation, "Null pointer passed to native code"),
            -2 => new ArgumentException("Invalid parameter", operation),
            -3 => new InvalidOperationException("Engine not initialized"),
            -4 => new RiskRejectedException(GetRiskReason(errorCode)),
            -5 => new DataLoadException("Failed to load data file"),
            -6 => new InvalidDataException("Invalid tick data"),
            -7 => new InsufficientCapitalException(),
            -8 => new ThrottleExceededException(),
            -9 => new PositionLimitException(),
            -10 => new FileNotFoundException(),
            _ => new EngineException($"Unknown error: {errorCode}")
        };
    }
}
```

## Testing Strategy

### Dual Testing Approach

本项目采用双重测试策略：

1. **Unit Tests (单元测试)**: 验证具体示例和边界情况
2. **Property-Based Tests (属性测试)**: 验证普遍性质在所有输入上成立

两者互补：单元测试捕获具体 bug，属性测试验证通用正确性。

### Rust Testing Framework

```toml
# Cargo.toml
[dev-dependencies]
proptest = "1.4"           # Property-based testing
quickcheck = "1.0"         # Alternative PBT library
criterion = "0.5"          # Benchmarking
```

### Property Test Configuration

- 每个属性测试运行 **最少 100 次迭代**
- 每个测试必须注释引用设计文档中的属性编号
- 标签格式: `Feature: aegisquant-hybrid, Property {N}: {property_text}`

### Test File Structure

```
aegisquant-core/
├── src/
│   ├── lib.rs
│   ├── types.rs
│   ├── engine.rs
│   ├── ffi.rs
│   ├── risk.rs
│   ├── gateway.rs
│   └── strategy.rs
└── tests/
    ├── property_tests/
    │   ├── ffi_layout_tests.rs      # Property 1
    │   ├── ffi_safety_tests.rs      # Property 2
    │   ├── strategy_tests.rs        # Property 3
    │   ├── risk_tests.rs            # Property 4
    │   ├── data_cleansing_tests.rs  # Property 5
    │   ├── equity_tests.rs          # Property 6
    │   ├── thread_safety_tests.rs   # Property 7
    │   └── gateway_tests.rs         # Property 8
    ├── unit_tests/
    │   ├── types_tests.rs
    │   ├── engine_tests.rs
    │   └── integration_tests.rs
    └── benchmarks/
        └── hot_path_bench.rs
```

### C# Testing Framework

```xml
<!-- Test project dependencies -->
<PackageReference Include="xunit" Version="2.6.1" />
<PackageReference Include="FsCheck.Xunit" Version="2.16.6" />  <!-- Property-based testing -->
<PackageReference Include="Moq" Version="4.20.69" />
```

### Example Property Test (Rust)

```rust
// tests/property_tests/ffi_layout_tests.rs

use proptest::prelude::*;
use aegisquant_core::types::*;

// Feature: aegisquant-hybrid, Property 1: FFI Struct Memory Layout Round-Trip
// Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 4.1
proptest! {
    #![proptest_config(ProptestConfig::with_cases(100))]
    
    #[test]
    fn tick_roundtrip(timestamp in any::<i64>(), price in 0.01f64..1000000.0, volume in 0.0f64..1000000.0) {
        let tick = Tick { timestamp, price, volume };
        
        // Serialize to bytes
        let bytes = unsafe {
            std::slice::from_raw_parts(
                &tick as *const Tick as *const u8,
                std::mem::size_of::<Tick>()
            )
        };
        
        // Deserialize back
        let reconstructed: Tick = unsafe {
            std::ptr::read(bytes.as_ptr() as *const Tick)
        };
        
        prop_assert_eq!(tick.timestamp, reconstructed.timestamp);
        prop_assert!((tick.price - reconstructed.price).abs() < f64::EPSILON);
        prop_assert!((tick.volume - reconstructed.volume).abs() < f64::EPSILON);
    }
}
```

### Example Property Test (C# with FsCheck)

```csharp
// Feature: aegisquant-hybrid, Property 2: FFI Safety - Error Codes Instead of Panics
// Validates: Requirements 2.5, 2.6, 8.1, 8.2, 8.3
public class FfiSafetyProperties
{
    [Property(MaxTest = 100)]
    public Property NullPointerReturnsErrorCode()
    {
        return Prop.ForAll<int>(seed =>
        {
            // Passing null engine pointer should return error, not crash
            unsafe
            {
                var result = NativeMethods.process_tick(IntPtr.Zero, null);
                return result == ErrorCodes.NullPointer;
            }
        });
    }
    
    [Property(MaxTest = 100)]
    public Property InvalidTickDataReturnsErrorCode(double price, double volume)
    {
        return Prop.ForAll(
            Arb.From<double>().Filter(p => p <= 0),  // Invalid prices
            invalidPrice =>
            {
                using var engine = new EngineWrapper(DefaultParams, DefaultRisk);
                var tick = new Tick { Price = invalidPrice, Volume = 100 };
                
                unsafe
                {
                    var result = NativeMethods.process_tick(
                        engine.Handle.DangerousGetHandle(), &tick);
                    return result == ErrorCodes.InvalidData;
                }
            });
    }
}
```

### Benchmark Tests

```rust
// benches/hot_path_bench.rs
use criterion::{criterion_group, criterion_main, Criterion};

fn process_tick_benchmark(c: &mut Criterion) {
    let mut engine = BacktestEngine::new(default_params(), default_risk());
    let tick = Tick { timestamp: 0, price: 100.0, volume: 1000.0 };
    
    c.bench_function("process_tick", |b| {
        b.iter(|| engine.process_tick(&tick))
    });
}

criterion_group!(benches, process_tick_benchmark);
criterion_main!(benches);
```

### Test Coverage Goals

| Component | Unit Test Coverage | Property Test Coverage |
|-----------|-------------------|----------------------|
| types.rs | 90% | Property 1 |
| ffi.rs | 85% | Property 1, 2 |
| engine.rs | 90% | Property 3, 6 |
| risk.rs | 95% | Property 4 |
| data_loader.rs | 85% | Property 5 |
| gateway.rs | 90% | Property 8 |
| (parallel) | - | Property 7 |
