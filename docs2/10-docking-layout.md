# 可停靠布局详解

## 概述

可停靠布局是专业交易软件的标配功能，允许用户自由拖拽、停靠、浮动各个面板。本文档详细说明如何使用 AvalonDock 实现布局管理，包括布局保存、恢复和重置功能。

## 问题分析

### 传统固定布局的局限

1. **不灵活**: 用户无法根据习惯调整界面
2. **屏幕利用率低**: 不同分辨率下体验差异大
3. **多显示器支持差**: 无法将面板拖到其他屏幕
4. **状态丢失**: 重启后布局恢复默认

### 设计目标

- 支持面板拖拽、停靠、浮动
- 布局状态持久化
- 支持默认布局重置
- 支持布局导入导出

## 解决方案

### AvalonDock 集成

```xml
<!-- MainWindow.xaml -->
<Window xmlns:avalonDock="https://github.com/Dirkster99/AvalonDock">
    <avalonDock:DockingManager x:Name="DockingManager">
        <avalonDock:LayoutRoot>
            <avalonDock:LayoutPanel Orientation="Horizontal">
                <!-- 左侧面板组 -->
                <avalonDock:LayoutAnchorablePane DockWidth="300">
                    <avalonDock:LayoutAnchorable Title="订单簿" ContentId="OrderBook">
                        <local:OrderBookControl />
                    </avalonDock:LayoutAnchorable>
                </avalonDock:LayoutAnchorablePane>
                
                <!-- 中央文档区 -->
                <avalonDock:LayoutDocumentPane>
                    <avalonDock:LayoutDocument Title="K线图" ContentId="Chart">
                        <local:CandlestickChartControl />
                    </avalonDock:LayoutDocument>
                </avalonDock:LayoutDocumentPane>
                
                <!-- 右侧面板组 -->
                <avalonDock:LayoutAnchorablePane DockWidth="250">
                    <avalonDock:LayoutAnchorable Title="持仓" ContentId="Positions">
                        <local:PositionPanel />
                    </avalonDock:LayoutAnchorable>
                </avalonDock:LayoutAnchorablePane>
            </avalonDock:LayoutPanel>
        </avalonDock:LayoutRoot>
    </avalonDock:DockingManager>
</Window>
```

### 布局服务实现

```csharp
// LayoutService.cs
public class LayoutService
{
    private static LayoutService? _instance;
    private static readonly object _lock = new();

    public static LayoutService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new LayoutService();
                }
            }
            return _instance;
        }
    }

    private const string LayoutFileName = "layout.xml";
    private const string DefaultLayoutFileName = "default_layout.xml";

    /// <summary>
    /// 获取布局文件路径
    /// </summary>
    private static string GetLayoutPath(string fileName)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AegisQuant"
        );

        if (!Directory.Exists(appDataPath))
            Directory.CreateDirectory(appDataPath);

        return Path.Combine(appDataPath, fileName);
    }

    /// <summary>
    /// 保存布局
    /// </summary>
    public void SaveLayout(DockingManager dockingManager, string? fileName = null)
    {
        try
        {
            var path = GetLayoutPath(fileName ?? LayoutFileName);
            var serializer = new XmlLayoutSerializer(dockingManager);
            
            using var writer = new StreamWriter(path);
            serializer.Serialize(writer);
            
            Debug.WriteLine($"Layout saved to: {path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save layout: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载布局
    /// </summary>
    public bool LoadLayout(DockingManager dockingManager, string? fileName = null)
    {
        try
        {
            var path = GetLayoutPath(fileName ?? LayoutFileName);
            
            if (!File.Exists(path))
            {
                Debug.WriteLine($"Layout file not found: {path}");
                return false;
            }

            var serializer = new XmlLayoutSerializer(dockingManager);
            
            // 处理布局反序列化回调
            serializer.LayoutSerializationCallback += (s, args) =>
            {
                // 根据 ContentId 恢复内容
                if (args.Model.ContentId != null)
                {
                    args.Cancel = false;  // 保持现有内容
                }
            };

            using var reader = new StreamReader(path);
            serializer.Deserialize(reader);
            
            Debug.WriteLine($"Layout loaded from: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load layout: {ex.Message}");
            return false;
        }
    }
}
```

### 布局保存与恢复

```csharp
/// <summary>
/// 保存为默认布局
/// </summary>
public void SaveAsDefaultLayout(DockingManager dockingManager)
{
    SaveLayout(dockingManager, DefaultLayoutFileName);
}

/// <summary>
/// 重置为默认布局
/// </summary>
public bool ResetToDefaultLayout(DockingManager dockingManager)
{
    return LoadLayout(dockingManager, DefaultLayoutFileName);
}

/// <summary>
/// 检查是否存在保存的布局
/// </summary>
public bool HasSavedLayout()
{
    var path = GetLayoutPath(LayoutFileName);
    return File.Exists(path);
}

/// <summary>
/// 删除保存的布局
/// </summary>
public void DeleteSavedLayout()
{
    try
    {
        var path = GetLayoutPath(LayoutFileName);
        if (File.Exists(path))
            File.Delete(path);
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Failed to delete layout: {ex.Message}");
    }
}
```

### 布局导入导出

```csharp
/// <summary>
/// 导出布局到指定路径
/// </summary>
public void ExportLayout(DockingManager dockingManager, string filePath)
{
    try
    {
        var serializer = new XmlLayoutSerializer(dockingManager);
        using var writer = new StreamWriter(filePath);
        serializer.Serialize(writer);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Failed to export layout: {ex.Message}", ex);
    }
}

/// <summary>
/// 从指定路径导入布局
/// </summary>
public void ImportLayout(DockingManager dockingManager, string filePath)
{
    try
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Layout file not found", filePath);

        var serializer = new XmlLayoutSerializer(dockingManager);
        using var reader = new StreamReader(filePath);
        serializer.Deserialize(reader);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Failed to import layout: {ex.Message}", ex);
    }
}
```

### 窗口生命周期集成

```csharp
// MainWindow.xaml.cs
public partial class MainWindow : Window
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        
        // 尝试加载保存的布局
        if (LayoutService.Instance.HasSavedLayout())
        {
            LayoutService.Instance.LoadLayout(DockingManager);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 保存当前布局
        LayoutService.Instance.SaveLayout(DockingManager);
        base.OnClosing(e);
    }
}
```

### 暗色主题支持

```xml
<!-- AvalonDockDarkTheme.xaml -->
<ResourceDictionary>
    <!-- 文档标签页样式 -->
    <Style TargetType="{x:Type avalonDock:LayoutDocumentTabItem}">
        <Setter Property="Background" Value="#2D2D30"/>
        <Setter Property="Foreground" Value="#F1F1F1"/>
    </Style>
    
    <!-- 可停靠面板样式 -->
    <Style TargetType="{x:Type avalonDock:LayoutAnchorableTabItem}">
        <Setter Property="Background" Value="#2D2D30"/>
        <Setter Property="Foreground" Value="#F1F1F1"/>
    </Style>
    
    <!-- 分隔条样式 -->
    <Style TargetType="{x:Type avalonDock:LayoutGridResizerControl}">
        <Setter Property="Background" Value="#3F3F46"/>
    </Style>
</ResourceDictionary>
```

## 使用示例

```csharp
// 保存当前布局
LayoutService.Instance.SaveLayout(DockingManager);

// 加载布局
LayoutService.Instance.LoadLayout(DockingManager);

// 重置为默认布局
LayoutService.Instance.ResetToDefaultLayout(DockingManager);

// 导出布局供分享
LayoutService.Instance.ExportLayout(DockingManager, "my_layout.xml");

// 导入他人布局
LayoutService.Instance.ImportLayout(DockingManager, "shared_layout.xml");
```

## 面试话术

### Q: 为什么选择 AvalonDock？

**A**: AvalonDock 是 WPF 生态中最成熟的停靠布局库：
1. **功能完整**: 支持拖拽、停靠、浮动、自动隐藏
2. **序列化支持**: 内置 XML 布局序列化
3. **主题支持**: 可自定义暗色/亮色主题
4. **社区活跃**: Dirkster99 维护的版本持续更新

### Q: 布局序列化是如何工作的？

**A**: AvalonDock 使用 `XmlLayoutSerializer`：
1. **序列化**: 将 DockingManager 的布局树转为 XML
2. **反序列化**: 从 XML 恢复布局树
3. **ContentId**: 每个面板有唯一 ID，用于匹配内容

关键是 `LayoutSerializationCallback`，在反序列化时决定如何恢复每个面板的内容。

### Q: 如何处理布局版本兼容问题？

**A**: 三个策略：
1. **ContentId 匹配**: 新增面板不影响旧布局
2. **异常处理**: 加载失败时回退到默认布局
3. **版本号**: 布局文件可以包含版本号，不兼容时重置

实际项目中，我会在布局 XML 中添加版本属性，升级时检查版本决定是否迁移。

### Q: 多显示器场景如何处理？

**A**: AvalonDock 的浮动窗口天然支持多显示器：
- 浮动面板可以拖到任意显示器
- 序列化时保存窗口位置
- 反序列化时恢复到原显示器

需要注意的是，如果用户减少了显示器数量，需要检测并将窗口移回主显示器。
