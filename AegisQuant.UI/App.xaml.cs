using System.Windows;
using AegisQuant.UI.Services;

namespace AegisQuant.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // 初始化语言设置
        LocalizationService.Initialize();
        
        // 初始化配色方案服务
        ColorSchemeService.Instance.Initialize();
    }
}
