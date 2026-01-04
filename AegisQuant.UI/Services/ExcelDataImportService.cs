using ClosedXML.Excel;
using System.Globalization;
using System.IO;
using System.Text;
using ScottPlot;

namespace AegisQuant.UI.Services;

/// <summary>
/// Excel 数据导入服务，支持 xlsx 和 xls 格式的行情数据导入。
/// 自动检测列映射，支持多种常见的数据格式（Tick 和 OHLC）。
/// </summary>
public class ExcelDataImportService
{
    /// <summary>
    /// 数据格式类型
    /// </summary>
    public enum DataFormatType
    {
        Tick,       // 逐笔数据：时间、价格、成交量
        OHLC        // K线数据：时间、开、高、低、收、量
    }

    /// <summary>
    /// 导入结果
    /// </summary>
    public class ImportResult
    {
        public bool Success { get; set; }
        public string? CsvFilePath { get; set; }
        public string? ErrorMessage { get; set; }
        public int RowCount { get; set; }
        public string? DetectedFormat { get; set; }
        public DataFormatType FormatType { get; set; }
        
        /// <summary>
        /// OHLC 数据（如果是 K 线格式）
        /// </summary>
        public List<OHLC>? OhlcData { get; set; }
        
        /// <summary>
        /// 成交量数据
        /// </summary>
        public List<double>? VolumeData { get; set; }
    }

    /// <summary>
    /// 列映射配置
    /// </summary>
    public class ColumnMapping
    {
        public int TimestampColumn { get; set; } = -1;
        public int DateColumn { get; set; } = -1;
        public int TimeColumn { get; set; } = -1;
        public int OpenColumn { get; set; } = -1;
        public int HighColumn { get; set; } = -1;
        public int LowColumn { get; set; } = -1;
        public int CloseColumn { get; set; } = -1;
        public int PriceColumn { get; set; } = -1;
        public int VolumeColumn { get; set; } = -1;
        public int AmountColumn { get; set; } = -1;  // 成交额
        
        /// <summary>
        /// 是否是 OHLC 格式
        /// </summary>
        public bool IsOhlcFormat => OpenColumn > 0 && HighColumn > 0 && LowColumn > 0 && CloseColumn > 0;
    }

    // 常见的列名映射
    private static readonly Dictionary<string, string> ColumnNameMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // 时间戳
        { "timestamp", "timestamp" },
        { "time", "timestamp" },
        { "datetime", "timestamp" },
        { "date", "date" },
        { "日期", "date" },
        { "时间", "time" },
        { "交易时间", "timestamp" },
        { "成交时间", "timestamp" },
        { "交易日期", "date" },
        
        // 价格
        { "price", "price" },
        { "close", "close" },
        { "收盘", "close" },
        { "收盘价", "close" },
        { "收", "close" },
        { "最新价", "price" },
        { "成交价", "price" },
        { "现价", "price" },
        
        // OHLC
        { "open", "open" },
        { "开盘", "open" },
        { "开盘价", "open" },
        { "开", "open" },
        { "high", "high" },
        { "最高", "high" },
        { "最高价", "high" },
        { "高", "high" },
        { "low", "low" },
        { "最低", "low" },
        { "最低价", "low" },
        { "低", "low" },
        
        // 成交量/成交额
        { "volume", "volume" },
        { "vol", "volume" },
        { "成交量", "volume" },
        { "总量", "volume" },
        { "成交量(股)", "volume" },
        { "成交量(手)", "volume" },
        { "amount", "amount" },
        { "成交额", "amount" },
        { "成交额(元)", "amount" },
    };

    /// <summary>
    /// 从 Excel 文件导入数据并转换为 CSV 格式
    /// </summary>
    public async Task<ImportResult> ImportExcelAsync(string excelPath)
    {
        return await Task.Run(() => ImportExcel(excelPath));
    }

    /// <summary>
    /// 同步导入 Excel 文件
    /// </summary>
    public ImportResult ImportExcel(string excelPath)
    {
        try
        {
            if (!File.Exists(excelPath))
            {
                return new ImportResult
                {
                    Success = false,
                    ErrorMessage = $"文件不存在: {excelPath}"
                };
            }

            var extension = Path.GetExtension(excelPath).ToLowerInvariant();
            if (extension != ".xlsx" && extension != ".xls")
            {
                return new ImportResult
                {
                    Success = false,
                    ErrorMessage = $"不支持的文件格式: {extension}，请使用 xlsx 或 xls 文件"
                };
            }

            using var workbook = new XLWorkbook(excelPath);
            var worksheet = workbook.Worksheets.First();

            // 检测列映射
            var mapping = DetectColumnMapping(worksheet);
            if (mapping == null)
            {
                return new ImportResult
                {
                    Success = false,
                    ErrorMessage = "无法识别数据格式，请确保文件包含时间、价格、成交量等列"
                };
            }

            // 根据数据格式选择处理方式
            if (mapping.IsOhlcFormat)
            {
                // OHLC 格式 - 直接解析为 K 线数据
                return ImportAsOhlc(worksheet, mapping);
            }
            else
            {
                // Tick 格式 - 生成 CSV 文件
                var csvPath = Path.Combine(
                    Path.GetTempPath(),
                    $"aegisquant_import_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                );

                var rowCount = ConvertToCsv(worksheet, mapping, csvPath);

                return new ImportResult
                {
                    Success = true,
                    CsvFilePath = csvPath,
                    RowCount = rowCount,
                    DetectedFormat = GetDetectedFormatDescription(mapping),
                    FormatType = DataFormatType.Tick
                };
            }
        }
        catch (Exception ex)
        {
            return new ImportResult
            {
                Success = false,
                ErrorMessage = $"导入失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 导入为 OHLC 格式
    /// </summary>
    private ImportResult ImportAsOhlc(IXLWorksheet worksheet, ColumnMapping mapping)
    {
        var ohlcData = new List<OHLC>();
        var volumeData = new List<double>();

        var firstRow = worksheet.FirstRowUsed();
        var lastRow = worksheet.LastRowUsed();
        
        if (firstRow == null || lastRow == null)
        {
            return new ImportResult
            {
                Success = false,
                ErrorMessage = "工作表为空"
            };
        }

        var currentRow = firstRow.RowBelow(); // 跳过表头

        while (currentRow != null && currentRow.RowNumber() <= lastRow.RowNumber())
        {
            try
            {
                // 解析日期时间
                DateTime dateTime = ParseDateTime(currentRow, mapping);
                if (dateTime == DateTime.MinValue)
                {
                    currentRow = currentRow.RowBelow();
                    continue;
                }

                // 解析 OHLC
                double open = ParseDouble(currentRow, mapping.OpenColumn);
                double high = ParseDouble(currentRow, mapping.HighColumn);
                double low = ParseDouble(currentRow, mapping.LowColumn);
                double close = ParseDouble(currentRow, mapping.CloseColumn);

                if (open <= 0 || high <= 0 || low <= 0 || close <= 0)
                {
                    currentRow = currentRow.RowBelow();
                    continue;
                }

                // 解析成交量
                double volume = mapping.VolumeColumn > 0 
                    ? ParseDouble(currentRow, mapping.VolumeColumn) 
                    : (mapping.AmountColumn > 0 ? ParseDouble(currentRow, mapping.AmountColumn) : 1000);

                ohlcData.Add(new OHLC(open, high, low, close, dateTime, TimeSpan.FromDays(1)));
                volumeData.Add(volume);
            }
            catch
            {
                // 跳过无法解析的行
            }

            currentRow = currentRow.RowBelow();
        }

        if (ohlcData.Count == 0)
        {
            return new ImportResult
            {
                Success = false,
                ErrorMessage = "未能解析任何有效的 K 线数据"
            };
        }

        // 按时间排序
        var sortedIndices = ohlcData
            .Select((ohlc, index) => new { ohlc, index })
            .OrderBy(x => x.ohlc.DateTime)
            .Select(x => x.index)
            .ToList();

        var sortedOhlc = sortedIndices.Select(i => ohlcData[i]).ToList();
        var sortedVolume = sortedIndices.Select(i => volumeData[i]).ToList();

        return new ImportResult
        {
            Success = true,
            RowCount = sortedOhlc.Count,
            DetectedFormat = GetDetectedFormatDescription(mapping),
            FormatType = DataFormatType.OHLC,
            OhlcData = sortedOhlc,
            VolumeData = sortedVolume
        };
    }

    /// <summary>
    /// 解析日期时间
    /// </summary>
    private DateTime ParseDateTime(IXLRow row, ColumnMapping mapping)
    {
        DateTime dateTime = DateTime.MinValue;

        if (mapping.TimestampColumn > 0)
        {
            var cell = row.Cell(mapping.TimestampColumn);
            if (cell.DataType == XLDataType.DateTime)
            {
                dateTime = cell.GetDateTime();
            }
            else if (!TryParseDateTime(cell.GetString(), out dateTime))
            {
                return DateTime.MinValue;
            }
        }
        else if (mapping.DateColumn > 0)
        {
            var dateCell = row.Cell(mapping.DateColumn);
            DateTime date;
            
            if (dateCell.DataType == XLDataType.DateTime)
            {
                date = dateCell.GetDateTime();
            }
            else if (!TryParseDateTime(dateCell.GetString(), out date))
            {
                return DateTime.MinValue;
            }

            // 如果有单独的时间列
            if (mapping.TimeColumn > 0)
            {
                var timeCell = row.Cell(mapping.TimeColumn);
                if (TimeSpan.TryParse(timeCell.GetString(), out var time))
                {
                    date = date.Date.Add(time);
                }
            }

            dateTime = date;
        }

        return dateTime;
    }

    /// <summary>
    /// 解析 double 值
    /// </summary>
    private double ParseDouble(IXLRow row, int column)
    {
        if (column <= 0) return 0;

        var cell = row.Cell(column);
        if (cell.DataType == XLDataType.Number)
            return cell.GetDouble();

        if (double.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return value;

        return 0;
    }

    /// <summary>
    /// 自动检测列映射
    /// </summary>
    private ColumnMapping? DetectColumnMapping(IXLWorksheet worksheet)
    {
        var mapping = new ColumnMapping();
        var headerRow = worksheet.FirstRowUsed();
        
        if (headerRow == null)
            return null;

        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (int col = 1; col <= lastColumn; col++)
        {
            var cellValue = headerRow.Cell(col).GetString().Trim();
            if (string.IsNullOrEmpty(cellValue))
                continue;

            if (ColumnNameMappings.TryGetValue(cellValue, out var mappedName))
            {
                switch (mappedName)
                {
                    case "timestamp":
                        mapping.TimestampColumn = col;
                        break;
                    case "date":
                        mapping.DateColumn = col;
                        break;
                    case "time":
                        mapping.TimeColumn = col;
                        break;
                    case "open":
                        mapping.OpenColumn = col;
                        break;
                    case "high":
                        mapping.HighColumn = col;
                        break;
                    case "low":
                        mapping.LowColumn = col;
                        break;
                    case "close":
                        mapping.CloseColumn = col;
                        break;
                    case "price":
                        mapping.PriceColumn = col;
                        break;
                    case "volume":
                        mapping.VolumeColumn = col;
                        break;
                    case "amount":
                        mapping.AmountColumn = col;
                        break;
                }
            }
        }

        // 验证必要的列是否存在
        bool hasTime = mapping.TimestampColumn > 0 || mapping.DateColumn > 0;
        bool hasPrice = mapping.PriceColumn > 0 || mapping.CloseColumn > 0;
        bool hasVolume = mapping.VolumeColumn > 0;

        if (!hasTime || !hasPrice)
        {
            // 尝试按位置猜测（常见格式：日期、开、高、低、收、量）
            return TryGuessMapping(worksheet);
        }

        // 如果没有成交量列，设置默认值
        if (!hasVolume)
        {
            mapping.VolumeColumn = 0; // 标记为需要使用默认值
        }

        return mapping;
    }

    /// <summary>
    /// 尝试根据数据格式猜测列映射
    /// </summary>
    private ColumnMapping? TryGuessMapping(IXLWorksheet worksheet)
    {
        var firstDataRow = worksheet.FirstRowUsed()?.RowBelow();
        if (firstDataRow == null)
            return null;

        var mapping = new ColumnMapping();
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        // 遍历列，根据数据类型猜测
        for (int col = 1; col <= lastColumn; col++)
        {
            var cell = firstDataRow.Cell(col);
            
            // 检查是否是日期/时间
            if (cell.DataType == XLDataType.DateTime || TryParseDateTime(cell.GetString(), out _))
            {
                if (mapping.DateColumn < 0)
                    mapping.DateColumn = col;
                else if (mapping.TimeColumn < 0)
                    mapping.TimeColumn = col;
            }
            // 检查是否是数字
            else if (cell.DataType == XLDataType.Number || double.TryParse(cell.GetString(), out _))
            {
                // 按顺序分配：开、高、低、收、量
                if (mapping.OpenColumn < 0)
                    mapping.OpenColumn = col;
                else if (mapping.HighColumn < 0)
                    mapping.HighColumn = col;
                else if (mapping.LowColumn < 0)
                    mapping.LowColumn = col;
                else if (mapping.CloseColumn < 0)
                    mapping.CloseColumn = col;
                else if (mapping.VolumeColumn < 0)
                    mapping.VolumeColumn = col;
            }
        }

        // 验证
        if (mapping.DateColumn < 0 || mapping.CloseColumn < 0)
            return null;

        return mapping;
    }

    /// <summary>
    /// 转换为 CSV 格式
    /// </summary>
    private int ConvertToCsv(IXLWorksheet worksheet, ColumnMapping mapping, string csvPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp,price,volume");

        var firstRow = worksheet.FirstRowUsed();
        var lastRow = worksheet.LastRowUsed();
        
        if (firstRow == null || lastRow == null)
            return 0;

        int rowCount = 0;
        var currentRow = firstRow.RowBelow(); // 跳过表头

        while (currentRow != null && currentRow.RowNumber() <= lastRow.RowNumber())
        {
            try
            {
                // 解析时间戳
                long timestamp = ParseTimestamp(currentRow, mapping);
                if (timestamp <= 0)
                {
                    currentRow = currentRow.RowBelow();
                    continue;
                }

                // 解析价格
                double price = ParsePrice(currentRow, mapping);
                if (price <= 0)
                {
                    currentRow = currentRow.RowBelow();
                    continue;
                }

                // 解析成交量
                double volume = ParseVolume(currentRow, mapping);

                sb.AppendLine($"{timestamp},{price:F4},{volume:F0}");
                rowCount++;
            }
            catch
            {
                // 跳过无法解析的行
            }

            currentRow = currentRow.RowBelow();
        }

        File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
        return rowCount;
    }

    /// <summary>
    /// 解析时间戳
    /// </summary>
    private long ParseTimestamp(IXLRow row, ColumnMapping mapping)
    {
        DateTime dateTime;

        if (mapping.TimestampColumn > 0)
        {
            var cell = row.Cell(mapping.TimestampColumn);
            if (cell.DataType == XLDataType.DateTime)
            {
                dateTime = cell.GetDateTime();
            }
            else if (!TryParseDateTime(cell.GetString(), out dateTime))
            {
                return 0;
            }
        }
        else if (mapping.DateColumn > 0)
        {
            var dateCell = row.Cell(mapping.DateColumn);
            DateTime date;
            
            if (dateCell.DataType == XLDataType.DateTime)
            {
                date = dateCell.GetDateTime();
            }
            else if (!TryParseDateTime(dateCell.GetString(), out date))
            {
                return 0;
            }

            // 如果有单独的时间列
            if (mapping.TimeColumn > 0)
            {
                var timeCell = row.Cell(mapping.TimeColumn);
                if (TimeSpan.TryParse(timeCell.GetString(), out var time))
                {
                    date = date.Date.Add(time);
                }
            }

            dateTime = date;
        }
        else
        {
            return 0;
        }

        // 转换为 Unix 毫秒时间戳
        return new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 解析价格
    /// </summary>
    private double ParsePrice(IXLRow row, ColumnMapping mapping)
    {
        int priceCol = mapping.PriceColumn > 0 ? mapping.PriceColumn : mapping.CloseColumn;
        if (priceCol <= 0)
            return 0;

        var cell = row.Cell(priceCol);
        if (cell.DataType == XLDataType.Number)
            return cell.GetDouble();

        if (double.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            return price;

        return 0;
    }

    /// <summary>
    /// 解析成交量
    /// </summary>
    private double ParseVolume(IXLRow row, ColumnMapping mapping)
    {
        if (mapping.VolumeColumn <= 0)
            return 1000; // 默认值

        var cell = row.Cell(mapping.VolumeColumn);
        if (cell.DataType == XLDataType.Number)
            return cell.GetDouble();

        if (double.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
            return volume;

        return 1000;
    }

    /// <summary>
    /// 尝试解析日期时间
    /// </summary>
    private bool TryParseDateTime(string value, out DateTime result)
    {
        result = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // 常见的日期格式
        string[] formats = {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy/MM/dd HH:mm",
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyyMMdd",
            "yyyyMMdd HH:mm:ss",
            "yyyy年MM月dd日",
            "yyyy年MM月dd日 HH:mm:ss",
            "MM/dd/yyyy HH:mm:ss",
            "MM/dd/yyyy",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy",
        };

        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, 
            DateTimeStyles.None, out result) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    /// <summary>
    /// 获取检测到的格式描述
    /// </summary>
    private string GetDetectedFormatDescription(ColumnMapping mapping)
    {
        var parts = new List<string>();
        
        if (mapping.TimestampColumn > 0)
            parts.Add("时间戳");
        else if (mapping.DateColumn > 0)
            parts.Add(mapping.TimeColumn > 0 ? "日期+时间" : "日期");

        if (mapping.OpenColumn > 0 && mapping.HighColumn > 0 && 
            mapping.LowColumn > 0 && mapping.CloseColumn > 0)
            parts.Add("OHLC");
        else if (mapping.CloseColumn > 0)
            parts.Add("收盘价");
        else if (mapping.PriceColumn > 0)
            parts.Add("价格");

        if (mapping.VolumeColumn > 0)
            parts.Add("成交量");

        return string.Join(" + ", parts);
    }
}
