using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AegisQuant.UI.Services;
using AegisQuant.UI.Services.Interfaces;
using AegisQuant.UI.Models;
using AegisQuant.UI.ViewModels;
using AegisQuant.UI.Views;

namespace AegisQuant.UI;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            LocalizationService.Initialize();
            ColorSchemeService.Instance.Initialize();

            var loginWindow = new LoginWindow();
            var loginResult = loginWindow.ShowDialog();

            if (loginResult == true)
            {
                try
                {
                    // 登录成功后切换到主窗口关闭时退出模式
                    ShutdownMode = ShutdownMode.OnMainWindowClose;

                    var mainWindow = Services.GetRequiredService<MainWindow>();
                    MainWindow = mainWindow;
                    mainWindow.Show();
                }
                catch (Exception mainEx)
                {
                    MessageBox.Show($"创建主窗口失败:\n{mainEx.Message}\n\n内部错误:\n{mainEx.InnerException?.Message}\n\n{mainEx.StackTrace}",
                        "主窗口错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                }
            }
            else
            {
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动错误:\n{ex.Message}\n\n内部错误:\n{ex.InnerException?.Message}\n\n{ex.StackTrace}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBacktestService, BacktestService>();
        services.AddSingleton<IStrategyManagerService, StrategyManagerService>();
        services.AddSingleton<IReplayService, StrategyReplayServiceAdapter>();
        services.AddSingleton<IMarketDataStore, MarketDataStore>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<PythonRuntimeService>();
        services.AddSingleton<MultiStrategyManagerService>();

        services.AddTransient<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<IBacktestService>(),
            sp.GetRequiredService<IReplayService>()
        ));

        services.AddTransient<MainWindow>(sp => new MainWindow(
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<IStrategyManagerService>(),
            sp.GetRequiredService<MultiStrategyManagerService>(),
            sp.GetRequiredService<IReplayService>(),
            sp.GetRequiredService<IBacktestService>()
        ));
    }
}
