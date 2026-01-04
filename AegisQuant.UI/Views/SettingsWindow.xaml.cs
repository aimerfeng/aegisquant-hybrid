using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Services;
using Microsoft.Win32;

namespace AegisQuant.UI.Views;

public partial class SettingsWindow : Window
{
    private string _selectedLanguage;
    private ThemeMode _selectedTheme = ThemeMode.Dark;
    private ColorScheme _selectedColorScheme = ColorScheme.International;
    private string _selectedEnvironment = "Development";
    private string _dataDirectory = "";
    
    public SettingsWindow()
    {
        InitializeComponent();
        
        _selectedLanguage = LocalizationService.CurrentLanguage;
        
        // 设置当前选中的语言
        foreach (ComboBoxItem item in LanguageComboBox.Items)
        {
            if (item.Tag?.ToString() == _selectedLanguage)
            {
                LanguageComboBox.SelectedItem = item;
                break;
            }
        }
        
        // 设置当前主题
        _selectedTheme = ColorSchemeService.Instance.CurrentTheme;
        var themeTag = _selectedTheme == ThemeMode.Dark ? "Dark" : "Light";
        foreach (ComboBoxItem item in ThemeComboBox.Items)
        {
            if (item.Tag?.ToString() == themeTag)
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }
        
        // 设置当前颜色方案
        _selectedColorScheme = ColorSchemeService.Instance.CurrentScheme;
        var schemeTag = _selectedColorScheme == ColorScheme.China ? "China" : "International";
        foreach (ComboBoxItem item in ColorSchemeComboBox.Items)
        {
            if (item.Tag?.ToString() == schemeTag)
            {
                ColorSchemeComboBox.SelectedItem = item;
                break;
            }
        }
        
        // 设置当前环境
        _selectedEnvironment = EnvironmentService.Instance.CurrentEnvironment.ToString();
        foreach (ComboBoxItem item in EnvironmentComboBox.Items)
        {
            if (item.Tag?.ToString() == _selectedEnvironment)
            {
                EnvironmentComboBox.SelectedItem = item;
                break;
            }
        }
        
        // 设置默认回测模式
        BacktestModeComboBox.SelectedIndex = 0;
        
        // 设置数据目录
        _dataDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AegisQuant", "Data");
        DataDirectoryTextBox.Text = _dataDirectory;
    }
    
    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            _selectedLanguage = lang;
        }
    }
    
    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string theme)
        {
            _selectedTheme = theme == "Dark" ? ThemeMode.Dark : ThemeMode.Light;
        }
    }
    
    private void ColorSchemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorSchemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string scheme)
        {
            _selectedColorScheme = scheme == "China" ? ColorScheme.China : ColorScheme.International;
        }
    }
    
    private void EnvironmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentComboBox.SelectedItem is ComboBoxItem item && item.Tag is string env)
        {
            _selectedEnvironment = env;
        }
    }
    
    private void BrowseDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择数据目录"
        };
        
        if (dialog.ShowDialog() == true)
        {
            _dataDirectory = dialog.FolderName;
            DataDirectoryTextBox.Text = _dataDirectory;
        }
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        bool needsRestart = false;
        
        // 应用语言设置
        if (_selectedLanguage != LocalizationService.CurrentLanguage)
        {
            LocalizationService.SetLanguage(_selectedLanguage);
            needsRestart = true;
        }
        
        // 应用主题设置
        if (_selectedTheme != ColorSchemeService.Instance.CurrentTheme)
        {
            ColorSchemeService.Instance.SetTheme(_selectedTheme);
        }
        
        // 应用颜色方案设置
        if (_selectedColorScheme != ColorSchemeService.Instance.CurrentScheme)
        {
            ColorSchemeService.Instance.SetScheme(_selectedColorScheme);
        }
        
        // 应用环境设置
        if (Enum.TryParse<TradingEnvironment>(_selectedEnvironment, out var envType))
        {
            if (envType != EnvironmentService.Instance.CurrentEnvironment)
            {
                EnvironmentService.Instance.SetEnvironment(envType);
            }
        }
        
        if (needsRestart)
        {
            MessageBox.Show(
                "部分设置已更改。某些界面可能需要重启应用后才能完全更新。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        
        DialogResult = true;
        Close();
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
