using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AegisQuant.UI.Strategy.Loaders;
using AegisQuant.UI.Strategy.Models;
using Python.Runtime;

namespace AegisQuant.UI.Strategy;

/// <summary>
/// Strategy implementation that wraps a Python script.
/// </summary>
public class PythonScriptStrategy : IStrategy
{
    private readonly PyModule _scope;
    private readonly PyObject _strategyClass;
    private readonly string? _sourcePath;
    private PyObject? _strategyInstance;
    private readonly Dictionary<string, object> _parameters;
    private bool _disposed;

    /// <summary>
    /// Execution timeout in milliseconds.
    /// </summary>
    public int ExecutionTimeoutMs { get; set; } = 100;

    public PythonScriptStrategy(PyModule scope, PyObject strategyClass, string? sourcePath = null)
    {
        _scope = scope;
        _strategyClass = strategyClass;
        _sourcePath = sourcePath;
        _parameters = new Dictionary<string, object>();

        // Create strategy instance and extract metadata
        using (Py.GIL())
        {
            _strategyInstance = _strategyClass.Invoke();
            ExtractParameters();
        }
    }

    public string Name
    {
        get
        {
            using (Py.GIL())
            {
                if (_strategyInstance?.HasAttr("name") == true)
                {
                    return _strategyInstance.GetAttr("name").ToString() ?? "Python Strategy";
                }
                return _strategyClass.GetAttr("__name__").ToString() ?? "Python Strategy";
            }
        }
    }

    public string Description
    {
        get
        {
            using (Py.GIL())
            {
                if (_strategyInstance?.HasAttr("description") == true)
                {
                    return _strategyInstance.GetAttr("description").ToString() ?? "";
                }
                if (_strategyClass.HasAttr("__doc__"))
                {
                    var doc = _strategyClass.GetAttr("__doc__");
                    return doc?.ToString() ?? "";
                }
                return "";
            }
        }
    }

    public StrategyType Type => StrategyType.PythonScript;

    public IReadOnlyDictionary<string, object> Parameters => _parameters;

    /// <summary>
    /// Sets a parameter value.
    /// </summary>
    public void SetParameter(string name, object value)
    {
        if (_parameters.ContainsKey(name))
        {
            _parameters[name] = value;

            // Update Python instance
            using (Py.GIL())
            {
                if (_strategyInstance != null)
                {
                    _strategyInstance.SetAttr(name, value.ToPython());
                }
            }
        }
    }

    public Signal OnTick(StrategyContext context)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PythonScriptStrategy));

        if (_strategyInstance == null)
            return Signal.None;

        try
        {
            // Execute with timeout
            var result = ExecuteWithTimeout(() =>
            {
                using (Py.GIL())
                {
                    // Create Python context object
                    var pyContext = CreatePythonContext(context);

                    // Call on_tick method
                    var signalObj = _strategyInstance.InvokeMethod("on_tick", pyContext);

                    // Convert result to Signal
                    return ConvertToSignal(signalObj);
                }
            }, ExecutionTimeoutMs);

            return result;
        }
        catch (TimeoutException)
        {
            // Strategy execution timed out
            return Signal.None;
        }
        catch (PythonException ex)
        {
            // Log Python exception and return None
            System.Diagnostics.Debug.WriteLine($"Python strategy error: {ex.Message}");
            return Signal.None;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Strategy execution error: {ex.Message}");
            return Signal.None;
        }
    }

    private PyObject CreatePythonContext(StrategyContext context)
    {
        using (Py.GIL())
        {
            // Create a simple Python dict-like context
            var pyDict = new PyDict();

            // Add current tick data
            pyDict["price"] = context.Price.ToPython();
            pyDict["volume"] = context.Volume.ToPython();
            pyDict["timestamp"] = context.Timestamp.ToPython();

            // Add position info
            var posDict = new PyDict();
            posDict["quantity"] = context.Position.Quantity.ToPython();
            posDict["average_price"] = context.Position.AveragePrice.ToPython();
            posDict["unrealized_pnl"] = context.Position.UnrealizedPnL.ToPython();
            posDict["has_position"] = context.Position.HasPosition.ToPython();
            posDict["is_long"] = context.Position.IsLong.ToPython();
            posDict["is_short"] = context.Position.IsShort.ToPython();
            pyDict["position"] = posDict;

            // Add account info
            var accountDict = new PyDict();
            accountDict["balance"] = context.Account.Balance.ToPython();
            accountDict["equity"] = context.Account.Equity.ToPython();
            accountDict["available"] = context.Account.Available.ToPython();
            accountDict["position_count"] = context.Account.PositionCount.ToPython();
            accountDict["total_pnl"] = context.Account.TotalPnl.ToPython();
            pyDict["account"] = accountDict;

            // Add indicator access
            var indicators = CreateIndicatorProxy(context);
            pyDict["indicators"] = indicators;

            // Add price history as list
            var priceList = new PyList();
            foreach (var tick in context.PriceHistory)
            {
                priceList.Append(tick.Price.ToPython());
            }
            pyDict["price_history"] = priceList;

            return pyDict;
        }
    }

    private PyObject CreateIndicatorProxy(StrategyContext context)
    {
        using (Py.GIL())
        {
            var indicatorDict = new PyDict();

            // Pre-calculate common indicators and add to dict
            // SMA
            for (int period = 5; period <= 200; period += 5)
            {
                var sma = context.Indicators.SMA(period);
                if (sma.HasValue)
                {
                    indicatorDict[$"sma_{period}"] = sma.Value.ToPython();
                }
            }

            // EMA
            for (int period = 5; period <= 200; period += 5)
            {
                var ema = context.Indicators.EMA(period);
                if (ema.HasValue)
                {
                    indicatorDict[$"ema_{period}"] = ema.Value.ToPython();
                }
            }

            // RSI
            var rsi = context.Indicators.RSI(14);
            if (rsi.HasValue)
            {
                indicatorDict["rsi_14"] = rsi.Value.ToPython();
            }

            // MACD
            var (macd, signal, histogram) = context.Indicators.MACD();
            if (macd.HasValue)
            {
                indicatorDict["macd"] = macd.Value.ToPython();
                indicatorDict["macd_signal"] = signal!.Value.ToPython();
                indicatorDict["macd_histogram"] = histogram!.Value.ToPython();
            }

            // Bollinger Bands
            var (upper, middle, lower) = context.Indicators.BollingerBands();
            if (upper.HasValue)
            {
                indicatorDict["bb_upper"] = upper.Value.ToPython();
                indicatorDict["bb_middle"] = middle!.Value.ToPython();
                indicatorDict["bb_lower"] = lower!.Value.ToPython();
            }

            // ATR
            var atr = context.Indicators.ATR();
            if (atr.HasValue)
            {
                indicatorDict["atr_14"] = atr.Value.ToPython();
            }

            // Stochastic
            var (k, d) = context.Indicators.Stochastic();
            if (k.HasValue)
            {
                indicatorDict["stoch_k"] = k.Value.ToPython();
                indicatorDict["stoch_d"] = d!.Value.ToPython();
            }

            return indicatorDict;
        }
    }

    private Signal ConvertToSignal(PyObject signalObj)
    {
        using (Py.GIL())
        {
            if (signalObj == null || signalObj.IsNone())
                return Signal.None;

            // Handle Signal enum from Python
            if (signalObj.HasAttr("value"))
            {
                var value = signalObj.GetAttr("value").As<int>();
                return value switch
                {
                    1 => Signal.Buy,
                    -1 => Signal.Sell,
                    _ => Signal.None
                };
            }

            // Handle integer directly
            if (PyInt.IsIntType(signalObj))
            {
                var value = signalObj.As<int>();
                return value switch
                {
                    1 => Signal.Buy,
                    -1 => Signal.Sell,
                    _ => Signal.None
                };
            }

            // Handle string
            var str = signalObj.ToString()?.ToUpperInvariant();
            return str switch
            {
                "BUY" => Signal.Buy,
                "SELL" => Signal.Sell,
                _ => Signal.None
            };
        }
    }

    private T ExecuteWithTimeout<T>(Func<T> action, int timeoutMs)
    {
        T result = default!;
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            // Thread didn't complete in time
            throw new TimeoutException($"Strategy execution exceeded {timeoutMs}ms timeout");
        }

        if (exception != null)
        {
            throw exception;
        }

        return result;
    }

    private void ExtractParameters()
    {
        using (Py.GIL())
        {
            if (_strategyInstance == null) return;

            // Look for parameters attribute
            if (_strategyInstance.HasAttr("parameters"))
            {
                var paramsObj = _strategyInstance.GetAttr("parameters");
                if (PyDict.IsDictType(paramsObj))
                {
                    var paramsDict = new PyDict(paramsObj);
                    foreach (PyObject key in paramsDict.Keys())
                    {
                        var name = key.ToString() ?? "";
                        var value = paramsDict[key];
                        _parameters[name] = ConvertPyObject(value);
                    }
                }
            }
        }
    }

    private object ConvertPyObject(PyObject obj)
    {
        using (Py.GIL())
        {
            if (obj.IsNone())
                return 0.0;

            if (PyInt.IsIntType(obj))
                return obj.As<int>();

            if (PyFloat.IsFloatType(obj))
                return obj.As<double>();

            if (PyString.IsStringType(obj))
                return obj.As<string>() ?? "";

            return obj.ToString() ?? "";
        }
    }

    public void Reset()
    {
        using (Py.GIL())
        {
            if (_strategyInstance?.HasAttr("reset") == true)
            {
                _strategyInstance.InvokeMethod("reset");
            }
        }
    }

    public ValidationResult Validate()
    {
        var errors = new List<ValidationError>();

        using (Py.GIL())
        {
            // Check for required on_tick method
            if (_strategyInstance?.HasAttr("on_tick") != true)
            {
                errors.Add(new ValidationError
                {
                    Code = "MISSING_ON_TICK",
                    Message = "Strategy must have an 'on_tick' method"
                });
            }
        }

        return errors.Count > 0
            ? new ValidationResult { IsValid = false, Errors = errors }
            : ValidationResult.Success();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            using (Py.GIL())
            {
                _strategyInstance?.Dispose();
                _scope?.Dispose();
            }
            _disposed = true;
        }
    }
}
