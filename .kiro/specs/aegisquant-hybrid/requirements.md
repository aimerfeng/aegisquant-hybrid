# Requirements Document

## Introduction

AegisQuant-Hybrid 是一个本地高性能量化回测与交易系统，采用 Rust + C# 混合架构。Rust 核心引擎负责行情清洗、策略计算、订单撮合和风控检查，追求极致低延迟和内存安全；C# 业务层负责 UI 展示、策略配置管理、数据可视化以及与 Rust 核心的交互。

## Glossary

- **Rust_Engine**: Rust 编写的核心引擎，编译为动态链接库 (cdylib)，提供 FFI 接口
- **CSharp_Wrapper**: C# 编写的互操作层，通过 P/Invoke 调用 Rust 引擎
- **UI_Client**: C# 编写的图形界面客户端，使用 WPF 或 Avalonia UI
- **Tick**: 行情数据单元，包含时间戳、价格、成交量
- **OrderRequest**: 订单请求结构，包含标的、数量、方向
- **AccountStatus**: 账户状态结构，包含余额、净值、持仓
- **BacktestEngine**: 回测引擎，负责策略执行和订单撮合
- **FFI**: Foreign Function Interface，外部函数接口
- **Hot_Path**: 热路径，指频繁执行的关键代码路径

## Requirements

### Requirement 1: Rust 核心数据结构定义

**User Story:** As a 量化开发者, I want to 定义与 C# 兼容的核心数据结构, so that 两端可以安全高效地交换数据。

#### Acceptance Criteria

1. THE Rust_Engine SHALL define a `Tick` struct with `repr(C)` layout containing timestamp (i64), price (f64), and volume (f64) fields
2. THE Rust_Engine SHALL define an `OrderRequest` struct with `repr(C)` layout containing symbol (fixed-size array), quantity (f64), and direction (i32) fields
3. THE Rust_Engine SHALL define an `AccountStatus` struct with `repr(C)` layout containing balance (f64), equity (f64), and position_count (i32) fields
4. THE Rust_Engine SHALL define a `Position` struct with `repr(C)` layout containing symbol, quantity, average_price, and unrealized_pnl fields
5. WHEN any struct is used across FFI boundary, THE Rust_Engine SHALL ensure memory layout matches C# StructLayout.Sequential

### Requirement 2: Rust FFI 接口导出

**User Story:** As a C# 开发者, I want to 通过标准 C ABI 调用 Rust 函数, so that 我可以在 .NET 中使用高性能 Rust 引擎。

#### Acceptance Criteria

1. THE Rust_Engine SHALL export an `init_engine` function with `extern "C"` and `#[no_mangle]` that returns an opaque engine pointer
2. THE Rust_Engine SHALL export a `process_tick` function that accepts engine pointer and Tick pointer, returning an error code (i32)
3. THE Rust_Engine SHALL export a `get_account_status` function that accepts engine pointer and writes AccountStatus to output pointer
4. THE Rust_Engine SHALL export a `free_engine` function that safely deallocates engine resources
5. WHEN any FFI function encounters an error, THE Rust_Engine SHALL return a non-zero error code instead of panicking
6. THE Rust_Engine SHALL define error codes as constants: 0 for success, negative values for errors

### Requirement 3: Rust 回测引擎实现

**User Story:** As a 量化策略师, I want to 运行回测模拟, so that 我可以验证策略在历史数据上的表现。

#### Acceptance Criteria

1. THE BacktestEngine SHALL maintain internal state including account balance, positions, and order history
2. WHEN a Tick is processed, THE BacktestEngine SHALL update price data and trigger strategy calculation
3. THE BacktestEngine SHALL implement a dual moving average (双均线) strategy as demo: buy when short MA crosses above long MA, sell when crosses below
4. WHEN strategy generates a signal, THE BacktestEngine SHALL create an OrderRequest and execute simulated fill
5. THE BacktestEngine SHALL track equity curve by calculating mark-to-market value after each tick
6. THE BacktestEngine SHALL use stack memory or pre-allocated pools for hot path operations to minimize heap allocation
7. THE BacktestEngine SHALL use rust_decimal crate for account balance and PnL calculations to avoid floating-point precision issues (Note: price/volume may use f64 for performance, but monetary values require Decimal)

### Requirement 4: C# P/Invoke 互操作层

**User Story:** As a .NET 开发者, I want to 安全地调用 Rust DLL, so that 我可以在 C# 中集成高性能引擎。

#### Acceptance Criteria

1. THE CSharp_Wrapper SHALL define structs with `[StructLayout(LayoutKind.Sequential)]` matching Rust `repr(C)` structs exactly
2. THE CSharp_Wrapper SHALL use `[DllImport]` or `LibraryImport` to declare native method signatures
3. THE CSharp_Wrapper SHALL wrap raw pointer operations in SafeHandle or IDisposable pattern
4. WHEN calling native methods, THE CSharp_Wrapper SHALL check return codes and throw appropriate exceptions for errors
5. THE CSharp_Wrapper SHALL ensure Rust-allocated memory is freed via `free_engine` in finalizer or Dispose method
6. THE CSharp_Wrapper SHALL use decimal type for monetary values received from Rust to maintain precision

### Requirement 5: 高性能数据加载 (Polars Integration)

**User Story:** As a 量化研究员, I want to 快速加载大规模历史数据, so that 我可以处理 GB 级别的 Tick 数据文件。

#### Acceptance Criteria

1. THE Rust_Engine SHALL integrate Polars library for high-performance data loading
2. THE Rust_Engine SHALL export `load_data_from_file` FFI function accepting file path string
3. THE Rust_Engine SHALL support CSV and Parquet file formats via Polars Lazy API
4. WHEN loading data, THE Rust_Engine SHALL use Polars to parse directly into internal Series/DataFrame, avoiding C#-to-Rust conversion overhead
5. THE CSharp_Wrapper SHALL only pass file path to Rust, not parsed data arrays
6. THE UI_Client SHALL display loading progress and file statistics (row count, date range, file size)
7. IF file loading fails, THEN THE Rust_Engine SHALL return error code with descriptive message

### Requirement 6: C# 回测执行控制

**User Story:** As a 用户, I want to 控制回测的启动和停止, so that 我可以观察策略执行过程。

#### Acceptance Criteria

1. WHEN user clicks "开始回测" button, THE UI_Client SHALL initialize Rust engine and start background thread
2. THE UI_Client SHALL iterate through Tick array, calling Rust `process_tick` for each tick on background thread
3. THE UI_Client SHALL poll or receive callbacks for AccountStatus updates at configurable intervals
4. WHEN user clicks "停止回测" button, THE UI_Client SHALL gracefully stop background thread and free engine
5. THE UI_Client SHALL display backtest progress (percentage complete, current date)

### Requirement 7: 实时净值曲线可视化

**User Story:** As a 用户, I want to 实时查看净值曲线, so that 我可以直观了解策略表现。

#### Acceptance Criteria

1. THE UI_Client SHALL integrate ScottPlot 5.0 or LiveCharts2 for high-performance chart rendering
2. WHEN AccountStatus is updated, THE UI_Client SHALL append equity value to chart data series
3. THE UI_Client SHALL render equity curve with X-axis as time and Y-axis as equity value
4. THE UI_Client SHALL support chart zoom and pan interactions
5. THE UI_Client SHALL display key metrics: total return, max drawdown, current equity

### Requirement 8: 内存安全与错误处理

**User Story:** As a 系统架构师, I want to 确保跨语言调用的内存安全, so that 系统不会因内存问题崩溃。

#### Acceptance Criteria

1. THE Rust_Engine SHALL never panic across FFI boundary; all errors return error codes
2. THE Rust_Engine SHALL validate all pointer parameters before dereferencing
3. IF null pointer is passed to FFI function, THEN THE Rust_Engine SHALL return error code without crashing
4. THE CSharp_Wrapper SHALL mark all pointer operations as `unsafe` with explanatory comments
5. THE CSharp_Wrapper SHALL implement IDisposable pattern for engine wrapper class
6. WHEN engine wrapper is disposed, THE CSharp_Wrapper SHALL call `free_engine` exactly once

### Requirement 9: 代码质量与架构规范

**User Story:** As a 开发团队, I want to 遵循代码质量标准, so that 代码可维护且符合最佳实践。

#### Acceptance Criteria

1. THE Rust_Engine SHALL pass `cargo clippy` without warnings
2. THE CSharp_Wrapper SHALL follow MVVM pattern with separate Model, ViewModel, and View layers
3. THE UI_Client SHALL use data binding for UI updates instead of direct control manipulation
4. THE Rust_Engine SHALL document all public FFI functions with safety requirements
5. THE CSharp_Wrapper SHALL use nullable reference types and handle null cases explicitly

### Requirement 10: 风控模块 (Risk Management)

**User Story:** As a 风控经理, I want to 在订单执行前进行风险检查, so that 系统不会因异常交易导致重大损失。

#### Acceptance Criteria

1. WHEN an OrderRequest is generated, THE Rust_Engine SHALL pass it through RiskManager::check() before execution
2. THE RiskManager SHALL implement pre-trade capital check: IF available_balance < order_amount, THEN THE RiskManager SHALL reject the order with error code
3. THE RiskManager SHALL implement order throttling: IF order_count_per_second > 10, THEN THE RiskManager SHALL reject excess orders
4. THE RiskManager SHALL implement position limit check: IF position_quantity > max_position_limit, THEN THE RiskManager SHALL reject the order
5. WHEN an order is rejected by RiskManager, THE Rust_Engine SHALL return specific rejection reason code to caller
6. THE RiskManager SHALL be configurable with parameters: max_order_rate, max_position_size, max_order_value

### Requirement 11: 数据清洗与验证 (Data Cleansing)

**User Story:** As a 量化研究员, I want to 确保输入数据质量, so that 回测结果不会被脏数据污染。

#### Acceptance Criteria

1. WHEN Tick data is received, THE Rust_Engine SHALL validate price > 0 and volume >= 0
2. IF price <= 0 OR volume < 0, THEN THE Rust_Engine SHALL mark tick as invalid and skip processing
3. THE Rust_Engine SHALL detect price jump anomalies: IF abs(price_change_percent) > 10%, THEN THE Rust_Engine SHALL flag as suspicious
4. THE Rust_Engine SHALL detect timestamp anomalies: IF timestamp <= previous_timestamp, THEN THE Rust_Engine SHALL reject tick as out-of-order
5. THE Rust_Engine SHALL maintain data quality statistics: valid_tick_count, invalid_tick_count, anomaly_count
6. THE Rust_Engine SHALL provide `get_data_quality_report` FFI function returning cleansing statistics

### Requirement 12: 参数优化支持 (Parameter Optimization with Rayon)

**User Story:** As a 量化研究员, I want to 批量运行不同参数组合的回测, so that 我可以找到最优策略参数。

#### Acceptance Criteria

1. THE Rust_Engine SHALL integrate Rayon library for data-parallel parameter optimization
2. THE Rust_Engine SHALL support creating multiple independent engine instances for parallel backtests
3. THE Rust_Engine SHALL accept strategy parameters (e.g., short_ma_period, long_ma_period) via `init_engine_with_params` function
4. THE Rust_Engine SHALL export `run_parameter_sweep` FFI function that uses Rayon to parallelize across CPU cores
5. THE CSharp_Wrapper SHALL define parameter ranges and pass to Rust for parallel execution
6. THE UI_Client SHALL display parameter optimization progress and intermediate results
7. WHEN optimization completes, THE UI_Client SHALL display results sorted by performance metric (e.g., Sharpe ratio, total return)
8. THE Rust_Engine SHALL be thread-safe: Rayon workers can run concurrently without data races

### Requirement 13: 结构化日志系统 (Structured Logging)

**User Story:** As a 运维人员, I want to 查看详细的系统日志, so that 我可以排查问题和满足监管审计要求。

#### Acceptance Criteria

1. THE Rust_Engine SHALL implement structured logging with levels: ERROR, WARN, INFO, DEBUG, TRACE
2. THE Rust_Engine SHALL log all order events: order_created, order_filled, order_rejected with timestamp and details
3. THE Rust_Engine SHALL log all risk check events: check_passed, check_failed with reason
4. THE Rust_Engine SHALL provide `set_log_callback` FFI function allowing C# to receive log messages
5. THE CSharp_Wrapper SHALL implement log sink that writes to file and/or displays in UI
6. THE Rust_Engine SHALL include correlation_id in logs to trace related events across system


### Requirement 14: 交易网关抽象层 (Gateway Abstraction)

**User Story:** As a 系统架构师, I want to 预留交易所接口抽象层, so that 系统可以从回测无缝切换到实盘交易。

#### Acceptance Criteria

1. THE Rust_Engine SHALL define a `Gateway` trait with methods: `submit_order`, `cancel_order`, `query_position`, `query_account`
2. THE Rust_Engine SHALL implement `SimulatedGateway` for backtesting that executes orders against historical data
3. THE Rust_Engine SHALL define `LiveGateway` trait extension points for future exchange integrations (e.g., CTP, XTP, IB)
4. WHEN BacktestEngine processes orders, THE BacktestEngine SHALL route through Gateway trait, not direct execution
5. THE Gateway trait SHALL support async order submission with callback for fill notifications
6. THE Rust_Engine SHALL provide `set_gateway` FFI function to switch between simulated and live gateways
7. THE Gateway abstraction SHALL isolate strategy logic from execution details, enabling same strategy code for backtest and live trading
