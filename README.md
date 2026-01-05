# AegisQuant 🛡️📈

<div align="center">

![Rust](https://img.shields.io/badge/Rust-000000?style=for-the-badge&logo=rust&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/WPF-0078D6?style=for-the-badge&logo=windows&logoColor=white)

**高性能量化回测系统 | High-Performance Quantitative Backtesting System**

[English](#english) | [中文](#中文)

</div>

---

## 🚀 AegisQuant 2.0 预告 | Coming Soon

<div align="center">

![Tauri](https://img.shields.io/badge/Tauri-FFC131?style=for-the-badge&logo=tauri&logoColor=black)
![React](https://img.shields.io/badge/React-61DAFB?style=for-the-badge&logo=react&logoColor=black)
![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?style=for-the-badge&logo=typescript&logoColor=white)

</div>

### 🎯 2.0 架构升级计划

AegisQuant 2.0 将采用全新的 **Tauri + React** 架构，实现真正的跨平台支持！

```
┌─────────────────────────────────────────────────────────┐
│                    React Frontend                        │
│  • TypeScript + React 18                                │
│  • TailwindCSS + Shadcn/UI                              │
│  • TradingView Lightweight Charts                       │
│  • Zustand 状态管理                                      │
└─────────────────────────┬───────────────────────────────┘
                          │ Tauri IPC
┌─────────────────────────▼───────────────────────────────┐
│                 Tauri Backend (Rust)                     │
│  • 原生系统集成                                          │
│  • 文件系统访问                                          │
│  • 系统托盘 & 通知                                       │
└─────────────────────────┬───────────────────────────────┘
                          │ Direct Call
┌─────────────────────────▼───────────────────────────────┐
│                 Rust Core Engine                         │
│  • 复用现有 aegisquant-core                             │
│  • 高性能回测引擎                                        │
│  • 策略执行 & 风控                                       │
└─────────────────────────────────────────────────────────┘
```

### ✨ 2.0 新特性预览

| 特性 | 1.x (WPF) | 2.0 (Tauri) |
|------|-----------|-------------|
| 跨平台 | ❌ Windows Only | ✅ Windows/macOS/Linux |
| 安装包大小 | ~150MB | ~15MB |
| 启动速度 | 3-5s | <1s |
| 内存占用 | ~200MB | ~50MB |
| 图表库 | ScottPlot | TradingView Charts |
| 主题 | 暗色/亮色 | 自定义主题系统 |
| 插件系统 | C#/Python | WASM 插件 |
| 实时数据 | WebSocket | WebSocket + SSE |

### 🗓️ 开发路线图

- [x] **v1.0** - WPF 基础版本 ✅
- [x] **v1.5** - 混合回测模式 + 外部策略支持 ✅
- [ ] **v2.0-alpha** - Tauri 框架搭建 (Q1 2026)
- [ ] **v2.0-beta** - 核心功能迁移 (Q2 2026)
- [ ] **v2.0** - 正式发布 (Q3 2026)

### 🔧 技术栈对比

| 层级 | 1.x | 2.0 |
|------|-----|-----|
| 前端框架 | WPF (XAML) | React 18 |
| 状态管理 | MVVM | Zustand |
| 样式 | ResourceDictionary | TailwindCSS |
| 图表 | ScottPlot | TradingView Lightweight |
| 后端 | C# Interop | Tauri (Rust) |
| 核心引擎 | Rust FFI | Rust (直接调用) |
| 打包 | MSIX | Tauri Bundle |

---

## English

### 🎯 Overview

AegisQuant is a high-performance quantitative backtesting and trading system built with a **Rust + C# hybrid architecture**. The Rust core engine handles computationally intensive tasks (data processing, strategy execution, risk management), while the C# layer provides a modern WPF GUI with real-time visualization.

### ✨ Features

- ⚡ **Ultra-Low Latency** - Hot path < 1μs with stack memory and pre-allocation
- 📊 **Large-Scale Data** - Process GB-level tick data with Polars
- 🛡️ **Memory Safe** - Zero panics across FFI boundary
- 🔄 **Backtest/Live Ready** - Gateway abstraction for seamless switching
- 🌍 **Multi-Language UI** - English and Chinese support
- 📈 **Real-Time Charts** - Live equity curve with ScottPlot
- 🎬 **Hybrid Backtest Mode** - Visual replay with external strategy support (Python/JSON)
- 🔄 **Multi-Timeframe Cache** - Instant switching between 1m/5m/15m/30m/1h/4h/1d
- 📝 **Smart Data Import** - Auto-detect CSV column mapping and date formats
- 🎯 **Trade Markers** - Visual buy/sell signals on candlestick charts
- 📋 **Strategy Logging** - Real-time signal and execution logs with filtering
- 🖊️ **Drawing Tools** - Trend lines, horizontal/vertical lines on charts
- 🐍 **Python Strategy** - Write strategies in Python with full indicator support

### 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    C# WPF UI                             │
│  • MVVM Pattern (CommunityToolkit.Mvvm)                 │
│  • Real-time Charts (ScottPlot 5.0)                     │
│  • i18n Support (EN/CN)                                 │
└─────────────────────────┬───────────────────────────────┘
                          │ P/Invoke
┌─────────────────────────▼───────────────────────────────┐
│                 C# Interop Layer                         │
│  • SafeHandle for resource management                   │
│  • Error code to exception mapping                      │
│  • Callback delegate pinning                            │
└─────────────────────────┬───────────────────────────────┘
                          │ FFI (extern "C")
┌─────────────────────────▼───────────────────────────────┐
│                 Rust Core Engine                         │
│  • BacktestEngine - Strategy execution                  │
│  • RiskManager - Pre-trade risk checks                  │
│  • Gateway - Order routing abstraction                  │
│  • DataLoader - Polars CSV/Parquet loading              │
│  • Optimizer - Rayon parallel parameter sweep           │
└─────────────────────────────────────────────────────────┘
```

### 🚀 Quick Start

#### Prerequisites

- Rust (stable >= 1.75)
- .NET SDK 8.0
- Windows 10/11

#### Build & Run

```bash
# 1. Clone the repository
git clone https://github.com/aimerfeng/aegisquant-hybrid.git
cd aegisquant-hybrid

# 2. Build Rust core engine
cd aegisquant-core
cargo build --release

# 3. Copy DLL to UI project
copy target\release\aegisquant_core.dll ..\AegisQuant.UI\bin\Debug\net8.0-windows\

# 4. Run the application
cd ..\AegisQuant.UI
dotnet run
```

### 📁 Project Structure

```
aegisquant-hybrid/
├── aegisquant-core/          # Rust core engine
│   ├── src/
│   │   ├── engine.rs         # Backtest engine
│   │   ├── strategy.rs       # Trading strategies
│   │   ├── risk.rs           # Risk management
│   │   ├── gateway.rs        # Order execution
│   │   ├── data_loader.rs    # Data loading (Polars)
│   │   ├── ffi.rs            # FFI exports
│   │   └── types.rs          # Core data types
│   └── tests/                # Property-based tests
├── AegisQuant.Interop/       # C# interop layer
│   ├── NativeTypes.cs        # FFI struct definitions
│   ├── NativeMethods.cs      # P/Invoke declarations
│   ├── EngineWrapper.cs      # Safe wrapper class
│   └── ErrorHandler.cs       # Error handling
├── AegisQuant.UI/            # WPF application
│   ├── Views/                # XAML views
│   ├── ViewModels/           # MVVM view models
│   ├── Models/               # Business logic
│   ├── Controls/             # Custom controls
│   │   └── DrawingTools/     # Chart drawing tools
│   ├── Strategy/             # Strategy system
│   │   └── Loaders/          # Strategy loaders
│   ├── Resources/            # i18n resources
│   └── Services/             # Application services
├── strategies/               # Strategy examples
│   └── examples/             # JSON/Python strategies
├── test_samples/             # Test data & strategies
│   └── profitable_strategy/  # Profitable strategy demo
└── AegisQuant.Interop.Tests/ # Integration tests
```

### 🧪 Testing

```bash
# Run Rust tests
cd aegisquant-core
cargo test

# Run C# tests
cd ..
dotnet test
```

### 📜 License

MIT License

---

## 中文

### 🎯 概述

AegisQuant 是一个高性能量化回测与交易系统，采用 **Rust + C# 混合架构**。Rust 核心引擎负责计算密集型任务（数据处理、策略执行、风控管理），C# 层提供现代化的 WPF 图形界面和实时可视化。

### ✨ 特性

- ⚡ **超低延迟** - 热路径 < 1μs，栈内存 + 预分配
- 📊 **大规模数据** - Polars 处理 GB 级 Tick 数据
- 🛡️ **内存安全** - 跨 FFI 边界零 panic
- 🔄 **回测/实盘就绪** - Gateway 抽象层支持无缝切换
- 🌍 **多语言界面** - 支持中英文切换
- 📈 **实时图表** - ScottPlot 实时净值曲线
- 🎬 **混合回测模式** - 可视化回放，支持外部策略 (Python/JSON)
- 🔄 **多周期缓存** - 1m/5m/15m/30m/1h/4h/1d 瞬时切换
- 📝 **智能数据导入** - 自动检测 CSV 列映射和日期格式
- 🎯 **交易标记** - K线图上显示买卖信号
- 📋 **策略日志** - 实时信号和执行日志，支持过滤
- 🖊️ **绘图工具** - 趋势线、水平线、垂直线绘制
- 🐍 **Python 策略** - 支持 Python 编写策略，完整指标支持

### 🏗️ 架构

```
┌─────────────────────────────────────────────────────────┐
│                    C# WPF 界面                           │
│  • MVVM 模式 (CommunityToolkit.Mvvm)                    │
│  • 实时图表 (ScottPlot 5.0)                             │
│  • 国际化支持 (中/英)                                    │
└─────────────────────────┬───────────────────────────────┘
                          │ P/Invoke
┌─────────────────────────▼───────────────────────────────┐
│                 C# 互操作层                              │
│  • SafeHandle 资源管理                                  │
│  • 错误码到异常映射                                      │
│  • 回调委托固定                                          │
└─────────────────────────┬───────────────────────────────┘
                          │ FFI (extern "C")
┌─────────────────────────▼───────────────────────────────┐
│                 Rust 核心引擎                            │
│  • BacktestEngine - 策略执行                            │
│  • RiskManager - 前置风控检查                           │
│  • Gateway - 订单路由抽象                               │
│  • DataLoader - Polars CSV/Parquet 加载                 │
│  • Optimizer - Rayon 并行参数扫描                       │
└─────────────────────────────────────────────────────────┘
```

### 🚀 快速开始

#### 环境要求

- Rust (stable >= 1.75)
- .NET SDK 8.0
- Windows 10/11

#### 构建与运行

```bash
# 1. 克隆仓库
git clone https://github.com/aimerfeng/aegisquant-hybrid.git
cd aegisquant-hybrid

# 2. 编译 Rust 核心引擎
cd aegisquant-core
cargo build --release

# 3. 复制 DLL 到 UI 项目
copy target\release\aegisquant_core.dll ..\AegisQuant.UI\bin\Debug\net8.0-windows\

# 4. 运行应用
cd ..\AegisQuant.UI
dotnet run
```

### 📁 项目结构

```
aegisquant-hybrid/
├── aegisquant-core/          # Rust 核心引擎
│   ├── src/
│   │   ├── engine.rs         # 回测引擎
│   │   ├── strategy.rs       # 交易策略
│   │   ├── risk.rs           # 风控管理
│   │   ├── gateway.rs        # 订单执行
│   │   ├── data_loader.rs    # 数据加载 (Polars)
│   │   ├── ffi.rs            # FFI 导出
│   │   └── types.rs          # 核心数据类型
│   └── tests/                # 属性测试
├── AegisQuant.Interop/       # C# 互操作层
│   ├── NativeTypes.cs        # FFI 结构体定义
│   ├── NativeMethods.cs      # P/Invoke 声明
│   ├── EngineWrapper.cs      # 安全封装类
│   └── ErrorHandler.cs       # 错误处理
├── AegisQuant.UI/            # WPF 应用
│   ├── Views/                # XAML 视图
│   ├── ViewModels/           # MVVM 视图模型
│   ├── Models/               # 业务逻辑
│   ├── Controls/             # 自定义控件
│   │   └── DrawingTools/     # 图表绘图工具
│   ├── Strategy/             # 策略系统
│   │   └── Loaders/          # 策略加载器
│   ├── Resources/            # 国际化资源
│   └── Services/             # 应用服务
├── strategies/               # 策略示例
│   └── examples/             # JSON/Python 策略
├── test_samples/             # 测试数据和策略
│   └── profitable_strategy/  # 盈利策略演示
└── AegisQuant.Interop.Tests/ # 集成测试
```

### 🧪 测试

```bash
# 运行 Rust 测试
cd aegisquant-core
cargo test

# 运行 C# 测试
cd ..
dotnet test
```

### 📜 许可证

MIT License

---

<div align="center">

**Made with ❤️ by [aimerfeng](https://github.com/aimerfeng)**

⭐ Star this repo if you find it useful!

</div>
