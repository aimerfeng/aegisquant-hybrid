# AegisQuant 测试样本

##  文件夹结构

`
test_samples/
 data/                           # 测试数据
    600519_maotai_2024.csv     # 贵州茅台 2024年日线数据
    300750_catl_2024.csv       # 宁德时代 2024年日线数据
 strategies/                     # 测试策略
    ma_crossover_strategy.json # JSON格式 - 双均线交叉策略
    rsi_strategy.py            # Python格式 - RSI超买超卖策略
 README.md                       # 本说明文件
`

##  数据说明

### 600519_maotai_2024.csv (贵州茅台)
- 时间范围: 2024年1月 - 2024年5月
- 数据类型: 日线 OHLCV
- 特点: 稳定上涨趋势，适合测试趋势跟踪策略

### 300750_catl_2024.csv (宁德时代)
- 时间范围: 2024年1月 - 2024年5月
- 数据类型: 日线 OHLCV
- 特点: 有涨有跌，适合测试震荡策略

### CSV 格式
`
timestamp,open,high,low,close,volume
2024-01-02,188.50,192.30,186.20,190.80,45600000
`

##  策略说明

### 1. 双均线交叉策略 (JSON)
文件: ma_crossover_strategy.json

**原理:**
- 短期均线 (5日) 上穿长期均线 (20日) -> 买入
- 短期均线下穿长期均线 -> 卖出

**参数:**
- short_period: 5 (短期均线周期)
- long_period: 20 (长期均线周期)
- position_size: 100 (每次交易数量)

### 2. RSI超买超卖策略 (Python)
文件: si_strategy.py

**原理:**
- RSI 从超卖区 (<30) 回升 -> 买入
- RSI 从超买区 (>70) 回落 -> 卖出

**参数:**
- rsi_period: 14 (RSI周期)
- overbought: 70 (超买线)
- oversold: 30 (超卖线)

##  使用方法

1. **加载数据:**
   - 点击 "加载数据" 按钮
   - 选择 	est_samples/data/ 下的 CSV 文件

2. **加载策略:**
   - 点击 "加载策略" 按钮
   - 选择 	est_samples/strategies/ 下的策略文件

3. **运行回测:**
   - 点击 "开始" 按钮运行回测
   - 查看图表和指标结果

##  注意事项

- 这些数据是模拟生成的，仅供测试使用
- 实际交易请使用真实市场数据
- 策略仅供学习参考，不构成投资建议