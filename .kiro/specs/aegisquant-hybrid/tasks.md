# Implementation Plan: AegisQuant-Hybrid

## Overview

本实现计划按照 4 个 Phase 分步执行，每个 Phase 完成后进行检查点验证。采用 Rust 核心引擎 + C# 互操作层 + C# UI 的混合架构。

**GitHub 仓库:** https://github.com/aimerfeng/aegisquant-hybrid.git

## Environment Requirements

- **Rust:** stable (latest, >= 1.75)
- **.NET SDK:** 8.0
- **Python:** 3.10+ (用于测试数据生成)
- **OS:** Windows 10/11 (主要), Linux/macOS (可选)

## Tasks

- [x] 1. Phase 1: Rust 基础数据结构与 FFI 接口
  - [x] 1.1 创建 Rust cdylib 项目结构
    - 运行 `cargo new aegisquant-core --lib`
    - 配置 `Cargo.toml`: crate-type = ["cdylib"], 添加依赖 (rust_decimal, polars, rayon, thiserror)
    - _Requirements: 1.1, 2.1_

  - [x] 1.2 实现核心 `repr(C)` 数据结构 (types.rs)
    - 定义 `Tick` struct (timestamp: i64, price: f64, volume: f64)
    - 定义 `OrderRequest` struct (symbol: [u8; 16], quantity: f64, direction: i32, order_type: i32, limit_price: f64)
    - 定义 `Position` struct (symbol, quantity, average_price, unrealized_pnl, realized_pnl)
    - 定义 `AccountStatus` struct (balance, equity, available, position_count, total_pnl)
    - 定义 `StrategyParams` struct (short_ma_period, long_ma_period, position_size, stop_loss_pct, take_profit_pct)
    - 定义 `RiskConfig` struct (max_order_rate, max_position_size, max_order_value, max_drawdown_pct)
    - 定义 `DataQualityReport` struct
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 1.3 创建测试数据生成脚本
    - 创建 `scripts/generate_test_data.py`
    - 生成符合 Tick 格式的随机 CSV 数据 (timestamp, price, volume)
    - 支持配置行数 (默认 10万行, 最大 100万行 ~500MB)
    - 包含正常数据、边界情况 (价格跳变、时间戳乱序) 用于测试数据清洗
    - 输出到 `test_data/` 目录
    - _Requirements: 5.3, 11.1_

  - [x] 1.4 配置 GitHub Actions CI/CD
    - 创建 `.github/workflows/ci.yml`
    - 配置 Rust 构建和测试 (cargo build, cargo clippy, cargo test)
    - 配置 .NET 构建和测试 (dotnet build, dotnet test)
    - 配置 artifact 上传 (编译后的 .dll)
    - _Requirements: 9.1_

  - [x] 1.5 编写属性测试: FFI 结构体内存布局
    - **Property 1: FFI Struct Memory Layout Round-Trip**
    - 使用 proptest 生成随机结构体，验证序列化/反序列化一致性
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**

  - [x] 1.6 实现 FFI 导出函数骨架 (ffi.rs)
    - 定义错误码常量 (ERR_SUCCESS, ERR_NULL_POINTER, etc.)
    - 实现 `init_engine` 函数 (extern "C", #[no_mangle])
    - 实现 `free_engine` 函数
    - 实现 `process_tick` 函数骨架
    - 实现 `get_account_status` 函数骨架
    - 添加 `catch_unwind` 防止 panic 跨 FFI 边界
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 8.1_

  - [x] 1.7 编写属性测试: FFI 安全性
    - **Property 2: FFI Safety - Error Codes Instead of Panics**
    - 测试 null 指针、无效参数返回错误码而非 crash
    - **Validates: Requirements 2.5, 2.6, 8.1, 8.2, 8.3**

- [ ] 2. Checkpoint - Phase 1 验证
  - 运行 `cargo build --release` 确保编译通过
  - 运行 `cargo clippy` 确保无警告
  - 运行 `cargo test` 确保属性测试通过
  - 确认生成 .dll/.so 文件
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 3. Phase 2: Rust 核心引擎逻辑
  - [ ] 3.1 实现 RiskManager (risk.rs)
    - 实现 `RiskManager` struct 和 `check()` 方法
    - 实现 `check_capital()` - 资金预检
    - 实现 `check_throttle()` - 流控 (VecDeque 时间窗口)
    - 实现 `check_position_limit()` - 持仓限制
    - 实现 `check_drawdown()` - 最大回撤检查
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6_

  - [ ] 3.2 编写属性测试: 风控订单验证
    - **Property 4: Risk Manager Order Validation**
    - 测试资金不足、流控超限、持仓超限场景
    - **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**

  - [ ] 3.3 实现 Gateway trait 和 SimulatedGateway (gateway.rs)
    - 定义 `Gateway` trait (submit_order, cancel_order, query_position, query_account)
    - 实现 `SimulatedGateway` struct (slippage, commission_rate)
    - 实现模拟撮合逻辑 (立即成交 + 滑点 + 手续费)
    - _Requirements: 14.1, 14.2, 14.4, 14.5_

  - [ ] 3.4 编写属性测试: 订单通过 Gateway 路由
    - **Property 8: Order Routing Through Gateway**
    - 验证所有订单都经过 Gateway::submit_order
    - **Validates: Requirements 14.4**

  - [ ] 3.5 实现数据加载与清洗 (data_loader.rs)
    - 集成 Polars 读取 CSV/Parquet
    - 实现 `load_data_from_file` 函数
    - 实现数据验证: price > 0, volume >= 0
    - 实现价格跳变检测 (>10%)
    - 实现时间戳顺序检查
    - 生成 `DataQualityReport`
    - _Requirements: 5.1, 5.2, 5.3, 5.7, 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

  - [ ] 3.6 编写属性测试: 数据清洗
    - **Property 5: Data Cleansing Filters Invalid Ticks**
    - 测试无效价格、负成交量、时间戳乱序、价格跳变
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5**

  - [ ] 3.7 实现双均线策略 (strategy.rs)
    - 定义 `Strategy` trait
    - 实现 `DualMAStrategy` struct
    - 实现均线计算 (使用预分配 Vec<f64> 缓冲区)
    - 实现交叉检测逻辑 (金叉买入、死叉卖出)
    - _Requirements: 3.3, 3.4_

  - [ ] 3.8 编写属性测试: 策略信号正确性
    - **Property 3: Dual Moving Average Strategy Signal Correctness**
    - 验证信号仅在 MA 交叉时产生
    - **Validates: Requirements 3.3, 3.4**

  - [ ] 3.9 实现 BacktestEngine (engine.rs)
    - 实现 `BacktestEngine` struct (使用 rust_decimal 进行账务计算)
    - 实现 `new()`, `load_data()`, `process_tick()`, `run()`
    - 实现净值曲线追踪
    - 集成 RiskManager、Gateway、Strategy
    - _Requirements: 3.1, 3.2, 3.5, 3.6, 3.7_

  - [ ] 3.10 编写属性测试: 净值计算精度
    - **Property 6: Equity Calculation Precision and Consistency**
    - 验证 equity = balance + unrealized_pnl
    - **Validates: Requirements 3.5, 3.7, 4.6, 7.5**

  - [ ] 3.11 实现结构化日志 (logger.rs)
    - 实现 `Logger` struct 和日志级别
    - 实现 `set_log_callback` FFI 函数
    - 实现订单事件、风控事件日志
    - 添加 correlation_id 支持
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.6_

  - [ ] 3.12 实现参数扫描 (optimizer.rs)
    - 集成 Rayon 并行库
    - 实现 `run_parameter_sweep` FFI 函数
    - 实现多引擎并行执行
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.8_

  - [ ] 3.13 编写属性测试: 多引擎线程安全
    - **Property 7: Multi-Engine Thread Safety**
    - 验证并行执行结果与串行一致
    - **Validates: Requirements 12.2, 12.8**

- [ ] 4. Checkpoint - Phase 2 验证
  - 运行 `cargo build --release` 确保编译通过
  - 运行 `cargo clippy` 确保无警告
  - 运行 `cargo test` 确保所有属性测试通过
  - 运行 `cargo bench` 验证热路径性能
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Phase 3: C# Interop Layer
  - [ ] 5.1 创建 C# 解决方案结构
    - 创建 `AegisQuant.Interop` 类库项目 (.NET 8)
    - 创建 `AegisQuant.Interop.Tests` 测试项目
    - 配置项目引用和 NuGet 包 (FsCheck.Xunit)
    - _Requirements: 4.1_

  - [ ] 5.2 实现 C# 互操作结构体 (NativeTypes.cs)
    - 定义 `Tick` struct with `[StructLayout(LayoutKind.Sequential)]`
    - 定义 `OrderRequest` struct with `fixed byte[16]` for symbol
    - 定义 `AccountStatus`, `Position`, `StrategyParams`, `RiskConfig` structs
    - 定义 `DataQualityReport` struct
    - _Requirements: 4.1, 1.5_

  - [ ] 5.3 实现 NativeMethods 类 (NativeMethods.cs)
    - 使用 `[LibraryImport]` 声明所有 FFI 函数
    - 配置 StringMarshalling.Utf8 用于字符串参数
    - 声明 LogCallback delegate with `[UnmanagedFunctionPointer]`
    - _Requirements: 4.2_

  - [ ] 5.4 实现 EngineHandle (SafeHandle)
    - 继承 `SafeHandle` 实现 `EngineHandle`
    - 重写 `ReleaseHandle()` 调用 `free_engine`
    - 确保只释放一次
    - _Requirements: 4.3, 4.5, 8.6_

  - [ ] 5.5 实现 EngineWrapper 类 (EngineWrapper.cs)
    - 实现 `IDisposable` 模式
    - 实现 `_logCallbackKeepAlive` 防止 GC 回收
    - 实现 `LoadData()`, `ProcessTick()`, `GetAccountStatus()` 方法
    - 实现 `SetLogCallback()` 方法
    - 所有 unsafe 代码添加 SAFETY 注释
    - _Requirements: 4.3, 4.4, 4.5, 8.4, 8.5, 8.6_

  - [ ] 5.6 实现错误处理 (ErrorHandler.cs)
    - 定义 `ErrorCodes` 常量类
    - 实现 `CheckResult()` 方法映射错误码到异常
    - 定义自定义异常类 (EngineException, RiskRejectedException, etc.)
    - _Requirements: 4.4_

  - [ ] 5.7 编写 C# 属性测试
    - 使用 FsCheck 测试 FFI 调用安全性
    - 测试 Dispose 模式正确性
    - **Validates: Requirements 4.4, 4.5, 8.6**

- [ ] 6. Checkpoint - Phase 3 验证
  - 复制 Rust 编译的 .dll 到 C# 项目输出目录
  - 运行 `dotnet build` 确保编译通过
  - 运行 `dotnet test` 确保测试通过
  - 编写简单集成测试: 初始化引擎 -> 加载数据 -> 运行回测 -> 获取结果
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 7. Phase 4: C# GUI & Visualization
  - [ ] 7.1 创建 WPF/Avalonia UI 项目
    - 创建 `AegisQuant.UI` 项目
    - 配置 MVVM 框架 (CommunityToolkit.Mvvm)
    - 添加 ScottPlot.WPF 或 ScottPlot.Avalonia 包
    - _Requirements: 7.1, 9.2_

  - [ ] 7.2 实现 Model 层 (BacktestService.cs)
    - 封装 `EngineWrapper` 调用
    - 实现 `RunBacktestAsync()` 异步方法
    - 实现 `OnStatusUpdated` 事件
    - 实现 `OnLogReceived` 事件
    - _Requirements: 6.2, 6.3_

  - [ ] 7.3 实现 ViewModel 层 (MainViewModel.cs)
    - 定义 `ObservableCollection<double> EquityCurve`
    - 定义 `AccountStatus CurrentStatus`
    - 定义 `double Progress`
    - 实现 `LoadDataCommand` (ICommand)
    - 实现 `StartBacktestCommand` (ICommand)
    - 实现 `StopBacktestCommand` (ICommand)
    - _Requirements: 6.1, 6.4, 6.5, 9.2, 9.3_

  - [ ] 7.4 实现 View 层 (MainWindow.xaml)
    - 设计主界面布局 (工具栏、图表区、状态栏)
    - 集成 ScottPlot 图表控件
    - 实现数据绑定到 ViewModel
    - 实现参数配置面板
    - _Requirements: 7.2, 7.3, 7.4, 7.5, 9.3_

  - [ ] 7.5 实现文件加载功能
    - 实现文件选择对话框
    - 显示加载进度和统计信息
    - 显示数据质量报告
    - _Requirements: 5.6, 6.1_

  - [ ] 7.6 实现回测控制功能
    - 实现后台线程执行回测
    - 实现进度更新 (Timer 轮询或回调)
    - 实现停止功能 (CancellationToken)
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

  - [ ] 7.7 实现实时图表更新
    - 实现净值曲线实时绘制
    - 实现图表缩放和平移
    - 显示关键指标 (总收益、最大回撤、当前净值)
    - _Requirements: 7.2, 7.3, 7.4, 7.5_

  - [ ] 7.8 实现参数优化界面
    - 实现参数范围输入
    - 显示优化进度
    - 显示结果排序表格 (按 Sharpe ratio)
    - _Requirements: 12.5, 12.6, 12.7_

  - [ ] 7.9 实现日志显示
    - 实现日志面板 (ListView)
    - 实现日志级别过滤
    - 实现日志导出功能
    - _Requirements: 13.5_

- [ ] 8. Checkpoint - Phase 4 验证
  - 运行完整 UI 应用
  - 测试加载 CSV 数据
  - 测试运行回测并观察实时图表
  - 测试参数优化功能
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 9. Final Integration & Polish
  - [ ] 9.1 端到端集成测试
    - 编写完整回测流程测试
    - 验证 Rust <-> C# 数据一致性
    - 验证内存无泄漏 (使用 dotMemory 或类似工具)
    - _Requirements: 8.5, 8.6_

  - [ ] 9.2 性能优化验证
    - 运行 Rust benchmark 验证热路径延迟
    - 验证大数据集 (1GB+) 加载性能
    - 验证并行参数扫描性能
    - _Requirements: 3.6, 5.1, 12.1_

  - [ ] 9.3 代码质量检查
    - 运行 `cargo clippy` 确保无警告
    - 运行 `dotnet format` 格式化 C# 代码
    - 确保所有 unsafe 代码有 SAFETY 注释
    - _Requirements: 9.1, 9.4, 9.5_

- [ ] 10. Final Checkpoint
  - 所有测试通过
  - 代码质量检查通过
  - 文档完整
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- All tasks are required for comprehensive testing
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties (8 properties total)
- Unit tests validate specific examples and edge cases
- Rust 代码必须通过 `cargo clippy` 检查
- C# 代码遵循 MVVM 模式
- 所有 unsafe 代码必须有 SAFETY 注释
