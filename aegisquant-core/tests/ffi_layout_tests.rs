//! Property-based tests for FFI struct memory layout.
//!
//! Feature: aegisquant-hybrid, Property 1: FFI Struct Memory Layout Round-Trip
//! Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5

use proptest::prelude::*;
use aegisquant_core::types::*;

/// Generate a random symbol as [u8; 16]
fn arb_symbol() -> impl Strategy<Value = [u8; 16]> {
    proptest::collection::vec(b'A'..=b'Z', 1..=15)
        .prop_map(|chars| {
            let mut symbol = [0u8; 16];
            for (i, &c) in chars.iter().enumerate() {
                symbol[i] = c;
            }
            symbol
        })
}

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]

    /// Property 1: Tick struct round-trip through raw bytes
    /// For any valid Tick, serializing to bytes and deserializing should produce identical values.
    #[test]
    fn tick_memory_layout_roundtrip(
        timestamp in any::<i64>(),
        price in 0.01f64..1_000_000.0,
        volume in 0.0f64..1_000_000.0
    ) {
        let original = Tick { timestamp, price, volume };
        
        // Serialize to raw bytes
        let bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(
                &original as *const Tick as *const u8,
                std::mem::size_of::<Tick>()
            )
        };
        
        // Deserialize from raw bytes
        let reconstructed: Tick = unsafe {
            std::ptr::read(bytes.as_ptr() as *const Tick)
        };
        
        prop_assert_eq!(original.timestamp, reconstructed.timestamp);
        prop_assert!((original.price - reconstructed.price).abs() < f64::EPSILON);
        prop_assert!((original.volume - reconstructed.volume).abs() < f64::EPSILON);
    }

    /// Property 1: OrderRequest struct round-trip through raw bytes
    #[test]
    fn order_request_memory_layout_roundtrip(
        symbol in arb_symbol(),
        quantity in 0.01f64..100_000.0,
        direction in prop_oneof![Just(1i32), Just(-1i32)],
        order_type in 0i32..=1,
        limit_price in 0.01f64..1_000_000.0
    ) {
        let original = OrderRequest {
            symbol,
            quantity,
            direction,
            order_type,
            limit_price,
        };
        
        let bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(
                &original as *const OrderRequest as *const u8,
                std::mem::size_of::<OrderRequest>()
            )
        };
        
        let reconstructed: OrderRequest = unsafe {
            std::ptr::read(bytes.as_ptr() as *const OrderRequest)
        };
        
        prop_assert_eq!(original.symbol, reconstructed.symbol);
        prop_assert!((original.quantity - reconstructed.quantity).abs() < f64::EPSILON);
        prop_assert_eq!(original.direction, reconstructed.direction);
        prop_assert_eq!(original.order_type, reconstructed.order_type);
        prop_assert!((original.limit_price - reconstructed.limit_price).abs() < f64::EPSILON);
    }

    /// Property 1: Position struct round-trip through raw bytes
    #[test]
    fn position_memory_layout_roundtrip(
        symbol in arb_symbol(),
        quantity in -100_000.0f64..100_000.0,
        average_price in 0.01f64..1_000_000.0,
        unrealized_pnl in -1_000_000.0f64..1_000_000.0,
        realized_pnl in -1_000_000.0f64..1_000_000.0
    ) {
        let original = Position {
            symbol,
            quantity,
            average_price,
            unrealized_pnl,
            realized_pnl,
        };
        
        let bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(
                &original as *const Position as *const u8,
                std::mem::size_of::<Position>()
            )
        };
        
        let reconstructed: Position = unsafe {
            std::ptr::read(bytes.as_ptr() as *const Position)
        };
        
        prop_assert_eq!(original.symbol, reconstructed.symbol);
        prop_assert!((original.quantity - reconstructed.quantity).abs() < f64::EPSILON);
        prop_assert!((original.average_price - reconstructed.average_price).abs() < f64::EPSILON);
        prop_assert!((original.unrealized_pnl - reconstructed.unrealized_pnl).abs() < f64::EPSILON);
        prop_assert!((original.realized_pnl - reconstructed.realized_pnl).abs() < f64::EPSILON);
    }

    /// Property 1: AccountStatus struct round-trip through raw bytes
    #[test]
    fn account_status_memory_layout_roundtrip(
        balance in 0.0f64..10_000_000.0,
        equity in 0.0f64..10_000_000.0,
        available in 0.0f64..10_000_000.0,
        position_count in 0i32..1000,
        total_pnl in -1_000_000.0f64..1_000_000.0
    ) {
        let original = AccountStatus {
            balance,
            equity,
            available,
            position_count,
            total_pnl,
        };
        
        let bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(
                &original as *const AccountStatus as *const u8,
                std::mem::size_of::<AccountStatus>()
            )
        };
        
        let reconstructed: AccountStatus = unsafe {
            std::ptr::read(bytes.as_ptr() as *const AccountStatus)
        };
        
        prop_assert!((original.balance - reconstructed.balance).abs() < f64::EPSILON);
        prop_assert!((original.equity - reconstructed.equity).abs() < f64::EPSILON);
        prop_assert!((original.available - reconstructed.available).abs() < f64::EPSILON);
        prop_assert_eq!(original.position_count, reconstructed.position_count);
        prop_assert!((original.total_pnl - reconstructed.total_pnl).abs() < f64::EPSILON);
    }

    /// Property 1: StrategyParams struct round-trip through raw bytes
    #[test]
    fn strategy_params_memory_layout_roundtrip(
        short_ma_period in 1i32..100,
        long_ma_period in 10i32..500,
        position_size in 0.01f64..100_000.0,
        stop_loss_pct in 0.001f64..0.5,
        take_profit_pct in 0.001f64..1.0,
        warmup_bars in 0i32..100
    ) {
        let original = StrategyParams {
            short_ma_period,
            long_ma_period,
            position_size,
            stop_loss_pct,
            take_profit_pct,
            warmup_bars,
        };
        
        let bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(
                &original as *const StrategyParams as *const u8,
                std::mem::size_of::<StrategyParams>()
            )
        };
        
        let reconstructed: StrategyParams = unsafe {
            std::ptr::read(bytes.as_ptr() as *const StrategyParams)
        };
        
        prop_assert_eq!(original.short_ma_period, reconstructed.short_ma_period);
        prop_assert_eq!(original.long_ma_period, reconstructed.long_ma_period);
        prop_assert!((original.position_size - reconstructed.position_size).abs() < f64::EPSILON);
        prop_assert!((original.stop_loss_pct - reconstructed.stop_loss_pct).abs() < f64::EPSILON);
        prop_assert!((original.take_profit_pct - reconstructed.take_profit_pct).abs() < f64::EPSILON);
        prop_assert_eq!(original.warmup_bars, reconstructed.warmup_bars);
    }

    /// Property 1: RiskConfig struct round-trip through raw bytes
    #[test]
    fn risk_config_memory_layout_roundtrip(
        max_order_rate in 1i32..100,
        max_position_size in 0.01f64..1_000_000.0,
        max_order_value in 0.01f64..10_000_000.0,
        max_drawdown_pct in 0.01f64..1.0
    ) {
        let original = RiskConfig {
            max_order_rate,
            max_position_size,
            max_order_value,
            max_drawdown_pct,
        };
        
        let bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(
                &original as *const RiskConfig as *const u8,
                std::mem::size_of::<RiskConfig>()
            )
        };
        
        let reconstructed: RiskConfig = unsafe {
            std::ptr::read(bytes.as_ptr() as *const RiskConfig)
        };
        
        prop_assert_eq!(original.max_order_rate, reconstructed.max_order_rate);
        prop_assert!((original.max_position_size - reconstructed.max_position_size).abs() < f64::EPSILON);
        prop_assert!((original.max_order_value - reconstructed.max_order_value).abs() < f64::EPSILON);
        prop_assert!((original.max_drawdown_pct - reconstructed.max_drawdown_pct).abs() < f64::EPSILON);
    }

    /// Property 1: DataQualityReport struct round-trip through raw bytes
    #[test]
    fn data_quality_report_memory_layout_roundtrip(
        total_ticks in 0i64..10_000_000,
        valid_ticks in 0i64..10_000_000,
        invalid_ticks in 0i64..100_000,
        anomaly_ticks in 0i64..100_000,
        first_timestamp in any::<i64>(),
        last_timestamp in any::<i64>()
    ) {
        let original = DataQualityReport {
            total_ticks,
            valid_ticks,
            invalid_ticks,
            anomaly_ticks,
            first_timestamp,
            last_timestamp,
        };
        
        let bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(
                &original as *const DataQualityReport as *const u8,
                std::mem::size_of::<DataQualityReport>()
            )
        };
        
        let reconstructed: DataQualityReport = unsafe {
            std::ptr::read(bytes.as_ptr() as *const DataQualityReport)
        };
        
        prop_assert_eq!(original.total_ticks, reconstructed.total_ticks);
        prop_assert_eq!(original.valid_ticks, reconstructed.valid_ticks);
        prop_assert_eq!(original.invalid_ticks, reconstructed.invalid_ticks);
        prop_assert_eq!(original.anomaly_ticks, reconstructed.anomaly_ticks);
        prop_assert_eq!(original.first_timestamp, reconstructed.first_timestamp);
        prop_assert_eq!(original.last_timestamp, reconstructed.last_timestamp);
    }
}
