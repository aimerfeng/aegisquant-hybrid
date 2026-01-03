# AegisQuant-Hybrid 优化版本文档 (docs2)

本目录包含 AegisQuant-Hybrid 系统优化升级的详细说明文档，与原始 `docs/` 目录分开管理。

## 文档索引

### 核心引擎优化

| 文档 | 主题 | 对应需求 |
|------|------|----------|
| [01-ffi-string-safety.md](./01-ffi-string-safety.md) | FFI 字符串处理内存安全 | Requirement 1 |
| [02-error-propagation.md](./02-error-propagation.md) | 错误传播与 Panic 消除 | Requirement 2 |
| [03-float-precision.md](./03-float-precision.md) | 浮点数比较健壮性 | Requirement 3 |
| [04-atomic-logger.md](./04-atomic-logger.md) | 线程安全的日志回调系统 | Requirement 4 |
| [05-l1-orderbook.md](./05-l1-orderbook.md) | L1 订单簿模拟 | Requirement 5 |
| [06-event-bus.md](./06-event-bus.md) | 异步事件总线架构 | Requirement 6 |
| [07-warmup-period.md](./07-warmup-period.md) | 策略预热机制 | Requirement 7 |
| [08-callback-lifecycle.md](./08-callback-lifecycle.md) | C# 回调生命周期管理 | Requirement 8 |

### 前端可视化优化

| 文档 | 主题 | 对应需求 |
|------|------|----------|
| [09-candlestick-chart.md](./09-candlestick-chart.md) | 专业 K 线图表与技术指标 | Requirement 9 |
| [10-docking-layout.md](./10-docking-layout.md) | 可停靠布局系统 | Requirement 10 |
| [18-china-colors.md](./18-china-colors.md) | A 股配色规范 (红涨绿跌) | Requirement 18 |
| [19-multi-indicators.md](./19-multi-indicators.md) | 多指标叠加系统 | Requirement 19 |
| [20-level2-orderbook.md](./20-level2-orderbook.md) | 五档盘口与深度图 | Requirement 20 |

### 安全合规与运维

| 文档 | 主题 | 对应需求 |
|------|------|----------|
| [11-multi-environment.md](./11-multi-environment.md) | 多环境配置管理 | Requirement 11 |
| [12-audit-trail.md](./12-audit-trail.md) | 审计日志系统 | Requirement 12 |
| [13-latency-monitoring.md](./13-latency-monitoring.md) | 延迟监控与性能指标 | Requirement 13 |
| [14-login-auth.md](./14-login-auth.md) | 模拟登录与权限控制 | Requirement 14 |

### 生产级必备功能

| 文档 | 主题 | 对应需求 |
|------|------|----------|
| [15-persistence.md](./15-persistence.md) | 本地数据持久化 | Requirement 15 |
| [16-manual-override.md](./16-manual-override.md) | 手动干预与应急控制 | Requirement 16 |
| [17-notifications.md](./17-notifications.md) | 消息通知系统 | Requirement 17 |

## 五大能力总结

```
┌─────────────────────────────────────────────────────────────┐
│                    AegisQuant-Hybrid v2.0                    │
├─────────────────────────────────────────────────────────────┤
│  能跑 (Run)    │ Rust Engine + Polars + ta crate            │
│  能看 (View)   │ WPF + ScottPlot + K线 + 五档盘口           │
│  能控 (Control)│ Risk Manager + Manual Override + 一键停止  │
│  能存 (Store)  │ SQLite/DuckDB Persistence                  │
│  能防 (Protect)│ FFI Safety + Panic Catch + 审计日志        │
└─────────────────────────────────────────────────────────────┘
```

## 文档状态

### 已完成 (20/20) ✅

- [x] 01-ffi-string-safety.md - FFI 字符串处理内存安全
- [x] 02-error-propagation.md - 错误传播与 Panic 消除
- [x] 03-float-precision.md - 浮点数比较健壮性
- [x] 04-atomic-logger.md - 线程安全的日志回调系统
- [x] 05-l1-orderbook.md - L1 订单簿模拟
- [x] 06-event-bus.md - 异步事件总线架构
- [x] 07-warmup-period.md - 策略预热机制
- [x] 08-callback-lifecycle.md - C# 回调生命周期管理
- [x] 09-candlestick-chart.md - 专业 K 线图表与技术指标
- [x] 10-docking-layout.md - 可停靠布局系统
- [x] 11-multi-environment.md - 多环境配置管理
- [x] 12-audit-trail.md - 审计日志系统
- [x] 13-latency-monitoring.md - 延迟监控与性能指标
- [x] 14-login-auth.md - 模拟登录与权限控制
- [x] 15-persistence.md - 本地数据持久化
- [x] 16-manual-override.md - 手动干预与应急控制
- [x] 17-notifications.md - 消息通知系统
- [x] 18-china-colors.md - A 股配色规范 (红涨绿跌)
- [x] 19-multi-indicators.md - 多指标叠加系统
- [x] 20-level2-orderbook.md - 五档盘口与深度图

## 版本历史

- **v2.0** (当前): 20 项优化需求，涵盖核心引擎、前端可视化、安全合规、生产级功能
- **v1.0** (原始): 14 项基础需求，见 `docs/` 目录
