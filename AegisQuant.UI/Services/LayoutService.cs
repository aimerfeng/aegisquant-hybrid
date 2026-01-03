using System.IO;
using System.Xml;
using AvalonDock;
using AvalonDock.Layout.Serialization;

namespace AegisQuant.UI.Services;

/// <summary>
/// 布局管理服务 - 保存和恢复 AvalonDock 布局
/// </summary>
public class LayoutService
{
    private static LayoutService? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// 单例实例
    /// </summary>
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

    private LayoutService() { }

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
        {
            Directory.CreateDirectory(appDataPath);
        }

        return Path.Combine(appDataPath, fileName);
    }

    /// <summary>
    /// 保存布局
    /// </summary>
    /// <param name="dockingManager">DockingManager 实例</param>
    /// <param name="fileName">文件名 (可选，默认为 layout.xml)</param>
    public void SaveLayout(DockingManager dockingManager, string? fileName = null)
    {
        try
        {
            var path = GetLayoutPath(fileName ?? LayoutFileName);
            var serializer = new XmlLayoutSerializer(dockingManager);
            
            using var writer = new StreamWriter(path);
            serializer.Serialize(writer);
            
            System.Diagnostics.Debug.WriteLine($"Layout saved to: {path}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save layout: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载布局
    /// </summary>
    /// <param name="dockingManager">DockingManager 实例</param>
    /// <param name="fileName">文件名 (可选，默认为 layout.xml)</param>
    /// <returns>是否成功加载</returns>
    public bool LoadLayout(DockingManager dockingManager, string? fileName = null)
    {
        try
        {
            var path = GetLayoutPath(fileName ?? LayoutFileName);
            
            if (!File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine($"Layout file not found: {path}");
                return false;
            }

            var serializer = new XmlLayoutSerializer(dockingManager);
            
            // 处理布局反序列化回调
            serializer.LayoutSerializationCallback += (s, args) =>
            {
                // 根据 ContentId 恢复内容
                // 这里可以根据需要自定义恢复逻辑
                if (args.Model.ContentId != null)
                {
                    // 保持现有内容
                    args.Cancel = false;
                }
            };

            using var reader = new StreamReader(path);
            serializer.Deserialize(reader);
            
            System.Diagnostics.Debug.WriteLine($"Layout loaded from: {path}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load layout: {ex.Message}");
            return false;
        }
    }

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
    /// <param name="dockingManager">DockingManager 实例</param>
    /// <returns>是否成功重置</returns>
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
    /// 检查是否存在默认布局
    /// </summary>
    public bool HasDefaultLayout()
    {
        var path = GetLayoutPath(DefaultLayoutFileName);
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
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to delete layout: {ex.Message}");
        }
    }

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
            {
                throw new FileNotFoundException("Layout file not found", filePath);
            }

            var serializer = new XmlLayoutSerializer(dockingManager);
            using var reader = new StreamReader(filePath);
            serializer.Deserialize(reader);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to import layout: {ex.Message}", ex);
        }
    }
}
