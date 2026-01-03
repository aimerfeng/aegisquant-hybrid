//! Property-based tests for Floating-Point Precision.
//!
//! Feature: aegisquant-optimizations, Property 3: Float Precision Calculation
//! Validates: Requirements 3.2, 3.3, 3.4, 3.5
//!
//! This test verifies that:
//! 1. AccountBalance using rust_decimal has no cumulative errors
//! 2. Exported f64 values are within PRICE_EPSILON of Decimal values
//! 3. Multiple trades don't cause precision drift

use proptest::prelude::*;
use rust_decimal::prelude::*;
use rust_decimal::Decimal;

use aegisquant_core::precision::{
    AccountBalance, PRICE_EPSILON, QUANTITY_EPSILON,
    approx_eq, price_eq, quantity_eq, spread_bps,
};

proptest! {
    #![proptest_config(ProptestConfig::with_cases(50))]

    /// Property 3: Decimal precision has no drift after multiple trades
    #[test]
    fn decimal_precision_no_drift(
        trades in prop::collection::vec(
            (1.0f64..1000.0, 1.0f64..100.0, prop::bool::ANY),
            1..100
        )
    ) {
        let initial = Decimal::from(1000000);
        let mut balance = AccountBalance::new(initial);
        let mut expected = initial;

        for (price, quantity, is_buy) in trades {
            balance.execute_trade(quantity, price, is_buy);

            let qty = Decimal::from_f64_retain(quantity).unwrap_or_default();
            let prc = Decimal::from_f64_retain(price).unwrap_or_default();
            let value = qty * prc;

            if is_buy {
                expected -= value;
            } else {
                expected += value;
            }
        }

        prop_assert_eq!(balance.balance(), expected);
    }

    /// Property 3: Exported f64 is within PRICE_EPSILON of Decimal value
    #[test]
    fn exported_f64_within_epsilon(
        trades in prop::collection::vec(
            (1.0f64..1000.0, 1.0f64..100.0, prop::bool::ANY),
            1..50
        )
    ) {
        let initial = Decimal::from(1000000);
        let mut balance = AccountBalance::new(initial);
        let mut expected = initial;

        for (price, quantity, is_buy) in trades {
            balance.execute_trade(quantity, price, is_buy);

            let qty = Decimal::from_f64_retain(quantity).unwrap_or_default();
            let prc = Decimal::from_f64_retain(price).unwrap_or_default();
            let value = qty * prc;

            if is_buy {
                expected -= value;
            } else {
                expected += value;
            }
        }

        let exported = balance.to_f64();
        let expected_f64 = expected.to_f64().unwrap_or(0.0);

        prop_assert!(
            (exported - expected_f64).abs() < PRICE_EPSILON,
            "Exported {} differs from expected {} by more than PRICE_EPSILON",
            exported, expected_f64
        );
    }

    /// Property 3: approx_eq is symmetric
    #[test]
    fn approx_eq_symmetric(
        a in -1e10f64..1e10,
        b in -1e10f64..1e10,
        epsilon in 1e-15f64..1e-5
    ) {
        let result_ab = approx_eq(a, b, epsilon);
        let result_ba = approx_eq(b, a, epsilon);
        prop_assert_eq!(result_ab, result_ba, "approx_eq should be symmetric");
    }

    /// Property 3: approx_eq is reflexive
    #[test]
    fn approx_eq_reflexive(
        a in -1e10f64..1e10,
        epsilon in 1e-15f64..1e-5
    ) {
        prop_assert!(
            approx_eq(a, a, epsilon),
            "approx_eq(a, a, epsilon) should always be true"
        );
    }

    /// Property 3: price_eq uses PRICE_EPSILON correctly
    #[test]
    fn price_eq_uses_epsilon(
        base_price in 1.0f64..10000.0,
        delta in -1e-11f64..1e-11
    ) {
        let price1 = base_price;
        let price2 = base_price + delta;

        if delta.abs() < PRICE_EPSILON {
            prop_assert!(
                price_eq(price1, price2),
                "Prices {} and {} should be equal (delta={})",
                price1, price2, delta
            );
        }
    }

    /// Property 3: quantity_eq uses QUANTITY_EPSILON correctly
    #[test]
    fn quantity_eq_uses_epsilon(
        base_qty in 1.0f64..100000.0,
        delta in -1e-9f64..1e-9
    ) {
        let qty1 = base_qty;
        let qty2 = base_qty + delta;

        if delta.abs() < QUANTITY_EPSILON {
            prop_assert!(
                quantity_eq(qty1, qty2),
                "Quantities {} and {} should be equal (delta={})",
                qty1, qty2, delta
            );
        }
    }

    /// Property 3: spread_bps is always non-negative for valid inputs
    #[test]
    fn spread_bps_non_negative(
        bid in 1.0f64..10000.0,
        spread_pct in 0.0f64..0.1
    ) {
        let ask = bid * (1.0 + spread_pct);
        let bps = spread_bps(bid, ask);

        prop_assert!(
            bps >= 0.0,
            "spread_bps should be non-negative, got {} for bid={}, ask={}",
            bps, bid, ask
        );
    }

    /// Property 3: spread_bps calculation is correct
    #[test]
    fn spread_bps_calculation_correct(
        bid in 10.0f64..1000.0,
        spread_pct in 0.001f64..0.05
    ) {
        let ask = bid * (1.0 + spread_pct);
        let bps = spread_bps(bid, ask);

        let mid = (bid + ask) / 2.0;
        let spread = ask - bid;
        let expected_bps = spread / mid * 10000.0;

        prop_assert!(
            (bps - expected_bps).abs() < 0.01,
            "spread_bps {} differs from expected {} by more than 0.01",
            bps, expected_bps
        );
    }

    /// Property 3: AccountBalance deposit/withdraw are inverse operations
    #[test]
    fn deposit_withdraw_inverse(
        initial in 10000.0f64..1000000.0,
        amount in 1.0f64..10000.0
    ) {
        let mut balance = AccountBalance::from_f64(initial);
        let original = balance.to_f64();

        balance.deposit(amount);
        let after_deposit = balance.to_f64();

        let tolerance = 1e-6;
        prop_assert!(
            (after_deposit - original - amount).abs() < tolerance,
            "Deposit should increase balance by exact amount. Expected {}, got {}",
            original + amount, after_deposit
        );

        let success = balance.withdraw(amount);
        prop_assert!(success, "Withdraw should succeed");

        let after_withdraw = balance.to_f64();
        prop_assert!(
            (after_withdraw - original).abs() < tolerance,
            "After deposit and withdraw, balance should return to original. Expected {}, got {}",
            original, after_withdraw
        );
    }

    /// Property 3: Buy and sell of same quantity/price are inverse
    #[test]
    fn buy_sell_inverse(
        initial in 100000.0f64..1000000.0,
        quantity in 1.0f64..100.0,
        price in 10.0f64..1000.0
    ) {
        let mut balance = AccountBalance::from_f64(initial);
        let original = balance.to_f64();

        balance.execute_trade(quantity, price, true);
        balance.execute_trade(quantity, price, false);

        let final_balance = balance.to_f64();

        prop_assert!(
            (final_balance - original).abs() < 1e-6,
            "Buy+Sell should return to original balance. Original={}, Final={}",
            original, final_balance
        );
    }

    /// Property 3: Commission always reduces balance
    #[test]
    fn commission_reduces_balance(
        initial in 100000.0f64..1000000.0,
        quantity in 1.0f64..100.0,
        price in 10.0f64..1000.0,
        commission in 0.01f64..100.0
    ) {
        let mut balance_with_comm = AccountBalance::from_f64(initial);
        let mut balance_without_comm = AccountBalance::from_f64(initial);

        balance_with_comm.execute_trade_with_commission(quantity, price, true, commission);
        balance_without_comm.execute_trade(quantity, price, true);

        let with_comm = balance_with_comm.to_f64();
        let without_comm = balance_without_comm.to_f64();

        prop_assert!(
            with_comm < without_comm,
            "Balance with commission {} should be less than without {}",
            with_comm, without_comm
        );

        let diff = without_comm - with_comm;
        prop_assert!(
            (diff - commission).abs() < 1e-6,
            "Difference {} should equal commission {}",
            diff, commission
        );
    }

    /// Property 3: Equity equals balance plus unrealized PnL
    #[test]
    fn equity_equals_balance_plus_unrealized(
        initial in 100000.0f64..1000000.0,
        unrealized in -10000.0f64..10000.0
    ) {
        let mut balance = AccountBalance::from_f64(initial);
        balance.update_unrealized_pnl(unrealized);

        let equity = balance.equity_f64();
        let expected = initial + unrealized;

        prop_assert!(
            (equity - expected).abs() < 1e-6,
            "Equity {} should equal balance + unrealized {}",
            equity, expected
        );
    }
}
