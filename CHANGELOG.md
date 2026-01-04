# Changelog

All notable changes to AegisQuant will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-01-04

### 🎬 Hybrid Backtest Mode (混合回测模式)

This release introduces a major feature: **Hybrid Backtest Mode**, enabling visual replay and external strategy execution.

### Added

#### Core Features
- **Hybrid Backtest Mode** - Switch between HighSpeed (Rust-driven) and Visual (C#-driven) modes
- **External Strategy Support** - Execute Python scripts and JSON config strategies during backtest
- **Multi-Timeframe Cache** - Pre-computed OHLC data for 1m, 5m, 15m, 30m, 1h, 4h, 1d timeframes
- **Replay Mode Integration** - Step-by-step historical data replay with Play/Pause/Seek controls
- **Trade Signal Visualization** - Buy/Sell markers on candlestick charts with color coding

#### UI Components
- **ReplayControlPanel** - Play, Pause, Step Forward, Step Backward buttons with time slider
- **StatusPanel** - Real-time display of Equity, Position, Unrealized P&L, Realized P&L
- **LogPanel** - Strategy signal and execution logs with level filtering (Debug/Info/Trade/Error)
- **ImportWizardWindow** - Visual CSV import wizard with column mapping and date format detection

#### Data Layer
- **TickDataStore** - Memory-efficient Structure of Arrays (SoA) layout for tick data
- **MarketDataStore** - Thread-safe multi-timeframe OHLC cache with time-window aggregation
- **ColumnMappingDetector** - Auto-detect CSV column names (supports Chinese variants)

#### Rust FFI Extensions
- `process_tick_with_result` - Process tick with pre-allocated event buffer (Caller Allocates pattern)
- `place_order` - Submit buy/sell orders from C# to Rust engine
- `get_ohlc_data` - Retrieve pre-aggregated OHLC data by timeframe
- `load_csv_with_mapping` - Load CSV with custom column mapping and date format
- `fast_forward_to` - Fast-forward engine state without callbacks for seek optimization

#### Architecture
- **Dependency Injection** - Microsoft.Extensions.DependencyInjection for service management
- **Service Interfaces** - IBacktestService, IMarketDataStore, IReplayService, IStrategyManagerService
- **State Machine** - ServiceState (Ready, Running, Faulted) with error recovery

### Performance
- Timeframe switching < 100ms for 1-year datasets
- Chart update < 16ms (60 FPS target)
- Memory footprint < 300MB for 10 million ticks
- Incremental chart updates (no full redraws during replay)

### Testing
- 262 Rust tests (all passing)
- 188 C# tests (184 passing, 4 skipped - require native DLL)
- Property-based tests using proptest (Rust) and FsCheck (C#)
- Memory leak tests for FFI boundary

### Requirements Implemented
- 16 requirements fully implemented and tested
- 24 correctness properties validated

---

## [1.0.0] - 2025-12-01

### Initial Release

- Rust core engine with FFI exports
- C# WPF UI with MVVM pattern
- Backtest engine with MA crossover strategy
- Risk management (position limits, drawdown checks)
- Real-time equity curve visualization
- Multi-language support (English/Chinese)
- Parameter optimization with Rayon parallel execution

---

## Version History

| Version | Date | Highlights |
|---------|------|------------|
| 1.1.0 | 2026-01-04 | Hybrid Backtest Mode, External Strategies, Multi-Timeframe Cache |
| 1.0.0 | 2025-12-01 | Initial Release |
