using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AegisQuant.UI.Strategy.Loaders;

/// <summary>
/// Factory for creating appropriate strategy loaders based on file type.
/// </summary>
public static class StrategyLoaderFactory
{
    private static readonly Lazy<IStrategyLoader[]> _loaders = new(() => new IStrategyLoader[]
    {
        new JsonStrategyLoader(),
        new PythonStrategyLoader()
    });

    /// <summary>
    /// Gets all registered strategy loaders.
    /// </summary>
    public static IReadOnlyList<IStrategyLoader> Loaders => _loaders.Value;

    /// <summary>
    /// Gets the appropriate loader for a file.
    /// </summary>
    /// <param name="filePath">Path to the strategy file</param>
    /// <returns>Strategy loader that can handle the file</returns>
    /// <exception cref="NotSupportedException">Thrown if no loader supports the file type</exception>
    public static IStrategyLoader GetLoader(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        var loader = _loaders.Value.FirstOrDefault(l => l.CanLoad(filePath));
        
        if (loader == null)
        {
            var ext = Path.GetExtension(filePath);
            throw new NotSupportedException($"No strategy loader found for file type '{ext}'");
        }

        return loader;
    }

    /// <summary>
    /// Gets all supported file extensions.
    /// </summary>
    public static string[] GetSupportedExtensions()
    {
        return _loaders.Value
            .SelectMany(l => l.SupportedExtensions)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Gets a file filter string for open file dialogs.
    /// </summary>
    public static string GetFileFilter()
    {
        var filters = new List<string>();
        
        foreach (var loader in _loaders.Value)
        {
            var exts = string.Join(";", loader.SupportedExtensions.Select(e => $"*{e}"));
            var name = loader.GetType().Name.Replace("StrategyLoader", " Strategy");
            filters.Add($"{name} ({exts})|{exts}");
        }

        var allExts = string.Join(";", GetSupportedExtensions().Select(e => $"*{e}"));
        filters.Insert(0, $"All Strategy Files ({allExts})|{allExts}");
        filters.Add("All Files (*.*)|*.*");

        return string.Join("|", filters);
    }

    /// <summary>
    /// Loads a strategy from a file using the appropriate loader.
    /// </summary>
    /// <param name="filePath">Path to the strategy file</param>
    /// <returns>Loaded strategy instance</returns>
    public static IStrategy LoadStrategy(string filePath)
    {
        var loader = GetLoader(filePath);
        return loader.LoadFromFile(filePath);
    }
}
