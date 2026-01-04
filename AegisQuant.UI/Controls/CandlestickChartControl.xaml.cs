using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScottPlot;
using ScottPlot.Plottables;
using AegisQuant.UI.Services;
using AegisQuant.UI.ViewModels;
using AegisQuant.UI.Models;
using AegisQuant.UI.Strategy;

namespace AegisQuant.UI.Controls;

public partial class CandlestickChartControl : UserControl
{
    private Crosshair? _mainCrosshair;
    private Crosshair? _volumeCrosshair;
    private Crosshair? _macdCrosshair;

    // 原始tick数据
    private List<(DateTime time, double price, double volume)> _rawTicks = new();
    
    // 当前周期的OHLC数据
    private List<OHLC> _ohlcData = new();
    private List<double> _volumes = new();
    private List<DateTime> _dateTimes = new();
    
    // 指标数据
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
    private List<(int index, double price, bool isBuy)> _tradeMarkers = new();

    // 当前周期(分钟)
    private int _currentPeriodMinutes = 1;

    #region Incremental Update Support (Replay Mode)

    // Reusable plottables for incremental updates (never recreated during replay)
    private CandlestickPlot? _candlePlot;
    private ScottPlot.Plottables.Signal? _ma5Plot;
    private ScottPlot.Plottables.Signal? _ma10Plot;
    private ScottPlot.Plottables.Signal? _ma20Plot;
    private ScottPlot.Plottables.Signal? _ma60Plot;
    private ScottPlot.Plottables.Signal? _bollUpperPlot;
    private ScottPlot.Plottables.Signal? _bollMiddlePlot;
    private ScottPlot.Plottables.Signal? _bollLowerPlot;

    // Display data for incremental updates (updated in place)
    private List<OHLC> _displayData = new();
    private double[] _ma5DisplayData = Array.Empty<double>();
    private double[] _ma10DisplayData = Array.Empty<double>();
    private double[] _ma20DisplayData = Array.Empty<double>();
    private double[] _ma60DisplayData = Array.Empty<double>();

    // Trade markers for replay mode (persisted until reset)
    private readonly List<Marker> _replayTradeMarkers = new();
    private readonly TradeMarkerManager _tradeMarkerManager = new();

    // Flag to indicate if plottables are initialized for incremental mode
    private bool _incrementalModeInitialized = false;

    #endregion

    public CandlestickChartControl()
    {
        InitializeComponent();
        InitializeCharts();
        SetupMouseTracking();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChartViewModel oldVm)
        {
            oldVm.IndicatorOptionsChanged -= OnIndicatorOptionsChanged;
            oldVm.PeriodChanged -= OnPeriodChanged;
            oldVm.TimeRangeChanged -= OnTimeRangeChanged;
        }
        if (e.NewValue is ChartViewModel newVm)
        {
            newVm.IndicatorOptionsChanged += OnIndicatorOptionsChanged;
            newVm.PeriodChanged += OnPeriodChanged;
            newVm.TimeRangeChanged += OnTimeRangeChanged;
        }
    }

    private void OnIndicatorOptionsChanged(object? sender, EventArgs e)
    {
        RenderMainChart();
    }

    private void OnPeriodChanged(object? sender, int periodMinutes)
    {
        _currentPeriodMinutes = periodMinutes;
        if (_rawTicks.Count > 0)
        {
            RegenerateOhlcFromTicks();
            CalculateIndicators();
            RenderAllCharts();
            UpdateTimeRangeInViewModel();
        }
    }

    private void OnTimeRangeChanged(object? sender, TimeRangeChangedEventArgs e)
    {
        if (_ohlcData.Count == 0) return;

        switch (e.NavigationType)
        {
            case TimeNavigationType.DatePicker:
                NavigateToDate(e.SelectedDate);
                break;
            case TimeNavigationType.Slider:
                NavigateBySlider(e.SliderValue);
                break;
            case TimeNavigationType.GoToLatest:
                NavigateToEnd();
                break;
            case TimeNavigationType.GoToEarliest:
                NavigateToStart();
                break;
            case TimeNavigationType.PageForward:
                PageForward();
                break;
            case TimeNavigationType.PageBackward:
                PageBackward();
                break;
        }
    }

    // 周期选择变更
    private void PeriodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeriodComboBox.SelectedIndex < 0) return;
        
        int[] periods = { 1, 5, 15, 30, 60, 1440, 10080 };
        _currentPeriodMinutes = periods[PeriodComboBox.SelectedIndex];
        
        if (_rawTicks.Count > 0)
        {
            RegenerateOhlcFromTicks();
            CalculateIndicators();
            RenderAllCharts();
        }
    }

    // 指标复选框点击
    private void IndicatorCheckBox_Click(object sender, RoutedEventArgs e)
    {
        RenderMainChart();
    }

    private void InitializeCharts()
    {
        ConfigureChart(MainChart.Plot);
        ConfigureChart(VolumeChart.Plot);
        ConfigureChart(MacdChart.Plot);

        _mainCrosshair = MainChart.Plot.Add.Crosshair(0, 0);
        _mainCrosshair.IsVisible = false;
        _mainCrosshair.LineColor = ScottPlot.Color.FromHex("#666666");
        
        _volumeCrosshair = VolumeChart.Plot.Add.Crosshair(0, 0);
        _volumeCrosshair.IsVisible = false;
        _volumeCrosshair.LineColor = ScottPlot.Color.FromHex("#666666");
        
        _macdCrosshair = MacdChart.Plot.Add.Crosshair(0, 0);
        _macdCrosshair.IsVisible = false;
        _macdCrosshair.LineColor = ScottPlot.Color.FromHex("#666666");

        MainChart.Interaction.Enable();
        VolumeChart.Interaction.Enable();
        MacdChart.Interaction.Enable();
    }

    private void ConfigureChart(Plot plot)
    {
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1a1a2e");
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#16213e");
        plot.Axes.Color(ScottPlot.Color.FromHex("#8892b0"));
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#2a2a4a");
        plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#1f1f3a");
        plot.Grid.MajorLineWidth = 1;
        plot.Grid.IsVisible = true;
        plot.Title(string.Empty);
    }

    private void SetupMouseTracking()
    {
        MainChart.MouseMove += OnChartMouseMove;
        MainChart.MouseLeave += OnChartMouseLeave;
        VolumeChart.MouseMove += OnChartMouseMove;
        VolumeChart.MouseLeave += OnChartMouseLeave;
        MacdChart.MouseMove += OnChartMouseMove;
        MacdChart.MouseLeave += OnChartMouseLeave;
    }

    private void OnChartMouseMove(object sender, MouseEventArgs e)
    {
        var chart = sender as ScottPlot.WPF.WpfPlot;
        if (chart == null) return;
        
        var position = e.GetPosition(chart);
        var pixel = new Pixel((float)position.X, (float)position.Y);
        var coordinates = chart.Plot.GetCoordinates(pixel);

        UpdateCrosshairs(coordinates.X);
        UpdateCrosshairInfo((int)Math.Round(coordinates.X));
        CrosshairInfo.Visibility = Visibility.Visible;
    }

    private void OnChartMouseLeave(object sender, MouseEventArgs e)
    {
        if (_mainCrosshair != null) _mainCrosshair.IsVisible = false;
        if (_volumeCrosshair != null) _volumeCrosshair.IsVisible = false;
        if (_macdCrosshair != null) _macdCrosshair.IsVisible = false;
        CrosshairInfo.Visibility = Visibility.Collapsed;
        RefreshAllCharts();
    }

    private void UpdateCrosshairs(double x)
    {
        if (_mainCrosshair != null) { _mainCrosshair.IsVisible = true; _mainCrosshair.X = x; }
        if (_volumeCrosshair != null) { _volumeCrosshair.IsVisible = true; _volumeCrosshair.X = x; }
        if (_macdCrosshair != null) { _macdCrosshair.IsVisible = true; _macdCrosshair.X = x; }
        RefreshAllCharts();
    }

    private void UpdateCrosshairInfo(int index)
    {
        if (index < 0 || index >= _ohlcData.Count) return;
        var ohlc = _ohlcData[index];
        CrosshairTime.Text = ohlc.DateTime.ToString("yyyy-MM-dd HH:mm");
        CrosshairOpen.Text = ohlc.Open.ToString("F2");
        CrosshairHigh.Text = ohlc.High.ToString("F2");
        CrosshairLow.Text = ohlc.Low.ToString("F2");
        CrosshairClose.Text = ohlc.Close.ToString("F2");
        
        var change = ohlc.Close - ohlc.Open;
        CrosshairClose.Foreground = change >= 0 
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 83, 80))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 166, 154));
    }

    private void RefreshAllCharts()
    {
        MainChart.Refresh();
        VolumeChart.Refresh();
        MacdChart.Refresh();
    }


    /// <summary>
    /// 设置原始tick数据
    /// </summary>
    public void SetRawTicks(List<(DateTime time, double price, double volume)> ticks)
    {
        _rawTicks = ticks;
        RegenerateOhlcFromTicks();
        CalculateIndicators();
        InitializeViewRange();
        RenderAllCharts();
        UpdateTimeRangeInViewModel();
    }

    /// <summary>
    /// 更新OHLC数据(直接设置)
    /// </summary>
    public void UpdateOhlcData(List<OHLC> data)
    {
        _ohlcData = data;
        _dateTimes = data.Select(o => o.DateTime).ToList();
        
        // 同时保存为原始数据以支持周期切换
        _rawTicks = data.Select(o => (o.DateTime, o.Close, 0.0)).ToList();
        
        CalculateIndicators();
        InitializeViewRange();
        RenderAllCharts();
        UpdateTimeRangeInViewModel();
    }

    /// <summary>
    /// 初始化视图范围（显示最新数据）
    /// </summary>
    private void InitializeViewRange()
    {
        if (_ohlcData.Count == 0) return;

        _viewEndIndex = _ohlcData.Count - 1;
        _viewStartIndex = Math.Max(0, _viewEndIndex - _visibleBars);
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
    /// 更新指标数据(外部计算)
    /// </summary>
    public void UpdateIndicators(
        List<double> ma5, List<double> ma10, List<double> ma20, List<double> ma60,
        List<double> bollUpper, List<double> bollMiddle, List<double> bollLower,
        List<double> macdDif, List<double> macdDea, List<double> macdHistogram)
    {
        _ma5 = ma5; _ma10 = ma10; _ma20 = ma20; _ma60 = ma60;
        _bollUpper = bollUpper; _bollMiddle = bollMiddle; _bollLower = bollLower;
        _macdDif = macdDif; _macdDea = macdDea; _macdHistogram = macdHistogram;
        RenderMainChart();
        RenderMacdChart();
    }

    /// <summary>
    /// 从原始tick重新生成OHLC
    /// </summary>
    private void RegenerateOhlcFromTicks()
    {
        if (_rawTicks.Count == 0) return;

        var grouped = new Dictionary<DateTime, List<(double price, double volume)>>();
        
        foreach (var tick in _rawTicks)
        {
            DateTime key;
            if (_currentPeriodMinutes >= 1440) // 日线或周线
            {
                key = tick.time.Date;
                if (_currentPeriodMinutes >= 10080) // 周线
                {
                    int diff = (7 + (tick.time.DayOfWeek - DayOfWeek.Monday)) % 7;
                    key = tick.time.Date.AddDays(-diff);
                }
            }
            else
            {
                int totalMinutes = tick.time.Hour * 60 + tick.time.Minute;
                int periodStart = (totalMinutes / _currentPeriodMinutes) * _currentPeriodMinutes;
                key = tick.time.Date.AddMinutes(periodStart);
            }
            
            if (!grouped.ContainsKey(key))
                grouped[key] = new List<(double, double)>();
            grouped[key].Add((tick.price, tick.volume));
        }

        _ohlcData.Clear();
        _volumes.Clear();
        _dateTimes.Clear();

        foreach (var kvp in grouped.OrderBy(k => k.Key))
        {
            var prices = kvp.Value;
            if (prices.Count == 0) continue;
            
            var open = prices.First().price;
            var close = prices.Last().price;
            var high = prices.Max(p => p.price);
            var low = prices.Min(p => p.price);
            var vol = prices.Sum(p => p.volume);

            var timeSpan = _currentPeriodMinutes >= 1440 
                ? TimeSpan.FromDays(_currentPeriodMinutes / 1440.0)
                : TimeSpan.FromMinutes(_currentPeriodMinutes);
            
            _ohlcData.Add(new OHLC(open, high, low, close, kvp.Key, timeSpan));
            _volumes.Add(vol);
            _dateTimes.Add(kvp.Key);
        }
    }

    /// <summary>
    /// 计算所有指标
    /// </summary>
    private void CalculateIndicators()
    {
        if (_ohlcData.Count == 0) return;
        
        var closes = _ohlcData.Select(o => o.Close).ToList();
        
        _ma5 = CalculateMA(closes, 5);
        _ma10 = CalculateMA(closes, 10);
        _ma20 = CalculateMA(closes, 20);
        _ma60 = CalculateMA(closes, 60);
        
        (_bollUpper, _bollMiddle, _bollLower) = CalculateBollinger(closes, 20, 2);
        (_macdDif, _macdDea, _macdHistogram) = CalculateMACD(closes, 12, 26, 9);
    }

    private List<double> CalculateMA(List<double> data, int period)
    {
        var result = new List<double>();
        for (int i = 0; i < data.Count; i++)
        {
            if (i < period - 1) { result.Add(double.NaN); continue; }
            double sum = 0;
            for (int j = 0; j < period; j++) sum += data[i - j];
            result.Add(sum / period);
        }
        return result;
    }

    private (List<double>, List<double>, List<double>) CalculateBollinger(List<double> data, int period, double mult)
    {
        var upper = new List<double>();
        var middle = new List<double>();
        var lower = new List<double>();
        
        for (int i = 0; i < data.Count; i++)
        {
            if (i < period - 1) { upper.Add(double.NaN); middle.Add(double.NaN); lower.Add(double.NaN); continue; }
            var slice = data.Skip(i - period + 1).Take(period).ToList();
            var avg = slice.Average();
            var std = Math.Sqrt(slice.Sum(x => Math.Pow(x - avg, 2)) / period);
            middle.Add(avg);
            upper.Add(avg + mult * std);
            lower.Add(avg - mult * std);
        }
        return (upper, middle, lower);
    }

    private (List<double>, List<double>, List<double>) CalculateMACD(List<double> data, int fast, int slow, int signal)
    {
        var emaFast = CalculateEMA(data, fast);
        var emaSlow = CalculateEMA(data, slow);
        var dif = emaFast.Zip(emaSlow, (f, s) => f - s).ToList();
        var dea = CalculateEMA(dif, signal);
        var hist = dif.Zip(dea, (d, e) => (d - e) * 2).ToList();
        return (dif, dea, hist);
    }

    private List<double> CalculateEMA(List<double> data, int period)
    {
        var result = new List<double>();
        double mult = 2.0 / (period + 1);
        for (int i = 0; i < data.Count; i++)
        {
            if (i == 0) result.Add(data[i]);
            else result.Add((data[i] - result[i - 1]) * mult + result[i - 1]);
        }
        return result;
    }


    private void RenderAllCharts()
    {
        RenderMainChart();
        RenderVolumeChart();
        RenderMacdChart();
    }

    private void RenderMainChart()
    {
        MainChart.Plot.Clear();
        if (_ohlcData.Count == 0) return;

        // K线
        var candlestick = MainChart.Plot.Add.Candlestick(_ohlcData);

        // 均线 - 根据复选框状态
        bool showMa5 = Ma5Check?.IsChecked == true;
        bool showMa10 = Ma10Check?.IsChecked == true;
        bool showMa20 = Ma20Check?.IsChecked == true;
        bool showMa60 = Ma60Check?.IsChecked == true;
        bool showBoll = BollCheck?.IsChecked == true;
        bool showMarkers = TradeMarkersCheck?.IsChecked == true;

        if (showMa5 && _ma5.Count > 0) AddLine(_ma5, "#FFD700");
        if (showMa10 && _ma10.Count > 0) AddLine(_ma10, "#FF69B4");
        if (showMa20 && _ma20.Count > 0) AddLine(_ma20, "#00CED1");
        if (showMa60 && _ma60.Count > 0) AddLine(_ma60, "#9370DB");

        if (showBoll && _bollUpper.Count > 0)
        {
            AddLine(_bollUpper, "#4FC3F7");
            AddLine(_bollMiddle, "#4FC3F7");
            AddLine(_bollLower, "#4FC3F7");
        }

        if (showMarkers)
        {
            // Get colors from ColorSchemeService for consistency
            var colorService = ColorSchemeService.Instance;
            var buyColor = colorService.UpColor;   // Red in China scheme (buy = up)
            var sellColor = colorService.DownColor; // Green in China scheme (sell = down)
            
            // Convert WPF colors to ScottPlot colors
            var scottBuyColor = new ScottPlot.Color(buyColor.R, buyColor.G, buyColor.B, buyColor.A);
            var scottSellColor = new ScottPlot.Color(sellColor.R, sellColor.G, sellColor.B, sellColor.A);
            
            foreach (var (index, price, isBuy) in _tradeMarkers)
            {
                var marker = MainChart.Plot.Add.Marker(index, price);
                marker.Shape = isBuy ? MarkerShape.TriUp : MarkerShape.TriDown;
                marker.Size = 12;
                marker.Color = isBuy ? scottBuyColor : scottSellColor;
            }
        }

        _mainCrosshair = MainChart.Plot.Add.Crosshair(0, 0);
        _mainCrosshair.IsVisible = false;
        _mainCrosshair.LineColor = ScottPlot.Color.FromHex("#666666");

        MainChart.Plot.Axes.AutoScale();
        MainChart.Refresh();
    }

    private void AddLine(List<double> data, string color)
    {
        var clean = data.Select(v => double.IsNaN(v) ? 0 : v).ToArray();
        var sig = MainChart.Plot.Add.Signal(clean);
        sig.Color = ScottPlot.Color.FromHex(color);
        sig.LineWidth = 1.5f;
    }

    private void RenderVolumeChart()
    {
        VolumeChart.Plot.Clear();
        if (_volumes.Count == 0 || _ohlcData.Count == 0) return;

        var bars = new List<Bar>();
        for (int i = 0; i < _volumes.Count && i < _ohlcData.Count; i++)
        {
            bool isUp = _ohlcData[i].Close >= _ohlcData[i].Open;
            bars.Add(new Bar
            {
                Position = i,
                Value = _volumes[i],
                FillColor = isUp ? ScottPlot.Color.FromHex("#ef5350") : ScottPlot.Color.FromHex("#26a69a")
            });
        }
        VolumeChart.Plot.Add.Bars(bars);

        _volumeCrosshair = VolumeChart.Plot.Add.Crosshair(0, 0);
        _volumeCrosshair.IsVisible = false;
        _volumeCrosshair.LineColor = ScottPlot.Color.FromHex("#666666");

        VolumeChart.Plot.Axes.AutoScale();
        VolumeChart.Refresh();
    }

    private void RenderMacdChart()
    {
        MacdChart.Plot.Clear();
        if (_macdDif.Count == 0) return;

        var difData = _macdDif.Select(v => double.IsNaN(v) ? 0 : v).ToArray();
        var deaData = _macdDea.Select(v => double.IsNaN(v) ? 0 : v).ToArray();

        var difSig = MacdChart.Plot.Add.Signal(difData);
        difSig.Color = ScottPlot.Color.FromHex("#FFD700");
        difSig.LineWidth = 1.5f;

        var deaSig = MacdChart.Plot.Add.Signal(deaData);
        deaSig.Color = ScottPlot.Color.FromHex("#00CED1");
        deaSig.LineWidth = 1.5f;

        var macdBars = new List<Bar>();
        for (int i = 0; i < _macdHistogram.Count; i++)
        {
            var val = double.IsNaN(_macdHistogram[i]) ? 0 : _macdHistogram[i];
            macdBars.Add(new Bar
            {
                Position = i,
                Value = val,
                FillColor = val >= 0 ? ScottPlot.Color.FromHex("#ef5350") : ScottPlot.Color.FromHex("#26a69a")
            });
        }
        MacdChart.Plot.Add.Bars(macdBars);

        var zeroLine = MacdChart.Plot.Add.HorizontalLine(0);
        zeroLine.Color = ScottPlot.Color.FromHex("#555555");
        zeroLine.LineWidth = 1;

        _macdCrosshair = MacdChart.Plot.Add.Crosshair(0, 0);
        _macdCrosshair.IsVisible = false;
        _macdCrosshair.LineColor = ScottPlot.Color.FromHex("#666666");

        MacdChart.Plot.Axes.AutoScale();
        MacdChart.Refresh();
    }

    public void AddTradeMarker(int index, double price, bool isBuy)
    {
        _tradeMarkers.Add((index, price, isBuy));
        RenderMainChart();
    }

    /// <summary>
    /// Add a trade marker using Signal enum (for replay mode).
    /// Supports color-coded buy/sell markers per Requirements 6.1, 6.2, 6.3.
    /// Buy signals display upward arrow markers in UpColor (red in China scheme).
    /// Sell signals display downward arrow markers in DownColor (green in China scheme).
    /// </summary>
    /// <param name="barIndex">The bar index where the signal occurred</param>
    /// <param name="signal">The trading signal (Buy or Sell)</param>
    /// <param name="price">The price at which the signal was generated</param>
    public void AddTradeMarker(int barIndex, Strategy.Signal signal, double price)
    {
        if (signal == Strategy.Signal.None) return;

        bool isBuy = signal == Strategy.Signal.Buy || signal == Strategy.Signal.CloseLong;
        
        // Add to internal tracking
        _tradeMarkers.Add((barIndex, price, isBuy));
        
        // Add to TradeMarkerManager for persistence tracking
        if (isBuy)
            _tradeMarkerManager.AddBuy(barIndex, price, 0, DateTime.Now);
        else
            _tradeMarkerManager.AddSell(barIndex, price, 0, DateTime.Now);

        // Get colors from ColorSchemeService for consistency
        var colorService = ColorSchemeService.Instance;
        var buyColor = colorService.UpColor;   // Red in China scheme (buy = up)
        var sellColor = colorService.DownColor; // Green in China scheme (sell = down)
        
        // Convert WPF colors to ScottPlot colors
        var scottBuyColor = new ScottPlot.Color(buyColor.R, buyColor.G, buyColor.B, buyColor.A);
        var scottSellColor = new ScottPlot.Color(sellColor.R, sellColor.G, sellColor.B, sellColor.A);

        // If in incremental mode, add marker directly without full redraw
        if (_incrementalModeInitialized)
        {
            var marker = MainChart.Plot.Add.Marker(barIndex, price);
            marker.Shape = isBuy ? MarkerShape.TriUp : MarkerShape.TriDown;
            marker.Size = 12;
            marker.Color = isBuy ? scottBuyColor : scottSellColor;
            _replayTradeMarkers.Add(marker);
            MainChart.Refresh();
        }
        else
        {
            RenderMainChart();
        }
    }

    /// <summary>
    /// Add a trade marker from TradeMarker model.
    /// </summary>
    public void AddTradeMarker(TradeMarker marker)
    {
        AddTradeMarker(marker.BarIndex, marker.IsBuy ? Strategy.Signal.Buy : Strategy.Signal.Sell, marker.Price);
    }

    public void ClearTradeMarkers()
    {
        _tradeMarkers.Clear();
        _tradeMarkerManager.Clear();
        
        // Clear replay markers from plot
        foreach (var marker in _replayTradeMarkers)
        {
            MainChart.Plot.Remove(marker);
        }
        _replayTradeMarkers.Clear();
        
        RenderMainChart();
    }

    /// <summary>
    /// Get all trade markers (for persistence/export).
    /// </summary>
    public IReadOnlyList<TradeMarker> GetTradeMarkers() => _tradeMarkerManager.Markers;

    /// <summary>
    /// Get the trade marker manager for external access.
    /// </summary>
    public TradeMarkerManager TradeMarkerManager => _tradeMarkerManager;

    public void Clear()
    {
        _rawTicks.Clear();
        _ohlcData.Clear();
        _volumes.Clear();
        _dateTimes.Clear();
        _ma5.Clear(); _ma10.Clear(); _ma20.Clear(); _ma60.Clear();
        _bollUpper.Clear(); _bollMiddle.Clear(); _bollLower.Clear();
        _macdDif.Clear(); _macdDea.Clear(); _macdHistogram.Clear();
        _tradeMarkers.Clear();
        _tradeMarkerManager.Clear();
        
        // Clear incremental mode data
        _displayData.Clear();
        _replayTradeMarkers.Clear();
        _incrementalModeInitialized = false;
        _candlePlot = null;
        _ma5Plot = null;
        _ma10Plot = null;
        _ma20Plot = null;
        _ma60Plot = null;
        _bollUpperPlot = null;
        _bollMiddlePlot = null;
        _bollLowerPlot = null;
        
        MainChart.Plot.Clear();
        VolumeChart.Plot.Clear();
        MacdChart.Plot.Clear();
        RefreshAllCharts();
    }

    #region Incremental Update Methods (Replay Mode)

    /// <summary>
    /// Initialize reusable plottable objects for incremental updates.
    /// Call this once before starting replay mode.
    /// This avoids recreating plottables during replay, improving performance.
    /// Requirements: 9.7, 9.8 - Reuse Plottable objects and avoid Plot.Clear()
    /// </summary>
    public void InitializePlottables()
    {
        MainChart.Plot.Clear();
        VolumeChart.Plot.Clear();
        MacdChart.Plot.Clear();
        
        _displayData.Clear();
        _replayTradeMarkers.Clear();
        _tradeMarkers.Clear();
        _tradeMarkerManager.Clear();
        
        // Initialize with empty data - plottables will be updated incrementally
        // Note: ScottPlot requires at least one data point, so we'll add plottables on first AppendBar
        _incrementalModeInitialized = true;
        
        // Add crosshairs
        _mainCrosshair = MainChart.Plot.Add.Crosshair(0, 0);
        _mainCrosshair.IsVisible = false;
        _mainCrosshair.LineColor = ScottPlot.Color.FromHex("#666666");
        
        _volumeCrosshair = VolumeChart.Plot.Add.Crosshair(0, 0);
        _volumeCrosshair.IsVisible = false;
        _volumeCrosshair.LineColor = ScottPlot.Color.FromHex("#666666");
        
        _macdCrosshair = MacdChart.Plot.Add.Crosshair(0, 0);
        _macdCrosshair.IsVisible = false;
        _macdCrosshair.LineColor = ScottPlot.Color.FromHex("#666666");
        
        RefreshAllCharts();
    }

    /// <summary>
    /// Append a single bar during replay (incremental update).
    /// This method adds one bar without redrawing the entire chart.
    /// Requirements: 9.1, 9.2 - Append bar instead of redrawing all data
    /// </summary>
    /// <param name="bar">The OHLC bar to append</param>
    public void AppendBar(OHLC bar)
    {
        if (!_incrementalModeInitialized)
        {
            InitializePlottables();
        }
        
        _displayData.Add(bar);
        _ohlcData.Add(bar);
        _dateTimes.Add(bar.DateTime);
        
        // Update moving averages incrementally
        UpdateMovingAveragesIncremental();
        
        // Update or create candlestick plot
        if (_candlePlot == null)
        {
            _candlePlot = MainChart.Plot.Add.Candlestick(_displayData);
        }
        // Note: ScottPlot's Candlestick plot automatically reflects changes to the underlying list
        
        // Auto-scroll to show latest bar
        int count = _displayData.Count;
        int visibleCount = Math.Min(count, _visibleBars);
        int startIndex = Math.Max(0, count - visibleCount);
        
        MainChart.Plot.Axes.SetLimitsX(startIndex - 0.5, count + 0.5);
        
        // Auto-scale Y axis for visible range
        if (count > 0)
        {
            var visibleBars = _displayData.Skip(startIndex).Take(visibleCount).ToList();
            if (visibleBars.Count > 0)
            {
                double minPrice = visibleBars.Min(o => o.Low);
                double maxPrice = visibleBars.Max(o => o.High);
                double padding = (maxPrice - minPrice) * 0.1;
                if (padding < 0.01) padding = maxPrice * 0.01; // Minimum padding
                MainChart.Plot.Axes.SetLimitsY(minPrice - padding, maxPrice + padding);
            }
        }
        
        MainChart.Refresh();
        
        // Update view indices for navigation
        _viewEndIndex = count - 1;
        _viewStartIndex = startIndex;
    }

    /// <summary>
    /// Append a bar with volume data.
    /// </summary>
    public void AppendBar(OHLC bar, double volume)
    {
        AppendBar(bar);
        _volumes.Add(volume);
        
        // Update volume chart
        RenderVolumeChart();
    }

    /// <summary>
    /// Batch update for seek operations.
    /// Sets the visible range to the specified bars without full redraw.
    /// Requirements: 9.6 - Batch-update visible bars only when seeking
    /// </summary>
    /// <param name="bars">The bars to display</param>
    public void SetVisibleRange(List<OHLC> bars)
    {
        if (!_incrementalModeInitialized)
        {
            InitializePlottables();
        }
        
        _displayData.Clear();
        _displayData.AddRange(bars);
        
        // Also update the main OHLC data
        _ohlcData.Clear();
        _ohlcData.AddRange(bars);
        _dateTimes = bars.Select(b => b.DateTime).ToList();
        
        // Recalculate indicators for the new range
        CalculateIndicators();
        
        // Update or create candlestick plot
        if (_candlePlot == null && _displayData.Count > 0)
        {
            _candlePlot = MainChart.Plot.Add.Candlestick(_displayData);
        }
        
        // Set axis limits
        int count = _displayData.Count;
        MainChart.Plot.Axes.SetLimitsX(-0.5, count + 0.5);
        
        if (count > 0)
        {
            double minPrice = _displayData.Min(o => o.Low);
            double maxPrice = _displayData.Max(o => o.High);
            double padding = (maxPrice - minPrice) * 0.1;
            if (padding < 0.01) padding = maxPrice * 0.01;
            MainChart.Plot.Axes.SetLimitsY(minPrice - padding, maxPrice + padding);
        }
        
        // Re-render indicators
        RenderMainChartIndicatorsOnly();
        
        MainChart.Refresh();
        
        // Update view indices
        _viewStartIndex = 0;
        _viewEndIndex = count - 1;
    }

    /// <summary>
    /// Set visible range with volume data.
    /// </summary>
    public void SetVisibleRange(List<OHLC> bars, List<double> volumes)
    {
        SetVisibleRange(bars);
        _volumes.Clear();
        _volumes.AddRange(volumes);
        RenderVolumeChart();
    }

    /// <summary>
    /// Update moving averages incrementally (only calculate for the new bar).
    /// </summary>
    private void UpdateMovingAveragesIncremental()
    {
        if (_displayData.Count == 0) return;
        
        var closes = _displayData.Select(o => o.Close).ToList();
        int idx = closes.Count - 1;
        
        // MA5
        if (idx >= 4)
        {
            double sum = 0;
            for (int j = 0; j < 5; j++) sum += closes[idx - j];
            if (_ma5.Count <= idx) _ma5.Add(sum / 5);
            else _ma5[idx] = sum / 5;
        }
        else if (_ma5.Count <= idx)
        {
            _ma5.Add(double.NaN);
        }
        
        // MA10
        if (idx >= 9)
        {
            double sum = 0;
            for (int j = 0; j < 10; j++) sum += closes[idx - j];
            if (_ma10.Count <= idx) _ma10.Add(sum / 10);
            else _ma10[idx] = sum / 10;
        }
        else if (_ma10.Count <= idx)
        {
            _ma10.Add(double.NaN);
        }
        
        // MA20
        if (idx >= 19)
        {
            double sum = 0;
            for (int j = 0; j < 20; j++) sum += closes[idx - j];
            if (_ma20.Count <= idx) _ma20.Add(sum / 20);
            else _ma20[idx] = sum / 20;
        }
        else if (_ma20.Count <= idx)
        {
            _ma20.Add(double.NaN);
        }
        
        // MA60
        if (idx >= 59)
        {
            double sum = 0;
            for (int j = 0; j < 60; j++) sum += closes[idx - j];
            if (_ma60.Count <= idx) _ma60.Add(sum / 60);
            else _ma60[idx] = sum / 60;
        }
        else if (_ma60.Count <= idx)
        {
            _ma60.Add(double.NaN);
        }
    }

    /// <summary>
    /// Render only the indicator lines without clearing the chart.
    /// Used during incremental updates to avoid full redraw.
    /// </summary>
    private void RenderMainChartIndicatorsOnly()
    {
        // Remove old indicator plots if they exist
        if (_ma5Plot != null) MainChart.Plot.Remove(_ma5Plot);
        if (_ma10Plot != null) MainChart.Plot.Remove(_ma10Plot);
        if (_ma20Plot != null) MainChart.Plot.Remove(_ma20Plot);
        if (_ma60Plot != null) MainChart.Plot.Remove(_ma60Plot);
        if (_bollUpperPlot != null) MainChart.Plot.Remove(_bollUpperPlot);
        if (_bollMiddlePlot != null) MainChart.Plot.Remove(_bollMiddlePlot);
        if (_bollLowerPlot != null) MainChart.Plot.Remove(_bollLowerPlot);
        
        bool showMa5 = Ma5Check?.IsChecked == true;
        bool showMa10 = Ma10Check?.IsChecked == true;
        bool showMa20 = Ma20Check?.IsChecked == true;
        bool showMa60 = Ma60Check?.IsChecked == true;
        bool showBoll = BollCheck?.IsChecked == true;
        
        if (showMa5 && _ma5.Count > 0)
        {
            var clean = _ma5.Select(v => double.IsNaN(v) ? 0 : v).ToArray();
            _ma5Plot = MainChart.Plot.Add.Signal(clean);
            _ma5Plot.Color = ScottPlot.Color.FromHex("#FFD700");
            _ma5Plot.LineWidth = 1.5f;
        }
        
        if (showMa10 && _ma10.Count > 0)
        {
            var clean = _ma10.Select(v => double.IsNaN(v) ? 0 : v).ToArray();
            _ma10Plot = MainChart.Plot.Add.Signal(clean);
            _ma10Plot.Color = ScottPlot.Color.FromHex("#FF69B4");
            _ma10Plot.LineWidth = 1.5f;
        }
        
        if (showMa20 && _ma20.Count > 0)
        {
            var clean = _ma20.Select(v => double.IsNaN(v) ? 0 : v).ToArray();
            _ma20Plot = MainChart.Plot.Add.Signal(clean);
            _ma20Plot.Color = ScottPlot.Color.FromHex("#00CED1");
            _ma20Plot.LineWidth = 1.5f;
        }
        
        if (showMa60 && _ma60.Count > 0)
        {
            var clean = _ma60.Select(v => double.IsNaN(v) ? 0 : v).ToArray();
            _ma60Plot = MainChart.Plot.Add.Signal(clean);
            _ma60Plot.Color = ScottPlot.Color.FromHex("#9370DB");
            _ma60Plot.LineWidth = 1.5f;
        }
        
        if (showBoll && _bollUpper.Count > 0)
        {
            var upperClean = _bollUpper.Select(v => double.IsNaN(v) ? 0 : v).ToArray();
            var middleClean = _bollMiddle.Select(v => double.IsNaN(v) ? 0 : v).ToArray();
            var lowerClean = _bollLower.Select(v => double.IsNaN(v) ? 0 : v).ToArray();
            
            _bollUpperPlot = MainChart.Plot.Add.Signal(upperClean);
            _bollUpperPlot.Color = ScottPlot.Color.FromHex("#4FC3F7");
            _bollUpperPlot.LineWidth = 1.5f;
            
            _bollMiddlePlot = MainChart.Plot.Add.Signal(middleClean);
            _bollMiddlePlot.Color = ScottPlot.Color.FromHex("#4FC3F7");
            _bollMiddlePlot.LineWidth = 1.5f;
            
            _bollLowerPlot = MainChart.Plot.Add.Signal(lowerClean);
            _bollLowerPlot.Color = ScottPlot.Color.FromHex("#4FC3F7");
            _bollLowerPlot.LineWidth = 1.5f;
        }
    }

    /// <summary>
    /// Check if incremental mode is initialized.
    /// </summary>
    public bool IsIncrementalModeInitialized => _incrementalModeInitialized;

    /// <summary>
    /// Get the current display data count.
    /// </summary>
    public int DisplayDataCount => _displayData.Count;

    /// <summary>
    /// Reset incremental mode (call before starting a new replay).
    /// Clears all data but preserves chart configuration.
    /// </summary>
    public void ResetIncrementalMode()
    {
        _displayData.Clear();
        _ohlcData.Clear();
        _volumes.Clear();
        _dateTimes.Clear();
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
        
        // Clear trade markers but keep them tracked for persistence
        foreach (var marker in _replayTradeMarkers)
        {
            MainChart.Plot.Remove(marker);
        }
        _replayTradeMarkers.Clear();
        _tradeMarkers.Clear();
        _tradeMarkerManager.Clear();
        
        // Reset plottable references
        _candlePlot = null;
        _ma5Plot = null;
        _ma10Plot = null;
        _ma20Plot = null;
        _ma60Plot = null;
        _bollUpperPlot = null;
        _bollMiddlePlot = null;
        _bollLowerPlot = null;
        
        // Re-initialize
        InitializePlottables();
    }

    #endregion

    #region 时间导航

    // 当前视图的起始和结束索引
    private int _viewStartIndex = 0;
    private int _viewEndIndex = 0;
    private int _visibleBars = 100; // 默认显示100根K线

    /// <summary>
    /// 更新 ViewModel 中的时间范围信息
    /// </summary>
    private void UpdateTimeRangeInViewModel()
    {
        if (DataContext is not ChartViewModel vm || _ohlcData.Count == 0) return;

        vm.DataStartDate = _ohlcData.First().DateTime;
        vm.DataEndDate = _ohlcData.Last().DateTime;
        vm.HasData = true;

        if (_viewEndIndex > 0 && _viewEndIndex < _ohlcData.Count)
        {
            vm.ViewStartTime = _ohlcData[_viewStartIndex].DateTime;
            vm.ViewEndTime = _ohlcData[_viewEndIndex].DateTime;
        }
    }

    /// <summary>
    /// 日期选择器变更
    /// </summary>
    private void DateSelector_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DateSelector.SelectedDate.HasValue && _ohlcData.Count > 0)
        {
            NavigateToDate(DateSelector.SelectedDate.Value);
        }
    }

    /// <summary>
    /// 时间滑块变更
    /// </summary>
    private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ohlcData.Count > 0 && !_isNavigating)
        {
            NavigateBySlider(e.NewValue);
        }
    }

    private bool _isNavigating = false;

    /// <summary>
    /// 导航到指定日期
    /// </summary>
    private void NavigateToDate(DateTime targetDate)
    {
        if (_ohlcData.Count == 0) return;

        // 找到最接近目标日期的K线索引
        int targetIndex = _ohlcData.FindIndex(o => o.DateTime.Date >= targetDate.Date);
        if (targetIndex < 0) targetIndex = _ohlcData.Count - 1;

        // 设置视图范围，以目标日期为中心
        _viewEndIndex = Math.Min(targetIndex + _visibleBars / 2, _ohlcData.Count - 1);
        _viewStartIndex = Math.Max(_viewEndIndex - _visibleBars, 0);

        ApplyViewRange();
        UpdateSliderPosition();
    }

    /// <summary>
    /// 通过滑块导航
    /// </summary>
    private void NavigateBySlider(double sliderValue)
    {
        if (_ohlcData.Count == 0) return;

        // 滑块值 0-100 映射到数据范围
        double ratio = sliderValue / 100.0;
        int maxStartIndex = Math.Max(0, _ohlcData.Count - _visibleBars);
        _viewStartIndex = (int)(maxStartIndex * ratio);
        _viewEndIndex = Math.Min(_viewStartIndex + _visibleBars, _ohlcData.Count - 1);

        ApplyViewRange();
        UpdateViewModelTimeRange();
    }

    /// <summary>
    /// 导航到最新数据
    /// </summary>
    private void NavigateToEnd()
    {
        if (_ohlcData.Count == 0) return;

        _viewEndIndex = _ohlcData.Count - 1;
        _viewStartIndex = Math.Max(0, _viewEndIndex - _visibleBars);

        ApplyViewRange();
        UpdateSliderPosition();
    }

    /// <summary>
    /// 导航到最早数据
    /// </summary>
    private void NavigateToStart()
    {
        if (_ohlcData.Count == 0) return;

        _viewStartIndex = 0;
        _viewEndIndex = Math.Min(_visibleBars, _ohlcData.Count - 1);

        ApplyViewRange();
        UpdateSliderPosition();
    }

    /// <summary>
    /// 向前翻页
    /// </summary>
    private void PageBackward()
    {
        if (_ohlcData.Count == 0) return;

        int pageSize = _visibleBars * 3 / 4; // 翻页时保留25%重叠
        _viewStartIndex = Math.Max(0, _viewStartIndex - pageSize);
        _viewEndIndex = Math.Min(_viewStartIndex + _visibleBars, _ohlcData.Count - 1);

        ApplyViewRange();
        UpdateSliderPosition();
    }

    /// <summary>
    /// 向后翻页
    /// </summary>
    private void PageForward()
    {
        if (_ohlcData.Count == 0) return;

        int pageSize = _visibleBars * 3 / 4;
        _viewEndIndex = Math.Min(_ohlcData.Count - 1, _viewEndIndex + pageSize);
        _viewStartIndex = Math.Max(0, _viewEndIndex - _visibleBars);

        ApplyViewRange();
        UpdateSliderPosition();
    }

    /// <summary>
    /// 应用视图范围到图表
    /// </summary>
    private void ApplyViewRange()
    {
        if (_ohlcData.Count == 0) return;

        // 设置X轴范围
        MainChart.Plot.Axes.SetLimitsX(_viewStartIndex - 0.5, _viewEndIndex + 0.5);
        VolumeChart.Plot.Axes.SetLimitsX(_viewStartIndex - 0.5, _viewEndIndex + 0.5);
        MacdChart.Plot.Axes.SetLimitsX(_viewStartIndex - 0.5, _viewEndIndex + 0.5);

        // 自动调整Y轴范围
        AutoScaleYAxis();

        RefreshAllCharts();
        UpdateViewModelTimeRange();
    }

    /// <summary>
    /// 自动调整Y轴范围
    /// </summary>
    private void AutoScaleYAxis()
    {
        if (_ohlcData.Count == 0) return;

        // 主图Y轴
        var visibleOhlc = _ohlcData.Skip(_viewStartIndex).Take(_viewEndIndex - _viewStartIndex + 1).ToList();
        if (visibleOhlc.Count > 0)
        {
            double minPrice = visibleOhlc.Min(o => o.Low);
            double maxPrice = visibleOhlc.Max(o => o.High);
            double padding = (maxPrice - minPrice) * 0.05;
            MainChart.Plot.Axes.SetLimitsY(minPrice - padding, maxPrice + padding);
        }

        // 成交量Y轴
        if (_volumes.Count > 0)
        {
            var visibleVol = _volumes.Skip(_viewStartIndex).Take(_viewEndIndex - _viewStartIndex + 1).ToList();
            if (visibleVol.Count > 0)
            {
                double maxVol = visibleVol.Max();
                VolumeChart.Plot.Axes.SetLimitsY(0, maxVol * 1.1);
            }
        }

        // MACD Y轴
        if (_macdHistogram.Count > 0)
        {
            var visibleMacd = _macdHistogram.Skip(_viewStartIndex).Take(_viewEndIndex - _viewStartIndex + 1)
                .Where(v => !double.IsNaN(v)).ToList();
            if (visibleMacd.Count > 0)
            {
                double maxAbs = visibleMacd.Max(v => Math.Abs(v));
                MacdChart.Plot.Axes.SetLimitsY(-maxAbs * 1.2, maxAbs * 1.2);
            }
        }
    }

    /// <summary>
    /// 更新滑块位置
    /// </summary>
    private void UpdateSliderPosition()
    {
        if (DataContext is not ChartViewModel vm || _ohlcData.Count == 0) return;

        _isNavigating = true;
        int maxStartIndex = Math.Max(1, _ohlcData.Count - _visibleBars);
        vm.TimeSliderValue = (double)_viewStartIndex / maxStartIndex * 100;
        _isNavigating = false;
    }

    /// <summary>
    /// 更新 ViewModel 中的视图时间范围
    /// </summary>
    private void UpdateViewModelTimeRange()
    {
        if (DataContext is not ChartViewModel vm || _ohlcData.Count == 0) return;

        if (_viewStartIndex >= 0 && _viewStartIndex < _ohlcData.Count)
            vm.ViewStartTime = _ohlcData[_viewStartIndex].DateTime;
        if (_viewEndIndex >= 0 && _viewEndIndex < _ohlcData.Count)
            vm.ViewEndTime = _ohlcData[_viewEndIndex].DateTime;
    }

    // UI 事件处理
    private void GoToEarliest_Click(object sender, RoutedEventArgs e) => NavigateToStart();
    private void GoToLatest_Click(object sender, RoutedEventArgs e) => NavigateToEnd();
    private void PageBackward_Click(object sender, RoutedEventArgs e) => PageBackward();
    private void PageForward_Click(object sender, RoutedEventArgs e) => PageForward();

    #endregion
}
