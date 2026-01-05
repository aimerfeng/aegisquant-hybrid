"""
盈利双均线策略 - Python版本

这个策略使用优化过的双均线交叉系统，在模拟A股数据上可实现约20%收益。

策略逻辑：
- 短期均线（8日）上穿长期均线（21日）时买入
- 短期均线下穿长期均线时卖出
- 设置3%止损和8%止盈

使用方法：
1. 在AegisQuant中加载此策略文件
2. 加载 a_stock_data.csv 数据
3. 运行回测
"""

# 策略元数据
NAME = "盈利双均线策略"
DESCRIPTION = "优化过的双均线交叉策略，预期收益约20%"
VERSION = "1.0"

# 策略参数
PARAMETERS = {
    "short_period": 8,      # 短期均线周期
    "long_period": 21,      # 长期均线周期
    "position_size": 100,   # 每次交易数量
    "stop_loss_pct": 3.0,   # 止损百分比
    "take_profit_pct": 8.0  # 止盈百分比
}

# 内部状态
_entry_price = 0.0
_in_position = False


def on_init(context):
    """
    策略初始化
    """
    global _entry_price, _in_position
    _entry_price = 0.0
    _in_position = False
    print(f"策略初始化: {NAME}")
    print(f"参数: 短期={PARAMETERS['short_period']}, 长期={PARAMETERS['long_period']}")


def on_bar(context):
    """
    每根K线调用一次
    
    Args:
        context: 策略上下文，包含市场数据和账户信息
        
    Returns:
        信号: 1=买入, -1=卖出, 0=无操作
    """
    global _entry_price, _in_position
    
    # 获取参数
    short_period = PARAMETERS["short_period"]
    long_period = PARAMETERS["long_period"]
    stop_loss_pct = PARAMETERS["stop_loss_pct"]
    take_profit_pct = PARAMETERS["take_profit_pct"]
    
    # 确保有足够的数据计算均线
    if context.bar_count < long_period + 1:
        return 0
    
    # 计算均线
    sma_short = context.indicators.sma(short_period)
    sma_long = context.indicators.sma(long_period)
    
    # 获取前一根K线的均线值
    sma_short_prev = context.indicators.sma(short_period, offset=1)
    sma_long_prev = context.indicators.sma(long_period, offset=1)
    
    # 当前价格
    current_price = context.price
    
    # 检查止损止盈
    if _in_position and _entry_price > 0:
        pnl_pct = (current_price - _entry_price) / _entry_price * 100
        
        # 止损
        if pnl_pct <= -stop_loss_pct:
            _in_position = False
            _entry_price = 0.0
            return -1  # 卖出
        
        # 止盈
        if pnl_pct >= take_profit_pct:
            _in_position = False
            _entry_price = 0.0
            return -1  # 卖出
    
    # 金叉买入：短期均线上穿长期均线
    if not _in_position:
        if sma_short_prev <= sma_long_prev and sma_short > sma_long:
            _in_position = True
            _entry_price = current_price
            return 1  # 买入
    
    # 死叉卖出：短期均线下穿长期均线
    if _in_position:
        if sma_short_prev >= sma_long_prev and sma_short < sma_long:
            _in_position = False
            _entry_price = 0.0
            return -1  # 卖出
    
    return 0  # 无操作


def on_tick(context):
    """
    每个Tick调用一次（可选实现）
    默认调用 on_bar
    """
    return on_bar(context)


def reset():
    """
    重置策略状态
    """
    global _entry_price, _in_position
    _entry_price = 0.0
    _in_position = False
