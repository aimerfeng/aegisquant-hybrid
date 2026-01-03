# 浮点数比较健壮性

## 概述

浮点数比较是量化系统中的常见陷阱。本文档说明如何避免精度问题导致的错误判断。

## 问题分析

### 原始代码 (有问题)

```rust
// types.rs 测试
#[test]
fn test_price_equality() {
    let price1 = 100.0;
    let price2 = 100.0 + 1e-16;
    
    // ⚠️ 使用 f64::EPSILON 太严格
    assert!((price1 - price2).abs() < f64::EPSILON);  // 可能失败！
}
```

**问题**: `f64::EPSILON` ≈ 2.22e-16，这是两个相邻浮点数的最小差值。但经过多次运算后，累积误差可能远大于这个值。

### 实际案例

```rust
// 累积误差示例
let mut sum = 0.0;
for _ in 0..1000 {
    sum += 0.001;  // 0.001 无法精确表示
}
println!("{}", sum);  // 输出: 0.9999999999999062，不是 1.0！

// 使用 f64::EPSILON 比较会失败
assert!((sum - 1.0).abs() < f64::EPSILON);  // ❌ 失败
```

## 解决方案

### 1. 定义合理的 Epsilon

```rust
// constants.rs
/// 价格比较精度 (适用于经过多次运算的值)
pub const PRICE_EPSILON: f64 = 1e-10;

/// 数量比较精度
pub const QUANTITY_EPSILON: f64 = 1e-8;

/// 百分比比较精度
pub const PERCENT_EPSILON: f64 = 1e-6;
```

### 2. 提供比较工具函数

```rust
// utils.rs
/// 近似相等比较
pub fn approx_eq(a: f64, b: f64, epsilon: f64) -> bool {
    (a - b).abs() < epsilon
}

/// 近似小于等于
pub fn approx_le(a: f64, b: f64, epsilon: f64) -> bool {
    a < b + epsilon
}

/// 近似大于等于
pub fn approx_ge(a: f64, b: f64, epsilon: f64) -> bool {
    a > b - epsilon
}

/// 价格专用比较
pub fn price_eq(a: f64, b: f64) -> bool {
    approx_eq(a, b, PRICE_EPSILON)
}

/// 相对误差比较 (适用于不同数量级的值)
pub fn relative_eq(a: f64, b: f64, rel_epsilon: f64) -> bool {
    let max_val = a.abs().max(b.abs());
    if max_val == 0.0 {
        return true;
    }
    (a - b).abs() / max_val < rel_epsilon
}
```

### 3. 使用 rust_decimal 进行账务计算

```rust
// engine.rs
use rust_decimal::Decimal;
use rust_decimal_macros::dec;

pub struct BacktestEngine {
    // 账务数据使用 Decimal
    balance: Decimal,
    realized_pnl: Decimal,
    
    // 价格数据使用 f64 (性能考虑)
    current_price: f64,
    price_buffer: Vec<f64>,
}

impl BacktestEngine {
    pub fn execute_trade(&mut self, quantity: f64, price: f64, is_buy: bool) {
        // 将 f64 转换为 Decimal 进行计算
        let qty = Decimal::from_f64_retain(quantity).unwrap_or(dec!(0));
        let prc = Decimal::from_f64_retain(price).unwrap_or(dec!(0));
        
        let trade_value = qty * prc;
        
        if is_buy {
            self.balance -= trade_value;
        } else {
            self.balance += trade_value;
        }
    }
    
    pub fn get_account_status(&self) -> AccountStatus {
        AccountStatus {
            // 导出时转换为 f64
            balance: self.balance.to_f64().unwrap_or(0.0),
            equity: self.calculate_equity().to_f64().unwrap_or(0.0),
            // ...
        }
    }
}
```

### 4. 测试中的浮点数比较

```rust
// tests/equity_tests.rs
use proptest::prelude::*;

proptest! {
    #![proptest_config(ProptestConfig::with_cases(100))]
    
    #[test]
    fn equity_calculation_precision(
        balance in 1000.0f64..1000000.0,
        price in 1.0f64..1000.0,
        quantity in 1.0f64..1000.0,
    ) {
        let mut engine = BacktestEngine::new_with_balance(balance);
        engine.execute_trade(quantity, price, true);
        
        let expected_balance = balance - price * quantity;
        let actual_balance = engine.get_account_status().balance;
        
        // 使用相对误差比较
        prop_assert!(
            relative_eq(actual_balance, expected_balance, 1e-10),
            "Expected {}, got {}", expected_balance, actual_balance
        );
    }
}
```

## 何时使用 f64 vs Decimal

| 场景 | 推荐类型 | 原因 |
|------|----------|------|
| 价格数据 | f64 | 性能优先，精度足够 |
| 成交量 | f64 | 性能优先 |
| 账户余额 | Decimal | 精度优先，避免累积误差 |
| PnL 计算 | Decimal | 精度优先 |
| 技术指标 | f64 | 性能优先，允许小误差 |
| 手续费计算 | Decimal | 精度优先 |

## 面试话术

### Q: 为什么不直接用 == 比较浮点数？

**A**: 浮点数有三个问题：
1. **表示误差**: 0.1 无法精确表示为二进制浮点数
2. **运算误差**: 每次运算都可能引入误差
3. **累积误差**: 多次运算后误差会累积

所以必须使用 epsilon 比较。我定义了 `PRICE_EPSILON = 1e-10`，比 `f64::EPSILON` 宽松但仍然足够精确。

### Q: 为什么账务计算用 Decimal？

**A**: 金融系统对精度要求极高。假设：
- 每笔交易有 1e-15 的误差
- 每天 10000 笔交易
- 一年后累积误差可能达到分级别

使用 `rust_decimal` 可以保证精确计算，避免"钱不见了"的问题。

### Q: Decimal 性能如何？

**A**: `rust_decimal` 比 f64 慢约 10-100 倍，但：
1. 账务计算不在热路径上
2. 每笔交易只计算一次
3. 正确性比性能更重要

我的策略是：热路径（指标计算）用 f64，冷路径（账务）用 Decimal。
