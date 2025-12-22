namespace PocketFence;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        MainPage = new AppShell();
    }
    
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
        
        if (window != null)
        {
            window.Title = "PocketFence - AI-Powered Network Manager";
            
            // Set initial window size for desktop platforms
#if WINDOWS || MACCATALYST
            window.Width = 800;
            window.Height = 600;
            window.MinimumWidth = 600;
            window.MinimumHeight = 400;
#endif
        }
        
        return window;
    }
}