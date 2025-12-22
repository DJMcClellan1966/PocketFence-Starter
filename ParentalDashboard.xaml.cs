using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using PocketFence.Models;
using PocketFence.Services;
using PocketFence.Services.AI;
using DeviceType = PocketFence.Models.DeviceType;

namespace PocketFence;

public partial class ParentalDashboard : ContentPage
{
    private readonly ILogger<ParentalDashboard> _logger;
    private readonly IUniversalDeviceController _deviceController;
    private readonly IAIParentalControlService _aiService;
    private readonly IParentAuthenticationService _authService;
    private readonly ICloudSyncService _cloudSync;
    private readonly IWebFilteringService _webFilter;

    public ObservableCollection<DeviceDisplayInfo> Devices { get; } = new();
    public ObservableCollection<InsightDisplayInfo> Insights { get; } = new();
    public ObservableCollection<AlertDisplayInfo> SecurityAlerts { get; } = new();

    public ParentalDashboard(
        ILogger<ParentalDashboard> logger,
        IUniversalDeviceController deviceController,
        IAIParentalControlService aiService,
        IParentAuthenticationService authService,
        ICloudSyncService cloudSync,
        IWebFilteringService webFilter)
    {
        InitializeComponent();
        
        _logger = logger;
        _deviceController = deviceController;
        _aiService = aiService;
        _authService = authService;
        _cloudSync = cloudSync;
        _webFilter = webFilter;
        
        DeviceCollectionView.ItemsSource = Devices;
        
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        await LoadDashboardDataAsync();
        
        // Start real-time updates
        var timer = Application.Current?.Dispatcher.CreateTimer();
        if (timer != null)
        {
            timer.Interval = TimeSpan.FromSeconds(30);
            timer.Tick += async (s, e) => await RefreshDataAsync();
            timer.Start();
        }
    }

    private async Task LoadDashboardDataAsync()
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            // Check parent authentication
            if (!await _authService.IsParentAuthenticatedAsync())
            {
                await DisplayAlert("Authentication Required", "Please authenticate to access parental controls", "OK");
                // Navigate to authentication page
                return;
            }

            // Load devices
            await LoadDevicesAsync();

            // Load AI insights
            await LoadAIInsightsAsync();

            // Load security alerts
            await LoadSecurityAlertsAsync();

            // Update status
            await UpdateStatusAsync();

            _logger.LogInformation("Dashboard loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard");
            await DisplayAlert("Error", "Failed to load dashboard data", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    private async Task LoadDevicesAsync()
    {
        try
        {
            var devices = await _deviceController.DiscoverAllDevicesAsync();
            var knownDevices = await _deviceController.LoadKnownDevicesAsync();

            // Merge discovered and known devices
            var allDevices = devices.ToList();
            foreach (var known in knownDevices)
            {
                if (!allDevices.Any(d => d.MacAddress == known.MacAddress))
                {
                    allDevices.Add(known);
                }
            }

            Devices.Clear();
            foreach (var device in allDevices.OrderBy(d => d.Name))
            {
                var displayInfo = new DeviceDisplayInfo
                {
                    Id = device.Id,
                    Name = device.Name,
                    AssignedUser = device.AssignedUser,
                    DeviceIcon = GetDeviceIcon(device.DeviceType),
                    Status = device.Status,
                    StatusColor = device.StatusColor,
                    IsAllowed = !device.IsBlocked,
                    TrustScore = device.TrustScore
                };
                
                Devices.Add(displayInfo);
            }

            DeviceCountLabel.Text = $"{Devices.Count} devices";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load devices");
        }
    }

    private async Task LoadAIInsightsAsync()
    {
        try
        {
            var devices = await _deviceController.LoadKnownDevicesAsync();
            var insights = await _aiService.GenerateInsightsAsync(devices);

            Insights.Clear();
            foreach (var insight in insights.Take(5)) // Show top 5 insights
            {
                var displayInfo = new InsightDisplayInfo
                {
                    Title = insight.Title,
                    Description = insight.Description,
                    Recommendation = insight.Recommendation,
                    SeverityColor = GetSeverityColor(insight.Severity)
                };
                
                Insights.Add(displayInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI insights");
        }
    }

    private async Task LoadSecurityAlertsAsync()
    {
        try
        {
            var devices = await _deviceController.LoadKnownDevicesAsync();
            var allAlerts = new List<SecurityAlert>();

            foreach (var device in devices)
            {
                allAlerts.AddRange(device.SecurityAlerts.Where(a => !a.IsResolved));
            }

            SecurityAlerts.Clear();
            foreach (var alert in allAlerts.OrderByDescending(a => a.Timestamp).Take(10))
            {
                var displayInfo = new AlertDisplayInfo
                {
                    Title = alert.Title,
                    Description = alert.Description,
                    SeverityIcon = GetSeverityIcon(alert.Severity),
                    TimeAgo = GetTimeAgo(alert.Timestamp)
                };
                
                SecurityAlerts.Add(displayInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load security alerts");
        }
    }

    private async Task UpdateStatusAsync()
    {
        try
        {
            var syncStatus = await _cloudSync.GetSyncStatusAsync();
            var blockedDevices = Devices.Count(d => !d.IsAllowed);
            var totalDevices = Devices.Count;

            StatusLabel.Text = blockedDevices > 0 
                ? $"Warning {blockedDevices} of {totalDevices} devices blocked"
                : $"Shield All {totalDevices} devices protected";

            SyncStatusLabel.Text = syncStatus.IsConnected
                ? $"Sync Last sync: {GetTimeAgo(syncStatus.LastSyncTime)}"
                : "Offline Sync disconnected";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status");
        }
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            await LoadDevicesAsync();
            await UpdateStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Background refresh failed");
        }
    }

    // Event Handlers
    private async void OnEmergencyBlockClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await DisplayAlert("Emergency Block", 
                "This will immediately block all child and teenager devices. Continue?", 
                "Yes", "No");
            
            if (result)
            {
                foreach (var device in Devices.Where(d => d.AssignedUser != "Parent"))
                {
                    await _deviceController.BlockDeviceAsync(device.Id, BlockReason.ParentBlocked);
                }
                
                await DisplayAlert("Emergency Block", "All child devices have been blocked", "OK");
                await RefreshDataAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Emergency block failed");
            await DisplayAlert("Error", "Failed to execute emergency block", "OK");
        }
    }

    private async void OnScanDevicesClicked(object sender, EventArgs e)
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            
            await LoadDevicesAsync();
            await DisplayAlert("Scan Complete", $"Found {Devices.Count} devices", "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device scan failed");
            await DisplayAlert("Error", "Device scan failed", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    private async void OnSyncCloudClicked(object sender, EventArgs e)
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            
            var success = await _cloudSync.SyncDeviceProfilesAsync() &&
                         await _cloudSync.SyncWebFilteringRulesAsync() &&
                         await _cloudSync.SyncParentSettingsAsync();
            
            if (success)
            {
                await DisplayAlert("Sync Complete", "All data synced to cloud", "OK");
            }
            else
            {
                await DisplayAlert("Sync Failed", "Some data failed to sync", "OK");
            }
            
            await UpdateStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud sync failed");
            await DisplayAlert("Error", "Cloud sync failed", "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }

    // Helper methods
    private string GetDeviceIcon(DeviceType deviceType) => deviceType switch
    {
        DeviceType.Smartphone => "📱",
        DeviceType.Tablet => "📱",
        DeviceType.Laptop => "💻",
        DeviceType.Desktop => "🖥️",
        DeviceType.SmartTV => "📺",
        DeviceType.GameConsole => "🎮",
        DeviceType.SmartSpeaker => "🔊",
        DeviceType.Camera => "📷",
        DeviceType.Printer => "🖨️",
        DeviceType.IoTDevice => "🏠",
        _ => "❓"
    };

    private string GetSeverityColor(InsightSeverity severity) => severity switch
    {
        InsightSeverity.Low => "#3498DB",
        InsightSeverity.Medium => "#F39C12",
        InsightSeverity.High => "#E74C3C",
        InsightSeverity.Critical => "#8E44AD",
        _ => "#95A5A6"
    };

    private string GetSeverityIcon(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Low => "ℹ️",
        AlertSeverity.Medium => "⚠️",
        AlertSeverity.High => "🚨",
        AlertSeverity.Critical => "🔥",
        _ => "❓"
    };

    private string GetTimeAgo(DateTime timestamp)
    {
        var timeSpan = DateTime.Now - timestamp;
        
        if (timeSpan.TotalMinutes < 1)
            return "Just now";
        if (timeSpan.TotalMinutes < 60)
            return $"{(int)timeSpan.TotalMinutes}m ago";
        if (timeSpan.TotalHours < 24)
            return $"{(int)timeSpan.TotalHours}h ago";
        
        return $"{(int)timeSpan.TotalDays}d ago";
    }
}

// Display models for data binding
public class DeviceDisplayInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssignedUser { get; set; } = string.Empty;
    public string DeviceIcon { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public double TrustScore { get; set; }
}

public class InsightDisplayInfo
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string SeverityColor { get; set; } = string.Empty;
}

public class AlertDisplayInfo
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SeverityIcon { get; set; } = string.Empty;
    public string TimeAgo { get; set; } = string.Empty;
}