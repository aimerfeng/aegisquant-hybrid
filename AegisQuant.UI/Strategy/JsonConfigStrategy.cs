using System;
using System.Collections.Generic;
using System.Text.Json;
using AegisQuant.UI.Strategy.Loaders;
using AegisQuant.UI.Strategy.Models;

namespace AegisQuant.UI.Strategy;

/// <summary>
/// Strategy implementation based on JSON configuration.
/// </summary>
public class JsonConfigStrategy : IStrategy
{
    private readonly JsonStrategyConfig _config;
    private readonly string? _sourcePath;
    private readonly ConditionParser _conditionParser;
    private readonly Dictionary<string, object> _parameters;
    private readonly Dictionary<string, double> _indicatorValues;
    private bool _disposed;

    public JsonConfigStrategy(JsonStrategyConfig config, string? sourcePath = null)
    {
        _config = config;
        _sourcePath = sourcePath;
        _conditionParser = new ConditionParser();
        _parameters = new Dictionary<string, object>();
        _indicatorValues = new Dictionary<string, double>();

        // Initialize parameters with defaults
        foreach (var (name, paramConfig) in config.Parameters)
        {
            _parameters[name] = ResolveJsonValue(paramConfig.Default, paramConfig.Type);
        }
    }

    public string Name => _config.Name;
    public string Description => _config.Description;
    public StrategyType Type => StrategyType.JsonConfig;
    public IReadOnlyDictionary<string, object> Parameters => _parameters;

    /// <summary>
    /// Sets a parameter value.
    /// </summary>
    public void SetParameter(string name, object value)
    {
        if (_parameters.ContainsKey(name))
        {
            _parameters[name] = value;
        }
    }

    public Signal OnTick(StrategyContext context)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JsonConfigStrategy));

        try
        {
            // Calculate all indicators
            CalculateIndicators(context);

            // Set up condition parser variables
            SetupConditionVariables(context);

            // Evaluate buy condition
            if (_config.Rules.Buy != null && !string.IsNullOrWhiteSpace(_config.Rules.Buy.Condition))
            {
                var buyCondition = SubstituteParameters(_config.Rules.Buy.Condition);
                if (_conditionParser.Evaluate(buyCondition))
                {
                    return Signal.Buy;
                }
            }

            // Evaluate sell condition
            if (_config.Rules.Sell != null && !string.IsNullOrWhiteSpace(_config.Rules.Sell.Condition))
            {
                var sellCondition = SubstituteParameters(_config.Rules.Sell.Condition);
                if (_conditionParser.Evaluate(sellCondition))
                {
                    return Signal.Sell;
                }
            }

            return Signal.None;
        }
        catch (Exception)
        {
            // Log error and return None on any exception
            return Signal.None;
        }
    }

    private void CalculateIndicators(StrategyContext context)
    {
        _indicatorValues.Clear();

        foreach (var indicator in _config.Indicators)
        {
            var value = CalculateIndicator(indicator, context);
            if (value.HasValue)
            {
                _indicatorValues[indicator.Name] = value.Value;
            }
        }
    }

    private double? CalculateIndicator(JsonIndicatorConfig indicator, StrategyContext context)
    {
        var type = indicator.Type.ToUpperInvariant();

        switch (type)
        {
            case "SMA":
                var smaPeriod = ResolveIntParameter(indicator.Period, 20);
                return context.Indicators.SMA(smaPeriod);

            case "EMA":
                var emaPeriod = ResolveIntParameter(indicator.Period, 20);
                return context.Indicators.EMA(emaPeriod);

            case "RSI":
                var rsiPeriod = ResolveIntParameter(indicator.Period, 14);
                return context.Indicators.RSI(rsiPeriod);

            case "MACD":
                var fastPeriod = ResolveIntParameter(indicator.FastPeriod, 12);
                var slowPeriod = ResolveIntParameter(indicator.SlowPeriod, 26);
                var signalPeriod = ResolveIntParameter(indicator.SignalPeriod, 9);
                var (macd, _, _) = context.Indicators.MACD(fastPeriod, slowPeriod, signalPeriod);
                return macd;

            case "MACD_SIGNAL":
                var fastP = ResolveIntParameter(indicator.FastPeriod, 12);
                var slowP = ResolveIntParameter(indicator.SlowPeriod, 26);
                var sigP = ResolveIntParameter(indicator.SignalPeriod, 9);
                var (_, signal, _) = context.Indicators.MACD(fastP, slowP, sigP);
                return signal;

            case "MACD_HISTOGRAM":
                var fp = ResolveIntParameter(indicator.FastPeriod, 12);
                var sp = ResolveIntParameter(indicator.SlowPeriod, 26);
                var sgp = ResolveIntParameter(indicator.SignalPeriod, 9);
                var (_, _, histogram) = context.Indicators.MACD(fp, sp, sgp);
                return histogram;

            case "BB":
            case "BOLLINGERBANDS":
                var bbPeriod = ResolveIntParameter(indicator.Period, 20);
                var stdDev = ResolveDoubleParameter(indicator.StdDev, 2.0);
                var (_, middle, _) = context.Indicators.BollingerBands(bbPeriod, stdDev);
                return middle;

            case "BB_UPPER":
                var bbUpPeriod = ResolveIntParameter(indicator.Period, 20);
                var stdDevUp = ResolveDoubleParameter(indicator.StdDev, 2.0);
                var (upper, _, _) = context.Indicators.BollingerBands(bbUpPeriod, stdDevUp);
                return upper;

            case "BB_LOWER":
                var bbLowPeriod = ResolveIntParameter(indicator.Period, 20);
                var stdDevLow = ResolveDoubleParameter(indicator.StdDev, 2.0);
                var (_, _, lower) = context.Indicators.BollingerBands(bbLowPeriod, stdDevLow);
                return lower;

            case "ATR":
                var atrPeriod = ResolveIntParameter(indicator.Period, 14);
                return context.Indicators.ATR(atrPeriod);

            case "STOCH":
            case "STOCHASTIC":
                var kPeriod = ResolveIntParameter(indicator.Period, 14);
                var (k, _) = context.Indicators.Stochastic(kPeriod);
                return k;

            default:
                return null;
        }
    }

    private int ResolveIntParameter(JsonElement? element, int defaultValue)
    {
        if (!element.HasValue)
            return defaultValue;

        var elem = element.Value;

        // Check if it's a parameter reference
        if (elem.ValueKind == JsonValueKind.String)
        {
            var str = elem.GetString();
            if (str != null && str.StartsWith("$"))
            {
                var paramName = str.Substring(1);
                if (_parameters.TryGetValue(paramName, out var paramValue))
                {
                    return Convert.ToInt32(paramValue);
                }
            }
            if (int.TryParse(str, out var parsed))
                return parsed;
        }

        if (elem.ValueKind == JsonValueKind.Number)
        {
            return elem.GetInt32();
        }

        return defaultValue;
    }

    private double ResolveDoubleParameter(JsonElement? element, double defaultValue)
    {
        if (!element.HasValue)
            return defaultValue;

        var elem = element.Value;

        if (elem.ValueKind == JsonValueKind.String)
        {
            var str = elem.GetString();
            if (str != null && str.StartsWith("$"))
            {
                var paramName = str.Substring(1);
                if (_parameters.TryGetValue(paramName, out var paramValue))
                {
                    return Convert.ToDouble(paramValue);
                }
            }
            if (double.TryParse(str, out var parsed))
                return parsed;
        }

        if (elem.ValueKind == JsonValueKind.Number)
        {
            return elem.GetDouble();
        }

        return defaultValue;
    }

    private void SetupConditionVariables(StrategyContext context)
    {
        // Add indicator values
        foreach (var (name, value) in _indicatorValues)
        {
            _conditionParser.SetVariable(name, value);
        }

        // Add price and volume
        _conditionParser.SetVariable("price", context.Price);
        _conditionParser.SetVariable("volume", context.Volume);

        // Add parameters
        foreach (var (name, value) in _parameters)
        {
            if (value is double d)
                _conditionParser.SetVariable(name, d);
            else if (value is int i)
                _conditionParser.SetVariable(name, i);
        }
    }

    private string SubstituteParameters(string condition)
    {
        var result = condition;
        foreach (var (name, value) in _parameters)
        {
            result = result.Replace($"${name}", value.ToString());
        }
        return result;
    }

    private static object ResolveJsonValue(JsonElement element, string type)
    {
        return type.ToLowerInvariant() switch
        {
            "int" => element.ValueKind == JsonValueKind.Number ? element.GetInt32() : 0,
            "double" => element.ValueKind == JsonValueKind.Number ? element.GetDouble() : 0.0,
            "bool" => element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False
                ? element.GetBoolean() : false,
            "string" => element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "",
            _ => element.ValueKind == JsonValueKind.Number ? element.GetDouble() : 0.0
        };
    }

    public void Reset()
    {
        _indicatorValues.Clear();
        _conditionParser.Clear();
    }

    public ValidationResult Validate()
    {
        var loader = new JsonStrategyLoader();
        return loader.ValidateConfig(_config);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _indicatorValues.Clear();
            _disposed = true;
        }
    }
}
