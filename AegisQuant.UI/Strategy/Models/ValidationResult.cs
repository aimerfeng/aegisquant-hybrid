using System.Collections.Generic;
using System.Linq;

namespace AegisQuant.UI.Strategy.Models;

/// <summary>
/// Result of strategy validation.
/// </summary>
public record ValidationResult
{
    /// <summary>Whether the strategy is valid</summary>
    public bool IsValid { get; init; }

    /// <summary>List of validation errors</summary>
    public List<ValidationError> Errors { get; init; } = new();

    /// <summary>List of validation warnings (non-fatal)</summary>
    public List<ValidationError> Warnings { get; init; } = new();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    public static ValidationResult Failure(params ValidationError[] errors) => new()
    {
        IsValid = false,
        Errors = errors.ToList()
    };

    /// <summary>
    /// Creates a failed validation result with a single error message.
    /// </summary>
    public static ValidationResult Failure(string code, string message, int? lineNumber = null) => new()
    {
        IsValid = false,
        Errors = new List<ValidationError>
        {
            new() { Code = code, Message = message, LineNumber = lineNumber }
        }
    };
}

/// <summary>
/// A validation error or warning.
/// </summary>
public record ValidationError
{
    /// <summary>Error code for programmatic handling</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Human-readable error message</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Line number where error occurred (if applicable)</summary>
    public int? LineNumber { get; init; }

    /// <summary>Column number where error occurred (if applicable)</summary>
    public int? ColumnNumber { get; init; }

    /// <summary>Source file path (if applicable)</summary>
    public string? FilePath { get; init; }

    public override string ToString()
    {
        var location = LineNumber.HasValue ? $" (line {LineNumber})" : "";
        return $"[{Code}]{location}: {Message}";
    }
}
