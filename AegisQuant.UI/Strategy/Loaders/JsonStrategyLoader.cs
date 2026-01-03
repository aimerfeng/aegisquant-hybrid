using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AegisQuant.UI.Strategy.Models;

namespace AegisQuant.UI.Strategy.Loaders;

/// <summary>
/// JSON strategy configuration model.
/// </summary>
public class JsonStrategyConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("parameters")]
    public Dictionary<string, JsonParameterConfig> Parameters { get; set; } = new();

    [JsonPropertyName("indicators")]
    public List<JsonIndicatorConfig> Indicators { get; set; } = new();

    [JsonPropertyName("rules")]
    public JsonRulesConfig Rules { get; set; } = new();
}

public class JsonParameterConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "double";

    [JsonPropertyName("default")]
    public JsonElement Default { get; set; }

    [JsonPropertyName("min")]
    public JsonElement? Min { get; set; }

    [JsonPropertyName("max")]
    public JsonElement? Max { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class JsonIndicatorConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("period")]
    public JsonElement? Period { get; set; }

    [JsonPropertyName("fastPeriod")]
    public JsonElement? FastPeriod { get; set; }

    [JsonPropertyName("slowPeriod")]
    public JsonElement? SlowPeriod { get; set; }

    [JsonPropertyName("signalPeriod")]
    public JsonElement? SignalPeriod { get; set; }

    [JsonPropertyName("stdDev")]
    public JsonElement? StdDev { get; set; }
}

public class JsonRulesConfig
{
    [JsonPropertyName("buy")]
    public JsonRuleConfig? Buy { get; set; }

    [JsonPropertyName("sell")]
    public JsonRuleConfig? Sell { get; set; }
}

public class JsonRuleConfig
{
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;
}

/// <summary>
/// Loads strategies from JSON configuration files.
/// </summary>
public class JsonStrategyLoader : IStrategyLoader
{
    private static readonly HashSet<string> SupportedIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        "SMA", "EMA", "RSI", "MACD", "BollingerBands", "BB", "ATR", "Stochastic", "STOCH"
    };

    /// <inheritdoc />
    public string[] SupportedExtensions => new[] { ".json" };

    /// <inheritdoc />
    public bool CanLoad(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext == ".json";
    }

    /// <inheritdoc />
    public IStrategy LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Strategy file not found: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        return LoadFromJson(json, filePath);
    }

    /// <summary>
    /// Loads a strategy from a JSON string.
    /// </summary>
    /// <param name="json">JSON content</param>
    /// <param name="sourcePath">Optional source path for error reporting</param>
    /// <returns>Loaded strategy</returns>
    public IStrategy LoadFromJson(string json, string? sourcePath = null)
    {
        JsonStrategyConfig config;
        try
        {
            config = JsonSerializer.Deserialize<JsonStrategyConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            }) ?? throw new InvalidOperationException("Failed to parse JSON");
        }
        catch (JsonException ex)
        {
            var lineNumber = GetLineNumber(json, ex.BytePositionInLine ?? 0);
            throw new StrategyLoadException($"JSON syntax error at line {lineNumber}: {ex.Message}", lineNumber);
        }

        // Validate configuration
        var validation = ValidateConfig(config);
        if (!validation.IsValid)
        {
            throw new StrategyLoadException(
                $"Strategy validation failed: {string.Join("; ", validation.Errors)}",
                validation.Errors[0].LineNumber);
        }

        return new JsonConfigStrategy(config, sourcePath);
    }

    /// <summary>
    /// Validates a strategy configuration.
    /// </summary>
    public ValidationResult ValidateConfig(JsonStrategyConfig config)
    {
        var errors = new List<ValidationError>();

        // Check required fields
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            errors.Add(new ValidationError
            {
                Code = "MISSING_NAME",
                Message = "Strategy name is required"
            });
        }

        // Validate indicators
        var definedIndicators = new HashSet<string>();
        foreach (var indicator in config.Indicators)
        {
            if (string.IsNullOrWhiteSpace(indicator.Name))
            {
                errors.Add(new ValidationError
                {
                    Code = "MISSING_INDICATOR_NAME",
                    Message = "Indicator name is required"
                });
                continue;
            }

            if (!SupportedIndicators.Contains(indicator.Type))
            {
                errors.Add(new ValidationError
                {
                    Code = "UNSUPPORTED_INDICATOR",
                    Message = $"Unsupported indicator type: {indicator.Type}. Supported: {string.Join(", ", SupportedIndicators)}"
                });
            }

            definedIndicators.Add(indicator.Name);
        }

        // Add parameter names to available variables
        var availableVars = new HashSet<string>(definedIndicators);
        foreach (var param in config.Parameters.Keys)
        {
            availableVars.Add(param);
        }
        availableVars.Add("price");
        availableVars.Add("volume");

        // Validate buy condition
        if (config.Rules.Buy != null && !string.IsNullOrWhiteSpace(config.Rules.Buy.Condition))
        {
            var conditionErrors = ConditionParser.ValidateSyntax(config.Rules.Buy.Condition, availableVars);
            foreach (var err in conditionErrors)
            {
                errors.Add(new ValidationError
                {
                    Code = "INVALID_BUY_CONDITION",
                    Message = $"Buy condition error: {err}"
                });
            }
        }

        // Validate sell condition
        if (config.Rules.Sell != null && !string.IsNullOrWhiteSpace(config.Rules.Sell.Condition))
        {
            var conditionErrors = ConditionParser.ValidateSyntax(config.Rules.Sell.Condition, availableVars);
            foreach (var err in conditionErrors)
            {
                errors.Add(new ValidationError
                {
                    Code = "INVALID_SELL_CONDITION",
                    Message = $"Sell condition error: {err}"
                });
            }
        }

        return errors.Count > 0
            ? new ValidationResult { IsValid = false, Errors = errors }
            : ValidationResult.Success();
    }

    /// <inheritdoc />
    public ValidationResult Validate(string content)
    {
        try
        {
            var config = JsonSerializer.Deserialize<JsonStrategyConfig>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });

            if (config == null)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Errors = new List<ValidationError>
                    {
                        new() { Code = "PARSE_ERROR", Message = "Failed to parse JSON" }
                    }
                };
            }

            return ValidateConfig(config);
        }
        catch (JsonException ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = new List<ValidationError>
                {
                    new() { Code = "JSON_SYNTAX", Message = ex.Message }
                }
            };
        }
    }

    /// <inheritdoc />
    public async Task<StrategyInfo?> GetStrategyInfoAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<JsonStrategyConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });

            if (config == null) return null;

            return new StrategyInfo
            {
                Name = config.Name,
                Description = config.Description,
                Type = StrategyType.JsonConfig,
                Version = config.Version,
                FilePath = filePath
            };
        }
        catch
        {
            return null;
        }
    }

    private static int GetLineNumber(string json, long bytePosition)
    {
        int line = 1;
        for (int i = 0; i < Math.Min(bytePosition, json.Length); i++)
        {
            if (json[i] == '\n') line++;
        }
        return line;
    }
}

/// <summary>
/// Exception thrown when strategy loading fails.
/// </summary>
public class StrategyLoadException : Exception
{
    public int? LineNumber { get; }

    public StrategyLoadException(string message, int? lineNumber = null)
        : base(message)
    {
        LineNumber = lineNumber;
    }
}
