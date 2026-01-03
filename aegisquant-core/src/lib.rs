//! AegisQuant-Hybrid Core Engine
//!
//! High-performance quantitative backtesting engine written in Rust.
//! Provides FFI interface for C# interop.

pub mod types;
pub mod ffi;
pub mod ffi_string;
pub mod error;
pub mod precision;
pub mod risk;
pub mod gateway;
pub mod data_loader;
pub mod data_pipeline;
pub mod strategy;
pub mod engine;
pub mod logger;
pub mod optimizer;
pub mod orderbook;
pub mod l1_gateway;
pub mod event_bus;
pub mod warmup;
pub mod indicators;
pub mod persistence;
pub mod emergency;
pub mod latency;

pub use types::*;
pub use ffi::*;
pub use ffi_string::{
    StringCallback, StringWithLenCallback,
    set_last_error_message, clear_last_error, get_last_error,
    with_string_callback, with_string_len_callback,
};
pub use error::{EngineError, EngineResult, set_last_error};
pub use precision::{
    PRICE_EPSILON, QUANTITY_EPSILON, Price, Quantity,
    approx_eq, price_eq, quantity_eq, spread_bps, AccountBalance,
};
pub use risk::*;
pub use gateway::*;
pub use data_loader::*;
pub use strategy::*;
pub use engine::*;
// Note: logger::set_log_callback is intentionally not re-exported here
// to avoid conflict with ffi::set_log_callback. Use ffi::set_log_callback for FFI.
pub use logger::{
    Logger, LogLevel, LogCallback, 
    clear_log_callback, has_log_callback, log,
    log_info, log_warn, log_error, log_debug, log_trace,
};
pub use optimizer::*;
pub use orderbook::{
    OrderBookLevel, OrderBookSnapshot, OrderBookStats, MAX_LEVELS,
    get_orderbook, get_orderbook_stats,
};
pub use l1_gateway::{
    GatewayMode, SlippageModel, FillResult, LevelFill, L1SimulatedGateway,
    set_gateway_mode_internal, get_gateway_mode, set_gateway_mode, get_gateway_mode_ffi,
};
pub use event_bus::{
    Event, EventBus, EventFilter, EventBusStats, EventDrivenStrategy,
    OrderStatus, Subscription, SubscriptionId, SharedEventBus,
    create_shared_event_bus, set_event_callback, clear_event_callback,
    subscribe_event, unsubscribe_event, get_event_bus_stats,
    EVENT_TYPE_TICK, EVENT_TYPE_TIMER, EVENT_TYPE_ORDER_UPDATE,
    EVENT_TYPE_ACCOUNT_UPDATE, EVENT_TYPE_SIGNAL, EVENT_TYPE_CUSTOM,
    // Timer management
    TimerId, TimerEntry, TimerManager, OrderUpdateSender,
    create_timer_manager, destroy_timer_manager,
    schedule_timer_once, schedule_timer_repeating, cancel_timer, process_timers,
};
pub use warmup::{
    WarmupManager, WarmupAware,
    is_warmup_complete, get_warmup_current_bar, get_warmup_remaining_bars,
};
pub use indicators::{
    IndicatorResult, IndicatorCalculator,
    create_indicator_calculator, free_indicator_calculator,
    calculate_indicators, calculate_indicators_batch, reset_indicator_calculator,
    calculate_sma, calculate_ema, calculate_bollinger_bands, calculate_macd,
};
pub use persistence::{
    PersistenceManager, TradeRecord, AccountSnapshot, PositionRecord, RecoveredState,
    FfiTradeRecord, FfiAccountSnapshot, ERR_DB_ERROR,
    create_persistence_manager, free_persistence_manager,
    save_trade_ffi, save_account_snapshot_ffi, save_position_ffi, load_state_ffi,
};
pub use emergency::{
    is_halted, activate_emergency_stop, reset_emergency_stop,
    generate_close_all_orders, check_halt,
    emergency_stop, reset_emergency_stop_ffi, is_emergency_halted, close_all_positions,
};
pub use latency::{
    LatencyStats, LatencyTracker, LatencyGuard,
    record_latency, get_latency_stats, reset_latency_stats,
    set_latency_sample_rate, set_latency_enabled,
    get_latency_stats_ffi, reset_latency_stats_ffi,
    set_latency_sample_rate_ffi, set_latency_enabled_ffi,
};
pub use data_pipeline::{
    DataPipeline, PipelineConfig, MarketDataStore,
    TimescaleDbStore, CsvFileStore, ParquetFileStore,
};
