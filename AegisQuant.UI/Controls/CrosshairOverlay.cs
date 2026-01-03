using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AegisQuant.UI.Controls;

/// <summary>
/// 十字光标覆盖层控件 - 用于在图表上显示十字光标和价格/时间标签
/// </summary>
public class CrosshairOverlay : Canvas
{
    #region Dependency Properties

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(CrosshairOverlay),
            new PropertyMetadata(false, OnIsActiveChanged));

    public static readonly DependencyProperty CrosshairColorProperty =
        DependencyProperty.Register(nameof(CrosshairColor), typeof(Brush), typeof(CrosshairOverlay),
            new PropertyMetadata(Brushes.Gray));

    public static readonly DependencyProperty LabelBackgroundProperty =
        DependencyProperty.Register(nameof(LabelBackground), typeof(Brush), typeof(CrosshairOverlay),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25))));

    public static readonly DependencyProperty LabelForegroundProperty =
        DependencyProperty.Register(nameof(LabelForeground), typeof(Brush), typeof(CrosshairOverlay),
            new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty PriceLabelProperty =
        DependencyProperty.Register(nameof(PriceLabel), typeof(string), typeof(CrosshairOverlay),
            new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty TimeLabelProperty =
        DependencyProperty.Register(nameof(TimeLabel), typeof(string), typeof(CrosshairOverlay),
            new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty CrosshairXProperty =
        DependencyProperty.Register(nameof(CrosshairX), typeof(double), typeof(CrosshairOverlay),
            new PropertyMetadata(0.0, OnPositionChanged));

    public static readonly DependencyProperty CrosshairYProperty =
        DependencyProperty.Register(nameof(CrosshairY), typeof(double), typeof(CrosshairOverlay),
            new PropertyMetadata(0.0, OnPositionChanged));

    #endregion

    #region Properties

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Brush CrosshairColor
    {
        get => (Brush)GetValue(CrosshairColorProperty);
        set => SetValue(CrosshairColorProperty, value);
    }

    public Brush LabelBackground
    {
        get => (Brush)GetValue(LabelBackgroundProperty);
        set => SetValue(LabelBackgroundProperty, value);
    }

    public Brush LabelForeground
    {
        get => (Brush)GetValue(LabelForegroundProperty);
        set => SetValue(LabelForegroundProperty, value);
    }

    public string PriceLabel
    {
        get => (string)GetValue(PriceLabelProperty);
        set => SetValue(PriceLabelProperty, value);
    }

    public string TimeLabel
    {
        get => (string)GetValue(TimeLabelProperty);
        set => SetValue(TimeLabelProperty, value);
    }

    public double CrosshairX
    {
        get => (double)GetValue(CrosshairXProperty);
        set => SetValue(CrosshairXProperty, value);
    }

    public double CrosshairY
    {
        get => (double)GetValue(CrosshairYProperty);
        set => SetValue(CrosshairYProperty, value);
    }

    #endregion

    private readonly Line _horizontalLine;
    private readonly Line _verticalLine;
    private readonly Border _priceLabelBorder;
    private readonly TextBlock _priceLabelText;
    private readonly Border _timeLabelBorder;
    private readonly TextBlock _timeLabelText;

    public CrosshairOverlay()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        // 创建水平线
        _horizontalLine = new Line
        {
            Stroke = CrosshairColor,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Visibility = Visibility.Collapsed
        };
        Children.Add(_horizontalLine);

        // 创建垂直线
        _verticalLine = new Line
        {
            Stroke = CrosshairColor,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Visibility = Visibility.Collapsed
        };
        Children.Add(_verticalLine);

        // 创建价格标签
        _priceLabelText = new TextBlock
        {
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            Foreground = LabelForeground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2)
        };
        _priceLabelBorder = new Border
        {
            Background = LabelBackground,
            CornerRadius = new CornerRadius(2),
            Child = _priceLabelText,
            Visibility = Visibility.Collapsed
        };
        Children.Add(_priceLabelBorder);

        // 创建时间标签
        _timeLabelText = new TextBlock
        {
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            Foreground = LabelForeground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2)
        };
        _timeLabelBorder = new Border
        {
            Background = LabelBackground,
            CornerRadius = new CornerRadius(2),
            Child = _timeLabelText,
            Visibility = Visibility.Collapsed
        };
        Children.Add(_timeLabelBorder);

        SizeChanged += OnSizeChanged;
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CrosshairOverlay overlay)
        {
            var visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            overlay._horizontalLine.Visibility = visibility;
            overlay._verticalLine.Visibility = visibility;
            overlay._priceLabelBorder.Visibility = visibility;
            overlay._timeLabelBorder.Visibility = visibility;
        }
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CrosshairOverlay overlay)
        {
            overlay.UpdateCrosshairPosition();
        }
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CrosshairOverlay overlay)
        {
            overlay.UpdateLabels();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCrosshairPosition();
    }

    private void UpdateCrosshairPosition()
    {
        if (!IsActive) return;

        var width = ActualWidth;
        var height = ActualHeight;

        // 更新水平线
        _horizontalLine.X1 = 0;
        _horizontalLine.Y1 = CrosshairY;
        _horizontalLine.X2 = width;
        _horizontalLine.Y2 = CrosshairY;

        // 更新垂直线
        _verticalLine.X1 = CrosshairX;
        _verticalLine.Y1 = 0;
        _verticalLine.X2 = CrosshairX;
        _verticalLine.Y2 = height;

        // 更新价格标签位置 (右侧)
        _priceLabelBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var priceLabelWidth = _priceLabelBorder.DesiredSize.Width;
        var priceLabelHeight = _priceLabelBorder.DesiredSize.Height;
        
        SetLeft(_priceLabelBorder, width - priceLabelWidth - 2);
        SetTop(_priceLabelBorder, CrosshairY - priceLabelHeight / 2);

        // 更新时间标签位置 (底部)
        _timeLabelBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var timeLabelWidth = _timeLabelBorder.DesiredSize.Width;
        var timeLabelHeight = _timeLabelBorder.DesiredSize.Height;
        
        SetLeft(_timeLabelBorder, CrosshairX - timeLabelWidth / 2);
        SetTop(_timeLabelBorder, height - timeLabelHeight - 2);
    }

    private void UpdateLabels()
    {
        _priceLabelText.Text = PriceLabel;
        _timeLabelText.Text = TimeLabel;
        UpdateCrosshairPosition();
    }

    /// <summary>
    /// 更新十字光标位置和标签
    /// </summary>
    public void Update(double x, double y, string priceLabel, string timeLabel)
    {
        CrosshairX = x;
        CrosshairY = y;
        PriceLabel = priceLabel;
        TimeLabel = timeLabel;
        IsActive = true;
    }

    /// <summary>
    /// 隐藏十字光标
    /// </summary>
    public void Hide()
    {
        IsActive = false;
    }
}
