using System.Collections.Generic;

namespace AegisQuant.UI.Models;

/// <summary>
/// Configuration for data import from CSV files.
/// Contains column mappings and date format settings.
/// </summary>
public class DataImportConfig
{
    /// <summary>
    /// Path to the CSV file to import.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Name of the column containing timestamp data.
    /// </summary>
    public string TimeColumnName { get; set; } = "timestamp";

    /// <summary>
    /// Name of the column containing open price data.
    /// </summary>
    public string OpenColumnName { get; set; } = "open";

    /// <summary>
    /// Name of the column containing high price data.
    /// </summary>
    public string HighColumnName { get; set; } = "high";

    /// <summary>
    /// Name of the column containing low price data.
    /// </summary>
    public string LowColumnName { get; set; } = "low";

    /// <summary>
    /// Name of the column containing close/price data.
    /// </summary>
    public string CloseColumnName { get; set; } = "close";

    /// <summary>
    /// Name of the column containing volume data.
    /// </summary>
    public string VolumeColumnName { get; set; } = "volume";

    /// <summary>
    /// Date format string for parsing timestamps.
    /// Special values: "unix" for Unix timestamps, "auto" for auto-detection.
    /// </summary>
    public string DateFormat { get; set; } = "auto";

    /// <summary>
    /// Whether to skip the first row (header row).
    /// </summary>
    public bool SkipFirstRow { get; set; } = true;

    /// <summary>
    /// List of columns to ignore during import.
    /// </summary>
    public List<string> IgnoredColumns { get; set; } = new();
}

/// <summary>
/// Represents a column mapping option for the import wizard.
/// </summary>
public enum ColumnMappingType
{
    /// <summary>Ignore this column.</summary>
    Ignore,
    /// <summary>Timestamp/DateTime column.</summary>
    Timestamp,
    /// <summary>Open price column.</summary>
    Open,
    /// <summary>High price column.</summary>
    High,
    /// <summary>Low price column.</summary>
    Low,
    /// <summary>Close price column.</summary>
    Close,
    /// <summary>Volume column.</summary>
    Volume
}
