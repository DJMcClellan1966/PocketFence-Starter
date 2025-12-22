namespace PocketFence;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        
        // Register routes for navigation
        Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
        Routing.RegisterRoute(nameof(DeviceListPage), typeof(DeviceListPage));
        Routing.RegisterRoute(nameof(AIControlPage), typeof(AIControlPage));
    }
}