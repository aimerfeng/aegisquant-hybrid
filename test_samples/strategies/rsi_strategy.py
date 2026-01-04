from aegisquant import BaseStrategy, Signal

class RSIOverboughtOversoldStrategy(BaseStrategy):
    """
    RSI 超买超卖策略
    
    当 RSI 低于超卖线时买入，高于超买线时卖出
    适合震荡行情
    """
    
    name = "RSI超买超卖策略"
    description = "RSI指标超买超卖策略，适合震荡行情"
    
    def __init__(self):
        super().__init__()
        # 策略参数
        self.rsi_period = 14      # RSI 周期
        self.overbought = 70      # 超买线
        self.oversold = 30        # 超卖线
        self.position_size = 100  # 每次交易数量
        
        # 状态变量
        self.prev_rsi = None
        self.in_position = False
    
    def on_tick(self, ctx):
        """
        处理每个 tick 数据
        
        参数:
            ctx: 策略上下文，包含价格、指标等信息
            
        返回:
            Signal.BUY, Signal.SELL, 或 Signal.NONE
        """
        # 计算 RSI
        rsi = ctx.rsi(self.rsi_period)
        
        # 第一次运行，保存 RSI 值
        if self.prev_rsi is None:
            self.prev_rsi = rsi
            return Signal.NONE
        
        signal = Signal.NONE
        
        # RSI 从超卖区回升 -> 买入信号
        if self.prev_rsi <= self.oversold and rsi > self.oversold:
            if not self.in_position:
                signal = Signal.BUY
                self.in_position = True
        
        # RSI 从超买区回落 -> 卖出信号
        elif self.prev_rsi >= self.overbought and rsi < self.overbought:
            if self.in_position:
                signal = Signal.SELL
                self.in_position = False
        
        # 更新状态
        self.prev_rsi = rsi
        
        return signal
    
    def on_bar(self, ctx):
        """
        处理每根 K 线数据（可选）
        """
        pass