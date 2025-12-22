using Microsoft.Extensions.Logging;
using PocketFence.Services;
using PocketFence.Services.AI;

namespace PocketFence;

public partial class MainPage : ContentPage
{
    private readonly IHotspotService _hotspotService;
    private readonly IVpnHelper _vpnHelper;
    private readonly IDeviceDiscoveryService _deviceDiscovery;
    private readonly IAIParentalControlService _aiService;
    private readonly ISmartNetworkManager _networkManager;
    private readonly ILogger<MainPage> _logger;
    
    private bool _isHotspotActive = false;

    public MainPage(IHotspotService hotspotService, IVpnHelper vpnHelper,
                   IDeviceDiscoveryService deviceDiscovery, IAIParentalControlService aiService,
                   ISmartNetworkManager networkManager, ILogger<MainPage> logger)
    {
        InitializeComponent();
        _hotspotService = hotspotService;
        _vpnHelper = vpnHelper;
        _deviceDiscovery = deviceDiscovery;
        _aiService = aiService;
        _networkManager = networkManager;
        _logger = logger;

        LoadSettings();
        UpdateUI();
    }

    private void LoadSettings()
    {
        try
        {
            SSIDEntry.Text = "MyPocketFence"; // Default value - would load from preferences
            BlockOthersCheckBox.IsChecked = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
        }
    }

    private void UpdateUI()
    {
        ToggleButton.Text = _isHotspotActive ? "Stop Hotspot" : "Start Hotspot";
        ToggleButton.BackgroundColor = _isHotspotActive ? Colors.Red : Color.FromArgb("#0078D4");
        StatusLabel.Text = $"Status: {(_isHotspotActive ? "Hotspot Active" : "Ready")}";
        StatusLabel.TextColor = _isHotspotActive ? Colors.Green : Color.FromArgb("#0078D4");
    }

    private async void OnToggleHotspotClicked(object sender, EventArgs e)
    {
        try
        {
            ToggleButton.IsEnabled = false;
            StatusLabel.Text = "Status: Processing...";

            if (_isHotspotActive)
            {
                var success = await _hotspotService.StopHotspotAsync();
                if (success)
                {
                    _isHotspotActive = false;
                    await ShowMessage("Success", "Hotspot stopped successfully!");
                }
                else
                {
                    await ShowMessage("Error", "Failed to stop hotspot.");
                }
            }
            else
            {
                var config = new HotspotConfig
                {
                    Ssid = SSIDEntry.Text?.Trim() ?? "MyPocketFence",
                    Password = "PocketFence123", // Would be configurable
                    BlockOtherDevices = BlockOthersCheckBox.IsChecked
                };

                var success = await _hotspotService.StartHotspotAsync(config);
                if (success)
                {
                    _isHotspotActive = true;
                    await ShowMessage("Success", $"Hotspot '{config.Ssid}' started successfully!");
                }
                else
                {
                    await ShowMessage("Error", "Failed to start hotspot.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling hotspot");
            await ShowMessage("Error", $"Error: {ex.Message}");
        }
        finally
        {
            ToggleButton.IsEnabled = true;
            UpdateUI();
        }
    }

    private async void OnViewDevicesClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(DeviceListPage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to device list");
            await ShowMessage("Error", $"Error opening device list: {ex.Message}");
        }
    }

    private async void OnAIControlsClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(AIControlPage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to AI controls");
            await ShowMessage("Error", $"Error opening AI controls: {ex.Message}");
        }
    }

    private async void OnApplyDnsClicked(object sender, EventArgs e)
    {
        try
        {
            StatusLabel.Text = "Status: Updating DNS settings...";
            
            var dnsServers = DNSPicker.SelectedIndex switch
            {
                0 => new List<string> { "8.8.8.8", "8.8.4.4" },
                1 => NextDnsConfig.NextDnsServers,
                _ => new List<string> { "8.8.8.8", "8.8.4.4" }
            };

            var success = await _vpnHelper.SetDnsAsync(dnsServers);
            
            if (success)
            {
                await ShowMessage("Success", "DNS settings updated successfully!");
            }
            else
            {
                await ShowMessage("Error", "Failed to update DNS settings.");
            }

            StatusLabel.Text = _isHotspotActive ? "Status: Hotspot Active" : "Status: Ready";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting DNS");
            await ShowMessage("Error", $"Error updating DNS: {ex.Message}");
        }
    }

    private async Task ShowMessage(string title, string message)
    {
        await DisplayAlert(title, message, "OK");
    }
}