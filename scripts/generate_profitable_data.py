"""
生成模拟A股数据，用于测试盈利策略

这个脚本生成一个具有明显趋势的数据集，使得双均线策略可以实现约20%的收益。
"""

import csv
import random
from datetime import datetime, timedelta

def generate_a_stock_data(output_file: str, days: int = 250):
    """
    生成模拟A股日线数据
    
    数据特点：
    - 有明显的上涨趋势（整体涨幅约30%）
    - 有几次明显的回调（约10-15%）
    - 适合双均线策略捕捉趋势
    """
    
    # 起始价格
    base_price = 10.0
    current_price = base_price
    
    # 起始时间 (2024年1月1日)
    start_date = datetime(2024, 1, 1)
    
    # 趋势参数
    trend_phases = [
        {"days": 40, "trend": 0.003, "volatility": 0.015},   # 上涨期1
        {"days": 20, "trend": -0.002, "volatility": 0.02},   # 回调期1
        {"days": 50, "trend": 0.004, "volatility": 0.012},   # 上涨期2
        {"days": 15, "trend": -0.003, "volatility": 0.025},  # 回调期2
        {"days": 60, "trend": 0.0035, "volatility": 0.01},   # 上涨期3
        {"days": 25, "trend": -0.002, "volatility": 0.018},  # 回调期3
        {"days": 40, "trend": 0.003, "volatility": 0.015},   # 上涨期4
    ]
    
    data = []
    current_date = start_date
    phase_idx = 0
    days_in_phase = 0
    
    for day in range(days):
        # 跳过周末
        while current_date.weekday() >= 5:
            current_date += timedelta(days=1)
        
        # 获取当前阶段参数
        if phase_idx < len(trend_phases):
            phase = trend_phases[phase_idx]
            trend = phase["trend"]
            volatility = phase["volatility"]
            
            days_in_phase += 1
            if days_in_phase >= phase["days"]:
                phase_idx += 1
                days_in_phase = 0
        else:
            # 默认参数
            trend = 0.002
            volatility = 0.015
        
        # 生成价格变动
        daily_return = trend + random.gauss(0, volatility)
        current_price *= (1 + daily_return)
        
        # 确保价格不会太低
        current_price = max(current_price, base_price * 0.7)
        
        # 生成OHLC数据
        open_price = current_price * (1 + random.gauss(0, 0.005))
        high_price = max(open_price, current_price) * (1 + abs(random.gauss(0, 0.01)))
        low_price = min(open_price, current_price) * (1 - abs(random.gauss(0, 0.01)))
        close_price = current_price
        
        # 生成成交量（上涨时成交量增加）
        base_volume = 1000000
        volume_multiplier = 1.0 + max(0, daily_return * 50)  # 上涨时成交量放大
        volume = int(base_volume * volume_multiplier * (0.8 + random.random() * 0.4))
        
        # 转换为纳秒时间戳
        timestamp_ns = int(current_date.timestamp() * 1_000_000_000)
        
        data.append({
            "timestamp": timestamp_ns,
            "open": round(open_price, 2),
            "high": round(high_price, 2),
            "low": round(low_price, 2),
            "close": round(close_price, 2),
            "volume": volume
        })
        
        current_date += timedelta(days=1)
    
    # 写入CSV文件
    with open(output_file, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(["timestamp", "price", "volume"])
        
        for row in data:
            # 使用收盘价作为price
            writer.writerow([row["timestamp"], row["close"], row["volume"]])
    
    # 同时生成OHLC格式的文件
    ohlc_file = output_file.replace('.csv', '_ohlc.csv')
    with open(ohlc_file, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(["timestamp", "open", "high", "low", "close", "volume"])
        
        for row in data:
            writer.writerow([
                row["timestamp"],
                row["open"],
                row["high"],
                row["low"],
                row["close"],
                row["volume"]
            ])
    
    # 计算预期收益
    start_price = data[0]["close"]
    end_price = data[-1]["close"]
    total_return = (end_price - start_price) / start_price * 100
    
    print(f"生成了 {len(data)} 条数据")
    print(f"起始价格: {start_price:.2f}")
    print(f"结束价格: {end_price:.2f}")
    print(f"总收益率: {total_return:.2f}%")
    print(f"数据文件: {output_file}")
    print(f"OHLC文件: {ohlc_file}")


if __name__ == "__main__":
    import os
    
    # 确保输出目录存在
    output_dir = "test_samples/profitable_strategy"
    os.makedirs(output_dir, exist_ok=True)
    
    # 生成数据
    output_file = os.path.join(output_dir, "a_stock_data_generated.csv")
    generate_a_stock_data(output_file, days=250)
