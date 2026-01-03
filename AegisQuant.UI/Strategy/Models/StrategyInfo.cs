using System;
using System.Collections.Generic;

namespace AegisQuant.UI.Strategy.Models;

/// <summary>
/// Information about a loaded strategy.
/// </summary>
public record StrategyInfo
{
    /// <summary>Strategy name</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Strategy description</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>File path (if loaded from file)</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Strategy type</summary>
    public StrategyType Type { get; init; }

    /// <summary>Last time this strategy was used</summary>
    public DateTime LastUsed { get; init; } = DateTime.Now;

    /// <summary>Strategy parameters</summary>
    public Dictionary<string, ParameterInfo> Parameters { get; init; } = new();

    /// <summary>Strategy version</summary>
    public string Version { get; init; } = "1.0";
}

/// <summary>
/// Information about a strategy parameter.
/// </summary>
public record ParameterInfo
{
    /// <summary>Parameter name</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Display name for UI</summary>
    public string? DisplayName { get; init; }

    /// <summary>Parameter type ("int", "double", "bool", "string")</summary>
    public string Type { get; init; } = "double";

    /// <summary>Default value</summary>
    public object DefaultValue { get; init; } = 0.0;

    /// <summary>Current value</summary>
    public object? CurrentValue { get; set; }

    /// <summary>Minimum value (for numeric types)</summary>
    public object? MinValue { get; init; }

    /// <summary>Maximum value (for numeric types)</summary>
    public object? MaxValue { get; init; }

    /// <summary>Parameter description</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the effective value (current or default).
    /// </summary>
    public object EffectiveValue => CurrentValue ?? DefaultValue;
}
