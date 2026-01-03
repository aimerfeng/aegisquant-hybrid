using System.Threading.Tasks;
using AegisQuant.UI.Strategy.Models;

namespace AegisQuant.UI.Strategy.Loaders;

/// <summary>
/// Common interface for all strategy loaders.
/// </summary>
public interface IStrategyLoader
{
    /// <summary>
    /// Gets the supported file extensions for this loader.
    /// </summary>
    string[] SupportedExtensions { get; }

    /// <summary>
    /// Loads a strategy from a file.
    /// </summary>
    /// <param name="filePath">Path to the strategy file</param>
    /// <returns>Loaded strategy instance</returns>
    /// <exception cref="StrategyLoadException">Thrown if loading fails</exception>
    IStrategy LoadFromFile(string filePath);

    /// <summary>
    /// Gets strategy information from a file without fully loading it.
    /// </summary>
    /// <param name="filePath">Path to the strategy file</param>
    /// <returns>Strategy info, or null if file is invalid</returns>
    Task<StrategyInfo?> GetStrategyInfoAsync(string filePath);

    /// <summary>
    /// Validates strategy content.
    /// </summary>
    /// <param name="content">Strategy content (JSON, Python code, etc.)</param>
    /// <returns>Validation result with any errors</returns>
    ValidationResult Validate(string content);

    /// <summary>
    /// Checks if this loader can handle the given file.
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <returns>True if this loader can handle the file</returns>
    bool CanLoad(string filePath);
}
