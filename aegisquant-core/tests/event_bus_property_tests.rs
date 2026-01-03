//! Property-based tests for Event Bus publish-subscribe correctness.
//!
//! Feature: aegisquant-optimizations, Property 6: 事件总线发布订阅
//! Validates: Requirements 6.2, 6.3, 6.5
//!
//! Tests that:
//! - All subscribers receive published events that match their filter
//! - Event content is preserved during delivery
//! - Multi-threaded publishing does not lose events (within channel capacity)

use std::sync::{Arc, Mutex};
use std::thread;

use proptest::prelude::*;
use aegisquant_core::event_bus::{
    Event, EventBus, EventFilter, OrderStatus, TimerManager,
};
use aegisquant_core::types::{AccountStatus, Tick};

/// Generate a random tick event.
fn arb_tick() -> impl Strategy<Value = Tick> {
    (0i64..1_000_000, 1.0f64..1000.0, 1.0f64..10000.0)
        .prop_map(|(timestamp, price, volume)| {
            let mut tick = Tick::default();
            tick.timestamp = timestamp;
            tick.price = price;
            tick.volume = volume;
            tick
        })
}

/// Generate a random event.
fn arb_event() -> impl Strategy<Value = Event> {
    prop_oneof![
        arb_tick().prop_map(Event::tick),
        (1u64..1000, 0i64..1_000_000).prop_map(|(id, ts)| Event::timer(id, ts)),
        (1u64..1000, 0..5i32, 0.0f64..1000.0, 0.0f64..1000.0)
            .prop_map(|(id, status, qty, price)| {
                let status = match status {
                    0 => OrderStatus::Pending,
                    1 => OrderStatus::PartiallyFilled,
                    2 => OrderStatus::Filled,
                    3 => OrderStatus::Cancelled,
                    _ => OrderStatus::Rejected,
                };
                Event::order_update(id, status, qty, price)
            }),
        (0.0f64..100000.0, 0.0f64..100000.0, 0.0f64..100000.0, 0i32..10, -1000.0f64..1000.0)
            .prop_map(|(balance, equity, available, pos_count, pnl)| {
                Event::account_update(AccountStatus {
                    balance,
                    equity,
                    available,
                    position_count: pos_count,
                    total_pnl: pnl,
                })
            }),
        ("TEST|BTCUSDT|ETHUSDT", -1i32..2, 0.0f64..1.0)
            .prop_map(|(symbol, direction, strength)| {
                Event::signal(symbol, direction, strength)
            }),
    ]
}

/// Generate a random event filter.
fn arb_filter() -> impl Strategy<Value = EventFilter> {
    (any::<bool>(), any::<bool>(), any::<bool>(), any::<bool>(), any::<bool>(), any::<bool>())
        .prop_map(|(tick, timer, order_update, account_update, signal, custom)| {
            EventFilter {
                tick,
                timer,
                order_update,
                account_update,
                signal,
                custom,
            }
        })
}

/// Check if two events are equivalent (for comparison after delivery).
fn events_equivalent(a: &Event, b: &Event) -> bool {
    match (a, b) {
        (Event::Tick(t1), Event::Tick(t2)) => {
            t1.timestamp == t2.timestamp 
                && (t1.price - t2.price).abs() < 0.0001
                && (t1.volume - t2.volume).abs() < 0.0001
        }
        (Event::Timer { id: id1, timestamp: ts1 }, Event::Timer { id: id2, timestamp: ts2 }) => {
            id1 == id2 && ts1 == ts2
        }
        (
            Event::OrderUpdate { order_id: id1, status: s1, filled_quantity: q1, fill_price: p1 },
            Event::OrderUpdate { order_id: id2, status: s2, filled_quantity: q2, fill_price: p2 },
        ) => {
            id1 == id2 && s1 == s2 && (q1 - q2).abs() < 0.0001 && (p1 - p2).abs() < 0.0001
        }
        (Event::AccountUpdate(a1), Event::AccountUpdate(a2)) => {
            (a1.balance - a2.balance).abs() < 0.0001
                && (a1.equity - a2.equity).abs() < 0.0001
                && a1.position_count == a2.position_count
        }
        (
            Event::Signal { symbol: s1, direction: d1, strength: st1 },
            Event::Signal { symbol: s2, direction: d2, strength: st2 },
        ) => {
            s1 == s2 && d1 == d2 && (st1 - st2).abs() < 0.0001
        }
        (Event::Custom { event_type: t1, payload: p1 }, Event::Custom { event_type: t2, payload: p2 }) => {
            t1 == t2 && p1 == p2
        }
        _ => false,
    }
}

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]
    
    /// Property 6: All subscribers receive matching events
    ///
    /// For any published event, all subscribers with matching filters
    /// should receive the event with content preserved.
    ///
    /// **Validates: Requirements 6.2, 6.3**
    #[test]
    fn prop_subscribers_receive_matching_events(
        events in prop::collection::vec(arb_event(), 1..20),
    ) {
        let mut bus = EventBus::new(1000);
        
        // Create subscribers with different filters
        let all_sub = bus.subscribe(EventFilter::all());
        let tick_sub = bus.subscribe(EventFilter::tick_only());
        let order_sub = bus.subscribe(EventFilter::orders_only());
        
        // Publish all events
        for event in &events {
            bus.publish(event.clone());
        }
        
        // Verify all_sub received all events
        let mut all_received = Vec::new();
        while let Ok(event) = all_sub.try_recv() {
            all_received.push(event);
        }
        prop_assert_eq!(all_received.len(), events.len());
        
        // Verify tick_sub received only tick events
        let mut tick_received = Vec::new();
        while let Ok(event) = tick_sub.try_recv() {
            tick_received.push(event);
        }
        let expected_ticks = events.iter().filter(|e| matches!(e, Event::Tick(_))).count();
        prop_assert_eq!(tick_received.len(), expected_ticks);
        
        // Verify order_sub received only order events
        let mut order_received = Vec::new();
        while let Ok(event) = order_sub.try_recv() {
            order_received.push(event);
        }
        let expected_orders = events.iter().filter(|e| matches!(e, Event::OrderUpdate { .. })).count();
        prop_assert_eq!(order_received.len(), expected_orders);
    }
    
    /// Property 6: Event content is preserved during delivery
    ///
    /// **Validates: Requirements 6.2**
    #[test]
    fn prop_event_content_preserved(
        event in arb_event(),
    ) {
        let mut bus = EventBus::new(100);
        let sub = bus.subscribe(EventFilter::all());
        
        bus.publish(event.clone());
        
        let received = sub.try_recv().unwrap();
        prop_assert!(
            events_equivalent(&event, &received),
            "Event content not preserved: {:?} vs {:?}",
            event, received
        );
    }
    
    /// Property 6: Filter correctly matches events
    ///
    /// **Validates: Requirements 6.3**
    #[test]
    fn prop_filter_matches_correctly(
        filter in arb_filter(),
        events in prop::collection::vec(arb_event(), 1..50),
    ) {
        let mut bus = EventBus::new(1000);
        let sub = bus.subscribe(filter.clone());
        
        // Count expected events
        let expected_count = events.iter().filter(|e| filter.matches(e)).count();
        
        // Publish all events
        for event in &events {
            bus.publish(event.clone());
        }
        
        // Count received events
        let mut received_count = 0;
        while sub.try_recv().is_ok() {
            received_count += 1;
        }
        
        prop_assert_eq!(
            received_count, expected_count,
            "Filter mismatch: received {} but expected {}",
            received_count, expected_count
        );
    }
    
    /// Property 6: Multi-threaded publishing preserves events
    ///
    /// **Validates: Requirements 6.5**
    #[test]
    fn prop_multithreaded_publish_no_loss(
        events_per_thread in 5usize..20,
        num_threads in 2usize..5,
    ) {
        let bus = Arc::new(Mutex::new(EventBus::new(10000)));
        
        // Subscribe before spawning threads
        let sub = {
            let mut bus = bus.lock().unwrap();
            bus.subscribe(EventFilter::all())
        };
        
        let total_events = events_per_thread * num_threads;
        
        // Spawn threads to publish events
        let handles: Vec<_> = (0..num_threads)
            .map(|thread_id| {
                let bus = Arc::clone(&bus);
                thread::spawn(move || {
                    for i in 0..events_per_thread {
                        let event = Event::timer(
                            (thread_id * 1000 + i) as u64,
                            (thread_id * 1000 + i) as i64,
                        );
                        let mut bus = bus.lock().unwrap();
                        bus.publish(event);
                    }
                })
            })
            .collect();
        
        // Wait for all threads to complete
        for handle in handles {
            handle.join().unwrap();
        }
        
        // Count received events
        let mut received_count = 0;
        while sub.try_recv().is_ok() {
            received_count += 1;
        }
        
        prop_assert_eq!(
            received_count, total_events,
            "Lost events in multi-threaded publish: received {} but expected {}",
            received_count, total_events
        );
    }
    
    /// Property 6: Timer events are correctly generated
    ///
    /// **Validates: Requirements 6.4, 6.5**
    #[test]
    fn prop_timer_events_generated(
        num_timers in 1usize..10,
        time_advance in 100i64..1000,
    ) {
        let mut manager = TimerManager::new();
        manager.set_time(0);
        
        // Schedule one-shot timers
        let timer_ids: Vec<_> = (0..num_timers)
            .map(|i| manager.schedule_once((i as u64 + 1) * 10))
            .collect();
        
        prop_assert_eq!(manager.active_count(), num_timers);
        
        // Advance time past all timers
        let events = manager.process(time_advance);
        
        // All timers should have fired
        prop_assert_eq!(
            events.len(), num_timers,
            "Expected {} timer events, got {}",
            num_timers, events.len()
        );
        
        // Verify timer IDs in events
        for event in &events {
            if let Event::Timer { id, .. } = event {
                prop_assert!(
                    timer_ids.contains(id),
                    "Unexpected timer ID: {}",
                    id
                );
            } else {
                prop_assert!(false, "Expected Timer event");
            }
        }
        
        // All one-shot timers should be inactive now
        prop_assert_eq!(manager.active_count(), 0);
    }
    
    /// Property 6: Repeating timers fire multiple times
    ///
    /// **Validates: Requirements 6.4**
    #[test]
    fn prop_repeating_timer_fires_multiple(
        interval in 10u64..100,
        num_fires in 2usize..5,
    ) {
        let mut manager = TimerManager::new();
        manager.set_time(0);
        
        let _timer_id = manager.schedule_repeating(interval);
        
        let mut total_events = 0;
        for i in 1..=num_fires {
            let events = manager.process((i as u64 * interval) as i64);
            total_events += events.len();
        }
        
        prop_assert_eq!(
            total_events, num_fires,
            "Repeating timer should fire {} times, got {}",
            num_fires, total_events
        );
        
        // Timer should still be active
        prop_assert_eq!(manager.active_count(), 1);
    }
}
