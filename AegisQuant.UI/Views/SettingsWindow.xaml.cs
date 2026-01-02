using System.Windows;
using System.Windows.Controls;
using AegisQuant.UI.Services;

namespace AegisQuant.UI.Views;

public partial class SettingsWindow : Window
{
    private string _selectedLanguage;
    
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
    }
    
    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            _selectedLanguage = lang;
        }
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        // 应用语言设置
        if (_selectedLanguage != LocalizationService.CurrentLanguage)
        {
            LocalizationService.SetLanguage(_selectedLanguage);
            
            // 提示用户重启以完全应用
            MessageBox.Show(
                LocalizationService.CurrentLanguage == "zh-CN" 
                    ? "语言已更改。部分界面可能需要重启应用后才能完全更新。" 
                    : "Language changed. Some UI elements may require restart to fully update.",
                LocalizationService.CurrentLanguage == "zh-CN" ? "提示" : "Notice",
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
