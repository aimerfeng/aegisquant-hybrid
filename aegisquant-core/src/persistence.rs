//! Persistence module for storing trading data to SQLite.
//!
//! Provides functionality to:
//! - Save trade records
//! - Save account snapshots
//! - Save position data
//! - Recover state from database
//!
//! Requirements: 15.1, 15.2, 15.3, 15.4, 15.5

use std::ffi::{c_char, CStr};
use std::path::Path;
use std::str::FromStr;

use rust_decimal::Decimal;
use rusqlite::{params, Connection, OptionalExtension};

use crate::error::{EngineError, EngineResult};
use crate::ffi::{ERR_NULL_POINTER, ERR_SUCCESS};
use crate::types::Position;

/// Database error code
pub const ERR_DB_ERROR: i32 = -13;

/// Trade record for persistence.
#[derive(Debug, Clone)]
pub struct TradeRecord {
    pub timestamp: i64,
    pub symbol: String,
    pub direction: i32,
    pub quantity: f64,
    pub price: f64,
    pub pnl: f64,
}

/// Account snapshot for persistence.
#[derive(Debug, Clone)]
pub struct AccountSnapshot {
    pub timestamp: i64,
    pub balance: Decimal,
    pub equity: Decimal,
    pub position_count: i32,
}

/// Position record for persistence.
#[derive(Debug, Clone)]
pub struct PositionRecord {
    pub symbol: String,
    pub quantity: f64,
    pub average_price: f64,
    pub unrealized_pnl: f64,
}

/// Recovered state from database.
#[derive(Debug, Clone, Default)]
pub struct RecoveredState {
    pub snapshot: Option<AccountSnapshot>,
    pub positions: Vec<PositionRecord>,
    pub trades: Vec<TradeRecord>,
}

/// Persistence manager for SQLite database operations.
pub struct PersistenceManager {
    conn: Connection,
}

impl PersistenceManager {
    /// Create a new PersistenceManager with the given database path.
    ///
    /// Creates the database file and tables if they don't exist.
    pub fn new<P: AsRef<Path>>(db_path: P) -> EngineResult<Self> {
        let conn = Connection::open(db_path)
            .map_err(|e| EngineError::database(format!("Failed to open database: {}", e)))?;

        let manager = Self { conn };
        manager.create_tables()?;
        Ok(manager)
    }

    /// Create a new in-memory PersistenceManager (for testing).
    pub fn in_memory() -> EngineResult<Self> {
        let conn = Connection::open_in_memory()
            .map_err(|e| EngineError::database(format!("Failed to open in-memory db: {}", e)))?;

        let manager = Self { conn };
        manager.create_tables()?;
        Ok(manager)
    }

    /// Create required database tables.
    fn create_tables(&self) -> EngineResult<()> {
        self.conn
            .execute(
                "CREATE TABLE IF NOT EXISTS trades (
                    id INTEGER PRIMARY KEY,
                    timestamp INTEGER NOT NULL,
                    symbol TEXT NOT NULL,
                    direction INTEGER NOT NULL,
                    quantity REAL NOT NULL,
                    price REAL NOT NULL,
                    pnl REAL,
                    session_date TEXT NOT NULL
                )",
                [],
            )
            .map_err(|e| EngineError::database(format!("Failed to create trades table: {}", e)))?;

        self.conn
            .execute(
                "CREATE TABLE IF NOT EXISTS account_snapshots (
                    id INTEGER PRIMARY KEY,
                    timestamp INTEGER NOT NULL,
                    balance TEXT NOT NULL,
                    equity TEXT NOT NULL,
                    position_count INTEGER NOT NULL,
                    session_date TEXT NOT NULL
                )",
                [],
            )
            .map_err(|e| {
                EngineError::database(format!("Failed to create account_snapshots table: {}", e))
            })?;

        self.conn
            .execute(
                "CREATE TABLE IF NOT EXISTS positions (
                    id INTEGER PRIMARY KEY,
                    symbol TEXT NOT NULL UNIQUE,
                    quantity REAL NOT NULL,
                    average_price REAL NOT NULL,
                    unrealized_pnl REAL NOT NULL,
                    session_date TEXT NOT NULL
                )",
                [],
            )
            .map_err(|e| {
                EngineError::database(format!("Failed to create positions table: {}", e))
            })?;

        // Create indexes for faster queries
        self.conn
            .execute(
                "CREATE INDEX IF NOT EXISTS idx_trades_session ON trades(session_date)",
                [],
            )
            .ok();
        self.conn
            .execute(
                "CREATE INDEX IF NOT EXISTS idx_snapshots_session ON account_snapshots(session_date)",
                [],
            )
            .ok();

        Ok(())
    }

    /// Save a trade record to the database.
    pub fn save_trade(&self, trade: &TradeRecord, session_date: &str) -> EngineResult<()> {
        self.conn
            .execute(
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
            )
            .map_err(|e| EngineError::database(format!("Failed to save trade: {}", e)))?;
        Ok(())
    }

    /// Save an account snapshot to the database.
    pub fn save_account_snapshot(
        &self,
        snapshot: &AccountSnapshot,
        session_date: &str,
    ) -> EngineResult<()> {
        self.conn
            .execute(
                "INSERT INTO account_snapshots (timestamp, balance, equity, position_count, session_date)
                 VALUES (?1, ?2, ?3, ?4, ?5)",
                params![
                    snapshot.timestamp,
                    snapshot.balance.to_string(),
                    snapshot.equity.to_string(),
                    snapshot.position_count,
                    session_date
                ],
            )
            .map_err(|e| EngineError::database(format!("Failed to save snapshot: {}", e)))?;
        Ok(())
    }

    /// Save or update a position in the database.
    pub fn save_position(&self, position: &PositionRecord, session_date: &str) -> EngineResult<()> {
        self.conn
            .execute(
                "INSERT OR REPLACE INTO positions (symbol, quantity, average_price, unrealized_pnl, session_date)
                 VALUES (?1, ?2, ?3, ?4, ?5)",
                params![
                    position.symbol,
                    position.quantity,
                    position.average_price,
                    position.unrealized_pnl,
                    session_date
                ],
            )
            .map_err(|e| EngineError::database(format!("Failed to save position: {}", e)))?;
        Ok(())
    }

    /// Save a Position struct (from types.rs) to the database.
    pub fn save_position_struct(&self, position: &Position, session_date: &str) -> EngineResult<()> {
        let symbol = position.symbol_str().to_string();
        let record = PositionRecord {
            symbol,
            quantity: position.quantity,
            average_price: position.average_price,
            unrealized_pnl: position.unrealized_pnl,
        };
        self.save_position(&record, session_date)
    }

    /// Recover state from the database for a given session date.
    pub fn recover_state(&self, session_date: &str) -> EngineResult<RecoveredState> {
        // 1. Recover latest account snapshot
        let snapshot = self.recover_latest_snapshot(session_date)?;

        // 2. Recover positions
        let positions = self.recover_positions(session_date)?;

        // 3. Recover trades
        let trades = self.recover_trades(session_date)?;

        Ok(RecoveredState {
            snapshot,
            positions,
            trades,
        })
    }

    /// Recover the latest account snapshot for a session.
    fn recover_latest_snapshot(&self, session_date: &str) -> EngineResult<Option<AccountSnapshot>> {
        let mut stmt = self
            .conn
            .prepare(
                "SELECT timestamp, balance, equity, position_count FROM account_snapshots 
                 WHERE session_date = ?1 ORDER BY timestamp DESC LIMIT 1",
            )
            .map_err(|e| EngineError::database(format!("Failed to prepare query: {}", e)))?;

        let snapshot = stmt
            .query_row(params![session_date], |row| {
                let balance_str: String = row.get(1)?;
                let equity_str: String = row.get(2)?;
                Ok(AccountSnapshot {
                    timestamp: row.get(0)?,
                    balance: Decimal::from_str(&balance_str).unwrap_or_default(),
                    equity: Decimal::from_str(&equity_str).unwrap_or_default(),
                    position_count: row.get(3)?,
                })
            })
            .optional()
            .map_err(|e| EngineError::database(format!("Failed to query snapshot: {}", e)))?;

        Ok(snapshot)
    }

    /// Recover positions for a session.
    fn recover_positions(&self, session_date: &str) -> EngineResult<Vec<PositionRecord>> {
        let mut stmt = self
            .conn
            .prepare(
                "SELECT symbol, quantity, average_price, unrealized_pnl FROM positions 
                 WHERE session_date = ?1",
            )
            .map_err(|e| EngineError::database(format!("Failed to prepare query: {}", e)))?;

        let positions = stmt
            .query_map(params![session_date], |row| {
                Ok(PositionRecord {
                    symbol: row.get(0)?,
                    quantity: row.get(1)?,
                    average_price: row.get(2)?,
                    unrealized_pnl: row.get(3)?,
                })
            })
            .map_err(|e| EngineError::database(format!("Failed to query positions: {}", e)))?
            .filter_map(|r| r.ok())
            .collect();

        Ok(positions)
    }

    /// Recover trades for a session.
    fn recover_trades(&self, session_date: &str) -> EngineResult<Vec<TradeRecord>> {
        let mut stmt = self
            .conn
            .prepare(
                "SELECT timestamp, symbol, direction, quantity, price, pnl FROM trades 
                 WHERE session_date = ?1 ORDER BY timestamp",
            )
            .map_err(|e| EngineError::database(format!("Failed to prepare query: {}", e)))?;

        let trades = stmt
            .query_map(params![session_date], |row| {
                Ok(TradeRecord {
                    timestamp: row.get(0)?,
                    symbol: row.get(1)?,
                    direction: row.get(2)?,
                    quantity: row.get(3)?,
                    price: row.get(4)?,
                    pnl: row.get(5)?,
                })
            })
            .map_err(|e| EngineError::database(format!("Failed to query trades: {}", e)))?
            .filter_map(|r| r.ok())
            .collect();

        Ok(trades)
    }

    /// Get all trades for a session.
    pub fn get_trades(&self, session_date: &str) -> EngineResult<Vec<TradeRecord>> {
        self.recover_trades(session_date)
    }

    /// Get trade count for a session.
    pub fn get_trade_count(&self, session_date: &str) -> EngineResult<i64> {
        let count: i64 = self
            .conn
            .query_row(
                "SELECT COUNT(*) FROM trades WHERE session_date = ?1",
                params![session_date],
                |row| row.get(0),
            )
            .map_err(|e| EngineError::database(format!("Failed to count trades: {}", e)))?;
        Ok(count)
    }

    /// Clear all data for a session (for testing).
    pub fn clear_session(&self, session_date: &str) -> EngineResult<()> {
        self.conn
            .execute("DELETE FROM trades WHERE session_date = ?1", params![session_date])
            .map_err(|e| EngineError::database(format!("Failed to clear trades: {}", e)))?;
        self.conn
            .execute(
                "DELETE FROM account_snapshots WHERE session_date = ?1",
                params![session_date],
            )
            .map_err(|e| EngineError::database(format!("Failed to clear snapshots: {}", e)))?;
        self.conn
            .execute("DELETE FROM positions WHERE session_date = ?1", params![session_date])
            .map_err(|e| EngineError::database(format!("Failed to clear positions: {}", e)))?;
        Ok(())
    }
}

// ============================================================================
// FFI Functions
// ============================================================================

/// Create a new PersistenceManager.
///
/// # Safety
/// - `db_path` must be a valid null-terminated UTF-8 string
/// - Caller must call `free_persistence_manager` to release the returned pointer
#[no_mangle]
pub unsafe extern "C" fn create_persistence_manager(
    db_path: *const c_char,
) -> *mut PersistenceManager {
    if db_path.is_null() {
        return std::ptr::null_mut();
    }

    // SAFETY: db_path validated above
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

/// Free a PersistenceManager.
///
/// # Safety
/// - `manager` must be a valid pointer returned by `create_persistence_manager`
/// - Must only be called once per manager
#[no_mangle]
pub unsafe extern "C" fn free_persistence_manager(manager: *mut PersistenceManager) {
    if manager.is_null() {
        return;
    }

    // SAFETY: We don't use catch_unwind here since dropping is unlikely to panic
    // and the Connection type doesn't implement RefUnwindSafe
    let _ = Box::from_raw(manager);
}

/// FFI-compatible trade record for saving.
#[repr(C)]
pub struct FfiTradeRecord {
    pub timestamp: i64,
    pub symbol: [u8; 16],
    pub direction: i32,
    pub quantity: f64,
    pub price: f64,
    pub pnl: f64,
}

/// Save a trade record via FFI.
///
/// # Safety
/// - `manager` must be a valid pointer from `create_persistence_manager`
/// - `trade` must be a valid pointer to FfiTradeRecord
/// - `session_date` must be a valid null-terminated UTF-8 string
#[no_mangle]
pub unsafe extern "C" fn save_trade_ffi(
    manager: *mut PersistenceManager,
    trade: *const FfiTradeRecord,
    session_date: *const c_char,
) -> i32 {
    if manager.is_null() || trade.is_null() || session_date.is_null() {
        return ERR_NULL_POINTER;
    }

    // SAFETY: Pointers validated above
    let manager_ref = &*manager;
    let trade_ref = &*trade;

    let session = match CStr::from_ptr(session_date).to_str() {
        Ok(s) => s,
        Err(_) => return ERR_DB_ERROR,
    };

    let symbol_end = trade_ref.symbol.iter().position(|&b| b == 0).unwrap_or(16);
    let symbol = std::str::from_utf8(&trade_ref.symbol[..symbol_end]).unwrap_or("");

    let record = TradeRecord {
        timestamp: trade_ref.timestamp,
        symbol: symbol.to_string(),
        direction: trade_ref.direction,
        quantity: trade_ref.quantity,
        price: trade_ref.price,
        pnl: trade_ref.pnl,
    };

    match manager_ref.save_trade(&record, session) {
        Ok(()) => ERR_SUCCESS,
        Err(_) => ERR_DB_ERROR,
    }
}

/// FFI-compatible account snapshot for saving.
#[repr(C)]
pub struct FfiAccountSnapshot {
    pub timestamp: i64,
    pub balance: f64,
    pub equity: f64,
    pub position_count: i32,
}

/// Save an account snapshot via FFI.
///
/// # Safety
/// - `manager` must be a valid pointer from `create_persistence_manager`
/// - `snapshot` must be a valid pointer to FfiAccountSnapshot
/// - `session_date` must be a valid null-terminated UTF-8 string
#[no_mangle]
pub unsafe extern "C" fn save_account_snapshot_ffi(
    manager: *mut PersistenceManager,
    snapshot: *const FfiAccountSnapshot,
    session_date: *const c_char,
) -> i32 {
    if manager.is_null() || snapshot.is_null() || session_date.is_null() {
        return ERR_NULL_POINTER;
    }

    // SAFETY: Pointers validated above
    let manager_ref = &*manager;
    let snapshot_ref = &*snapshot;

    let session = match CStr::from_ptr(session_date).to_str() {
        Ok(s) => s,
        Err(_) => return ERR_DB_ERROR,
    };

    let record = AccountSnapshot {
        timestamp: snapshot_ref.timestamp,
        balance: Decimal::from_f64_retain(snapshot_ref.balance).unwrap_or_default(),
        equity: Decimal::from_f64_retain(snapshot_ref.equity).unwrap_or_default(),
        position_count: snapshot_ref.position_count,
    };

    match manager_ref.save_account_snapshot(&record, session) {
        Ok(()) => ERR_SUCCESS,
        Err(_) => ERR_DB_ERROR,
    }
}

/// Save a position via FFI.
///
/// # Safety
/// - `manager` must be a valid pointer from `create_persistence_manager`
/// - `position` must be a valid pointer to Position
/// - `session_date` must be a valid null-terminated UTF-8 string
#[no_mangle]
pub unsafe extern "C" fn save_position_ffi(
    manager: *mut PersistenceManager,
    position: *const Position,
    session_date: *const c_char,
) -> i32 {
    if manager.is_null() || position.is_null() || session_date.is_null() {
        return ERR_NULL_POINTER;
    }

    // SAFETY: Pointers validated above
    let manager_ref = &*manager;
    let position_ref = &*position;

    let session = match CStr::from_ptr(session_date).to_str() {
        Ok(s) => s,
        Err(_) => return ERR_DB_ERROR,
    };

    match manager_ref.save_position_struct(position_ref, session) {
        Ok(()) => ERR_SUCCESS,
        Err(_) => ERR_DB_ERROR,
    }
}

/// Load state from database via FFI.
///
/// # Safety
/// - `manager` must be a valid pointer from `create_persistence_manager`
/// - `session_date` must be a valid null-terminated UTF-8 string
/// - `trade_count` must be a valid pointer to write the number of trades recovered
///
/// # Returns
/// - ERR_SUCCESS on success
/// - ERR_NULL_POINTER if any pointer is null
/// - ERR_DB_ERROR on database error
#[no_mangle]
pub unsafe extern "C" fn load_state_ffi(
    manager: *mut PersistenceManager,
    session_date: *const c_char,
    trade_count: *mut i32,
) -> i32 {
    if manager.is_null() || session_date.is_null() || trade_count.is_null() {
        return ERR_NULL_POINTER;
    }

    // SAFETY: Pointers validated above
    let manager_ref = &*manager;

    let session = match CStr::from_ptr(session_date).to_str() {
        Ok(s) => s,
        Err(_) => return ERR_DB_ERROR,
    };

    match manager_ref.recover_state(session) {
        Ok(state) => {
            *trade_count = state.trades.len() as i32;
            ERR_SUCCESS
        }
        Err(_) => ERR_DB_ERROR,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use rust_decimal_macros::dec;

    #[test]
    fn test_create_persistence_manager() {
        let manager = PersistenceManager::in_memory().unwrap();
        assert!(manager.get_trade_count("2024-01-01").unwrap() == 0);
    }

    #[test]
    fn test_save_and_recover_trade() {
        let manager = PersistenceManager::in_memory().unwrap();
        let session = "2024-01-01";

        let trade = TradeRecord {
            timestamp: 1704067200,
            symbol: "BTCUSDT".to_string(),
            direction: 1,
            quantity: 0.5,
            price: 42000.0,
            pnl: 100.0,
        };

        manager.save_trade(&trade, session).unwrap();

        let trades = manager.get_trades(session).unwrap();
        assert_eq!(trades.len(), 1);
        assert_eq!(trades[0].symbol, "BTCUSDT");
        assert_eq!(trades[0].quantity, 0.5);
    }

    #[test]
    fn test_save_and_recover_snapshot() {
        let manager = PersistenceManager::in_memory().unwrap();
        let session = "2024-01-01";

        let snapshot = AccountSnapshot {
            timestamp: 1704067200,
            balance: dec!(100000.50),
            equity: dec!(100500.75),
            position_count: 2,
        };

        manager.save_account_snapshot(&snapshot, session).unwrap();

        let state = manager.recover_state(session).unwrap();
        assert!(state.snapshot.is_some());

        let recovered = state.snapshot.unwrap();
        assert_eq!(recovered.balance, dec!(100000.50));
        assert_eq!(recovered.equity, dec!(100500.75));
        assert_eq!(recovered.position_count, 2);
    }

    #[test]
    fn test_save_and_recover_position() {
        let manager = PersistenceManager::in_memory().unwrap();
        let session = "2024-01-01";

        let position = PositionRecord {
            symbol: "ETHUSDT".to_string(),
            quantity: 10.0,
            average_price: 2500.0,
            unrealized_pnl: 250.0,
        };

        manager.save_position(&position, session).unwrap();

        let state = manager.recover_state(session).unwrap();
        assert_eq!(state.positions.len(), 1);
        assert_eq!(state.positions[0].symbol, "ETHUSDT");
        assert_eq!(state.positions[0].quantity, 10.0);
    }

    #[test]
    fn test_position_upsert() {
        let manager = PersistenceManager::in_memory().unwrap();
        let session = "2024-01-01";

        let position1 = PositionRecord {
            symbol: "BTCUSDT".to_string(),
            quantity: 1.0,
            average_price: 40000.0,
            unrealized_pnl: 0.0,
        };

        let position2 = PositionRecord {
            symbol: "BTCUSDT".to_string(),
            quantity: 2.0,
            average_price: 41000.0,
            unrealized_pnl: 1000.0,
        };

        manager.save_position(&position1, session).unwrap();
        manager.save_position(&position2, session).unwrap();

        let state = manager.recover_state(session).unwrap();
        assert_eq!(state.positions.len(), 1);
        assert_eq!(state.positions[0].quantity, 2.0);
        assert_eq!(state.positions[0].average_price, 41000.0);
    }

    #[test]
    fn test_recover_empty_session() {
        let manager = PersistenceManager::in_memory().unwrap();
        let state = manager.recover_state("nonexistent").unwrap();

        assert!(state.snapshot.is_none());
        assert!(state.positions.is_empty());
        assert!(state.trades.is_empty());
    }

    #[test]
    fn test_clear_session() {
        let manager = PersistenceManager::in_memory().unwrap();
        let session = "2024-01-01";

        let trade = TradeRecord {
            timestamp: 1704067200,
            symbol: "BTCUSDT".to_string(),
            direction: 1,
            quantity: 0.5,
            price: 42000.0,
            pnl: 100.0,
        };

        manager.save_trade(&trade, session).unwrap();
        assert_eq!(manager.get_trade_count(session).unwrap(), 1);

        manager.clear_session(session).unwrap();
        assert_eq!(manager.get_trade_count(session).unwrap(), 0);
    }

    #[test]
    fn test_multiple_sessions() {
        let manager = PersistenceManager::in_memory().unwrap();

        let trade1 = TradeRecord {
            timestamp: 1704067200,
            symbol: "BTCUSDT".to_string(),
            direction: 1,
            quantity: 1.0,
            price: 42000.0,
            pnl: 0.0,
        };

        let trade2 = TradeRecord {
            timestamp: 1704153600,
            symbol: "ETHUSDT".to_string(),
            direction: -1,
            quantity: 5.0,
            price: 2500.0,
            pnl: 100.0,
        };

        manager.save_trade(&trade1, "2024-01-01").unwrap();
        manager.save_trade(&trade2, "2024-01-02").unwrap();

        assert_eq!(manager.get_trade_count("2024-01-01").unwrap(), 1);
        assert_eq!(manager.get_trade_count("2024-01-02").unwrap(), 1);

        let state1 = manager.recover_state("2024-01-01").unwrap();
        assert_eq!(state1.trades[0].symbol, "BTCUSDT");

        let state2 = manager.recover_state("2024-01-02").unwrap();
        assert_eq!(state2.trades[0].symbol, "ETHUSDT");
    }
}
