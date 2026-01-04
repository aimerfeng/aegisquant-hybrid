using Xunit;
using FsCheck;
using FsCheck.Xunit;
using AegisQuant.UI.Services;
using AegisQuant.UI.Models;

namespace AegisQuant.UI.Tests;

/// <summary>
/// Property-based tests for the Import Wizard.
/// Tests Properties 20 and 21 from the design document.
/// </summary>
public class ImportWizardPropertyTests
{
    private readonly ColumnMappingDetector _detector = new();

    #region Property 20: Column Name Detection

    /// <summary>
    /// Property 20: Column Name Detection
    /// For any CSV with columns named "Date", "Close", "Vol" (or Chinese equivalents),
    /// the auto-detection SHALL correctly map them to timestamp, price, volume.
    /// **Validates: Requirements 13.2**
    /// </summary>
    [Theory]
    [InlineData("Date", ColumnMappingType.Timestamp)]
    [InlineData("date", ColumnMappingType.Timestamp)]
    [InlineData("Time", ColumnMappingType.Timestamp)]
    [InlineData("Timestamp", ColumnMappingType.Timestamp)]
    [InlineData("DateTime", ColumnMappingType.Timestamp)]
    [InlineData("日期", ColumnMappingType.Timestamp)]
    [InlineData("时间", ColumnMappingType.Timestamp)]
    public void Property20_ColumnNameDetection_TimestampVariants(string columnName, ColumnMappingType expected)
    {
        var result = _detector.DetectColumnType(columnName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Close", ColumnMappingType.Close)]
    [InlineData("close", ColumnMappingType.Close)]
    [InlineData("Price", ColumnMappingType.Close)]
    [InlineData("Last", ColumnMappingType.Close)]
    [InlineData("收盘", ColumnMappingType.Close)]
    [InlineData("收盘价", ColumnMappingType.Close)]
    [InlineData("价格", ColumnMappingType.Close)]
    public void Property20_ColumnNameDetection_CloseVariants(string columnName, ColumnMappingType expected)
    {
        var result = _detector.DetectColumnType(columnName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Volume", ColumnMappingType.Volume)]
    [InlineData("volume", ColumnMappingType.Volume)]
    [InlineData("Vol", ColumnMappingType.Volume)]
    [InlineData("成交量", ColumnMappingType.Volume)]
    [InlineData("量", ColumnMappingType.Volume)]
    public void Property20_ColumnNameDetection_VolumeVariants(string columnName, ColumnMappingType expected)
    {
        var result = _detector.DetectColumnType(columnName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Open", ColumnMappingType.Open)]
    [InlineData("开盘", ColumnMappingType.Open)]
    [InlineData("开盘价", ColumnMappingType.Open)]
    public void Property20_ColumnNameDetection_OpenVariants(string columnName, ColumnMappingType expected)
    {
        var result = _detector.DetectColumnType(columnName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("High", ColumnMappingType.High)]
    [InlineData("最高", ColumnMappingType.High)]
    [InlineData("最高价", ColumnMappingType.High)]
    public void Property20_ColumnNameDetection_HighVariants(string columnName, ColumnMappingType expected)
    {
        var result = _detector.DetectColumnType(columnName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Low", ColumnMappingType.Low)]
    [InlineData("最低", ColumnMappingType.Low)]
    [InlineData("最低价", ColumnMappingType.Low)]
    public void Property20_ColumnNameDetection_LowVariants(string columnName, ColumnMappingType expected)
    {
        var result = _detector.DetectColumnType(columnName);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Property 20: Column Name Detection - Property-based test
    /// For any column name containing a known pattern, detection SHALL return the correct type.
    /// **Validates: Requirements 13.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property20_ColumnNameDetection_PropertyBased()
    {
        var timestampPatterns = new[] { "time", "date", "timestamp", "datetime", "日期", "时间" };
        var closePatterns = new[] { "close", "price", "last", "收盘", "价格" };
        var volumePatterns = new[] { "volume", "vol", "成交量" };

        return Prop.ForAll(
            Gen.Elements(timestampPatterns).ToArbitrary(),
            pattern =>
            {
                var result = _detector.DetectColumnType(pattern);
                return result == ColumnMappingType.Timestamp;
            })
            .And(Prop.ForAll(
                Gen.Elements(closePatterns).ToArbitrary(),
                pattern =>
                {
                    var result = _detector.DetectColumnType(pattern);
                    return result == ColumnMappingType.Close;
                }))
            .And(Prop.ForAll(
                Gen.Elements(volumePatterns).ToArbitrary(),
                pattern =>
                {
                    var result = _detector.DetectColumnType(pattern);
                    return result == ColumnMappingType.Volume;
                }));
    }

    /// <summary>
    /// Property 20: Full header detection test
    /// For a typical OHLCV header, all columns SHALL be correctly mapped.
    /// **Validates: Requirements 13.2**
    /// </summary>
    [Fact]
    public void Property20_ColumnNameDetection_FullHeader()
    {
        var headers = new[] { "Date", "Open", "High", "Low", "Close", "Volume" };
        var mappings = _detector.DetectColumnMappings(headers);

        Assert.Equal(ColumnMappingType.Timestamp, mappings["Date"]);
        Assert.Equal(ColumnMappingType.Open, mappings["Open"]);
        Assert.Equal(ColumnMappingType.High, mappings["High"]);
        Assert.Equal(ColumnMappingType.Low, mappings["Low"]);
        Assert.Equal(ColumnMappingType.Close, mappings["Close"]);
        Assert.Equal(ColumnMappingType.Volume, mappings["Volume"]);
    }

    /// <summary>
    /// Property 20: Chinese header detection test
    /// For a Chinese OHLCV header, all columns SHALL be correctly mapped.
    /// **Validates: Requirements 13.2**
    /// </summary>
    [Fact]
    public void Property20_ColumnNameDetection_ChineseHeader()
    {
        var headers = new[] { "日期", "开盘价", "最高价", "最低价", "收盘价", "成交量" };
        var mappings = _detector.DetectColumnMappings(headers);

        Assert.Equal(ColumnMappingType.Timestamp, mappings["日期"]);
        Assert.Equal(ColumnMappingType.Open, mappings["开盘价"]);
        Assert.Equal(ColumnMappingType.High, mappings["最高价"]);
        Assert.Equal(ColumnMappingType.Low, mappings["最低价"]);
        Assert.Equal(ColumnMappingType.Close, mappings["收盘价"]);
        Assert.Equal(ColumnMappingType.Volume, mappings["成交量"]);
    }

    #endregion

    #region Property 21: Date Format Detection

    /// <summary>
    /// Property 21: Date Format Detection
    /// For any date string in formats "yyyy-MM-dd", "yyyy/MM/dd", or Unix timestamp,
    /// the auto-detection SHALL correctly identify the format.
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Theory]
    [InlineData("2024-01-15", "yyyy-MM-dd")]
    [InlineData("2024-01-15 09:30:00", "yyyy-MM-dd HH:mm:ss")]
    [InlineData("2024/01/15", "yyyy/MM/dd")]
    [InlineData("2024/01/15 09:30:00", "yyyy/MM/dd HH:mm:ss")]
    [InlineData("01/15/2024", "MM/dd/yyyy")]
    [InlineData("01/15/2024 09:30:00", "MM/dd/yyyy HH:mm:ss")]
    public void Property21_DateFormatDetection_CommonFormats(string sample, string expectedFormat)
    {
        var result = _detector.DetectDateFormat(sample);
        Assert.Equal(expectedFormat, result);
    }

    /// <summary>
    /// Property 21: Unix timestamp detection
    /// For Unix timestamps (seconds and milliseconds), detection SHALL return "unix" or "unix_ms".
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Theory]
    [InlineData("1705312200", "unix")]        // 2024-01-15 09:30:00 UTC in seconds
    [InlineData("1705312200000", "unix_ms")]  // 2024-01-15 09:30:00 UTC in milliseconds
    [InlineData("1609459200", "unix")]        // 2021-01-01 00:00:00 UTC
    [InlineData("1609459200000", "unix_ms")]  // 2021-01-01 00:00:00 UTC in ms
    public void Property21_DateFormatDetection_UnixTimestamp(string sample, string expectedFormat)
    {
        var result = _detector.DetectDateFormat(sample);
        Assert.Equal(expectedFormat, result);
    }

    /// <summary>
    /// Property 21: Date Format Detection - Property-based test
    /// For any valid date in yyyy-MM-dd format, detection SHALL return "yyyy-MM-dd".
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property21_DateFormatDetection_YyyyMmDd()
    {
        return Prop.ForAll(
            Gen.Choose(2000, 2030).ToArbitrary(),
            Gen.Choose(1, 12).ToArbitrary(),
            Gen.Choose(1, 28).ToArbitrary(), // Use 28 to avoid invalid dates
            (year, month, day) =>
            {
                var dateStr = $"{year:D4}-{month:D2}-{day:D2}";
                var result = _detector.DetectDateFormat(dateStr);
                return result == "yyyy-MM-dd";
            });
    }

    /// <summary>
    /// Property 21: Date Format Detection - Property-based test for yyyy/MM/dd
    /// For any valid date in yyyy/MM/dd format, detection SHALL return "yyyy/MM/dd".
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property21_DateFormatDetection_YyyySlashMmDd()
    {
        return Prop.ForAll(
            Gen.Choose(2000, 2030).ToArbitrary(),
            Gen.Choose(1, 12).ToArbitrary(),
            Gen.Choose(1, 28).ToArbitrary(),
            (year, month, day) =>
            {
                var dateStr = $"{year:D4}/{month:D2}/{day:D2}";
                var result = _detector.DetectDateFormat(dateStr);
                return result == "yyyy/MM/dd";
            });
    }

    /// <summary>
    /// Property 21: Date Format Detection - Property-based test for Unix timestamps
    /// For any valid Unix timestamp (seconds), detection SHALL return "unix".
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property21_DateFormatDetection_UnixSeconds()
    {
        return Prop.ForAll(
            // Generate timestamps between 2000-01-01 and 2030-01-01
            Gen.Choose(946684800, 1893456000).ToArbitrary(),
            timestamp =>
            {
                var result = _detector.DetectDateFormat(timestamp.ToString());
                return result == "unix";
            });
    }

    /// <summary>
    /// Property 21: Date Format Detection - Property-based test for Unix milliseconds
    /// For any valid Unix timestamp (milliseconds), detection SHALL return "unix_ms".
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property21_DateFormatDetection_UnixMilliseconds()
    {
        return Prop.ForAll(
            // Generate timestamps between 2010-01-01 and 2030-01-01 in milliseconds
            // Using a range that ensures the value is clearly in milliseconds range (> 1 trillion)
            Gen.Choose(1262304000, 1893456000).ToArbitrary(),
            timestamp =>
            {
                // Multiply by 1000 to get milliseconds, ensuring value > 1 trillion
                var msTimestamp = (long)timestamp * 1000L;
                // Verify the value is in the expected milliseconds range
                if (msTimestamp <= 1000000000000L) return true; // Skip if not clearly in ms range
                var result = _detector.DetectDateFormat(msTimestamp.ToString());
                return result == "unix_ms";
            });
    }

    /// <summary>
    /// Property 21: Empty or null input handling
    /// For empty or null input, detection SHALL return "auto".
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Property21_DateFormatDetection_EmptyInput(string? sample)
    {
        var result = _detector.DetectDateFormat(sample!);
        Assert.Equal("auto", result);
    }

    #endregion
}
