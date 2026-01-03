using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AegisQuant.UI.Services;

namespace AegisQuant.UI.Controls;

/// <summary>
/// 价格闪烁文本控件 - 当价格变化时显示闪烁动画
/// </summary>
public class FlashingTextBlock : TextBlock
{
    /// <summary>
    /// 价格值依赖属性
    /// </summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(FlashingTextBlock),
            new PropertyMetadata(0.0, OnValueChanged));

    /// <summary>
    /// 参考价格依赖属性 (用于计算涨跌颜色)
    /// </summary>
    public static readonly DependencyProperty ReferenceValueProperty =
        DependencyProperty.Register(
            nameof(ReferenceValue),
            typeof(double),
            typeof(FlashingTextBlock),
            new PropertyMetadata(0.0, OnReferenceValueChanged));

    /// <summary>
    /// 是否启用闪烁动画
    /// </summary>
    public static readonly DependencyProperty EnableFlashProperty =
        DependencyProperty.Register(
            nameof(EnableFlash),
            typeof(bool),
            typeof(FlashingTextBlock),
            new PropertyMetadata(true));

    /// <summary>
    /// 闪烁持续时间 (毫秒)
    /// </summary>
    public static readonly DependencyProperty FlashDurationProperty =
        DependencyProperty.Register(
            nameof(FlashDuration),
            typeof(int),
            typeof(FlashingTextBlock),
            new PropertyMetadata(500));

    /// <summary>
    /// 是否自动更新前景色
    /// </summary>
    public static readonly DependencyProperty AutoColorProperty =
        DependencyProperty.Register(
            nameof(AutoColor),
            typeof(bool),
            typeof(FlashingTextBlock),
            new PropertyMetadata(true));

    /// <summary>
    /// 价格值
    /// </summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// 参考价格 (用于计算涨跌颜色，如昨收价)
    /// </summary>
    public double ReferenceValue
    {
        get => (double)GetValue(ReferenceValueProperty);
        set => SetValue(ReferenceValueProperty, value);
    }

    /// <summary>
    /// 是否启用闪烁动画
    /// </summary>
    public bool EnableFlash
    {
        get => (bool)GetValue(EnableFlashProperty);
        set => SetValue(EnableFlashProperty, value);
    }

    /// <summary>
    /// 闪烁持续时间 (毫秒)
    /// </summary>
    public int FlashDuration
    {
        get => (int)GetValue(FlashDurationProperty);
        set => SetValue(FlashDurationProperty, value);
    }

    /// <summary>
    /// 是否自动根据涨跌更新前景色
    /// </summary>
    public bool AutoColor
    {
        get => (bool)GetValue(AutoColorProperty);
        set => SetValue(AutoColorProperty, value);
    }

    private double _previousValue;
    private bool _isInitialized;

    public FlashingTextBlock()
    {
        // 设置默认字体
        FontFamily = new FontFamily("Consolas, Microsoft YaHei UI");
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlashingTextBlock control)
        {
            var newValue = (double)e.NewValue;
            var oldValue = control._previousValue;

            // 首次设置值时不闪烁
            if (!control._isInitialized)
            {
                control._isInitialized = true;
                control._previousValue = newValue;
                control.UpdateForegroundColor();
                return;
            }

            // 检测价格变化
            if (Math.Abs(newValue - oldValue) > 0.0001)
            {
                bool isUp = newValue > oldValue;
                
                // 执行闪烁动画
                if (control.EnableFlash)
                {
                    control.Flash(isUp);
                }

                control._previousValue = newValue;
            }

            // 更新前景色
            control.UpdateForegroundColor();
        }
    }

    private static void OnReferenceValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlashingTextBlock control)
        {
            control.UpdateForegroundColor();
        }
    }

    /// <summary>
    /// 执行闪烁动画
    /// </summary>
    /// <param name="isUp">是否上涨</param>
    private void Flash(bool isUp)
    {
        var colorService = ColorSchemeService.Instance;
        var flashColor = isUp ? colorService.UpColor : colorService.DownColor;

        // 创建背景色动画
        var animation = new ColorAnimation
        {
            From = flashColor,
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(FlashDuration),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut }
        };

        // 创建画刷并应用动画
        var brush = new SolidColorBrush(flashColor);
        Background = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    /// <summary>
    /// 根据涨跌更新前景色
    /// </summary>
    private void UpdateForegroundColor()
    {
        if (!AutoColor) return;

        var colorService = ColorSchemeService.Instance;
        
        // 如果有参考价格，使用参考价格计算涨跌
        double referencePrice = ReferenceValue > 0 ? ReferenceValue : _previousValue;
        
        if (referencePrice > 0)
        {
            Foreground = colorService.GetPriceBrush(Value, referencePrice);
        }
    }

    /// <summary>
    /// 手动触发闪烁
    /// </summary>
    /// <param name="isUp">是否上涨方向</param>
    public void TriggerFlash(bool isUp)
    {
        if (EnableFlash)
        {
            Flash(isUp);
        }
    }

    /// <summary>
    /// 重置状态
    /// </summary>
    public void Reset()
    {
        _isInitialized = false;
        _previousValue = 0;
        Background = Brushes.Transparent;
    }
}
