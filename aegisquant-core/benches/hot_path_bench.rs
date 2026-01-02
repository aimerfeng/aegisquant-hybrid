//! Hot path benchmarks for AegisQuant-Core.
//!
//! These benchmarks measure the performance of critical paths:
//! - Tick processing latency
//! - Strategy signal generation
//! - Risk manager checks
//! - Account status retrieval
//!
//! Target: Hot path latency < 1μs

use criterion::{black_box, criterion_group, criterion_main, Criterion, BenchmarkId};
use aegisquant_core::types::*;
use aegisquant_core::ffi::*;

/// Benchmark: Engine initialization
fn bench_engine_init(c: &mut Criterion) {
    c.bench_function("engine_init", |b| {
        b.iter(|| {
            unsafe {
                let params = StrategyParams::default();
                let risk = RiskConfig::default();
                let engine = init_engine(&params, &risk);
                free_engine(engine);
            }
        })
    });
}

/// Benchmark: Process single tick (hot path)
fn bench_process_tick(c: &mut Criterion) {
    unsafe {
        let params = StrategyParams::default();
        let risk = RiskConfig::default();
        let engine = init_engine(&params, &risk);

        let tick = Tick {
            timestamp: 1704072600000000000,
            price: 100.0,
            volume: 1000.0,
        };

        c.bench_function("process_tick", |b| {
            b.iter(|| {
                process_tick(black_box(engine), black_box(&tick))
            })
        });

        free_engine(engine);
    }
}

/// Benchmark: Get account status
fn bench_get_account_status(c: &mut Criterion) {
    unsafe {
        let params = StrategyParams::default();
        let risk = RiskConfig::default();
        let engine = init_engine(&params, &risk);

        c.bench_function("get_account_status", |b| {
            b.iter(|| {
                let mut status = AccountStatus::default();
                get_account_status(black_box(engine), black_box(&mut status))
            })
        });

        free_engine(engine);
    }
}

/// Benchmark: Process multiple ticks (throughput)
fn bench_tick_throughput(c: &mut Criterion) {
    let tick_counts = [100, 1000, 10000];

    let mut group = c.benchmark_group("tick_throughput");
    
    for count in tick_counts {
        group.bench_with_input(
            BenchmarkId::from_parameter(count),
            &count,
            |b, &count| {
                unsafe {
                    let params = StrategyParams::default();
                    let risk = RiskConfig::default();
                    let engine = init_engine(&params, &risk);

                    // Pre-generate ticks
                    let ticks: Vec<Tick> = (0..count)
                        .map(|i| Tick {
                            timestamp: 1704072600000000000 + i as i64 * 1000000,
                            price: 100.0 + (i as f64 * 0.01).sin(),
                            volume: 1000.0,
                        })
                        .collect();

                    b.iter(|| {
                        for tick in &ticks {
                            process_tick(engine, tick);
                        }
                    });

                    free_engine(engine);
                }
            },
        );
    }
    
    group.finish();
}

/// Benchmark: Struct creation (memory allocation)
fn bench_struct_creation(c: &mut Criterion) {
    c.bench_function("tick_creation", |b| {
        b.iter(|| {
            black_box(Tick {
                timestamp: 1704072600000000000,
                price: 100.0,
                volume: 1000.0,
            })
        })
    });

    c.bench_function("order_request_creation", |b| {
        b.iter(|| {
            black_box(OrderRequest::with_symbol("BTCUSDT"))
        })
    });

    c.bench_function("account_status_creation", |b| {
        b.iter(|| {
            black_box(AccountStatus {
                balance: 100_000.0,
                equity: 100_000.0,
                available: 100_000.0,
                position_count: 0,
                total_pnl: 0.0,
            })
        })
    });
}

/// Benchmark: FFI null pointer handling
fn bench_null_pointer_check(c: &mut Criterion) {
    c.bench_function("null_pointer_check", |b| {
        b.iter(|| {
            unsafe {
                let tick = Tick::default();
                // This should return error code quickly
                process_tick(black_box(std::ptr::null_mut()), black_box(&tick))
            }
        })
    });
}

criterion_group!(
    benches,
    bench_engine_init,
    bench_process_tick,
    bench_get_account_status,
    bench_tick_throughput,
    bench_struct_creation,
    bench_null_pointer_check,
);

criterion_main!(benches);
