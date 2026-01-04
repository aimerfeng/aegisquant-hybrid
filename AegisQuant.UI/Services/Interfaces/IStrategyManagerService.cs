using AegisQuant.UI.Strategy;
using AegisQuant.UI.Strategy.Models;

namespace AegisQuant.UI.Services.Interfaces;

/// <summary>
/// Interface for strategy management service.
/// </summary>
public interface IStrategyManagerService : IDisposable
{
    /// <summary>
    /// Gets the currently loaded strategy.
    /// </summary>
    IStrategy? CurrentStrategy { get; }
    
    /// <summary>
    /// Gets the list of recently used strategies.
    /// </summary>
    IReadOnlyList<StrategyInfo> RecentStrategies { get; }
    
    /// <summary>
    /// Gets whether an external strategy is currently loaded.
    /// </summary>
    bool HasExternalStrategy { get; }
    
    /// <summary>
    /// Event raised when a strategy is loaded.
    /// </summary>
    event EventHandler<StrategyLoadedEventArgs>? StrategyLoaded;
    
    /// <summary>
    /// Event raised when a strategy error occurs.
    /// </summary>
    event EventHandler<StrategyErrorEventArgs>? StrategyError;
    
    /// <summary>
    /// Loads a strategy from a file.
    /// </summary>
    Task<IStrategy> LoadFromFileAsync(string filePath);
    
    /// <summary>
    /// Loads a strategy from JSON content.
    /// </summary>
    Task<IStrategy> LoadFromJsonAsync(string json);
    
    /// <summary>
    /// Loads a strategy from Python code.
    /// </summary>
    Task<IStrategy> LoadFromPythonAsync(string pythonCode);
    
    /// <summary>
    /// Unloads the current strategy.
    /// </summary>
    void UnloadStrategy();
    
    /// <summary>
    /// Sets a strategy directly.
    /// </summary>
    void SetStrategy(IStrategy strategy);
    
    /// <summary>
    /// Processes a tick with the current strategy.
    /// </summary>
    Signal ProcessTick(StrategyContext context);
    
    /// <summary>
    /// Gets strategy info for a file without loading it.
    /// </summary>
    Task<StrategyInfo?> GetStrategyInfoAsync(string filePath);
}
