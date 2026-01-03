using CommunityToolkit.Mvvm.ComponentModel;

namespace AegisQuant.UI.ViewModels;

/// <summary>
/// 图表 ViewModel - 控制指标显示选项
/// </summary>
public partial class ChartViewModel : ObservableObject
{
    #region 均线显示选项

    /// <summary>是否显示 MA5</summary>
    [ObservableProperty]
    private bool _showMa5 = true;

    /// <summary>是否显示 MA10</summary>
    [ObservableProperty]
    private bool _showMa10 = true;

    /// <summary>是否显示 MA20</summary>
    [ObservableProperty]
    private bool _showMa20 = true;

    /// <summary>是否显示 MA60</summary>
    [ObservableProperty]
    private bool _showMa60 = false;

    #endregion

    #region 其他指标显示选项

    /// <summary>是否显示布林带</summary>
    [ObservableProperty]
    private bool _showBollinger = false;

    /// <summary>是否显示买卖标记</summary>
    [ObservableProperty]
    private bool _showTradeMarkers = true;

    #endregion

    #region 副图指标选择

    /// <summary>选中的副图指标</summary>
    [ObservableProperty]
    private string _selectedSubIndicator = "MACD";

    #endregion

    #region 图表数据

    /// <summary>当前显示的 K 线数量</summary>
    [ObservableProperty]
    private int _visibleBars = 100;

    /// <summary>K 线周期 (分钟)</summary>
    [ObservableProperty]
    private int _periodMinutes = 1;

    /// <summary>是否自动滚动到最新</summary>
    [ObservableProperty]
    private bool _autoScroll = true;

    #endregion

    #region 十字光标数据

    /// <summary>十字光标时间</summary>
    [ObservableProperty]
    private DateTime _crosshairTime;

    /// <summary>十字光标开盘价</summary>
    [ObservableProperty]
    private double _crosshairOpen;

    /// <summary>十字光标最高价</summary>
    [ObservableProperty]
    private double _crosshairHigh;

    /// <summary>十字光标最低价</summary>
    [ObservableProperty]
    private double _crosshairLow;

    /// <summary>十字光标收盘价</summary>
    [ObservableProperty]
    private double _crosshairClose;

    /// <summary>十字光标成交量</summary>
    [ObservableProperty]
    private double _crosshairVolume;

    /// <summary>十字光标涨跌幅</summary>
    [ObservableProperty]
    private double _crosshairChangePercent;

    #endregion

    /// <summary>
    /// 指标显示选项变更事件
    /// </summary>
    public event EventHandler? IndicatorOptionsChanged;

    partial void OnShowMa5Changed(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowMa10Changed(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowMa20Changed(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowMa60Changed(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowBollingerChanged(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowTradeMarkersChanged(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnSelectedSubIndicatorChanged(string value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
}
