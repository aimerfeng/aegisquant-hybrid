using System;
using System.Globalization;
using System.Windows;

namespace AegisQuant.UI.Services;

/// <summary>
/// 语言切换服务
/// </summary>
public static class LocalizationService
{
    private static string _currentLanguage = "en-US";
    
    public static event EventHandler? LanguageChanged;
    
    public static string CurrentLanguage => _currentLanguage;
    
    public static string[] SupportedLanguages => new[] { "en-US", "zh-CN" };
    
    /// <summary>
    /// 切换语言
    /// </summary>
    public static void SetLanguage(string cultureName)
    {
        if (_currentLanguage == cultureName) return;
        
        _currentLanguage = cultureName;
        
        // 移除旧的语言资源
        var mergedDicts = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary? oldDict = null;
        
        foreach (var dict in mergedDicts)
        {
            if (dict.Source?.OriginalString.Contains("Strings.") == true)
            {
                oldDict = dict;
                break;
            }
        }
        
        if (oldDict != null)
        {
            mergedDicts.Remove(oldDict);
        }
        
        // 加载新的语言资源
        var newDict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/AegisQuant.UI;component/Resources/Strings.{cultureName}.xaml")
        };
        mergedDicts.Add(newDict);
        
        // 设置当前线程的文化
        var culture = new CultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        
        // 保存设置
        SaveLanguageSetting(cultureName);
        
        // 触发事件
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
    
    /// <summary>
    /// 初始化语言（从设置加载）
    /// </summary>
    public static void Initialize()
    {
        var savedLanguage = LoadLanguageSetting();
        SetLanguage(savedLanguage);
    }
    
    /// <summary>
    /// 获取本地化字符串
    /// </summary>
    public static string GetString(string key)
    {
        if (Application.Current.TryFindResource(key) is string value)
        {
            return value;
        }
        return key;
    }
    
    private static void SaveLanguageSetting(string language)
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AegisQuant",
                "settings.txt"
            );
            
            var dir = System.IO.Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            
            System.IO.File.WriteAllText(settingsPath, $"Language={language}");
        }
        catch
        {
            // 忽略保存失败
        }
    }
    
    private static string LoadLanguageSetting()
    {
        try
        {
            var settingsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AegisQuant",
                "settings.txt"
            );
            
            if (System.IO.File.Exists(settingsPath))
            {
                var content = System.IO.File.ReadAllText(settingsPath);
                if (content.StartsWith("Language="))
                {
                    var lang = content.Substring("Language=".Length).Trim();
                    if (Array.Exists(SupportedLanguages, l => l == lang))
                    {
                        return lang;
                    }
                }
            }
        }
        catch
        {
            // 忽略加载失败
        }
        
        // 默认根据系统语言选择
        var systemLang = CultureInfo.CurrentUICulture.Name;
        if (systemLang.StartsWith("zh"))
        {
            return "zh-CN";
        }
        return "en-US";
    }
}
