using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegisQuant.UI.ViewModels;

/// <summary>
/// 图表 ViewModel - 控制指标显示选项和周期切换
/// </summary>
public partial class ChartViewModel : ObservableObject
{
    #region 均线显示选项

    [ObservableProperty]
    private bool _showMa5 = true;

    [ObservableProperty]
    private bool _showMa10 = true;

    [ObservableProperty]
    private bool _showMa20 = true;

    [ObservableProperty]
    private bool _showMa60 = false;

    #endregion

    #region 其他指标显示选项

    [ObservableProperty]
    private bool _showBollinger = false;

    [ObservableProperty]
    private bool _showTradeMarkers = true;

    #endregion

    #region 周期选择

    [ObservableProperty]
    private int _selectedPeriodIndex = 0;

    [ObservableProperty]
    private string _selectedSubIndicator = "MACD";

    #endregion

    #region 时间导航

    /// <summary>
    /// 数据起始日期
    /// </summary>
    [ObservableProperty]
    private DateTime _dataStartDate = DateTime.Today.AddMonths(-1);

    /// <summary>
    /// 数据结束日期
    /// </summary>
    [ObservableProperty]
    private DateTime _dataEndDate = DateTime.Today;

    /// <summary>
    /// 当前选中的日期
    /// </summary>
    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    /// <summary>
    /// 当前视图起始时间
    /// </summary>
    [ObservableProperty]
    private DateTime _viewStartTime = DateTime.Today;

    /// <summary>
    /// 当前视图结束时间
    /// </summary>
    [ObservableProperty]
    private DateTime _viewEndTime = DateTime.Today;

    /// <summary>
    /// 时间滑块位置 (0-100)
    /// </summary>
    [ObservableProperty]
    private double _timeSliderValue = 100;

    /// <summary>
    /// 是否有数据加载
    /// </summary>
    [ObservableProperty]
    private bool _hasData = false;

    /// <summary>
    /// 显示的K线数量
    /// </summary>
    [ObservableProperty]
    private int _visibleBarsCount = 100;

    #endregion

    /// <summary>
    /// 指标显示选项变更事件
    /// </summary>
    public event EventHandler? IndicatorOptionsChanged;
    
    /// <summary>
    /// 周期变更事件
    /// </summary>
    public event EventHandler<int>? PeriodChanged;

    /// <summary>
    /// 时间范围变更事件
    /// </summary>
    public event EventHandler<TimeRangeChangedEventArgs>? TimeRangeChanged;

    partial void OnShowMa5Changed(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowMa10Changed(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowMa20Changed(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowMa60Changed(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowBollingerChanged(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnShowTradeMarkersChanged(bool value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    partial void OnSelectedSubIndicatorChanged(string value) => IndicatorOptionsChanged?.Invoke(this, EventArgs.Empty);
    
    partial void OnSelectedPeriodIndexChanged(int value)
    {
        // 周期映射: 0=1分钟, 1=5分钟, 2=15分钟, 3=30分钟, 4=1小时, 5=日线, 6=周线
        int[] periodMinutes = { 1, 5, 15, 30, 60, 1440, 10080 };
        int minutes = value >= 0 && value < periodMinutes.Length ? periodMinutes[value] : 1;
        PeriodChanged?.Invoke(this, minutes);
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        // 当选择日期变化时，触发时间范围变更
        TimeRangeChanged?.Invoke(this, new TimeRangeChangedEventArgs
        {
            SelectedDate = value,
            NavigationType = TimeNavigationType.DatePicker
        });
    }

    partial void OnTimeSliderValueChanged(double value)
    {
        // 当滑块值变化时，触发时间范围变更
        TimeRangeChanged?.Invoke(this, new TimeRangeChangedEventArgs
        {
            SliderValue = value,
            NavigationType = TimeNavigationType.Slider
        });
    }

    /// <summary>
    /// 设置数据时间范围
    /// </summary>
    public void SetDataTimeRange(DateTime start, DateTime end)
    {
        DataStartDate = start;
        DataEndDate = end;
        SelectedDate = end;
        ViewStartTime = start;
        ViewEndTime = end;
        HasData = true;
        TimeSliderValue = 100;
    }

    /// <summary>
    /// 跳转到最新
    /// </summary>
    [RelayCommand]
    private void GoToLatest()
    {
        SelectedDate = DataEndDate;
        TimeSliderValue = 100;
        TimeRangeChanged?.Invoke(this, new TimeRangeChangedEventArgs
        {
            NavigationType = TimeNavigationType.GoToLatest
        });
    }

    /// <summary>
    /// 跳转到最早
    /// </summary>
    [RelayCommand]
    private void GoToEarliest()
    {
        SelectedDate = DataStartDate;
        TimeSliderValue = 0;
        TimeRangeChanged?.Invoke(this, new TimeRangeChangedEventArgs
        {
            NavigationType = TimeNavigationType.GoToEarliest
        });
    }

    /// <summary>
    /// 向前翻页
    /// </summary>
    [RelayCommand]
    private void PageBackward()
    {
        TimeRangeChanged?.Invoke(this, new TimeRangeChangedEventArgs
        {
            NavigationType = TimeNavigationType.PageBackward
        });
    }

    /// <summary>
    /// 向后翻页
    /// </summary>
    [RelayCommand]
    private void PageForward()
    {
        TimeRangeChanged?.Invoke(this, new TimeRangeChangedEventArgs
        {
            NavigationType = TimeNavigationType.PageForward
        });
    }
}

/// <summary>
/// 时间范围变更事件参数
/// </summary>
public class TimeRangeChangedEventArgs : EventArgs
{
    public TimeNavigationType NavigationType { get; set; }
    public DateTime SelectedDate { get; set; }
    public double SliderValue { get; set; }
}

/// <summary>
/// 时间导航类型
/// </summary>
public enum TimeNavigationType
{
    DatePicker,
    Slider,
    GoToLatest,
    GoToEarliest,
    PageForward,
    PageBackward
}
