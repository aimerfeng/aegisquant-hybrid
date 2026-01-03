//! Event Bus module for asynchronous event-driven architecture.
//!
//! Provides a publish-subscribe mechanism for decoupled component communication
//! using crossbeam-channel for high-performance multi-threaded event delivery.
//!
//! # Requirements
//! - Requirement 6.1: Define Event enum with Tick, Timer, OrderUpdate, AccountUpdate variants
//! - Requirement 6.2: Use crossbeam-channel for event bus implementation
//! - Requirement 6.3: Strategy trait supports subscribing to multiple event types
//! - Requirement 6.6: Provide subscribe_event FFI function

use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;

use crossbeam_channel::{bounded, unbounded, Receiver, Sender, TryRecvError, TrySendError};

use crate::types::{AccountStatus, OrderRequest, Tick};

/// Unique identifier for event subscriptions.
pub type SubscriptionId = u64;

/// Global subscription ID counter.
static NEXT_SUBSCRIPTION_ID: AtomicU64 = AtomicU64::new(1);

/// Generate a new unique subscription ID.
fn next_subscription_id() -> SubscriptionId {
    NEXT_SUBSCRIPTION_ID.fetch_add(1, Ordering::SeqCst)
}

/// Order status for order update events.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum OrderStatus {
    /// Order has been submitted
    #[default]
    Pending = 0,
    /// Order is partially filled
    PartiallyFilled = 1,
    /// Order is fully filled
    Filled = 2,
    /// Order has been cancelled
    Cancelled = 3,
    /// Order has been rejected
    Rejected = 4,
}

/// Event types that can be published and subscribed to.
#[derive(Debug, Clone)]
pub enum Event {
    /// Market tick data event
    Tick(Tick),
    
    /// Timer event for scheduled callbacks
    Timer {
        /// Timer identifier
        id: u64,
        /// Timestamp when the timer fired
        timestamp: i64,
    },
    
    /// Order status update event
    OrderUpdate {
        /// Order identifier
        order_id: u64,
        /// New order status
        status: OrderStatus,
        /// Filled quantity (if applicable)
        filled_quantity: f64,
        /// Fill price (if applicable)
        fill_price: f64,
    },
    
    /// Account status update event
    AccountUpdate(AccountStatus),
    
    /// Trading signal event
    Signal {
        /// Symbol for the signal
        symbol: String,
        /// Direction: 1 = buy, -1 = sell, 0 = flat
        direction: i32,
        /// Signal strength (0.0 to 1.0)
        strength: f64,
    },
    
    /// Custom event for extensibility
    Custom {
        /// Event type identifier
        event_type: String,
        /// Event payload as JSON string
        payload: String,
    },
}

impl Event {
    /// Get the event type as a string identifier.
    pub fn event_type(&self) -> &'static str {
        match self {
            Event::Tick(_) => "Tick",
            Event::Timer { .. } => "Timer",
            Event::OrderUpdate { .. } => "OrderUpdate",
            Event::AccountUpdate(_) => "AccountUpdate",
            Event::Signal { .. } => "Signal",
            Event::Custom { .. } => "Custom",
        }
    }
    
    /// Create a new tick event.
    pub fn tick(tick: Tick) -> Self {
        Event::Tick(tick)
    }
    
    /// Create a new timer event.
    pub fn timer(id: u64, timestamp: i64) -> Self {
        Event::Timer { id, timestamp }
    }
    
    /// Create a new order update event.
    pub fn order_update(order_id: u64, status: OrderStatus, filled_quantity: f64, fill_price: f64) -> Self {
        Event::OrderUpdate {
            order_id,
            status,
            filled_quantity,
            fill_price,
        }
    }
    
    /// Create a new account update event.
    pub fn account_update(status: AccountStatus) -> Self {
        Event::AccountUpdate(status)
    }
    
    /// Create a new signal event.
    pub fn signal(symbol: impl Into<String>, direction: i32, strength: f64) -> Self {
        Event::Signal {
            symbol: symbol.into(),
            direction,
            strength,
        }
    }
}

/// Event filter for selective subscription.
#[derive(Debug, Clone, Default)]
pub struct EventFilter {
    /// Subscribe to tick events
    pub tick: bool,
    /// Subscribe to timer events
    pub timer: bool,
    /// Subscribe to order update events
    pub order_update: bool,
    /// Subscribe to account update events
    pub account_update: bool,
    /// Subscribe to signal events
    pub signal: bool,
    /// Subscribe to custom events
    pub custom: bool,
}

impl EventFilter {
    /// Create a filter that accepts all events.
    pub fn all() -> Self {
        Self {
            tick: true,
            timer: true,
            order_update: true,
            account_update: true,
            signal: true,
            custom: true,
        }
    }
    
    /// Create a filter that accepts only tick events.
    pub fn tick_only() -> Self {
        Self {
            tick: true,
            ..Default::default()
        }
    }
    
    /// Create a filter that accepts only order-related events.
    pub fn orders_only() -> Self {
        Self {
            order_update: true,
            ..Default::default()
        }
    }
    
    /// Check if an event matches this filter.
    pub fn matches(&self, event: &Event) -> bool {
        match event {
            Event::Tick(_) => self.tick,
            Event::Timer { .. } => self.timer,
            Event::OrderUpdate { .. } => self.order_update,
            Event::AccountUpdate(_) => self.account_update,
            Event::Signal { .. } => self.signal,
            Event::Custom { .. } => self.custom,
        }
    }
}

/// Subscription handle for receiving events.
#[derive(Debug)]
pub struct Subscription {
    /// Unique subscription ID
    pub id: SubscriptionId,
    /// Event receiver channel
    receiver: Receiver<Event>,
    /// Event filter
    filter: EventFilter,
}

impl Subscription {
    /// Try to receive an event without blocking.
    pub fn try_recv(&self) -> Result<Event, TryRecvError> {
        self.receiver.try_recv()
    }
    
    /// Receive an event, blocking until one is available.
    pub fn recv(&self) -> Result<Event, crossbeam_channel::RecvError> {
        self.receiver.recv()
    }
    
    /// Receive an event with a timeout.
    pub fn recv_timeout(&self, timeout: std::time::Duration) -> Result<Event, crossbeam_channel::RecvTimeoutError> {
        self.receiver.recv_timeout(timeout)
    }
    
    /// Check if there are pending events.
    pub fn is_empty(&self) -> bool {
        self.receiver.is_empty()
    }
    
    /// Get the number of pending events.
    pub fn len(&self) -> usize {
        self.receiver.len()
    }
    
    /// Get the event filter for this subscription.
    pub fn filter(&self) -> &EventFilter {
        &self.filter
    }
}

/// Internal subscriber entry.
#[derive(Debug)]
struct SubscriberEntry {
    id: SubscriptionId,
    sender: Sender<Event>,
    filter: EventFilter,
}

/// Event bus for publish-subscribe communication.
///
/// The event bus allows components to publish events and subscribe to
/// receive events of interest. It uses crossbeam-channel for efficient
/// multi-threaded event delivery.
#[derive(Debug)]
pub struct EventBus {
    /// Subscribers list
    subscribers: Vec<SubscriberEntry>,
    /// Default channel capacity for bounded subscriptions
    default_capacity: usize,
    /// Statistics: total events published
    events_published: u64,
    /// Statistics: total events delivered
    events_delivered: u64,
    /// Statistics: events dropped due to full channels
    events_dropped: u64,
}

impl Default for EventBus {
    fn default() -> Self {
        Self::new(1000)
    }
}

impl EventBus {
    /// Create a new event bus with the specified default channel capacity.
    pub fn new(default_capacity: usize) -> Self {
        Self {
            subscribers: Vec::new(),
            default_capacity,
            events_published: 0,
            events_delivered: 0,
            events_dropped: 0,
        }
    }
    
    /// Subscribe to events with the default capacity and filter.
    pub fn subscribe(&mut self, filter: EventFilter) -> Subscription {
        self.subscribe_with_capacity(filter, self.default_capacity)
    }
    
    /// Subscribe to events with a specific channel capacity.
    pub fn subscribe_with_capacity(&mut self, filter: EventFilter, capacity: usize) -> Subscription {
        let (sender, receiver) = bounded(capacity);
        let id = next_subscription_id();
        
        self.subscribers.push(SubscriberEntry {
            id,
            sender,
            filter: filter.clone(),
        });
        
        Subscription {
            id,
            receiver,
            filter,
        }
    }
    
    /// Subscribe to all events with an unbounded channel.
    pub fn subscribe_unbounded(&mut self, filter: EventFilter) -> Subscription {
        let (sender, receiver) = unbounded();
        let id = next_subscription_id();
        
        self.subscribers.push(SubscriberEntry {
            id,
            sender,
            filter: filter.clone(),
        });
        
        Subscription {
            id,
            receiver,
            filter,
        }
    }
    
    /// Unsubscribe from events.
    pub fn unsubscribe(&mut self, subscription_id: SubscriptionId) -> bool {
        let initial_len = self.subscribers.len();
        self.subscribers.retain(|s| s.id != subscription_id);
        self.subscribers.len() < initial_len
    }
    
    /// Publish an event to all matching subscribers.
    ///
    /// Returns the number of subscribers that received the event.
    pub fn publish(&mut self, event: Event) -> usize {
        self.events_published += 1;
        let mut delivered = 0;
        
        for subscriber in &self.subscribers {
            if subscriber.filter.matches(&event) {
                match subscriber.sender.try_send(event.clone()) {
                    Ok(()) => {
                        delivered += 1;
                        self.events_delivered += 1;
                    }
                    Err(TrySendError::Full(_)) => {
                        self.events_dropped += 1;
                    }
                    Err(TrySendError::Disconnected(_)) => {
                        // Subscriber disconnected, will be cleaned up later
                    }
                }
            }
        }
        
        delivered
    }
    
    /// Publish an event, blocking if channels are full.
    ///
    /// Returns the number of subscribers that received the event.
    pub fn publish_blocking(&mut self, event: Event) -> usize {
        self.events_published += 1;
        let mut delivered = 0;
        
        for subscriber in &self.subscribers {
            if subscriber.filter.matches(&event)
                && subscriber.sender.send(event.clone()).is_ok()
            {
                delivered += 1;
                self.events_delivered += 1;
            }
        }
        
        delivered
    }
    
    /// Get the number of active subscribers.
    pub fn subscriber_count(&self) -> usize {
        self.subscribers.len()
    }
    
    /// Clean up disconnected subscribers.
    pub fn cleanup(&mut self) {
        self.subscribers.retain(|s| !s.sender.is_full() || s.sender.capacity().is_some());
    }
    
    /// Get statistics about the event bus.
    pub fn stats(&self) -> EventBusStats {
        EventBusStats {
            subscriber_count: self.subscribers.len(),
            events_published: self.events_published,
            events_delivered: self.events_delivered,
            events_dropped: self.events_dropped,
        }
    }
}

/// Statistics about the event bus.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct EventBusStats {
    /// Number of active subscribers
    pub subscriber_count: usize,
    /// Total events published
    pub events_published: u64,
    /// Total events delivered
    pub events_delivered: u64,
    /// Events dropped due to full channels
    pub events_dropped: u64,
}

/// Trait for event-driven strategies.
///
/// Strategies implementing this trait can subscribe to and handle
/// various event types from the event bus.
pub trait EventDrivenStrategy {
    /// Handle a tick event.
    fn on_tick(&mut self, tick: &Tick) -> Option<OrderRequest>;
    
    /// Handle a timer event.
    fn on_timer(&mut self, id: u64, timestamp: i64);
    
    /// Handle an order update event.
    fn on_order_update(&mut self, order_id: u64, status: &OrderStatus, filled_quantity: f64, fill_price: f64);
    
    /// Handle an account update event.
    fn on_account_update(&mut self, status: &AccountStatus);
    
    /// Get the event filter for this strategy.
    fn event_filter(&self) -> EventFilter {
        EventFilter::all()
    }
}

// ============================================================================
// Thread-safe shared event bus
// ============================================================================

/// Thread-safe shared event bus using Arc and Mutex.
pub type SharedEventBus = Arc<std::sync::Mutex<EventBus>>;

/// Create a new shared event bus.
pub fn create_shared_event_bus(capacity: usize) -> SharedEventBus {
    Arc::new(std::sync::Mutex::new(EventBus::new(capacity)))
}

// ============================================================================
// FFI Interface
// ============================================================================

use std::ffi::c_void;

/// Event type constants for FFI.
pub const EVENT_TYPE_TICK: i32 = 0;
pub const EVENT_TYPE_TIMER: i32 = 1;
pub const EVENT_TYPE_ORDER_UPDATE: i32 = 2;
pub const EVENT_TYPE_ACCOUNT_UPDATE: i32 = 3;
pub const EVENT_TYPE_SIGNAL: i32 = 4;
pub const EVENT_TYPE_CUSTOM: i32 = 5;

/// FFI-safe event callback type.
pub type EventCallback = extern "C" fn(event_type: i32, data: *const c_void, data_len: usize);

/// Global event callback storage.
static EVENT_CALLBACK: std::sync::atomic::AtomicPtr<()> = 
    std::sync::atomic::AtomicPtr::new(std::ptr::null_mut());

/// Set the global event callback.
///
/// # Safety
/// The callback must be a valid function pointer that remains valid
/// for the lifetime of the application.
#[no_mangle]
pub unsafe extern "C" fn set_event_callback(callback: EventCallback) -> i32 {
    use crate::ffi::ERR_SUCCESS;
    EVENT_CALLBACK.store(callback as *mut (), Ordering::SeqCst);
    ERR_SUCCESS
}

/// Clear the global event callback.
#[no_mangle]
pub extern "C" fn clear_event_callback() -> i32 {
    use crate::ffi::ERR_SUCCESS;
    EVENT_CALLBACK.store(std::ptr::null_mut(), Ordering::SeqCst);
    ERR_SUCCESS
}

/// Subscribe to events with a filter mask.
///
/// # Arguments
/// * `event_bus` - Pointer to the event bus
/// * `filter_mask` - Bitmask of event types to subscribe to
///   - Bit 0: Tick events
///   - Bit 1: Timer events
///   - Bit 2: Order update events
///   - Bit 3: Account update events
///   - Bit 4: Signal events
///   - Bit 5: Custom events
///
/// # Returns
/// Subscription ID on success, or negative error code on failure.
///
/// # Safety
/// `event_bus` must be a valid pointer to an EventBus.
#[no_mangle]
pub unsafe extern "C" fn subscribe_event(
    event_bus: *mut EventBus,
    filter_mask: i32,
) -> i64 {
    use crate::ffi::ERR_NULL_POINTER;
    
    if event_bus.is_null() {
        return ERR_NULL_POINTER as i64;
    }
    
    let bus = &mut *event_bus;
    
    let filter = EventFilter {
        tick: (filter_mask & 0x01) != 0,
        timer: (filter_mask & 0x02) != 0,
        order_update: (filter_mask & 0x04) != 0,
        account_update: (filter_mask & 0x08) != 0,
        signal: (filter_mask & 0x10) != 0,
        custom: (filter_mask & 0x20) != 0,
    };
    
    let subscription = bus.subscribe(filter);
    subscription.id as i64
}

/// Unsubscribe from events.
///
/// # Safety
/// `event_bus` must be a valid pointer to an EventBus.
#[no_mangle]
pub unsafe extern "C" fn unsubscribe_event(
    event_bus: *mut EventBus,
    subscription_id: u64,
) -> i32 {
    use crate::ffi::{ERR_NULL_POINTER, ERR_SUCCESS, ERR_INVALID_PARAM};
    
    if event_bus.is_null() {
        return ERR_NULL_POINTER;
    }
    
    let bus = &mut *event_bus;
    
    if bus.unsubscribe(subscription_id) {
        ERR_SUCCESS
    } else {
        ERR_INVALID_PARAM
    }
}

/// Get event bus statistics.
///
/// # Safety
/// Both pointers must be valid.
#[no_mangle]
pub unsafe extern "C" fn get_event_bus_stats(
    event_bus: *const EventBus,
    stats: *mut EventBusStats,
) -> i32 {
    use crate::ffi::{ERR_NULL_POINTER, ERR_SUCCESS};
    
    if event_bus.is_null() || stats.is_null() {
        return ERR_NULL_POINTER;
    }
    
    let bus = &*event_bus;
    *stats = bus.stats();
    
    ERR_SUCCESS
}

// ============================================================================
// Timer Management
// ============================================================================

/// Unique identifier for timers.
pub type TimerId = u64;

/// Global timer ID counter.
static NEXT_TIMER_ID: AtomicU64 = AtomicU64::new(1);

/// Generate a new unique timer ID.
fn next_timer_id() -> TimerId {
    NEXT_TIMER_ID.fetch_add(1, Ordering::SeqCst)
}

/// Timer entry for scheduled callbacks.
#[derive(Debug, Clone)]
pub struct TimerEntry {
    /// Unique timer ID
    pub id: TimerId,
    /// Interval in milliseconds (0 for one-shot)
    pub interval_ms: u64,
    /// Next trigger timestamp in milliseconds
    pub next_trigger_ms: i64,
    /// Whether the timer is active
    pub active: bool,
    /// Whether this is a repeating timer
    pub repeating: bool,
}

impl TimerEntry {
    /// Create a new one-shot timer.
    pub fn one_shot(trigger_at_ms: i64) -> Self {
        Self {
            id: next_timer_id(),
            interval_ms: 0,
            next_trigger_ms: trigger_at_ms,
            active: true,
            repeating: false,
        }
    }
    
    /// Create a new repeating timer.
    pub fn repeating(interval_ms: u64, start_at_ms: i64) -> Self {
        Self {
            id: next_timer_id(),
            interval_ms,
            next_trigger_ms: start_at_ms + interval_ms as i64,
            active: true,
            repeating: true,
        }
    }
    
    /// Check if the timer should fire at the given timestamp.
    pub fn should_fire(&self, current_ms: i64) -> bool {
        self.active && current_ms >= self.next_trigger_ms
    }
    
    /// Advance the timer to the next trigger time (for repeating timers).
    pub fn advance(&mut self) {
        if self.repeating && self.interval_ms > 0 {
            self.next_trigger_ms += self.interval_ms as i64;
        } else {
            self.active = false;
        }
    }
}

/// Timer manager for scheduling and firing timer events.
#[derive(Debug, Default)]
pub struct TimerManager {
    /// Active timers
    timers: Vec<TimerEntry>,
    /// Current timestamp in milliseconds
    current_time_ms: i64,
}

impl TimerManager {
    /// Create a new timer manager.
    pub fn new() -> Self {
        Self {
            timers: Vec::new(),
            current_time_ms: 0,
        }
    }
    
    /// Set the current time.
    pub fn set_time(&mut self, time_ms: i64) {
        self.current_time_ms = time_ms;
    }
    
    /// Get the current time.
    pub fn current_time(&self) -> i64 {
        self.current_time_ms
    }
    
    /// Schedule a one-shot timer.
    pub fn schedule_once(&mut self, delay_ms: u64) -> TimerId {
        let timer = TimerEntry::one_shot(self.current_time_ms + delay_ms as i64);
        let id = timer.id;
        self.timers.push(timer);
        id
    }
    
    /// Schedule a repeating timer.
    pub fn schedule_repeating(&mut self, interval_ms: u64) -> TimerId {
        let timer = TimerEntry::repeating(interval_ms, self.current_time_ms);
        let id = timer.id;
        self.timers.push(timer);
        id
    }
    
    /// Cancel a timer.
    pub fn cancel(&mut self, timer_id: TimerId) -> bool {
        if let Some(timer) = self.timers.iter_mut().find(|t| t.id == timer_id) {
            timer.active = false;
            true
        } else {
            false
        }
    }
    
    /// Process timers and return events for any that should fire.
    pub fn process(&mut self, current_time_ms: i64) -> Vec<Event> {
        self.current_time_ms = current_time_ms;
        let mut events = Vec::new();
        
        for timer in &mut self.timers {
            if timer.should_fire(current_time_ms) {
                events.push(Event::timer(timer.id, current_time_ms));
                timer.advance();
            }
        }
        
        // Clean up inactive timers
        self.timers.retain(|t| t.active);
        
        events
    }
    
    /// Get the number of active timers.
    pub fn active_count(&self) -> usize {
        self.timers.iter().filter(|t| t.active).count()
    }
    
    /// Clear all timers.
    pub fn clear(&mut self) {
        self.timers.clear();
    }
}

// ============================================================================
// Order Update Helper
// ============================================================================

/// Helper struct for sending order update events.
pub struct OrderUpdateSender<'a> {
    event_bus: &'a mut EventBus,
}

impl<'a> OrderUpdateSender<'a> {
    /// Create a new order update sender.
    pub fn new(event_bus: &'a mut EventBus) -> Self {
        Self { event_bus }
    }
    
    /// Send an order submitted event.
    pub fn order_submitted(&mut self, order_id: u64) {
        self.event_bus.publish(Event::order_update(
            order_id,
            OrderStatus::Pending,
            0.0,
            0.0,
        ));
    }
    
    /// Send an order partially filled event.
    pub fn order_partially_filled(&mut self, order_id: u64, filled_quantity: f64, fill_price: f64) {
        self.event_bus.publish(Event::order_update(
            order_id,
            OrderStatus::PartiallyFilled,
            filled_quantity,
            fill_price,
        ));
    }
    
    /// Send an order filled event.
    pub fn order_filled(&mut self, order_id: u64, filled_quantity: f64, fill_price: f64) {
        self.event_bus.publish(Event::order_update(
            order_id,
            OrderStatus::Filled,
            filled_quantity,
            fill_price,
        ));
    }
    
    /// Send an order cancelled event.
    pub fn order_cancelled(&mut self, order_id: u64) {
        self.event_bus.publish(Event::order_update(
            order_id,
            OrderStatus::Cancelled,
            0.0,
            0.0,
        ));
    }
    
    /// Send an order rejected event.
    pub fn order_rejected(&mut self, order_id: u64) {
        self.event_bus.publish(Event::order_update(
            order_id,
            OrderStatus::Rejected,
            0.0,
            0.0,
        ));
    }
}

// ============================================================================
// FFI for Timer Management
// ============================================================================

/// Create a new timer manager.
///
/// # Returns
/// Pointer to the new timer manager, or null on failure.
#[no_mangle]
pub extern "C" fn create_timer_manager() -> *mut TimerManager {
    Box::into_raw(Box::new(TimerManager::new()))
}

/// Destroy a timer manager.
///
/// # Safety
/// `manager` must be a valid pointer returned by `create_timer_manager`.
#[no_mangle]
pub unsafe extern "C" fn destroy_timer_manager(manager: *mut TimerManager) {
    if !manager.is_null() {
        drop(Box::from_raw(manager));
    }
}

/// Schedule a one-shot timer.
///
/// # Safety
/// `manager` must be a valid pointer to a TimerManager.
#[no_mangle]
pub unsafe extern "C" fn schedule_timer_once(
    manager: *mut TimerManager,
    delay_ms: u64,
) -> u64 {
    if manager.is_null() {
        return 0;
    }
    
    let manager = &mut *manager;
    manager.schedule_once(delay_ms)
}

/// Schedule a repeating timer.
///
/// # Safety
/// `manager` must be a valid pointer to a TimerManager.
#[no_mangle]
pub unsafe extern "C" fn schedule_timer_repeating(
    manager: *mut TimerManager,
    interval_ms: u64,
) -> u64 {
    if manager.is_null() {
        return 0;
    }
    
    let manager = &mut *manager;
    manager.schedule_repeating(interval_ms)
}

/// Cancel a timer.
///
/// # Safety
/// `manager` must be a valid pointer to a TimerManager.
#[no_mangle]
pub unsafe extern "C" fn cancel_timer(
    manager: *mut TimerManager,
    timer_id: u64,
) -> i32 {
    use crate::ffi::{ERR_NULL_POINTER, ERR_SUCCESS, ERR_INVALID_PARAM};
    
    if manager.is_null() {
        return ERR_NULL_POINTER;
    }
    
    let manager = &mut *manager;
    if manager.cancel(timer_id) {
        ERR_SUCCESS
    } else {
        ERR_INVALID_PARAM
    }
}

/// Process timers and publish events to the event bus.
///
/// # Safety
/// Both pointers must be valid.
#[no_mangle]
pub unsafe extern "C" fn process_timers(
    manager: *mut TimerManager,
    event_bus: *mut EventBus,
    current_time_ms: i64,
) -> i32 {
    use crate::ffi::{ERR_NULL_POINTER, ERR_SUCCESS};
    
    if manager.is_null() || event_bus.is_null() {
        return ERR_NULL_POINTER;
    }
    
    let manager = &mut *manager;
    let bus = &mut *event_bus;
    
    let events = manager.process(current_time_ms);
    for event in events {
        bus.publish(event);
    }
    
    ERR_SUCCESS
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_event_creation() {
        let tick = Tick::default();
        let event = Event::tick(tick);
        assert_eq!(event.event_type(), "Tick");
        
        let event = Event::timer(1, 12345);
        assert_eq!(event.event_type(), "Timer");
        
        let event = Event::order_update(1, OrderStatus::Filled, 100.0, 50.0);
        assert_eq!(event.event_type(), "OrderUpdate");
    }
    
    #[test]
    fn test_event_filter() {
        let filter = EventFilter::all();
        assert!(filter.tick);
        assert!(filter.timer);
        assert!(filter.order_update);
        
        let filter = EventFilter::tick_only();
        assert!(filter.tick);
        assert!(!filter.timer);
        assert!(!filter.order_update);
        
        let tick_event = Event::tick(Tick::default());
        let timer_event = Event::timer(1, 0);
        
        assert!(filter.matches(&tick_event));
        assert!(!filter.matches(&timer_event));
    }
    
    #[test]
    fn test_event_bus_subscribe_publish() {
        let mut bus = EventBus::new(100);
        
        let sub1 = bus.subscribe(EventFilter::all());
        let sub2 = bus.subscribe(EventFilter::tick_only());
        
        assert_eq!(bus.subscriber_count(), 2);
        
        // Publish a tick event
        let tick = Tick::default();
        let delivered = bus.publish(Event::tick(tick));
        assert_eq!(delivered, 2);
        
        // Both should receive it
        assert!(!sub1.is_empty());
        assert!(!sub2.is_empty());
        
        // Publish a timer event
        let delivered = bus.publish(Event::timer(1, 0));
        assert_eq!(delivered, 1); // Only sub1 should receive it
        
        // sub1 should have 2 events, sub2 should have 1
        assert_eq!(sub1.len(), 2);
        assert_eq!(sub2.len(), 1);
    }
    
    #[test]
    fn test_event_bus_unsubscribe() {
        let mut bus = EventBus::new(100);
        
        let sub = bus.subscribe(EventFilter::all());
        assert_eq!(bus.subscriber_count(), 1);
        
        let removed = bus.unsubscribe(sub.id);
        assert!(removed);
        assert_eq!(bus.subscriber_count(), 0);
        
        // Unsubscribing again should return false
        let removed = bus.unsubscribe(sub.id);
        assert!(!removed);
    }
    
    #[test]
    fn test_subscription_receive() {
        let mut bus = EventBus::new(100);
        let sub = bus.subscribe(EventFilter::all());
        
        let tick = Tick::default();
        bus.publish(Event::tick(tick));
        
        let event = sub.try_recv().unwrap();
        assert_eq!(event.event_type(), "Tick");
        
        // No more events
        assert!(sub.try_recv().is_err());
    }
    
    #[test]
    fn test_event_bus_stats() {
        let mut bus = EventBus::new(100);
        let _sub = bus.subscribe(EventFilter::all());
        
        bus.publish(Event::tick(Tick::default()));
        bus.publish(Event::timer(1, 0));
        
        let stats = bus.stats();
        assert_eq!(stats.subscriber_count, 1);
        assert_eq!(stats.events_published, 2);
        assert_eq!(stats.events_delivered, 2);
        assert_eq!(stats.events_dropped, 0);
    }
    
    #[test]
    fn test_bounded_channel_drops() {
        let mut bus = EventBus::new(2); // Very small capacity
        let _sub = bus.subscribe(EventFilter::all());
        
        // Fill the channel
        bus.publish(Event::tick(Tick::default()));
        bus.publish(Event::tick(Tick::default()));
        
        // This should be dropped
        bus.publish(Event::tick(Tick::default()));
        
        let stats = bus.stats();
        assert_eq!(stats.events_published, 3);
        assert_eq!(stats.events_delivered, 2);
        assert_eq!(stats.events_dropped, 1);
    }
    
    #[test]
    fn test_shared_event_bus() {
        let bus = create_shared_event_bus(100);
        
        let sub = {
            let mut bus = bus.lock().unwrap();
            bus.subscribe(EventFilter::all())
        };
        
        {
            let mut bus = bus.lock().unwrap();
            bus.publish(Event::tick(Tick::default()));
        }
        
        assert!(!sub.is_empty());
    }
    
    // Timer tests
    #[test]
    fn test_timer_one_shot() {
        let mut manager = TimerManager::new();
        manager.set_time(1000);
        
        let timer_id = manager.schedule_once(100);
        assert!(timer_id > 0);
        assert_eq!(manager.active_count(), 1);
        
        // Not yet time to fire
        let events = manager.process(1050);
        assert!(events.is_empty());
        assert_eq!(manager.active_count(), 1);
        
        // Time to fire
        let events = manager.process(1100);
        assert_eq!(events.len(), 1);
        assert_eq!(manager.active_count(), 0); // One-shot timer removed
    }
    
    #[test]
    fn test_timer_repeating() {
        let mut manager = TimerManager::new();
        manager.set_time(1000);
        
        let timer_id = manager.schedule_repeating(100);
        assert!(timer_id > 0);
        
        // First fire
        let events = manager.process(1100);
        assert_eq!(events.len(), 1);
        assert_eq!(manager.active_count(), 1); // Still active
        
        // Second fire
        let events = manager.process(1200);
        assert_eq!(events.len(), 1);
        assert_eq!(manager.active_count(), 1);
    }
    
    #[test]
    fn test_timer_cancel() {
        let mut manager = TimerManager::new();
        manager.set_time(1000);
        
        let timer_id = manager.schedule_once(100);
        assert_eq!(manager.active_count(), 1);
        
        let cancelled = manager.cancel(timer_id);
        assert!(cancelled);
        
        // Process to clean up
        let events = manager.process(1100);
        assert!(events.is_empty());
        assert_eq!(manager.active_count(), 0);
    }
    
    #[test]
    fn test_order_update_sender() {
        let mut bus = EventBus::new(100);
        let sub = bus.subscribe(EventFilter::orders_only());
        
        {
            let mut sender = OrderUpdateSender::new(&mut bus);
            sender.order_submitted(1);
            sender.order_filled(1, 100.0, 50.0);
        }
        
        assert_eq!(sub.len(), 2);
        
        let event = sub.try_recv().unwrap();
        if let Event::OrderUpdate { order_id, status, .. } = event {
            assert_eq!(order_id, 1);
            assert_eq!(status, OrderStatus::Pending);
        } else {
            panic!("Expected OrderUpdate event");
        }
        
        let event = sub.try_recv().unwrap();
        if let Event::OrderUpdate { order_id, status, filled_quantity, fill_price } = event {
            assert_eq!(order_id, 1);
            assert_eq!(status, OrderStatus::Filled);
            assert_eq!(filled_quantity, 100.0);
            assert_eq!(fill_price, 50.0);
        } else {
            panic!("Expected OrderUpdate event");
        }
    }
}
