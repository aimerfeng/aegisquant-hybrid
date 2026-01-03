using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AegisQuant.UI.Strategy;
using AegisQuant.UI.Strategy.Loaders;
using AegisQuant.UI.Strategy.Models;
using Python.Runtime;

namespace AegisQuant.UI.Services;

/// <summary>
/// Event args for strategy loaded event.
/// </summary>
public class StrategyLoadedEventArgs : EventArgs
{
    public IStrategy Strategy { get; }
    public StrategyInfo Info { get; }

    public StrategyLoadedEventArgs(IStrategy strategy, StrategyInfo info)
    {
        Strategy = strategy;
        Info = info;
    }
}

/// <summary>
/// Event args for strategy error event.
/// </summary>
public class StrategyErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }
    public int? LineNumber { get; }

    public StrategyErrorEventArgs(string message, Exception? exception = null, int? lineNumber = null)
    {
        Message = message;
        Exception = exception;
        LineNumber = lineNumber;
    }
}

/// <summary>
/// Service for managing trading strategies.
/// </summary>
public class StrategyManagerService : IDisposable
{
    private IStrategy? _currentStrategy;
    private readonly List<StrategyInfo> _recentStrategies;
    private readonly JsonStrategyLoader _jsonLoader;
    private readonly PythonStrategyLoader _pythonLoader;
    private bool _disposed;

    private const int MaxRecentStrategies = 10;

    public StrategyManagerService()
    {
        _recentStrategies = new List<StrategyInfo>();
        _jsonLoader = new JsonStrategyLoader();
        _pythonLoader = new PythonStrategyLoader();
    }

    /// <summary>
    /// Gets the currently loaded strategy.
    /// </summary>
    public IStrategy? CurrentStrategy => _currentStrategy;

    /// <summary>
    /// Gets the list of recently used strategies.
    /// </summary>
    public IReadOnlyList<StrategyInfo> RecentStrategies => _recentStrategies;

    /// <summary>
    /// Event raised when a strategy is loaded.
    /// </summary>
    public event EventHandler<StrategyLoadedEventArgs>? StrategyLoaded;

    /// <summary>
    /// Event raised when a strategy error occurs.
    /// </summary>
    public event EventHandler<StrategyErrorEventArgs>? StrategyError;

    /// <summary>
    /// Loads a strategy from a file.
    /// </summary>
    /// <param name="filePath">Path to the strategy file (.json, .yaml, or .py)</param>
    /// <returns>Loaded strategy</returns>
    public async Task<IStrategy> LoadFromFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Strategy file not found: {filePath}");

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".json" => await LoadJsonStrategyAsync(filePath),
            ".yaml" or ".yml" => await LoadYamlStrategyAsync(filePath),
            ".py" => await LoadPythonStrategyAsync(filePath),
            _ => throw new NotSupportedException($"Unsupported file type: {extension}")
        };
    }

    /// <summary>
    /// Loads a strategy from JSON content.
    /// </summary>
    public async Task<IStrategy> LoadFromJsonAsync(string json)
    {
        return await Task.Run(() =>
        {
            try
            {
                var strategy = _jsonLoader.LoadFromJson(json);
                SetCurrentStrategy(strategy, null);
                return strategy;
            }
            catch (StrategyLoadException ex)
            {
                RaiseError(ex.Message, ex, ex.LineNumber);
                throw;
            }
            catch (Exception ex)
            {
                RaiseError($"Failed to load JSON strategy: {ex.Message}", ex);
                throw;
            }
        });
    }

    /// <summary>
    /// Loads a strategy from Python code.
    /// </summary>
    public async Task<IStrategy> LoadFromPythonAsync(string pythonCode)
    {
        return await Task.Run(() =>
        {
            try
            {
                var strategy = _pythonLoader.LoadFromCode(pythonCode);
                SetCurrentStrategy(strategy, null);
                return strategy;
            }
            catch (StrategyLoadException ex)
            {
                RaiseError(ex.Message, ex, ex.LineNumber);
                throw;
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
        });
    }

    /// <summary>
    /// Unloads the current strategy.
    /// </summary>
    public void UnloadStrategy()
    {
        if (_currentStrategy != null)
        {
            _currentStrategy.Dispose();
            _currentStrategy = null;
        }
    }

    /// <summary>
    /// Sets a strategy directly (without loading from file).
    /// </summary>
    /// <param name="strategy">The strategy to set</param>
    public void SetStrategy(IStrategy strategy)
    {
        // Dispose previous strategy
        _currentStrategy?.Dispose();
        _currentStrategy = strategy;

        // Create strategy info
        var info = new StrategyInfo
        {
            Name = strategy.Name,
            Description = strategy.Description,
            FilePath = "",
            Type = strategy.Type,
            LastUsed = DateTime.Now,
            Parameters = strategy.Parameters.ToDictionary(
                kvp => kvp.Key,
                kvp => new ParameterInfo
                {
                    Name = kvp.Key,
                    CurrentValue = kvp.Value
                })
        };

        // Raise event
        StrategyLoaded?.Invoke(this, new StrategyLoadedEventArgs(strategy, info));
    }

    /// <summary>
    /// Processes a tick with the current strategy.
    /// </summary>
    /// <param name="context">Strategy context</param>
    /// <returns>Trading signal</returns>
    public Signal ProcessTick(StrategyContext context)
    {
        if (_currentStrategy == null)
            return Signal.None;

        try
        {
            return _currentStrategy.OnTick(context);
        }
        catch (Exception ex)
        {
            RaiseError($"Strategy execution error: {ex.Message}", ex);
            return Signal.None;
        }
    }

    /// <summary>
    /// Gets strategy info for a file without loading it.
    /// </summary>
    public async Task<StrategyInfo?> GetStrategyInfoAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".json" => await GetJsonStrategyInfoAsync(filePath),
            ".py" => await GetPythonStrategyInfoAsync(filePath),
            _ => null
        };
    }

    private async Task<IStrategy> LoadJsonStrategyAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var strategy = _jsonLoader.LoadFromFile(filePath);
                SetCurrentStrategy(strategy, filePath);
                return strategy;
            }
            catch (StrategyLoadException ex)
            {
                RaiseError(ex.Message, ex, ex.LineNumber);
                throw;
            }
            catch (Exception ex)
            {
                RaiseError($"Failed to load strategy: {ex.Message}", ex);
                throw;
            }
        });
    }

    private async Task<IStrategy> LoadYamlStrategyAsync(string filePath)
    {
        // YAML support can be added later using YamlDotNet
        await Task.CompletedTask;
        throw new NotImplementedException("YAML strategy support is not yet implemented");
    }

    private async Task<IStrategy> LoadPythonStrategyAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var strategy = _pythonLoader.LoadFromFile(filePath);
                SetCurrentStrategy(strategy, filePath);
                return strategy;
            }
            catch (StrategyLoadException ex)
            {
                RaiseError(ex.Message, ex, ex.LineNumber);
                throw;
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
        });
    }

    private async Task<StrategyInfo?> GetJsonStrategyInfoAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new StrategyInfo
                {
                    Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Description = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                    FilePath = filePath,
                    Type = StrategyType.JsonConfig,
                    Version = root.TryGetProperty("version", out var ver) ? ver.GetString() ?? "1.0" : "1.0"
                };
            }
            catch
            {
                return null;
            }
        });
    }

    private async Task<StrategyInfo?> GetPythonStrategyInfoAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var code = File.ReadAllText(filePath);
                var name = Path.GetFileNameWithoutExtension(filePath);

                // Try to extract name and description from docstring or class definition
                // This is a simple implementation - can be enhanced later

                return new StrategyInfo
                {
                    Name = name,
                    Description = "Python strategy",
                    FilePath = filePath,
                    Type = StrategyType.PythonScript
                };
            }
            catch
            {
                return null;
            }
        });
    }

    private void SetCurrentStrategy(IStrategy strategy, string? filePath)
    {
        // Dispose previous strategy
        _currentStrategy?.Dispose();
        _currentStrategy = strategy;

        // Create strategy info
        var info = new StrategyInfo
        {
            Name = strategy.Name,
            Description = strategy.Description,
            FilePath = filePath ?? "",
            Type = strategy.Type,
            LastUsed = DateTime.Now,
            Parameters = strategy.Parameters.ToDictionary(
                kvp => kvp.Key,
                kvp => new ParameterInfo
                {
                    Name = kvp.Key,
                    CurrentValue = kvp.Value
                })
        };

        // Update recent strategies
        AddToRecentStrategies(info);

        // Raise event
        StrategyLoaded?.Invoke(this, new StrategyLoadedEventArgs(strategy, info));
    }

    private void AddToRecentStrategies(StrategyInfo info)
    {
        // Remove existing entry for same file
        _recentStrategies.RemoveAll(s => s.FilePath == info.FilePath);

        // Add to front
        _recentStrategies.Insert(0, info);

        // Trim to max size
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
            UnloadStrategy();
            _disposed = true;
        }
    }
}
