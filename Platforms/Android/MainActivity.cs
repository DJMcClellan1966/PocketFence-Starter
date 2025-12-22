using Android.App;
using Android.Content.PM;
using Android.OS;

namespace PocketFence;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        
        // Request necessary permissions for hotspot functionality
        RequestPermissions();
    }
    
    private async void RequestPermissions()
    {
        try
        {
            var permissions = new[]
            {
                Android.Manifest.Permission.AccessWifiState,
                Android.Manifest.Permission.ChangeWifiState,
                Android.Manifest.Permission.AccessNetworkState,
                Android.Manifest.Permission.ChangeNetworkState,
                Android.Manifest.Permission.AccessFineLocation,
                Android.Manifest.Permission.AccessCoarseLocation,
                Android.Manifest.Permission.WriteSettings
            };

            var status = await Microsoft.Maui.Authentication.WebAuthenticator.RequestAsync(
                new Microsoft.Maui.Authentication.WebAuthenticatorOptions()
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Permission request failed: {ex.Message}");
        }
    }
}