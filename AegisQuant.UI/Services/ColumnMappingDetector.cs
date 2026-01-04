using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AegisQuant.UI.Models;

namespace AegisQuant.UI.Services;

/// <summary>
/// Service for auto-detecting column mappings and date formats in CSV files.
/// Implements Property 20 (Column Name Detection) and Property 21 (Date Format Detection).
/// </summary>
public class ColumnMappingDetector
{
    // Common column name patterns for timestamp detection
    private static readonly string[] TimePatterns = 
    { 
        "time", "date", "timestamp", "datetime", "日期", "时间", "dt" 
    };

    // Common column name patterns for open price detection
    private static readonly string[] OpenPatterns = 
    { 
        "open", "开盘", "开盘价"
    };

    // Common column name patterns for high price detection
    private static readonly string[] HighPatterns = 
    { 
        "high", "最高", "最高价"
    };

    // Common column name patterns for low price detection
    private static readonly string[] LowPatterns = 
    { 
        "low", "最低", "最低价"
    };

    // Common column name patterns for close/price detection
    private static readonly string[] ClosePatterns = 
    { 
        "close", "price", "last", "收盘", "收盘价", "价格"
    };

    // Common column name patterns for volume detection
    private static readonly string[] VolumePatterns = 
    { 
        "volume", "vol", "成交量", "量"
    };

    // Common date formats to try
    private static readonly string[] DateFormats = 
    {
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy/MM/dd",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd HH:mm",
        "MM/dd/yyyy",
        "MM/dd/yyyy HH:mm:ss",
        "dd/MM/yyyy",
        "dd/MM/yyyy HH:mm:ss",
        "yyyyMMdd",
        "yyyyMMdd HH:mm:ss",
        "yyyy.MM.dd",
        "yyyy.MM.dd HH:mm:ss"
    };

    /// <summary>
    /// Detects the column mapping type for a given column name.
    /// </summary>
    /// <param name="columnName">The column name to analyze.</param>
    /// <returns>The detected column mapping type.</returns>
    public ColumnMappingType DetectColumnType(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return ColumnMappingType.Ignore;

        var normalized = columnName.ToLowerInvariant().Trim();

        // Check each pattern type in order of specificity
        // Use Contains for multi-character patterns, exact match for single-char patterns
        if (TimePatterns.Any(p => normalized.Contains(p)))
            return ColumnMappingType.Timestamp;

        if (OpenPatterns.Any(p => normalized.Contains(p)))
            return ColumnMappingType.Open;

        if (HighPatterns.Any(p => normalized.Contains(p)))
            return ColumnMappingType.High;

        if (LowPatterns.Any(p => normalized.Contains(p)))
            return ColumnMappingType.Low;

        if (ClosePatterns.Any(p => normalized.Contains(p)))
            return ColumnMappingType.Close;

        if (VolumePatterns.Any(p => normalized.Contains(p)))
            return ColumnMappingType.Volume;

        return ColumnMappingType.Ignore;
    }

    /// <summary>
    /// Detects column mappings for all columns in a CSV header.
    /// </summary>
    /// <param name="headers">Array of column header names.</param>
    /// <returns>Dictionary mapping column names to their detected types.</returns>
    public Dictionary<string, ColumnMappingType> DetectColumnMappings(string[] headers)
    {
        var mappings = new Dictionary<string, ColumnMappingType>();
        var usedTypes = new HashSet<ColumnMappingType>();

        foreach (var header in headers)
        {
            var detectedType = DetectColumnType(header);
            
            // Avoid duplicate mappings for the same type (except Ignore)
            if (detectedType != ColumnMappingType.Ignore && usedTypes.Contains(detectedType))
            {
                mappings[header] = ColumnMappingType.Ignore;
            }
            else
            {
                mappings[header] = detectedType;
                if (detectedType != ColumnMappingType.Ignore)
                    usedTypes.Add(detectedType);
            }
        }

        return mappings;
    }

    /// <summary>
    /// Detects the date format from a sample date string.
    /// </summary>
    /// <param name="sample">A sample date string to analyze.</param>
    /// <returns>The detected date format string, or "auto" if unable to detect.</returns>
    public string DetectDateFormat(string sample)
    {
        if (string.IsNullOrWhiteSpace(sample))
            return "auto";

        sample = sample.Trim();

        // Check for Unix timestamp (numeric only)
        if (long.TryParse(sample, out var unixValue))
        {
            // Validate it's a reasonable Unix timestamp (after 1970, before 2100)
            // Unix timestamps in seconds: 0 to ~4102444800
            // Unix timestamps in milliseconds: 0 to ~4102444800000
            if (unixValue > 0 && unixValue < 4102444800)
                return "unix";
            if (unixValue > 1000000000000 && unixValue < 4102444800000)
                return "unix_ms";
        }

        // Try each date format
        foreach (var format in DateFormats)
        {
            if (DateTime.TryParseExact(sample, format, CultureInfo.InvariantCulture, 
                DateTimeStyles.None, out _))
            {
                return format;
            }
        }

        // Try general parsing as fallback
        if (DateTime.TryParse(sample, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            // Infer format from string structure
            if (sample.Contains('-'))
                return sample.Contains(':') ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd";
            if (sample.Contains('/'))
                return sample.Contains(':') ? "yyyy/MM/dd HH:mm:ss" : "yyyy/MM/dd";
        }

        return "auto";
    }

    /// <summary>
    /// Creates a DataImportConfig from detected column mappings and date format.
    /// </summary>
    /// <param name="filePath">Path to the CSV file.</param>
    /// <param name="headers">Array of column header names.</param>
    /// <param name="sampleDateValue">A sample date value for format detection.</param>
    /// <returns>A configured DataImportConfig instance.</returns>
    public DataImportConfig CreateConfig(string filePath, string[] headers, string? sampleDateValue)
    {
        var config = new DataImportConfig
        {
            FilePath = filePath
        };

        var mappings = DetectColumnMappings(headers);

        foreach (var (columnName, mappingType) in mappings)
        {
            switch (mappingType)
            {
                case ColumnMappingType.Timestamp:
                    config.TimeColumnName = columnName;
                    break;
                case ColumnMappingType.Open:
                    config.OpenColumnName = columnName;
                    break;
                case ColumnMappingType.High:
                    config.HighColumnName = columnName;
                    break;
                case ColumnMappingType.Low:
                    config.LowColumnName = columnName;
                    break;
                case ColumnMappingType.Close:
                    config.CloseColumnName = columnName;
                    break;
                case ColumnMappingType.Volume:
                    config.VolumeColumnName = columnName;
                    break;
                case ColumnMappingType.Ignore:
                    config.IgnoredColumns.Add(columnName);
                    break;
            }
        }

        // Detect date format if we have a sample value
        if (!string.IsNullOrEmpty(sampleDateValue))
        {
            config.DateFormat = DetectDateFormat(sampleDateValue);
        }

        return config;
    }
}
