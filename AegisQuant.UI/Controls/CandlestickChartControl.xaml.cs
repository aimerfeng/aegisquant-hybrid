using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScottPlot;
using ScottPlot.Plottables;
using AegisQuant.UI.Services;
using AegisQuant.UI.ViewModels;

namespace AegisQuant.UI.Controls;

/// <summary>
/// 三段式 K 线图表控件
/// </summary>
public partial class CandlestickChartControl : UserControl
{
    private Crosshair? _mainCrosshair;
    private Crosshair? _volumeCrosshair;
    private Crosshair? _macdCrosshair;

    // 数据存储
    private List<OHLC> _ohlcData = new();
    private List<double> _volumes = new();
    private List<double> _ma5 = new();
    private List<double> _ma10 = new();
    private List<double> _ma20 = new();
    private List<double> _ma60 = new();
    private List<double> _bollUpper = new();
    private List<double> _bollMiddle = new();
    private List<double> _bollLower = new();
    private List<double> _macdDif = new();
    private List<double> _macdDea = new();
    private List<double> _macdHistogram = new();

    // 买卖标记
    private List<(int index, double price, bool isBuy)> _tradeMarkers = new();

    public CandlestickChartControl()
    {
        InitializeComponent();
        InitializeCharts();
        SetupMouseTracking();
    }

    /// <summary>
    /// 初始化图表
    /// </summary>
    private void InitializeCharts()
    {
        var colorService = ColorSchemeService.Instance;

        // 主图设置
        ConfigureChart(MainChart.Plot, "K线图");
        
        // 成交量图设置
        ConfigureChart(VolumeChart.Plot, "成交量");
        
        // MACD 图设置
        ConfigureChart(MacdChart.Plot, "MACD");

        // 添加十字光标
        _mainCrosshair = MainChart.Plot.Add.Crosshair(0, 0);
        _mainCrosshair.IsVisible = false;
        
        _volumeCrosshair = VolumeChart.Plot.Add.Crosshair(0, 0);
        _volumeCrosshair.IsVisible = false;
        
        _macdCrosshair = MacdChart.Plot.Add.Crosshair(0, 0);
        _macdCrosshair.IsVisible = false;
    }

    /// <summary>
    /// 配置图表通用设置
    /// </summary>
    private void ConfigureChart(Plot plot, string title)
    {
        // 暗色主题 - 使用新 API
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E1E");
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#252525");
        
        plot.Axes.Color(ScottPlot.Color.FromHex("#A0A0A0"));

        // 隐藏标题
        plot.Title(string.Empty);
        
        // 设置边距
        plot.Layout.Frameless();
    }

    /// <summary>
    /// 设置鼠标跟踪
    /// </summary>
    private void SetupMouseTracking()
    {
        MainChart.MouseMove += OnMainChartMouseMove;
        MainChart.MouseLeave += OnChartMouseLeave;
        
        VolumeChart.MouseMove += OnVolumeChartMouseMove;
        VolumeChart.MouseLeave += OnChartMouseLeave;
        
        MacdChart.MouseMove += OnMacdChartMouseMove;
        MacdChart.MouseLeave += OnChartMouseLeave;
    }

    /// <summary>
    /// 主图鼠标移动
    /// </summary>
    private void OnMainChartMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(MainChart);
        var pixel = new Pixel((float)position.X, (float)position.Y);
        var coordinates = MainChart.Plot.GetCoordinates(pixel);

        UpdateCrosshairs(coordinates.X, coordinates.Y);
        UpdateCrosshairInfo((int)coordinates.X);
        
        CrosshairInfo.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 成交量图鼠标移动
    /// </summary>
    private void OnVolumeChartMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(VolumeChart);
        var pixel = new Pixel((float)position.X, (float)position.Y);
        var coordinates = VolumeChart.Plot.GetCoordinates(pixel);

        UpdateCrosshairs(coordinates.X, coordinates.Y);
        UpdateCrosshairInfo((int)coordinates.X);
        
        CrosshairInfo.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// MACD 图鼠标移动
    /// </summary>
    private void OnMacdChartMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(MacdChart);
        var pixel = new Pixel((float)position.X, (float)position.Y);
        var coordinates = MacdChart.Plot.GetCoordinates(pixel);

        UpdateCrosshairs(coordinates.X, coordinates.Y);
        UpdateCrosshairInfo((int)coordinates.X);
        
        CrosshairInfo.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 鼠标离开图表
    /// </summary>
    private void OnChartMouseLeave(object sender, MouseEventArgs e)
    {
        if (_mainCrosshair != null) _mainCrosshair.IsVisible = false;
        if (_volumeCrosshair != null) _volumeCrosshair.IsVisible = false;
        if (_macdCrosshair != null) _macdCrosshair.IsVisible = false;
        
        CrosshairInfo.Visibility = Visibility.Collapsed;
        
        RefreshAllCharts();
    }

    /// <summary>
    /// 更新所有十字光标位置
    /// </summary>
    private void UpdateCrosshairs(double x, double y)
    {
        if (_mainCrosshair != null)
        {
            _mainCrosshair.IsVisible = true;
            _mainCrosshair.Position = new Coordinates(x, y);
        }
        
        if (_volumeCrosshair != null)
        {
            _volumeCrosshair.IsVisible = true;
            _volumeCrosshair.Position = new Coordinates(x, 0);
        }
        
        if (_macdCrosshair != null)
        {
            _macdCrosshair.IsVisible = true;
            _macdCrosshair.Position = new Coordinates(x, 0);
        }
        
        RefreshAllCharts();
    }

    /// <summary>
    /// 更新十字光标信息面板
    /// </summary>
    private void UpdateCrosshairInfo(int index)
    {
        if (index < 0 || index >= _ohlcData.Count) return;

        var ohlc = _ohlcData[index];
        
        CrosshairTime.Text = ohlc.DateTime.ToString("yyyy-MM-dd HH:mm");
        CrosshairOpen.Text = ohlc.Open.ToString("F2");
        CrosshairHigh.Text = ohlc.High.ToString("F2");
        CrosshairLow.Text = ohlc.Low.ToString("F2");
        CrosshairClose.Text = ohlc.Close.ToString("F2");

        // 根据涨跌设置颜色
        var colorService = ColorSchemeService.Instance;
        var brush = colorService.GetPriceChangeBrush(ohlc.Close - ohlc.Open);
        CrosshairClose.Foreground = brush;
    }

    /// <summary>
    /// 刷新所有图表
    /// </summary>
    private void RefreshAllCharts()
    {
        MainChart.Refresh();
        VolumeChart.Refresh();
        MacdChart.Refresh();
    }

    /// <summary>
    /// 更新 K 线数据
    /// </summary>
    public void UpdateOhlcData(List<OHLC> data)
    {
        _ohlcData = data;
        RenderMainChart();
    }

    /// <summary>
    /// 更新成交量数据
    /// </summary>
    public void UpdateVolumeData(List<double> volumes)
    {
        _volumes = volumes;
        RenderVolumeChart();
    }

    /// <summary>
    /// 更新指标数据
    /// </summary>
    public void UpdateIndicators(
        List<double> ma5, List<double> ma10, List<double> ma20, List<double> ma60,
        List<double> bollUpper, List<double> bollMiddle, List<double> bollLower,
        List<double> macdDif, List<double> macdDea, List<double> macdHistogram)
    {
        _ma5 = ma5;
        _ma10 = ma10;
        _ma20 = ma20;
        _ma60 = ma60;
        _bollUpper = bollUpper;
        _bollMiddle = bollMiddle;
        _bollLower = bollLower;
        _macdDif = macdDif;
        _macdDea = macdDea;
        _macdHistogram = macdHistogram;

        RenderMainChart();
        RenderMacdChart();
    }

    /// <summary>
    /// 添加买卖标记
    /// </summary>
    public void AddTradeMarker(int index, double price, bool isBuy)
    {
        _tradeMarkers.Add((index, price, isBuy));
        RenderMainChart();
    }

    /// <summary>
    /// 清除买卖标记
    /// </summary>
    public void ClearTradeMarkers()
    {
        _tradeMarkers.Clear();
        RenderMainChart();
    }

    /// <summary>
    /// 渲染主图
    /// </summary>
    private void RenderMainChart()
    {
        MainChart.Plot.Clear();
        
        if (_ohlcData.Count == 0) return;

        var colorService = ColorSchemeService.Instance;

        // 添加 K 线
        var candlestick = MainChart.Plot.Add.Candlestick(_ohlcData);
        // ScottPlot 5 使用默认颜色，暂不自定义

        // 添加均线
        var vm = DataContext as ChartViewModel;
        if (vm?.ShowMa5 == true && _ma5.Count > 0)
        {
            var signal = MainChart.Plot.Add.Signal(_ma5.ToArray());
            signal.Color = ScottPlot.Color.FromHex("#FFFF00"); // 黄色
            signal.LineWidth = 1;
        }
        
        if (vm?.ShowMa10 == true && _ma10.Count > 0)
        {
            var signal = MainChart.Plot.Add.Signal(_ma10.ToArray());
            signal.Color = ScottPlot.Color.FromHex("#FF00FF"); // 紫色
            signal.LineWidth = 1;
        }
        
        if (vm?.ShowMa20 == true && _ma20.Count > 0)
        {
            var signal = MainChart.Plot.Add.Signal(_ma20.ToArray());
            signal.Color = ScottPlot.Color.FromHex("#00FFFF"); // 青色
            signal.LineWidth = 1;
        }
        
        if (vm?.ShowMa60 == true && _ma60.Count > 0)
        {
            var signal = MainChart.Plot.Add.Signal(_ma60.ToArray());
            signal.Color = ScottPlot.Color.FromHex("#FFFFFF"); // 白色
            signal.LineWidth = 1;
        }

        // 添加布林带
        if (vm?.ShowBollinger == true && _bollUpper.Count > 0)
        {
            var upper = MainChart.Plot.Add.Signal(_bollUpper.ToArray());
            upper.Color = ScottPlot.Color.FromHex("#64B5F6");
            upper.LineWidth = 1;
            
            var middle = MainChart.Plot.Add.Signal(_bollMiddle.ToArray());
            middle.Color = ScottPlot.Color.FromHex("#64B5F6");
            middle.LineWidth = 1;
            
            var lower = MainChart.Plot.Add.Signal(_bollLower.ToArray());
            lower.Color = ScottPlot.Color.FromHex("#64B5F6");
            lower.LineWidth = 1;
        }

        // 添加买卖标记
        if (vm?.ShowTradeMarkers == true)
        {
            foreach (var (index, price, isBuy) in _tradeMarkers)
            {
                var marker = MainChart.Plot.Add.Marker(index, price);
                marker.Shape = isBuy ? MarkerShape.TriUp : MarkerShape.TriDown;
                marker.Size = 10;
                marker.Color = isBuy 
                    ? ScottPlot.Color.FromColor(System.Drawing.Color.FromArgb(
                        colorService.UpColor.R, colorService.UpColor.G, colorService.UpColor.B))
                    : ScottPlot.Color.FromColor(System.Drawing.Color.FromArgb(
                        colorService.DownColor.R, colorService.DownColor.G, colorService.DownColor.B));
            }
        }

        // 重新添加十字光标
        _mainCrosshair = MainChart.Plot.Add.Crosshair(0, 0);
        _mainCrosshair.IsVisible = false;

        MainChart.Plot.Axes.AutoScale();
        MainChart.Refresh();
    }

    /// <summary>
    /// 渲染成交量图
    /// </summary>
    private void RenderVolumeChart()
    {
        VolumeChart.Plot.Clear();
        
        if (_volumes.Count == 0 || _ohlcData.Count == 0) return;

        var colorService = ColorSchemeService.Instance;

        // 创建成交量柱状图
        var bars = new List<Bar>();
        for (int i = 0; i < _volumes.Count && i < _ohlcData.Count; i++)
        {
            bool isUp = _ohlcData[i].Close >= _ohlcData[i].Open;
            var bar = new Bar
            {
                Position = i,
                Value = _volumes[i],
                FillColor = isUp 
                    ? ScottPlot.Color.FromColor(System.Drawing.Color.FromArgb(
                        colorService.UpColor.R, colorService.UpColor.G, colorService.UpColor.B))
                    : ScottPlot.Color.FromColor(System.Drawing.Color.FromArgb(
                        colorService.DownColor.R, colorService.DownColor.G, colorService.DownColor.B))
            };
            bars.Add(bar);
        }
        VolumeChart.Plot.Add.Bars(bars);

        // 重新添加十字光标
        _volumeCrosshair = VolumeChart.Plot.Add.Crosshair(0, 0);
        _volumeCrosshair.IsVisible = false;

        VolumeChart.Plot.Axes.AutoScale();
        VolumeChart.Refresh();
    }

    /// <summary>
    /// 渲染 MACD 图
    /// </summary>
    private void RenderMacdChart()
    {
        MacdChart.Plot.Clear();
        
        if (_macdDif.Count == 0) return;

        var colorService = ColorSchemeService.Instance;

        // DIF 线
        var difSignal = MacdChart.Plot.Add.Signal(_macdDif.ToArray());
        difSignal.Color = ScottPlot.Color.FromHex("#FFFF00"); // 黄色
        difSignal.LineWidth = 1;

        // DEA 线
        var deaSignal = MacdChart.Plot.Add.Signal(_macdDea.ToArray());
        deaSignal.Color = ScottPlot.Color.FromHex("#00FFFF"); // 青色
        deaSignal.LineWidth = 1;

        // MACD 柱状图
        var macdBars = new List<Bar>();
        for (int i = 0; i < _macdHistogram.Count; i++)
        {
            var bar = new Bar
            {
                Position = i,
                Value = _macdHistogram[i],
                FillColor = _macdHistogram[i] >= 0 
                    ? ScottPlot.Color.FromColor(System.Drawing.Color.FromArgb(
                        colorService.UpColor.R, colorService.UpColor.G, colorService.UpColor.B))
                    : ScottPlot.Color.FromColor(System.Drawing.Color.FromArgb(
                        colorService.DownColor.R, colorService.DownColor.G, colorService.DownColor.B))
            };
            macdBars.Add(bar);
        }
        MacdChart.Plot.Add.Bars(macdBars);

        // 添加零线
        var zeroLine = MacdChart.Plot.Add.HorizontalLine(0);
        zeroLine.Color = ScottPlot.Color.FromHex("#666666");
        zeroLine.LineWidth = 1;

        // 重新添加十字光标
        _macdCrosshair = MacdChart.Plot.Add.Crosshair(0, 0);
        _macdCrosshair.IsVisible = false;

        MacdChart.Plot.Axes.AutoScale();
        MacdChart.Refresh();
    }

    /// <summary>
    /// 清除所有数据
    /// </summary>
    public void Clear()
    {
        _ohlcData.Clear();
        _volumes.Clear();
        _ma5.Clear();
        _ma10.Clear();
        _ma20.Clear();
        _ma60.Clear();
        _bollUpper.Clear();
        _bollMiddle.Clear();
        _bollLower.Clear();
        _macdDif.Clear();
        _macdDea.Clear();
        _macdHistogram.Clear();
        _tradeMarkers.Clear();

        MainChart.Plot.Clear();
        VolumeChart.Plot.Clear();
        MacdChart.Plot.Clear();

        RefreshAllCharts();
    }
}
