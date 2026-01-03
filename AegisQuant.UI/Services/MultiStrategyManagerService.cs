using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Strategy.Loaders;
using AegisQuant.UI.Strategy.Models;
using Python.Runtime;

namespace AegisQuant.UI.Services;

/// <summary>
/// Event args for multi-strategy signal aggregation.
/// </summary>
public class AggregatedSignalEventArgs : EventArgs
{
    public Dictionary<string, Signal> Signals { get; }
    public Signal AggregatedSignal { get; }
    public string? WinningStrategyId { get; }

    public AggregatedSignalEventArgs(Dictionary<string, Signal> signals, Signal aggregatedSignal, string? winningStrategyId)
    {
        Signals = signals;
        AggregatedSignal = aggregatedSignal;
        WinningStrategyId = winningStrategyId;
    }
}

/// <summary>
/// Signal aggregation mode for multi-strategy execution.
/// </summary>
public enum SignalAggregationMode
{
    /// <summary>Use the first non-None signal.</summary>
    FirstSignal,
    /// <summary>Use majority voting (most common signal).</summary>
    MajorityVote,
    /// <summary>All strategies must agree.</summary>
    Unanimous,
    /// <summary>Use weighted voting based on strategy performance.</summary>
    WeightedVote,
    /// <summary>Execute all signals independently.</summary>
    Independent
}

/// <summary>
/// Service for managing multiple trading strategies simultaneously.
/// </summary>
public class MultiStrategyManagerService : IDisposable
{
    private readonly ObservableCollection<ManagedStrategy> _strategies;
    private readonly JsonStrategyLoader _jsonLoader;
    private readonly PythonStrategyLoader _pythonLoader;
    private readonly List<StrategyInfo> _recentStrategies;
    private bool _disposed;
    private bool _isRunning;

    private const int MaxRecentStrategies = 20;
    private const int MaxStrategies = 10;

    public MultiStrategyManagerService()
    {
        _strategies = new ObservableCollection<ManagedStrategy>();
        _recentStrategies = new List<StrategyInfo>();
        _jsonLoader = new JsonStrategyLoader();
        _pythonLoader = new PythonStrategyLoader();
        AggregationMode = SignalAggregationMode.FirstSignal;
    }

    /// <summary>
    /// Gets the collection of managed strategies.
    /// </summary>
    public ObservableCollection<ManagedStrategy> Strategies => _strategies;

    /// <summary>
    /// Gets the list of recently used strategies.
    /// </summary>
    public IReadOnlyList<StrategyInfo> RecentStrategies => _recentStrategies;

    /// <summary>
    /// Gets or sets the signal aggregation mode.
    /// </summary>
    public SignalAggregationMode AggregationMode { get; set; }

    /// <summary>
    /// Gets whether any strategy is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets the count of enabled strategies.
    /// </summary>
    public int EnabledCount => _strategies.Count(s => s.IsEnabled);

    /// <summary>
    /// Event raised when a strategy is added.
    /// </summary>
    public event EventHandler<ManagedStrategy>? StrategyAdded;

    /// <summary>
    /// Event raised when a strategy is removed.
    /// </summary>
    public event EventHandler<ManagedStrategy>? StrategyRemoved;

    /// <summary>
    /// Event raised when signals are aggregated.
    /// </summary>
    public event EventHandler<AggregatedSignalEventArgs>? SignalsAggregated;

    /// <summary>
    /// Event raised when a strategy error occurs.
    /// </summary>
    public event EventHandler<StrategyErrorEventArgs>? StrategyError;

    /// <summary>
    /// Loads and adds a strategy from a file.
    /// </summary>
    public async Task<ManagedStrategy> AddStrategyFromFileAsync(string filePath)
    {
        if (_strategies.Count >= MaxStrategies)
            throw new InvalidOperationException($"Maximum of {MaxStrategies} strategies allowed");

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Strategy file not found: {filePath}");

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        IStrategy strategy;
        StrategyInfo info;

        try
        {
            strategy = extension switch
            {
                ".json" => await LoadJsonStrategyAsync(filePath),
                ".py" => await LoadPythonStrategyAsync(filePath),
                _ => throw new NotSupportedException($"Unsupported file type: {extension}")
            };

            info = new StrategyInfo
            {
                Name = strategy.Name,
                Description = strategy.Description,
                FilePath = filePath,
                Type = strategy.Type,
                LastUsed = DateTime.Now,
                Parameters = strategy.Parameters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new ParameterInfo { Name = kvp.Key, CurrentValue = kvp.Value })
            };
        }
        catch (Exception ex)
        {
            RaiseError($"Failed to load strategy: {ex.Message}", ex);
            throw;
        }

        var managed = new ManagedStrategy(strategy, info);
        _strategies.Add(managed);
        AddToRecentStrategies(info);
        StrategyAdded?.Invoke(this, managed);

        return managed;
    }

    /// <summary>
    /// Adds a strategy from JSON content.
    /// </summary>
    public async Task<ManagedStrategy> AddStrategyFromJsonAsync(string json, string? name = null)
    {
        if (_strategies.Count >= MaxStrategies)
            throw new InvalidOperationException($"Maximum of {MaxStrategies} strategies allowed");

        try
        {
            var strategy = await Task.Run(() => _jsonLoader.LoadFromJson(json));
            var info = new StrategyInfo
            {
                Name = name ?? strategy.Name,
                Description = strategy.Description,
                FilePath = "",
                Type = strategy.Type,
                LastUsed = DateTime.Now,
                Parameters = strategy.Parameters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new ParameterInfo { Name = kvp.Key, CurrentValue = kvp.Value })
            };

            var managed = new ManagedStrategy(strategy, info);
            _strategies.Add(managed);
            StrategyAdded?.Invoke(this, managed);
            return managed;
        }
        catch (Exception ex)
        {
            RaiseError($"Failed to load JSON strategy: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// Adds a strategy from Python code.
    /// </summary>
    public async Task<ManagedStrategy> AddStrategyFromPythonAsync(string pythonCode, string? name = null)
    {
        if (_strategies.Count >= MaxStrategies)
            throw new InvalidOperationException($"Maximum of {MaxStrategies} strategies allowed");

        try
        {
            var strategy = await Task.Run(() => _pythonLoader.LoadFromCode(pythonCode));
            var info = new StrategyInfo
            {
                Name = name ?? strategy.Name,
                Description = strategy.Description,
                FilePath = "",
                Type = strategy.Type,
                LastUsed = DateTime.Now,
                Parameters = strategy.Parameters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new ParameterInfo { Name = kvp.Key, CurrentValue = kvp.Value })
            };

            var managed = new ManagedStrategy(strategy, info);
            _strategies.Add(managed);
            StrategyAdded?.Invoke(this, managed);
            return managed;
        }
        catch (PythonException ex)
        {
            RaiseError($"Python error: {ex.Message}", ex);
            throw new StrategyLoadException($"Python error: {ex.Message}");
        }
        catch (Exception ex)
        {
            RaiseError($"Failed to load Python strategy: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// Removes a strategy by ID.
    /// </summary>
    public bool RemoveStrategy(string strategyId)
    {
        var strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
        if (strategy == null) return false;

        _strategies.Remove(strategy);
        strategy.Dispose();
        StrategyRemoved?.Invoke(this, strategy);
        return true;
    }

    /// <summary>
    /// Removes a managed strategy.
    /// </summary>
    public bool RemoveStrategy(ManagedStrategy strategy)
    {
        if (!_strategies.Contains(strategy)) return false;

        _strategies.Remove(strategy);
        strategy.Dispose();
        StrategyRemoved?.Invoke(this, strategy);
        return true;
    }

    /// <summary>
    /// Enables or disables a strategy.
    /// </summary>
    public void SetStrategyEnabled(string strategyId, bool enabled)
    {
        var strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
        if (strategy != null)
        {
            strategy.IsEnabled = enabled;
        }
    }

    /// <summary>
    /// Starts all enabled strategies.
    /// </summary>
    public void StartAll()
    {
        _isRunning = true;
        foreach (var strategy in _strategies.Where(s => s.IsEnabled))
        {
            strategy.IsRunning = true;
        }
    }

    /// <summary>
    /// Stops all strategies.
    /// </summary>
    public void StopAll()
    {
        _isRunning = false;
        foreach (var strategy in _strategies)
        {
            strategy.IsRunning = false;
        }
    }

    /// <summary>
    /// Processes a tick through all enabled strategies and aggregates signals.
    /// </summary>
    public Signal ProcessTick(StrategyContext context)
    {
        if (!_isRunning) return Signal.None;

        var signals = new Dictionary<string, Signal>();
        
        foreach (var strategy in _strategies.Where(s => s.IsEnabled && s.IsRunning))
        {
            var signal = strategy.ProcessTick(context);
            signals[strategy.Id] = signal;
        }

        var aggregated = AggregateSignals(signals, out var winningId);
        SignalsAggregated?.Invoke(this, new AggregatedSignalEventArgs(signals, aggregated, winningId));
        
        return aggregated;
    }

    /// <summary>
    /// Aggregates signals from multiple strategies based on the aggregation mode.
    /// </summary>
    private Signal AggregateSignals(Dictionary<string, Signal> signals, out string? winningStrategyId)
    {
        winningStrategyId = null;
        var nonNoneSignals = signals.Where(kvp => kvp.Value != Signal.None).ToList();

        if (nonNoneSignals.Count == 0)
            return Signal.None;

        switch (AggregationMode)
        {
            case SignalAggregationMode.FirstSignal:
                var first = nonNoneSignals.First();
                winningStrategyId = first.Key;
                return first.Value;

            case SignalAggregationMode.MajorityVote:
                var grouped = nonNoneSignals.GroupBy(kvp => kvp.Value)
                    .OrderByDescending(g => g.Count())
                    .First();
                winningStrategyId = grouped.First().Key;
                return grouped.Key;

            case SignalAggregationMode.Unanimous:
                var distinctSignals = nonNoneSignals.Select(kvp => kvp.Value).Distinct().ToList();
                if (distinctSignals.Count == 1)
                {
                    winningStrategyId = nonNoneSignals.First().Key;
                    return distinctSignals[0];
                }
                return Signal.None;

            case SignalAggregationMode.WeightedVote:
                // Weight by PnL - strategies with better performance get more weight
                var weighted = nonNoneSignals
                    .Select(kvp => new { 
                        Signal = kvp.Value, 
                        Weight = Math.Max(1, _strategies.First(s => s.Id == kvp.Key).PnL + 100) 
                    })
                    .GroupBy(x => x.Signal)
                    .Select(g => new { Signal = g.Key, TotalWeight = g.Sum(x => x.Weight) })
                    .OrderByDescending(x => x.TotalWeight)
                    .First();
                return weighted.Signal;

            case SignalAggregationMode.Independent:
                // Return first signal but all are processed independently
                var independent = nonNoneSignals.First();
                winningStrategyId = independent.Key;
                return independent.Value;

            default:
                return Signal.None;
        }
    }

    /// <summary>
    /// Gets a strategy by ID.
    /// </summary>
    public ManagedStrategy? GetStrategy(string strategyId)
    {
        return _strategies.FirstOrDefault(s => s.Id == strategyId);
    }

    /// <summary>
    /// Clears all strategies.
    /// </summary>
    public void ClearAll()
    {
        StopAll();
        foreach (var strategy in _strategies.ToList())
        {
            strategy.Dispose();
        }
        _strategies.Clear();
    }

    /// <summary>
    /// Resets statistics for all strategies.
    /// </summary>
    public void ResetAllStats()
    {
        foreach (var strategy in _strategies)
        {
            strategy.ResetStats();
        }
    }

    private async Task<IStrategy> LoadJsonStrategyAsync(string filePath)
    {
        return await Task.Run(() => _jsonLoader.LoadFromFile(filePath));
    }

    private async Task<IStrategy> LoadPythonStrategyAsync(string filePath)
    {
        return await Task.Run(() => _pythonLoader.LoadFromFile(filePath));
    }

    private void AddToRecentStrategies(StrategyInfo info)
    {
        _recentStrategies.RemoveAll(s => s.FilePath == info.FilePath);
        _recentStrategies.Insert(0, info);
        while (_recentStrategies.Count > MaxRecentStrategies)
        {
            _recentStrategies.RemoveAt(_recentStrategies.Count - 1);
        }
    }

    private void RaiseError(string message, Exception? ex = null, int? lineNumber = null)
    {
        StrategyError?.Invoke(this, new StrategyErrorEventArgs(message, ex, lineNumber));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ClearAll();
            _disposed = true;
        }
    }
}
