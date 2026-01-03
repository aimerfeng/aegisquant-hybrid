# 持久化详解

## 概述

持久化是交易系统的核心功能，用于保存交易记录、账户快照和持仓数据。本文档详细说明如何使用 SQLite 实现数据持久化和状态恢复。

## 问题分析

### 无持久化的风险

1. **数据丢失**: 程序崩溃后数据全部丢失
2. **无法恢复**: 重启后无法继续之前的状态
3. **审计困难**: 无历史交易记录可查
4. **分析受限**: 无法进行事后分析

### 设计目标

- 保存交易记录、账户快照、持仓数据
- 支持按会话日期隔离
- 支持状态恢复
- 使用 SQLite 轻量级数据库


## 解决方案

### 数据结构定义

```rust
// persistence.rs

/// 交易记录
#[derive(Debug, Clone)]
pub struct TradeRecord {
    pub timestamp: i64,
    pub symbol: String,
    pub direction: i32,
    pub quantity: f64,
    pub price: f64,
    pub pnl: f64,
}

/// 账户快照
#[derive(Debug, Clone)]
pub struct AccountSnapshot {
    pub timestamp: i64,
    pub balance: Decimal,
    pub equity: Decimal,
    pub position_count: i32,
}

/// 持仓记录
#[derive(Debug, Clone)]
pub struct PositionRecord {
    pub symbol: String,
    pub quantity: f64,
    pub average_price: f64,
    pub unrealized_pnl: f64,
}

/// 恢复的状态
#[derive(Debug, Clone, Default)]
pub struct RecoveredState {
    pub snapshot: Option<AccountSnapshot>,
    pub positions: Vec<PositionRecord>,
    pub trades: Vec<TradeRecord>,
}
```

### 数据库表结构

```rust
fn create_tables(&self) -> EngineResult<()> {
    // 交易记录表
    self.conn.execute(
        "CREATE TABLE IF NOT EXISTS trades (
            id INTEGER PRIMARY KEY,
            timestamp INTEGER NOT NULL,
            symbol TEXT NOT NULL,
            direction INTEGER NOT NULL,
            quantity REAL NOT NULL,
            price REAL NOT NULL,
            pnl REAL,
            session_date TEXT NOT NULL
        )", [])?;

    // 账户快照表
    self.conn.execute(
        "CREATE TABLE IF NOT EXISTS account_snapshots (
            id INTEGER PRIMARY KEY,
            timestamp INTEGER NOT NULL,
            balance TEXT NOT NULL,
            equity TEXT NOT NULL,
            position_count INTEGER NOT NULL,
            session_date TEXT NOT NULL
        )", [])?;

    // 持仓表
    self.conn.execute(
        "CREATE TABLE IF NOT EXISTS positions (
            id INTEGER PRIMARY KEY,
            symbol TEXT NOT NULL UNIQUE,
            quantity REAL NOT NULL,
            average_price REAL NOT NULL,
            unrealized_pnl REAL NOT NULL,
            session_date TEXT NOT NULL
        )", [])?;

    // 创建索引
    self.conn.execute(
        "CREATE INDEX IF NOT EXISTS idx_trades_session ON trades(session_date)",
        [])?;

    Ok(())
}
```

### 数据保存

```rust
/// 保存交易记录
pub fn save_trade(&self, trade: &TradeRecord, session_date: &str) -> EngineResult<()> {
    self.conn.execute(
        "INSERT INTO trades (timestamp, symbol, direction, quantity, price, pnl, session_date)
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)",
        params![
            trade.timestamp,
            trade.symbol,
            trade.direction,
            trade.quantity,
            trade.price,
            trade.pnl,
            session_date
        ],
    )?;
    Ok(())
}

/// 保存或更新持仓 (UPSERT)
pub fn save_position(&self, position: &PositionRecord, session_date: &str) -> EngineResult<()> {
    self.conn.execute(
        "INSERT OR REPLACE INTO positions (symbol, quantity, average_price, unrealized_pnl, session_date)
         VALUES (?1, ?2, ?3, ?4, ?5)",
        params![
            position.symbol,
            position.quantity,
            position.average_price,
            position.unrealized_pnl,
            session_date
        ],
    )?;
    Ok(())
}
```

### 状态恢复

```rust
/// 恢复指定会话的状态
pub fn recover_state(&self, session_date: &str) -> EngineResult<RecoveredState> {
    // 1. 恢复最新账户快照
    let snapshot = self.recover_latest_snapshot(session_date)?;

    // 2. 恢复持仓
    let positions = self.recover_positions(session_date)?;

    // 3. 恢复交易记录
    let trades = self.recover_trades(session_date)?;

    Ok(RecoveredState { snapshot, positions, trades })
}

/// 恢复最新账户快照
fn recover_latest_snapshot(&self, session_date: &str) -> EngineResult<Option<AccountSnapshot>> {
    let mut stmt = self.conn.prepare(
        "SELECT timestamp, balance, equity, position_count FROM account_snapshots 
         WHERE session_date = ?1 ORDER BY timestamp DESC LIMIT 1")?;

    let snapshot = stmt.query_row(params![session_date], |row| {
        let balance_str: String = row.get(1)?;
        let equity_str: String = row.get(2)?;
        Ok(AccountSnapshot {
            timestamp: row.get(0)?,
            balance: Decimal::from_str(&balance_str).unwrap_or_default(),
            equity: Decimal::from_str(&equity_str).unwrap_or_default(),
            position_count: row.get(3)?,
        })
    }).optional()?;

    Ok(snapshot)
}
```

### FFI 接口

```rust
/// 创建持久化管理器
#[no_mangle]
pub unsafe extern "C" fn create_persistence_manager(
    db_path: *const c_char,
) -> *mut PersistenceManager {
    if db_path.is_null() { return std::ptr::null_mut(); }

    let path_cstr = CStr::from_ptr(db_path);
    let path = match path_cstr.to_str() {
        Ok(s) => s,
        Err(_) => return std::ptr::null_mut(),
    };

    match PersistenceManager::new(path) {
        Ok(manager) => Box::into_raw(Box::new(manager)),
        Err(_) => std::ptr::null_mut(),
    }
}

/// 释放持久化管理器
#[no_mangle]
pub unsafe extern "C" fn free_persistence_manager(manager: *mut PersistenceManager) {
    if !manager.is_null() {
        let _ = Box::from_raw(manager);
    }
}
```

## 使用示例

```rust
// 创建管理器
let manager = PersistenceManager::new("trading.db")?;

// 保存交易
let trade = TradeRecord {
    timestamp: 1704067200,
    symbol: "BTCUSDT".to_string(),
    direction: 1,
    quantity: 0.5,
    price: 42000.0,
    pnl: 100.0,
};
manager.save_trade(&trade, "2024-01-01")?;

// 保存账户快照
let snapshot = AccountSnapshot {
    timestamp: 1704067200,
    balance: dec!(100000.50),
    equity: dec!(100500.75),
    position_count: 2,
};
manager.save_account_snapshot(&snapshot, "2024-01-01")?;

// 恢复状态
let state = manager.recover_state("2024-01-01")?;
println!("恢复了 {} 笔交易", state.trades.len());
```

## 面试话术

### Q: 为什么选择 SQLite？

**A**: 三个原因：
1. **零配置**: 无需安装数据库服务
2. **单文件**: 便于备份和迁移
3. **性能足够**: 单机交易系统完全够用

### Q: 为什么按会话日期隔离？

**A**: 数据管理需要：
1. **查询效率**: 按日期索引加速查询
2. **数据清理**: 可以按日期清理旧数据
3. **状态恢复**: 恢复特定日期的状态

### Q: 持仓为什么用 UPSERT？

**A**: 持仓是实时状态：
- 同一标的只有一条记录
- 每次更新覆盖旧数据
- `INSERT OR REPLACE` 实现原子更新
