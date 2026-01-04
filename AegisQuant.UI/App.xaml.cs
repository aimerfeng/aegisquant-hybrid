using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AegisQuant.UI.Services;
using AegisQuant.UI.Services.Interfaces;
using AegisQuant.UI.Models;
using AegisQuant.UI.ViewModels;
using AegisQuant.UI.Views;

namespace AegisQuant.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    public IServiceProvider Services { get; private set; } = null!;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Configure DI container
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
        
        // 初始化语言设置
        LocalizationService.Initialize();
        
        // 初始化配色方案服务
        ColorSchemeService.Instance.Initialize();
        
        // Create and show main window via DI
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Singletons - shared across the application
        services.AddSingleton<IBacktestService, BacktestService>();
        services.AddSingleton<IStrategyManagerService, StrategyManagerService>();
        services.AddSingleton<IReplayService, StrategyReplayServiceAdapter>();
        services.AddSingleton<IMarketDataStore, MarketDataStore>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<PythonRuntimeService>();
        services.AddSingleton<MultiStrategyManagerService>();
        
        // Transients - new instance each time
        // MainViewModel needs both IBacktestService and IReplayService
        services.AddTransient<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<IBacktestService>(),
            sp.GetRequiredService<IReplayService>()
        ));
        
        // MainWindow needs IReplayService and IBacktestService for chart updates and data flow
        // Requirements: 9.10, 9.11 - Channel-based display updates
        services.AddTransient<MainWindow>(sp => new MainWindow(
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<IStrategyManagerService>(),
            sp.GetRequiredService<MultiStrategyManagerService>(),
            sp.GetRequiredService<IReplayService>(),
            sp.GetRequiredService<IBacktestService>()
        ));
    }
}
