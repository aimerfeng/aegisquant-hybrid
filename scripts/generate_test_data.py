#!/usr/bin/env python3
"""
Test data generator for AegisQuant-Hybrid.

Generates CSV tick data with configurable row count and edge cases
for testing data cleansing functionality.
"""

import argparse
import csv
import os
import random
import time
from datetime import datetime, timedelta
from pathlib import Path


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


def generate_test_data(
    output_path: str,
    num_rows: int = 100000,
    include_anomalies: bool = True,
    anomaly_rate: float = 0.01,
    invalid_rate: float = 0.005,
    out_of_order_rate: float = 0.002,
):
    """
    Generate test tick data CSV file.
    
    Args:
        output_path: Path to output CSV file
        num_rows: Number of rows to generate (default 100,000)
        include_anomalies: Whether to include edge cases
        anomaly_rate: Rate of price jump anomalies (default 1%)
        invalid_rate: Rate of invalid data (default 0.5%)
        out_of_order_rate: Rate of out-of-order timestamps (default 0.2%)
    """
    # Ensure output directory exists
    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    
    # Starting values
    start_time = datetime(2024, 1, 1, 9, 30, 0)
    base_price = 100.0
    tick_interval_ns = 1_000_000  # 1ms between ticks
    
    rows = []
    current_timestamp = int(start_time.timestamp() * 1_000_000_000)
    current_price = base_price
    
    print(f"Generating {num_rows:,} rows of test data...")
    start = time.time()
    
    for i in range(num_rows):
        # Determine tick type
        rand_val = random.random()
        
        if include_anomalies:
            if rand_val < invalid_rate / 2:
                # Invalid price
                tick = generate_invalid_price_tick(current_timestamp)
            elif rand_val < invalid_rate:
                # Invalid volume
                tick = generate_invalid_volume_tick(current_timestamp, current_price)
            elif rand_val < invalid_rate + anomaly_rate:
                # Price jump anomaly
                tick = generate_price_jump_tick(current_timestamp, current_price)
                current_price = tick[1]  # Update price after jump
            elif rand_val < invalid_rate + anomaly_rate + out_of_order_rate:
                # Out of order timestamp (go back in time)
                old_timestamp = current_timestamp - random.randint(1, 100) * tick_interval_ns
                tick = generate_normal_tick(old_timestamp, current_price)
            else:
                # Normal tick
                tick = generate_normal_tick(current_timestamp, current_price)
                current_price = tick[1]
        else:
            # Only normal ticks
            tick = generate_normal_tick(current_timestamp, current_price)
            current_price = tick[1]
        
        rows.append(tick)
        current_timestamp += tick_interval_ns + random.randint(0, 100000)  # Add some jitter
        
        # Progress indicator
        if (i + 1) % 100000 == 0:
            print(f"  Generated {i + 1:,} rows...")
    
    # Write to CSV
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
    
    # Print statistics
    if include_anomalies:
        invalid_count = sum(1 for r in rows if r[1] <= 0 or r[2] < 0)
        print(f"Invalid ticks: ~{invalid_count} ({invalid_count/num_rows*100:.2f}%)")


def main():
    parser = argparse.ArgumentParser(
        description='Generate test tick data for AegisQuant-Hybrid'
    )
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
    parser.add_argument(
        '--clean',
        action='store_true',
        help='Generate only clean data without anomalies'
    )
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
    
    args = parser.parse_args()
    
    # Validate num_rows
    if args.num_rows > 1000000:
        print("Warning: Limiting to 1,000,000 rows maximum")
        args.num_rows = 1000000
    
    generate_test_data(
        output_path=args.output,
        num_rows=args.num_rows,
        include_anomalies=not args.clean,
        anomaly_rate=args.anomaly_rate,
        invalid_rate=args.invalid_rate,
    )


if __name__ == '__main__':
    main()
