using Microsoft.Extensions.Logging;
using PocketFence.Models;
using PocketFence.Services;
using System.Collections.ObjectModel;

namespace PocketFence;

public partial class DeviceListPage : ContentPage
{
    private readonly IDeviceDiscoveryService _deviceDiscovery;
    private readonly ILogger<DeviceListPage> _logger;
    
    public ObservableCollection<PocketFence.Models.DeviceInfo> Devices { get; } = new();
    
    public DeviceListPage(IDeviceDiscoveryService deviceDiscovery, ILogger<DeviceListPage> logger)
    {
        InitializeComponent();
        _deviceDiscovery = deviceDiscovery;
        _logger = logger;
        
        DevicesCollectionView.ItemsSource = Devices;
        
        // Load devices on page load
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        await RefreshDevicesAsync();
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await RefreshDevicesAsync();
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            RefreshButton.IsEnabled = false;
            StatusLabel.Text = "🔄 Discovering devices...";
            LoadingIndicator.IsVisible = true;

            Devices.Clear();
            var devices = await _deviceDiscovery.ListDevicesAsync();

            foreach (var device in devices)
            {
                var deviceInfo = new PocketFence.Models.DeviceInfo
                {
                    Name = device.ContainsKey("Name") ? device["Name"]?.ToString() ?? "Unknown" : "Unknown",
                    IpAddress = device.ContainsKey("IpAddress") ? device["IpAddress"]?.ToString() ?? "0.0.0.0" : "0.0.0.0",
                    MacAddress = device.ContainsKey("MacAddress") ? device["MacAddress"]?.ToString() ?? "Unknown" : "Unknown",
                    IsConnected = device.ContainsKey("IsConnected") && device["IsConnected"] is bool connected && connected,
                    LastSeen = device.ContainsKey("LastSeen") && device["LastSeen"] is DateTime lastSeen ? lastSeen : DateTime.Now
                };
                Devices.Add(deviceInfo);
            }

            StatusLabel.Text = $"✅ Found {Devices.Count} device(s)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh devices");
            StatusLabel.Text = "❌ Error discovering devices";
            await DisplayAlert("Error", $"Failed to discover devices: {ex.Message}", "OK");
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            LoadingIndicator.IsVisible = false;
        }
    }

    private async void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is DeviceInfo selectedDevice)
        {
            await DisplayAlert("Device Details", 
                $"Name: {selectedDevice.Name}\n" +
                $"IP: {selectedDevice.IpAddress}\n" +
                $"MAC: {selectedDevice.MacAddress ?? "Unknown"}\n" +
                $"Last Seen: {selectedDevice.LastSeen:HH:mm:ss}",
                "OK");
                
            // Deselect the item
            DevicesCollectionView.SelectedItem = null;
        }
    }
}

public class DeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public DateTime LastSeen { get; set; }
    public string DeviceIcon { get; set; } = "📱";
    public string StatusColor { get; set; } = "#28A745";
}