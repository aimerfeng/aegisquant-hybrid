#!/usr/bin/env python3
"""
Test data generator for AegisQuant-Hybrid.

Generates CSV tick data with configurable row count and edge cases
for testing data cleansing functionality.

Enhanced features:
- L1 order book data (bid1-5, ask1-5, bid_vol1-5, ask_vol1-5)
- Continuous price data (±2% limit)
- Specific patterns (golden cross, death cross, breakout)
- Malformed CSV files for error handling tests
"""

import argparse
import csv
import os
import random
import time
from datetime import datetime, timedelta
from pathlib import Path
from typing import List, Tuple, Dict, Optional
import math


# ============================================================================
# Basic Tick Generation (Original)
# ============================================================================

def generate_normal_tick(timestamp_ns: int, base_price: float) -> tuple:
    """Generate a normal tick with small price variation."""
    price_change = random.gauss(0, 0.001) * base_price
    new_price = base_price + price_change
    volume = random.uniform(100, 10000)
    return (timestamp_ns, round(new_price, 4), round(volume, 2))


def generate_price_jump_tick(timestamp_ns: int, base_price: float) -> tuple:
    """Generate a tick with >10% price jump (anomaly)."""
    direction = random.choice([-1, 1])
    jump_pct = random.uniform(0.11, 0.25)  # 11-25% jump
    new_price = base_price * (1 + direction * jump_pct)
    volume = random.uniform(100, 10000)
    return (timestamp_ns, round(new_price, 4), round(volume, 2))


def generate_invalid_price_tick(timestamp_ns: int) -> tuple:
    """Generate a tick with invalid price (<= 0)."""
    invalid_price = random.choice([0, -1, -100, -0.01])
    volume = random.uniform(100, 10000)
    return (timestamp_ns, invalid_price, round(volume, 2))


def generate_invalid_volume_tick(timestamp_ns: int, base_price: float) -> tuple:
    """Generate a tick with invalid volume (< 0)."""
    price_change = random.gauss(0, 0.001) * base_price
    new_price = base_price + price_change
    invalid_volume = random.choice([-1, -100, -0.01])
    return (timestamp_ns, round(new_price, 4), invalid_volume)


# ============================================================================
# L1 Order Book Generation (New - Requirement 5)
# ============================================================================

def generate_l1_orderbook(base_price: float, spread_bps: float = 10.0) -> Dict:
    """
    Generate L1 order book data with 5 levels of bid/ask.
    
    Args:
        base_price: Current mid price
        spread_bps: Spread in basis points (default 10 bps = 0.1%)
    
    Returns:
        Dict with bid1-5, ask1-5, bid_vol1-5, ask_vol1-5
    """
    spread = base_price * spread_bps / 10000
    mid_price = base_price
    
    orderbook = {}
    
    # Generate ask levels (sell side) - prices above mid
    for i in range(1, 6):
        level_spread = spread * (0.5 + 0.2 * (i - 1))  # Increasing spread per level
        orderbook[f'ask{i}'] = round(mid_price + level_spread, 4)
        # Volume typically decreases at further levels
        base_vol = random.uniform(1000, 50000)
        orderbook[f'ask_vol{i}'] = round(base_vol / (1 + 0.3 * (i - 1)), 2)
    
    # Generate bid levels (buy side) - prices below mid
    for i in range(1, 6):
        level_spread = spread * (0.5 + 0.2 * (i - 1))
        orderbook[f'bid{i}'] = round(mid_price - level_spread, 4)
        base_vol = random.uniform(1000, 50000)
        orderbook[f'bid_vol{i}'] = round(base_vol / (1 + 0.3 * (i - 1)), 2)
    
    return orderbook


def generate_l1_tick(timestamp_ns: int, base_price: float, spread_bps: float = 10.0) -> Dict:
    """Generate a complete L1 tick with price, volume, and order book."""
    price_change = random.gauss(0, 0.001) * base_price
    new_price = base_price + price_change
    volume = random.uniform(100, 10000)
    
    tick = {
        'timestamp': timestamp_ns,
        'price': round(new_price, 4),
        'volume': round(volume, 2),
    }
    tick.update(generate_l1_orderbook(new_price, spread_bps))
    
    return tick, new_price


# ============================================================================
# Continuous Price Generation (±2% limit - Requirement 3)
# ============================================================================

def generate_continuous_price(current_price: float, max_change_pct: float = 0.02) -> float:
    """
    Generate next price with limited change (default ±2%).
    
    This ensures price continuity for realistic backtesting.
    """
    max_change = current_price * max_change_pct
    change = random.gauss(0, max_change / 3)  # 3-sigma within limit
    change = max(-max_change, min(max_change, change))  # Clamp to limit
    return round(current_price + change, 4)


def generate_continuous_tick(timestamp_ns: int, current_price: float, 
                            max_change_pct: float = 0.02) -> Tuple[tuple, float]:
    """Generate tick with continuous price movement."""
    new_price = generate_continuous_price(current_price, max_change_pct)
    volume = random.uniform(100, 10000)
    return (timestamp_ns, new_price, round(volume, 2)), new_price


# ============================================================================
# Pattern Generation (Golden Cross, Death Cross, Breakout)
# ============================================================================

def generate_golden_cross_pattern(start_price: float, num_bars: int = 200) -> List[float]:
    """
    Generate price series that creates a golden cross (MA5 crosses above MA20).
    
    Pattern: Downtrend -> Consolidation -> Uptrend (golden cross occurs)
    """
    prices = []
    price = start_price
    
    # Phase 1: Downtrend (40% of bars)
    downtrend_bars = int(num_bars * 0.4)
    for _ in range(downtrend_bars):
        price *= (1 - random.uniform(0.001, 0.005))
        prices.append(round(price, 4))
    
    # Phase 2: Consolidation (20% of bars)
    consolidation_bars = int(num_bars * 0.2)
    for _ in range(consolidation_bars):
        price *= (1 + random.uniform(-0.002, 0.002))
        prices.append(round(price, 4))
    
    # Phase 3: Uptrend - creates golden cross (40% of bars)
    uptrend_bars = num_bars - downtrend_bars - consolidation_bars
    for _ in range(uptrend_bars):
        price *= (1 + random.uniform(0.002, 0.008))
        prices.append(round(price, 4))
    
    return prices


def generate_death_cross_pattern(start_price: float, num_bars: int = 200) -> List[float]:
    """
    Generate price series that creates a death cross (MA5 crosses below MA20).
    
    Pattern: Uptrend -> Consolidation -> Downtrend (death cross occurs)
    """
    prices = []
    price = start_price
    
    # Phase 1: Uptrend (40% of bars)
    uptrend_bars = int(num_bars * 0.4)
    for _ in range(uptrend_bars):
        price *= (1 + random.uniform(0.001, 0.005))
        prices.append(round(price, 4))
    
    # Phase 2: Consolidation (20% of bars)
    consolidation_bars = int(num_bars * 0.2)
    for _ in range(consolidation_bars):
        price *= (1 + random.uniform(-0.002, 0.002))
        prices.append(round(price, 4))
    
    # Phase 3: Downtrend - creates death cross (40% of bars)
    downtrend_bars = num_bars - uptrend_bars - consolidation_bars
    for _ in range(downtrend_bars):
        price *= (1 - random.uniform(0.002, 0.008))
        prices.append(round(price, 4))
    
    return prices


def generate_breakout_pattern(start_price: float, num_bars: int = 200, 
                             breakout_up: bool = True) -> List[float]:
    """
    Generate price series with consolidation followed by breakout.
    
    Pattern: Consolidation in range -> Sharp breakout
    """
    prices = []
    price = start_price
    range_high = start_price * 1.02
    range_low = start_price * 0.98
    
    # Phase 1: Consolidation (80% of bars)
    consolidation_bars = int(num_bars * 0.8)
    for _ in range(consolidation_bars):
        # Bounce within range
        if price >= range_high:
            price *= (1 - random.uniform(0.001, 0.003))
        elif price <= range_low:
            price *= (1 + random.uniform(0.001, 0.003))
        else:
            price *= (1 + random.uniform(-0.002, 0.002))
        prices.append(round(price, 4))
    
    # Phase 2: Breakout (20% of bars)
    breakout_bars = num_bars - consolidation_bars
    for i in range(breakout_bars):
        if breakout_up:
            # Accelerating upward breakout
            price *= (1 + random.uniform(0.005, 0.015) * (1 + i / breakout_bars))
        else:
            # Accelerating downward breakout
            price *= (1 - random.uniform(0.005, 0.015) * (1 + i / breakout_bars))
        prices.append(round(price, 4))
    
    return prices


# ============================================================================
# Malformed CSV Generation (Error Handling Tests)
# ============================================================================

def generate_malformed_csv_missing_column(output_path: str, num_rows: int = 100):
    """Generate CSV with missing column (no 'volume' column)."""
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    with open(output_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['timestamp', 'price'])  # Missing 'volume'
        
        timestamp = int(datetime(2024, 1, 1, 9, 30, 0).timestamp() * 1_000_000_000)
        price = 100.0
        
        for _ in range(num_rows):
            writer.writerow([timestamp, round(price, 4)])
            timestamp += 1_000_000
            price *= (1 + random.gauss(0, 0.001))
    
    print(f"Generated malformed CSV (missing column): {output_path}")


def generate_malformed_csv_wrong_type(output_path: str, num_rows: int = 100):
    """Generate CSV with wrong data types (string in numeric field)."""
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    with open(output_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['timestamp', 'price', 'volume'])
        
        timestamp = int(datetime(2024, 1, 1, 9, 30, 0).timestamp() * 1_000_000_000)
        price = 100.0
        
        for i in range(num_rows):
            if i == num_rows // 2:
                # Insert invalid data in the middle
                writer.writerow([timestamp, "INVALID_PRICE", 1000.0])
            else:
                writer.writerow([timestamp, round(price, 4), round(random.uniform(100, 10000), 2)])
            timestamp += 1_000_000
            price *= (1 + random.gauss(0, 0.001))
    
    print(f"Generated malformed CSV (wrong type): {output_path}")


def generate_malformed_csv_missing_values(output_path: str, num_rows: int = 100):
    """Generate CSV with missing values (empty cells)."""
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    with open(output_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['timestamp', 'price', 'volume'])
        
        timestamp = int(datetime(2024, 1, 1, 9, 30, 0).timestamp() * 1_000_000_000)
        price = 100.0
        
        for i in range(num_rows):
            if i % 20 == 10:
                # Missing price
                writer.writerow([timestamp, '', round(random.uniform(100, 10000), 2)])
            elif i % 20 == 15:
                # Missing volume
                writer.writerow([timestamp, round(price, 4), ''])
            else:
                writer.writerow([timestamp, round(price, 4), round(random.uniform(100, 10000), 2)])
            timestamp += 1_000_000
            price *= (1 + random.gauss(0, 0.001))
    
    print(f"Generated malformed CSV (missing values): {output_path}")


def generate_malformed_csv_extra_columns(output_path: str, num_rows: int = 100):
    """Generate CSV with inconsistent column count (extra commas)."""
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    with open(output_path, 'w', newline='') as f:
        f.write('timestamp,price,volume\n')
        
        timestamp = int(datetime(2024, 1, 1, 9, 30, 0).timestamp() * 1_000_000_000)
        price = 100.0
        
        for i in range(num_rows):
            volume = round(random.uniform(100, 10000), 2)
            if i % 10 == 5:
                # Extra column
                f.write(f'{timestamp},{round(price, 4)},{volume},extra_data\n')
            elif i % 10 == 7:
                # Missing column (fewer commas)
                f.write(f'{timestamp},{round(price, 4)}\n')
            else:
                f.write(f'{timestamp},{round(price, 4)},{volume}\n')
            timestamp += 1_000_000
            price *= (1 + random.gauss(0, 0.001))
    
    print(f"Generated malformed CSV (extra columns): {output_path}")


def generate_malformed_csv_empty(output_path: str):
    """Generate empty CSV file."""
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    with open(output_path, 'w') as f:
        pass  # Empty file
    
    print(f"Generated malformed CSV (empty): {output_path}")


def generate_malformed_csv_header_only(output_path: str):
    """Generate CSV with header but no data."""
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    with open(output_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['timestamp', 'price', 'volume'])
    
    print(f"Generated malformed CSV (header only): {output_path}")


def generate_malformed_csv_encoding(output_path: str, num_rows: int = 100):
    """Generate CSV with encoding issues (invalid UTF-8 bytes)."""
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    with open(output_path, 'wb') as f:
        f.write(b'timestamp,price,volume\n')
        
        timestamp = int(datetime(2024, 1, 1, 9, 30, 0).timestamp() * 1_000_000_000)
        price = 100.0
        
        for i in range(num_rows):
            volume = round(random.uniform(100, 10000), 2)
            if i == num_rows // 2:
                # Insert invalid UTF-8 bytes
                f.write(f'{timestamp},{round(price, 4)},'.encode('utf-8'))
                f.write(b'\xff\xfe')  # Invalid UTF-8
                f.write(f'{volume}\n'.encode('utf-8'))
            else:
                f.write(f'{timestamp},{round(price, 4)},{volume}\n'.encode('utf-8'))
            timestamp += 1_000_000
            price *= (1 + random.gauss(0, 0.001))
    
    print(f"Generated malformed CSV (encoding issues): {output_path}")


# ============================================================================
# Main Generation Functions
# ============================================================================

def generate_test_data(
    output_path: str,
    num_rows: int = 100000,
    include_anomalies: bool = True,
    anomaly_rate: float = 0.01,
    invalid_rate: float = 0.005,
    out_of_order_rate: float = 0.002,
):
    """
    Generate test tick data CSV file (original format).
    
    Args:
        output_path: Path to output CSV file
        num_rows: Number of rows to generate (default 100,000)
        include_anomalies: Whether to include edge cases
        anomaly_rate: Rate of price jump anomalies (default 1%)
        invalid_rate: Rate of invalid data (default 0.5%)
        out_of_order_rate: Rate of out-of-order timestamps (default 0.2%)
    """
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    start_time = datetime(2024, 1, 1, 9, 30, 0)
    base_price = 100.0
    tick_interval_ns = 1_000_000  # 1ms between ticks
    
    rows = []
    current_timestamp = int(start_time.timestamp() * 1_000_000_000)
    current_price = base_price
    
    print(f"Generating {num_rows:,} rows of test data...")
    start = time.time()
    
    for i in range(num_rows):
        rand_val = random.random()
        
        if include_anomalies:
            if rand_val < invalid_rate / 2:
                tick = generate_invalid_price_tick(current_timestamp)
            elif rand_val < invalid_rate:
                tick = generate_invalid_volume_tick(current_timestamp, current_price)
            elif rand_val < invalid_rate + anomaly_rate:
                tick = generate_price_jump_tick(current_timestamp, current_price)
                current_price = tick[1]
            elif rand_val < invalid_rate + anomaly_rate + out_of_order_rate:
                old_timestamp = current_timestamp - random.randint(1, 100) * tick_interval_ns
                tick = generate_normal_tick(old_timestamp, current_price)
            else:
                tick = generate_normal_tick(current_timestamp, current_price)
                current_price = tick[1]
        else:
            tick = generate_normal_tick(current_timestamp, current_price)
            current_price = tick[1]
        
        rows.append(tick)
        current_timestamp += tick_interval_ns + random.randint(0, 100000)
        
        if (i + 1) % 100000 == 0:
            print(f"  Generated {i + 1:,} rows...")
    
    print(f"Writing to {output_path}...")
    with open(output_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['timestamp', 'price', 'volume'])
        writer.writerows(rows)
    
    elapsed = time.time() - start
    file_size = os.path.getsize(output_path) / (1024 * 1024)
    
    print(f"Done! Generated {num_rows:,} rows in {elapsed:.2f}s")
    print(f"File size: {file_size:.2f} MB")
    print(f"Output: {output_path}")
    
    if include_anomalies:
        invalid_count = sum(1 for r in rows if r[1] <= 0 or r[2] < 0)
        print(f"Invalid ticks: ~{invalid_count} ({invalid_count/num_rows*100:.2f}%)")


def generate_l1_test_data(
    output_path: str,
    num_rows: int = 10000,
    spread_bps: float = 10.0,
    continuous: bool = True,
    max_change_pct: float = 0.02,
):
    """
    Generate L1 order book test data with 5 levels of bid/ask.
    
    Args:
        output_path: Path to output CSV file
        num_rows: Number of rows to generate
        spread_bps: Spread in basis points
        continuous: Whether to use continuous price generation (±2% limit)
        max_change_pct: Maximum price change per tick (if continuous)
    """
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    start_time = datetime(2024, 1, 1, 9, 30, 0)
    base_price = 100.0
    tick_interval_ns = 1_000_000
    
    print(f"Generating {num_rows:,} rows of L1 order book data...")
    start = time.time()
    
    # Define column order
    columns = ['timestamp', 'price', 'volume']
    for i in range(1, 6):
        columns.extend([f'bid{i}', f'bid_vol{i}', f'ask{i}', f'ask_vol{i}'])
    
    rows = []
    current_timestamp = int(start_time.timestamp() * 1_000_000_000)
    current_price = base_price
    
    for i in range(num_rows):
        if continuous:
            current_price = generate_continuous_price(current_price, max_change_pct)
        else:
            current_price *= (1 + random.gauss(0, 0.001))
            current_price = round(current_price, 4)
        
        tick, _ = generate_l1_tick(current_timestamp, current_price, spread_bps)
        
        # Build row in column order
        row = [tick['timestamp'], tick['price'], tick['volume']]
        for i in range(1, 6):
            row.extend([
                tick[f'bid{i}'], tick[f'bid_vol{i}'],
                tick[f'ask{i}'], tick[f'ask_vol{i}']
            ])
        rows.append(row)
        
        current_timestamp += tick_interval_ns + random.randint(0, 100000)
        
        if (i + 1) % 10000 == 0:
            print(f"  Generated {i + 1:,} rows...")
    
    print(f"Writing to {output_path}...")
    with open(output_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(columns)
        writer.writerows(rows)
    
    elapsed = time.time() - start
    file_size = os.path.getsize(output_path) / (1024 * 1024)
    
    print(f"Done! Generated {num_rows:,} rows in {elapsed:.2f}s")
    print(f"File size: {file_size:.2f} MB")
    print(f"Output: {output_path}")


def generate_pattern_test_data(
    output_path: str,
    pattern: str = 'golden_cross',
    num_bars: int = 200,
    start_price: float = 100.0,
):
    """
    Generate test data with specific price patterns.
    
    Args:
        output_path: Path to output CSV file
        pattern: One of 'golden_cross', 'death_cross', 'breakout_up', 'breakout_down'
        num_bars: Number of bars to generate
        start_price: Starting price
    """
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    print(f"Generating {pattern} pattern with {num_bars} bars...")
    
    if pattern == 'golden_cross':
        prices = generate_golden_cross_pattern(start_price, num_bars)
    elif pattern == 'death_cross':
        prices = generate_death_cross_pattern(start_price, num_bars)
    elif pattern == 'breakout_up':
        prices = generate_breakout_pattern(start_price, num_bars, breakout_up=True)
    elif pattern == 'breakout_down':
        prices = generate_breakout_pattern(start_price, num_bars, breakout_up=False)
    else:
        raise ValueError(f"Unknown pattern: {pattern}")
    
    start_time = datetime(2024, 1, 1, 9, 30, 0)
    tick_interval_ns = 1_000_000
    
    rows = []
    current_timestamp = int(start_time.timestamp() * 1_000_000_000)
    
    for price in prices:
        volume = round(random.uniform(100, 10000), 2)
        rows.append((current_timestamp, price, volume))
        current_timestamp += tick_interval_ns + random.randint(0, 100000)
    
    with open(output_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['timestamp', 'price', 'volume'])
        writer.writerows(rows)
    
    print(f"Generated {pattern} pattern: {output_path}")


def generate_continuous_test_data(
    output_path: str,
    num_rows: int = 10000,
    max_change_pct: float = 0.02,
    start_price: float = 100.0,
):
    """
    Generate continuous price data with ±2% limit per tick.
    
    Args:
        output_path: Path to output CSV file
        num_rows: Number of rows to generate
        max_change_pct: Maximum price change per tick (default 2%)
        start_price: Starting price
    """
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    print(f"Generating {num_rows:,} rows of continuous price data (±{max_change_pct*100}% limit)...")
    start = time.time()
    
    start_time = datetime(2024, 1, 1, 9, 30, 0)
    tick_interval_ns = 1_000_000
    
    rows = []
    current_timestamp = int(start_time.timestamp() * 1_000_000_000)
    current_price = start_price
    
    for i in range(num_rows):
        tick, current_price = generate_continuous_tick(current_timestamp, current_price, max_change_pct)
        rows.append(tick)
        current_timestamp += tick_interval_ns + random.randint(0, 100000)
        
        if (i + 1) % 10000 == 0:
            print(f"  Generated {i + 1:,} rows...")
    
    with open(output_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['timestamp', 'price', 'volume'])
        writer.writerows(rows)
    
    elapsed = time.time() - start
    file_size = os.path.getsize(output_path) / (1024 * 1024)
    
    print(f"Done! Generated {num_rows:,} rows in {elapsed:.2f}s")
    print(f"File size: {file_size:.2f} MB")
    print(f"Output: {output_path}")


def generate_all_malformed_csvs(output_dir: str = 'test_data/malformed'):
    """Generate all types of malformed CSV files for error handling tests."""
    Path(output_dir).mkdir(parents=True, exist_ok=True)
    
    print(f"Generating malformed CSV files in {output_dir}/...")
    
    generate_malformed_csv_missing_column(f'{output_dir}/missing_column.csv')
    generate_malformed_csv_wrong_type(f'{output_dir}/wrong_type.csv')
    generate_malformed_csv_missing_values(f'{output_dir}/missing_values.csv')
    generate_malformed_csv_extra_columns(f'{output_dir}/extra_columns.csv')
    generate_malformed_csv_empty(f'{output_dir}/empty.csv')
    generate_malformed_csv_header_only(f'{output_dir}/header_only.csv')
    generate_malformed_csv_encoding(f'{output_dir}/encoding_issues.csv')
    
    print(f"\nGenerated 7 malformed CSV files in {output_dir}/")


# ============================================================================
# CLI Entry Point
# ============================================================================

def main():
    parser = argparse.ArgumentParser(
        description='Generate test tick data for AegisQuant-Hybrid',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Generate basic tick data
  python generate_test_data.py -n 10000 -o test_data/ticks.csv
  
  # Generate L1 order book data
  python generate_test_data.py --l1 -n 5000 -o test_data/l1_ticks.csv
  
  # Generate continuous price data (±2% limit)
  python generate_test_data.py --continuous -n 10000 -o test_data/continuous.csv
  
  # Generate pattern data (golden cross)
  python generate_test_data.py --pattern golden_cross -o test_data/golden_cross.csv
  
  # Generate all malformed CSVs for error testing
  python generate_test_data.py --malformed
  
  # Generate all test data types
  python generate_test_data.py --all
"""
    )
    
    # Output options
    parser.add_argument(
        '-n', '--num-rows',
        type=int,
        default=100000,
        help='Number of rows to generate (default: 100,000, max: 1,000,000)'
    )
    parser.add_argument(
        '-o', '--output',
        type=str,
        default='test_data/ticks.csv',
        help='Output file path (default: test_data/ticks.csv)'
    )
    
    # Data type options
    parser.add_argument(
        '--clean',
        action='store_true',
        help='Generate only clean data without anomalies'
    )
    parser.add_argument(
        '--l1',
        action='store_true',
        help='Generate L1 order book data with 5 levels of bid/ask'
    )
    parser.add_argument(
        '--continuous',
        action='store_true',
        help='Generate continuous price data with ±2%% limit'
    )
    parser.add_argument(
        '--pattern',
        type=str,
        choices=['golden_cross', 'death_cross', 'breakout_up', 'breakout_down'],
        help='Generate specific price pattern'
    )
    parser.add_argument(
        '--malformed',
        action='store_true',
        help='Generate all malformed CSV files for error testing'
    )
    parser.add_argument(
        '--all',
        action='store_true',
        help='Generate all test data types'
    )
    
    # Fine-tuning options
    parser.add_argument(
        '--anomaly-rate',
        type=float,
        default=0.01,
        help='Rate of price jump anomalies (default: 0.01 = 1%%)'
    )
    parser.add_argument(
        '--invalid-rate',
        type=float,
        default=0.005,
        help='Rate of invalid data (default: 0.005 = 0.5%%)'
    )
    parser.add_argument(
        '--spread-bps',
        type=float,
        default=10.0,
        help='Spread in basis points for L1 data (default: 10)'
    )
    parser.add_argument(
        '--max-change',
        type=float,
        default=0.02,
        help='Max price change per tick for continuous data (default: 0.02 = 2%%)'
    )
    parser.add_argument(
        '--start-price',
        type=float,
        default=100.0,
        help='Starting price (default: 100.0)'
    )
    
    args = parser.parse_args()
    
    # Validate num_rows
    if args.num_rows > 1000000:
        print("Warning: Limiting to 1,000,000 rows maximum")
        args.num_rows = 1000000
    
    if args.all:
        # Generate all test data types
        print("=" * 60)
        print("Generating all test data types...")
        print("=" * 60)
        
        # Basic tick data
        generate_test_data(
            output_path='test_data/ticks.csv',
            num_rows=10000,
            include_anomalies=True,
        )
        print()
        
        # Clean tick data
        generate_test_data(
            output_path='test_data/ticks_clean.csv',
            num_rows=10000,
            include_anomalies=False,
        )
        print()
        
        # L1 order book data
        generate_l1_test_data(
            output_path='test_data/l1_ticks.csv',
            num_rows=5000,
            spread_bps=args.spread_bps,
            continuous=True,
        )
        print()
        
        # Continuous price data
        generate_continuous_test_data(
            output_path='test_data/continuous_ticks.csv',
            num_rows=10000,
            max_change_pct=args.max_change,
        )
        print()
        
        # Pattern data
        for pattern in ['golden_cross', 'death_cross', 'breakout_up', 'breakout_down']:
            generate_pattern_test_data(
                output_path=f'test_data/pattern_{pattern}.csv',
                pattern=pattern,
                num_bars=200,
            )
        print()
        
        # Malformed CSVs
        generate_all_malformed_csvs('test_data/malformed')
        
        print()
        print("=" * 60)
        print("All test data generated successfully!")
        print("=" * 60)
        
    elif args.malformed:
        generate_all_malformed_csvs('test_data/malformed')
        
    elif args.pattern:
        generate_pattern_test_data(
            output_path=args.output,
            pattern=args.pattern,
            num_bars=args.num_rows if args.num_rows < 1000 else 200,
            start_price=args.start_price,
        )
        
    elif args.l1:
        generate_l1_test_data(
            output_path=args.output,
            num_rows=args.num_rows,
            spread_bps=args.spread_bps,
            continuous=args.continuous or True,
            max_change_pct=args.max_change,
        )
        
    elif args.continuous:
        generate_continuous_test_data(
            output_path=args.output,
            num_rows=args.num_rows,
            max_change_pct=args.max_change,
            start_price=args.start_price,
        )
        
    else:
        generate_test_data(
            output_path=args.output,
            num_rows=args.num_rows,
            include_anomalies=not args.clean,
            anomaly_rate=args.anomaly_rate,
            invalid_rate=args.invalid_rate,
        )


if __name__ == '__main__':
    main()
