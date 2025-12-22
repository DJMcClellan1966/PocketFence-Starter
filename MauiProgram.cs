using Microsoft.Extensions.Logging;
using PocketFence.Services;
using PocketFence.Services.AI;

namespace PocketFence;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Register services - Universal Parental Control System
        
        // Core Device and Network Services
        builder.Services.AddSingleton<IHotspotService, HotspotService>();
        builder.Services.AddSingleton<IVpnHelper, VpnHelper>();
        builder.Services.AddSingleton<IDeviceDiscoveryService, DeviceDiscoveryService>();
        builder.Services.AddSingleton<IUniversalDeviceController, UniversalDeviceController>();
        
        // AI and Intelligence Services
        builder.Services.AddSingleton<IAIParentalControlService, AIParentalControlService>();
        builder.Services.AddSingleton<IAIContentAnalyzer, AIContentAnalyzer>();
        builder.Services.AddSingleton<IWebFilteringService, WebFilteringService>();
        builder.Services.AddSingleton<ISmartNetworkManager, SmartNetworkManager>();
        
        // Security and Authentication Services
        builder.Services.AddSingleton<IParentAuthenticationService, ParentAuthenticationService>();
        builder.Services.AddSingleton<IBiometricService, BiometricService>();
        builder.Services.AddSingleton<ITwoFactorService, TwoFactorService>();
        
        // Cloud and Remote Control Services
        builder.Services.AddSingleton<ICloudSyncService, CloudSyncService>();
        builder.Services.AddSingleton<IAutonomousProtectionService, AutonomousProtectionService>();
        builder.Services.AddSingleton<IPerformanceOptimizer, PerformanceOptimizer>();
        builder.Services.AddSingleton<IAISelfHealingService, AISelfHealingService>();
        builder.Services.AddSingleton<ITimeLimitsService, TimeLimitsService>();
        builder.Services.AddSingleton<HttpClient>();
        
        // Register pages
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<DeviceListPage>();
        builder.Services.AddTransient<AIControlPage>();
        builder.Services.AddTransient<ParentalDashboard>();

#if DEBUG
        builder.Logging.AddDebug();
        builder.Services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });
#endif

        return builder.Build();
    }
}