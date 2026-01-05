using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using ScottPlot;
using ScottPlot.Plottables;

namespace AegisQuant.UI.Controls.DrawingTools;

/// <summary>
/// 绘图工具类型
/// </summary>
public enum DrawingToolType
{
    None,
    TrendLine,
    HorizontalLine,
    VerticalLine,
    Rectangle,
    FibonacciRetracement,
    Text
}

/// <summary>
/// 绘图对象基类
/// </summary>
public abstract class DrawingObject
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public DrawingToolType Type { get; protected set; }
    public bool IsSelected { get; set; }
    public bool IsVisible { get; set; } = true;
    public DateTime CreatedAt { get; } = DateTime.Now;
    
    /// <summary>
    /// 将绘图对象添加到图表
    /// </summary>
    public abstract void AddToPlot(Plot plot);
    
    /// <summary>
    /// 从图表移除绘图对象
    /// </summary>
    public abstract void RemoveFromPlot(Plot plot);
    
    /// <summary>
    /// 更新绘图对象位置
    /// </summary>
    public abstract void Update(double x1, double y1, double x2, double y2);
    
    /// <summary>
    /// 检查点是否在绘图对象上
    /// </summary>
    public abstract bool HitTest(double x, double y, double tolerance = 5);
}

/// <summary>
/// 趋势线绘图对象
/// </summary>
public class TrendLineDrawing : DrawingObject
{
    private LinePlot? _line;
    public double X1 { get; private set; }
    public double Y1 { get; private set; }
    public double X2 { get; private set; }
    public double Y2 { get; private set; }
    public ScottPlot.Color LineColor { get; set; } = ScottPlot.Color.FromHex("#FF6B6B");
    public float LineWidth { get; set; } = 2f;

    public TrendLineDrawing(double x1, double y1, double x2, double y2)
    {
        Type = DrawingToolType.TrendLine;
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
    }

    public override void AddToPlot(Plot plot)
    {
        var start = new Coordinates(X1, Y1);
        var end = new Coordinates(X2, Y2);
        _line = plot.Add.Line(start, end);
        _line.LineStyle.Color = LineColor;
        _line.LineStyle.Width = LineWidth;
    }

    public override void RemoveFromPlot(Plot plot)
    {
        if (_line != null) plot.Remove(_line);
    }

    public override void Update(double x1, double y1, double x2, double y2)
    {
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        if (_line != null)
        {
            _line.Start = new Coordinates(x1, y1);
            _line.End = new Coordinates(x2, y2);
        }
    }

    public override bool HitTest(double x, double y, double tolerance = 5)
    {
        // 计算点到线段的距离
        double dx = X2 - X1;
        double dy = Y2 - Y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.0001) return Math.Sqrt((x - X1) * (x - X1) + (y - Y1) * (y - Y1)) < tolerance;
        
        double t = Math.Max(0, Math.Min(1, ((x - X1) * dx + (y - Y1) * dy) / (length * length)));
        double projX = X1 + t * dx;
        double projY = Y1 + t * dy;
        double distance = Math.Sqrt((x - projX) * (x - projX) + (y - projY) * (y - projY));
        return distance < tolerance;
    }
}

/// <summary>
/// 水平线绘图对象
/// </summary>
public class HorizontalLineDrawing : DrawingObject
{
    private HorizontalLine? _line;
    public double Y { get; private set; }
    public ScottPlot.Color LineColor { get; set; } = ScottPlot.Color.FromHex("#4ECDC4");
    public float LineWidth { get; set; } = 1.5f;

    public HorizontalLineDrawing(double y)
    {
        Type = DrawingToolType.HorizontalLine;
        Y = y;
    }

    public override void AddToPlot(Plot plot)
    {
        _line = plot.Add.HorizontalLine(Y);
        _line.Color = LineColor;
        _line.LineWidth = LineWidth;
    }

    public override void RemoveFromPlot(Plot plot)
    {
        if (_line != null) plot.Remove(_line);
    }

    public override void Update(double x1, double y1, double x2, double y2)
    {
        Y = y1;
        if (_line != null) _line.Y = Y;
    }

    public override bool HitTest(double x, double y, double tolerance = 5)
    {
        return Math.Abs(y - Y) < tolerance;
    }
}

/// <summary>
/// 垂直线绘图对象
/// </summary>
public class VerticalLineDrawing : DrawingObject
{
    private VerticalLine? _line;
    public double X { get; private set; }
    public ScottPlot.Color LineColor { get; set; } = ScottPlot.Color.FromHex("#FFE66D");
    public float LineWidth { get; set; } = 1.5f;

    public VerticalLineDrawing(double x)
    {
        Type = DrawingToolType.VerticalLine;
        X = x;
    }

    public override void AddToPlot(Plot plot)
    {
        _line = plot.Add.VerticalLine(X);
        _line.Color = LineColor;
        _line.LineWidth = LineWidth;
    }

    public override void RemoveFromPlot(Plot plot)
    {
        if (_line != null) plot.Remove(_line);
    }

    public override void Update(double x1, double y1, double x2, double y2)
    {
        X = x1;
        if (_line != null) _line.X = X;
    }

    public override bool HitTest(double x, double y, double tolerance = 5)
    {
        return Math.Abs(x - X) < tolerance;
    }
}

/// <summary>
/// 绘图工具管理器
/// </summary>
public class DrawingToolManager
{
    private readonly List<DrawingObject> _drawings = new();
    private DrawingToolType _currentTool = DrawingToolType.None;
    private DrawingObject? _activeDrawing;
    private bool _isDrawing;
    private double _startX, _startY;
    
    public event EventHandler<DrawingObject>? DrawingAdded;
    public event EventHandler<DrawingObject>? DrawingRemoved;
    public event EventHandler<DrawingToolType>? ToolChanged;

    public IReadOnlyList<DrawingObject> Drawings => _drawings;
    public DrawingToolType CurrentTool => _currentTool;
    public bool IsDrawing => _isDrawing;

    /// <summary>
    /// 设置当前绘图工具
    /// </summary>
    public void SetTool(DrawingToolType tool)
    {
        _currentTool = tool;
        _isDrawing = false;
        _activeDrawing = null;
        ToolChanged?.Invoke(this, tool);
    }

    /// <summary>
    /// 开始绘制
    /// </summary>
    public void StartDrawing(double x, double y, Plot plot)
    {
        if (_currentTool == DrawingToolType.None) return;
        
        _startX = x;
        _startY = y;
        _isDrawing = true;

        _activeDrawing = _currentTool switch
        {
            DrawingToolType.TrendLine => new TrendLineDrawing(x, y, x, y),
            DrawingToolType.HorizontalLine => new HorizontalLineDrawing(y),
            DrawingToolType.VerticalLine => new VerticalLineDrawing(x),
            _ => null
        };

        if (_activeDrawing != null)
        {
            _activeDrawing.AddToPlot(plot);
        }
    }

    /// <summary>
    /// 更新绘制中的对象
    /// </summary>
    public void UpdateDrawing(double x, double y)
    {
        if (!_isDrawing || _activeDrawing == null) return;
        _activeDrawing.Update(_startX, _startY, x, y);
    }

    /// <summary>
    /// 完成绘制
    /// </summary>
    public void FinishDrawing(double x, double y)
    {
        if (!_isDrawing || _activeDrawing == null) return;
        
        _activeDrawing.Update(_startX, _startY, x, y);
        _drawings.Add(_activeDrawing);
        DrawingAdded?.Invoke(this, _activeDrawing);
        
        _isDrawing = false;
        _activeDrawing = null;
    }

    /// <summary>
    /// 取消绘制
    /// </summary>
    public void CancelDrawing(Plot plot)
    {
        if (_activeDrawing != null)
        {
            _activeDrawing.RemoveFromPlot(plot);
            _activeDrawing = null;
        }
        _isDrawing = false;
    }

    /// <summary>
    /// 删除绘图对象
    /// </summary>
    public void RemoveDrawing(DrawingObject drawing, Plot plot)
    {
        drawing.RemoveFromPlot(plot);
        _drawings.Remove(drawing);
        DrawingRemoved?.Invoke(this, drawing);
    }

    /// <summary>
    /// 清除所有绘图
    /// </summary>
    public void ClearAll(Plot plot)
    {
        foreach (var drawing in _drawings.ToArray())
        {
            RemoveDrawing(drawing, plot);
        }
    }

    /// <summary>
    /// 点击测试
    /// </summary>
    public DrawingObject? HitTest(double x, double y, double tolerance = 5)
    {
        foreach (var drawing in _drawings)
        {
            if (drawing.IsVisible && drawing.HitTest(x, y, tolerance))
            {
                return drawing;
            }
        }
        return null;
    }

    /// <summary>
    /// 重新绘制所有对象
    /// </summary>
    public void RedrawAll(Plot plot)
    {
        foreach (var drawing in _drawings)
        {
            if (drawing.IsVisible)
            {
                drawing.RemoveFromPlot(plot);
                drawing.AddToPlot(plot);
            }
        }
    }
}
