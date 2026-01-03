using System;
using System.Collections.Generic;
using System.Linq;

namespace AegisQuant.UI.Strategy;

/// <summary>
/// Service for calculating technical indicators.
/// Provides caching to avoid redundant calculations.
/// </summary>
public class IndicatorService
{
    private readonly IReadOnlyList<TickData> _priceHistory;
    private readonly Dictionary<string, double?> _cache = new();
    private int _lastCacheTickCount = -1;

    public IndicatorService(IReadOnlyList<TickData> priceHistory)
    {
        _priceHistory = priceHistory;
    }

    /// <summary>
    /// Invalidates the indicator cache (call when new data arrives).
    /// </summary>
    public void InvalidateCache()
    {
        if (_priceHistory.Count != _lastCacheTickCount)
        {
            _cache.Clear();
            _lastCacheTickCount = _priceHistory.Count;
        }
    }

    private double? GetCached(string key, Func<double?> calculate)
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var value = calculate();
        _cache[key] = value;
        return value;
    }

    /// <summary>
    /// Simple Moving Average.
    /// </summary>
    /// <param name="period">Number of periods</param>
    /// <returns>SMA value or null if insufficient data</returns>
    public double? SMA(int period)
    {
        return GetCached($"SMA_{period}", () => CalculateSMA(period));
    }

    private double? CalculateSMA(int period)
    {
        if (_priceHistory.Count < period || period <= 0)
            return null;

        double sum = 0;
        for (int i = _priceHistory.Count - period; i < _priceHistory.Count; i++)
        {
            sum += _priceHistory[i].Price;
        }
        return sum / period;
    }

    /// <summary>
    /// Exponential Moving Average.
    /// </summary>
    /// <param name="period">Number of periods</param>
    /// <returns>EMA value or null if insufficient data</returns>
    public double? EMA(int period)
    {
        return GetCached($"EMA_{period}", () => CalculateEMA(period));
    }

    private double? CalculateEMA(int period)
    {
        if (_priceHistory.Count < period || period <= 0)
            return null;

        double multiplier = 2.0 / (period + 1);
        
        // Start with SMA for first period
        double sum = 0;
        for (int i = 0; i < period; i++)
        {
            sum += _priceHistory[i].Price;
        }
        double ema = sum / period;

        // Calculate EMA for remaining prices
        for (int i = period; i < _priceHistory.Count; i++)
        {
            ema = (_priceHistory[i].Price - ema) * multiplier + ema;
        }

        return ema;
    }

    /// <summary>
    /// Relative Strength Index.
    /// </summary>
    /// <param name="period">Number of periods (default 14)</param>
    /// <returns>RSI value (0-100) or null if insufficient data</returns>
    public double? RSI(int period = 14)
    {
        return GetCached($"RSI_{period}", () => CalculateRSI(period));
    }

    private double? CalculateRSI(int period)
    {
        if (_priceHistory.Count < period + 1 || period <= 0)
            return null;

        double avgGain = 0, avgLoss = 0;

        // Calculate initial average gain/loss
        for (int i = 1; i <= period; i++)
        {
            var change = _priceHistory[i].Price - _priceHistory[i - 1].Price;
            if (change > 0)
                avgGain += change;
            else
                avgLoss += Math.Abs(change);
        }
        avgGain /= period;
        avgLoss /= period;

        // Calculate smoothed RSI for remaining periods
        for (int i = period + 1; i < _priceHistory.Count; i++)
        {
            var change = _priceHistory[i].Price - _priceHistory[i - 1].Price;
            if (change > 0)
            {
                avgGain = (avgGain * (period - 1) + change) / period;
                avgLoss = (avgLoss * (period - 1)) / period;
            }
            else
            {
                avgGain = (avgGain * (period - 1)) / period;
                avgLoss = (avgLoss * (period - 1) + Math.Abs(change)) / period;
            }
        }

        if (avgLoss == 0)
            return 100;

        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    /// <summary>
    /// Moving Average Convergence Divergence.
    /// </summary>
    /// <param name="fastPeriod">Fast EMA period (default 12)</param>
    /// <param name="slowPeriod">Slow EMA period (default 26)</param>
    /// <param name="signalPeriod">Signal line period (default 9)</param>
    /// <returns>Tuple of (MACD line, Signal line, Histogram) or nulls if insufficient data</returns>
    public (double? macd, double? signal, double? histogram) MACD(
        int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        var key = $"MACD_{fastPeriod}_{slowPeriod}_{signalPeriod}";
        
        if (_cache.TryGetValue(key + "_macd", out var cachedMacd) &&
            _cache.TryGetValue(key + "_signal", out var cachedSignal) &&
            _cache.TryGetValue(key + "_hist", out var cachedHist))
        {
            return (cachedMacd, cachedSignal, cachedHist);
        }

        var result = CalculateMACD(fastPeriod, slowPeriod, signalPeriod);
        _cache[key + "_macd"] = result.macd;
        _cache[key + "_signal"] = result.signal;
        _cache[key + "_hist"] = result.histogram;
        return result;
    }

    private (double? macd, double? signal, double? histogram) CalculateMACD(
        int fastPeriod, int slowPeriod, int signalPeriod)
    {
        if (_priceHistory.Count < slowPeriod + signalPeriod)
            return (null, null, null);

        // Calculate MACD line values
        var macdValues = new List<double>();
        double fastMultiplier = 2.0 / (fastPeriod + 1);
        double slowMultiplier = 2.0 / (slowPeriod + 1);

        // Initialize EMAs with SMA
        double fastEma = _priceHistory.Take(fastPeriod).Average(t => t.Price);
        double slowEma = _priceHistory.Take(slowPeriod).Average(t => t.Price);

        for (int i = slowPeriod; i < _priceHistory.Count; i++)
        {
            fastEma = (_priceHistory[i].Price - fastEma) * fastMultiplier + fastEma;
            slowEma = (_priceHistory[i].Price - slowEma) * slowMultiplier + slowEma;
            macdValues.Add(fastEma - slowEma);
        }

        if (macdValues.Count < signalPeriod)
            return (null, null, null);

        // Calculate signal line (EMA of MACD)
        double signalMultiplier = 2.0 / (signalPeriod + 1);
        double signalLine = macdValues.Take(signalPeriod).Average();

        for (int i = signalPeriod; i < macdValues.Count; i++)
        {
            signalLine = (macdValues[i] - signalLine) * signalMultiplier + signalLine;
        }

        var macdLine = macdValues.Last();
        var histogram = macdLine - signalLine;

        return (macdLine, signalLine, histogram);
    }

    /// <summary>
    /// Bollinger Bands.
    /// </summary>
    /// <param name="period">Number of periods (default 20)</param>
    /// <param name="stdDev">Standard deviation multiplier (default 2.0)</param>
    /// <returns>Tuple of (Upper band, Middle band, Lower band) or nulls if insufficient data</returns>
    public (double? upper, double? middle, double? lower) BollingerBands(int period = 20, double stdDev = 2.0)
    {
        var key = $"BB_{period}_{stdDev}";
        
        if (_cache.TryGetValue(key + "_upper", out var cachedUpper) &&
            _cache.TryGetValue(key + "_middle", out var cachedMiddle) &&
            _cache.TryGetValue(key + "_lower", out var cachedLower))
        {
            return (cachedUpper, cachedMiddle, cachedLower);
        }

        var result = CalculateBollingerBands(period, stdDev);
        _cache[key + "_upper"] = result.upper;
        _cache[key + "_middle"] = result.middle;
        _cache[key + "_lower"] = result.lower;
        return result;
    }

    private (double? upper, double? middle, double? lower) CalculateBollingerBands(int period, double stdDev)
    {
        if (_priceHistory.Count < period || period <= 0)
            return (null, null, null);

        var prices = _priceHistory.Skip(_priceHistory.Count - period).Select(t => t.Price).ToList();
        var middle = prices.Average();
        
        var variance = prices.Sum(p => Math.Pow(p - middle, 2)) / period;
        var std = Math.Sqrt(variance);

        var upper = middle + stdDev * std;
        var lower = middle - stdDev * std;

        return (upper, middle, lower);
    }

    /// <summary>
    /// Average True Range.
    /// </summary>
    /// <param name="period">Number of periods (default 14)</param>
    /// <returns>ATR value or null if insufficient data</returns>
    public double? ATR(int period = 14)
    {
        return GetCached($"ATR_{period}", () => CalculateATR(period));
    }

    private double? CalculateATR(int period)
    {
        if (_priceHistory.Count < period + 1 || period <= 0)
            return null;

        var trueRanges = new List<double>();

        for (int i = 1; i < _priceHistory.Count; i++)
        {
            var current = _priceHistory[i];
            var previous = _priceHistory[i - 1];

            var high = current.High ?? current.Price;
            var low = current.Low ?? current.Price;
            var prevClose = previous.Close ?? previous.Price;

            var tr = Math.Max(
                high - low,
                Math.Max(
                    Math.Abs(high - prevClose),
                    Math.Abs(low - prevClose)
                )
            );
            trueRanges.Add(tr);
        }

        if (trueRanges.Count < period)
            return null;

        // Calculate initial ATR as simple average
        double atr = trueRanges.Take(period).Average();

        // Smooth ATR for remaining periods
        for (int i = period; i < trueRanges.Count; i++)
        {
            atr = (atr * (period - 1) + trueRanges[i]) / period;
        }

        return atr;
    }

    /// <summary>
    /// Stochastic Oscillator.
    /// </summary>
    /// <param name="kPeriod">%K period (default 14)</param>
    /// <param name="dPeriod">%D period (default 3)</param>
    /// <returns>Tuple of (%K, %D) or nulls if insufficient data</returns>
    public (double? k, double? d) Stochastic(int kPeriod = 14, int dPeriod = 3)
    {
        var key = $"STOCH_{kPeriod}_{dPeriod}";
        
        if (_cache.TryGetValue(key + "_k", out var cachedK) &&
            _cache.TryGetValue(key + "_d", out var cachedD))
        {
            return (cachedK, cachedD);
        }

        var result = CalculateStochastic(kPeriod, dPeriod);
        _cache[key + "_k"] = result.k;
        _cache[key + "_d"] = result.d;
        return result;
    }

    private (double? k, double? d) CalculateStochastic(int kPeriod, int dPeriod)
    {
        if (_priceHistory.Count < kPeriod + dPeriod - 1 || kPeriod <= 0 || dPeriod <= 0)
            return (null, null);

        var kValues = new List<double>();

        for (int i = kPeriod - 1; i < _priceHistory.Count; i++)
        {
            var periodData = _priceHistory.Skip(i - kPeriod + 1).Take(kPeriod).ToList();
            var highest = periodData.Max(t => t.High ?? t.Price);
            var lowest = periodData.Min(t => t.Low ?? t.Price);
            var current = _priceHistory[i].Close ?? _priceHistory[i].Price;

            if (highest - lowest > 0)
            {
                kValues.Add(100 * (current - lowest) / (highest - lowest));
            }
            else
            {
                kValues.Add(50); // Default when range is 0
            }
        }

        if (kValues.Count < dPeriod)
            return (null, null);

        var k = kValues.Last();
        var d = kValues.Skip(kValues.Count - dPeriod).Average();

        return (k, d);
    }

    /// <summary>
    /// Gets the previous value of an indicator (for crossover detection).
    /// </summary>
    /// <param name="indicatorName">Name of the indicator</param>
    /// <param name="period">Period parameter</param>
    /// <returns>Previous value or null</returns>
    public double? GetPrevious(string indicatorName, int period)
    {
        // This would require storing historical indicator values
        // For now, return null - can be enhanced later
        return null;
    }
}
