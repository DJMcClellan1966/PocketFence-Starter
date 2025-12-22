using System.Text.Json;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using PocketFence.Models;

namespace PocketFence.Services
{
    public interface ICloudSyncService
    {
        Task<bool> SyncDeviceProfilesAsync();
        Task<bool> SyncWebFilteringRulesAsync();
        Task<bool> SyncParentSettingsAsync();
        Task<bool> SendRemoteCommandAsync(string deviceId, RemoteCommand command);
        Task<List<RemoteCommand>> GetPendingCommandsAsync();
        Task<bool> RegisterDeviceForRemoteControlAsync(string deviceId, string deviceType);
        Task<bool> EnableRealTimeSyncAsync();
        Task<bool> BackupAllDataAsync();
        Task<bool> RestoreDataFromCloudAsync();
        Task<CloudSyncStatus> GetSyncStatusAsync();
        event EventHandler<RemoteCommandEventArgs>? CommandReceived;
    }

    public class CloudSyncService : ICloudSyncService
    {
        private readonly ILogger<CloudSyncService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IParentAuthenticationService _authService;
        private readonly string _cloudEndpoint;
        private readonly string _apiKey;
        private readonly Timer _syncTimer;
        private readonly Timer _commandTimer;
        private bool _isRealTimeSyncEnabled = false;

        public event EventHandler<RemoteCommandEventArgs>? CommandReceived;

        public CloudSyncService(
            ILogger<CloudSyncService> logger,
            IParentAuthenticationService authService,
            HttpClient httpClient)
        {
            _logger = logger;
            _authService = authService;
            _httpClient = httpClient;
            _cloudEndpoint = "https://api.pocketfence.cloud"; // Placeholder endpoint
            _apiKey = Environment.GetEnvironmentVariable("POCKETFENCE_API_KEY") ?? "demo-key";
            
            // Setup periodic sync timers
            _syncTimer = new Timer(PeriodicSync, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
            _commandTimer = new Timer(CheckForCommands, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        public async Task<bool> SyncDeviceProfilesAsync()
        {
            try
            {
                if (!await _authService.IsParentAuthenticatedAsync())
                {
                    _logger.LogWarning("Cannot sync device profiles - parent not authenticated");
                    return false;
                }

                var localProfilesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PocketFence", "DeviceProfiles");

                if (!Directory.Exists(localProfilesPath))
                    return true;

                var profileFiles = Directory.GetFiles(localProfilesPath, "*.json");
                var syncData = new List<DeviceProfile>();

                foreach (var file in profileFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var device = JsonSerializer.Deserialize<DeviceInfo>(json);
                        if (device != null)
                        {
                            syncData.Add(new DeviceProfile
                            {
                                DeviceId = device.Id,
                                Data = json,
                                LastModified = File.GetLastWriteTime(file),
                                Hash = GetDataHash(json)
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to read device profile: {file}");
                    }
                }

                var syncRequest = new SyncRequest
                {
                    Type = "device_profiles",
                    Data = JsonSerializer.Serialize(syncData),
                    Timestamp = DateTime.UtcNow
                };

                var response = await SendCloudRequestAsync("/sync/devices", syncRequest);
                
                if (response.Success)
                {
                    // Download any updates from cloud
                    await DownloadDeviceUpdatesAsync(response.Data);
                    _logger.LogInformation($"Successfully synced {syncData.Count} device profiles");
                }

                return response.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync device profiles");
                return false;
            }
        }

        public async Task<bool> SyncWebFilteringRulesAsync()
        {
            try
            {
                var filteringRulesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PocketFence", "FilteringRules.json");

                var rules = new WebFilteringRules();
                if (File.Exists(filteringRulesPath))
                {
                    var json = await File.ReadAllTextAsync(filteringRulesPath);
                    rules = JsonSerializer.Deserialize<WebFilteringRules>(json) ?? new();
                }

                var syncRequest = new SyncRequest
                {
                    Type = "filtering_rules",
                    Data = JsonSerializer.Serialize(rules),
                    Timestamp = DateTime.UtcNow
                };

                var response = await SendCloudRequestAsync("/sync/rules", syncRequest);
                
                if (response.Success && !string.IsNullOrEmpty(response.Data))
                {
                    var cloudRules = JsonSerializer.Deserialize<WebFilteringRules>(response.Data);
                    if (cloudRules != null)
                    {
                        await File.WriteAllTextAsync(filteringRulesPath, JsonSerializer.Serialize(cloudRules, new JsonSerializerOptions { WriteIndented = true }));
                        _logger.LogInformation("Web filtering rules synced successfully");
                    }
                }

                return response.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync web filtering rules");
                return false;
            }
        }

        public async Task<bool> SyncParentSettingsAsync()
        {
            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PocketFence", "ParentSettings.json");

                var settings = new ParentSettings();
                if (File.Exists(settingsPath))
                {
                    var json = await File.ReadAllTextAsync(settingsPath);
                    settings = JsonSerializer.Deserialize<ParentSettings>(json) ?? new();
                }

                var syncRequest = new SyncRequest
                {
                    Type = "parent_settings",
                    Data = JsonSerializer.Serialize(settings),
                    Timestamp = DateTime.UtcNow
                };

                var response = await SendCloudRequestAsync("/sync/settings", syncRequest);
                
                if (response.Success && !string.IsNullOrEmpty(response.Data))
                {
                    var cloudSettings = JsonSerializer.Deserialize<ParentSettings>(response.Data);
                    if (cloudSettings != null)
                    {
                        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(cloudSettings, new JsonSerializerOptions { WriteIndented = true }));
                        _logger.LogInformation("Parent settings synced successfully");
                    }
                }

                return response.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync parent settings");
                return false;
            }
        }

        public async Task<bool> SendRemoteCommandAsync(string deviceId, RemoteCommand command)
        {
            try
            {
                if (!await _authService.IsParentAuthenticatedAsync())
                {
                    _logger.LogWarning("Cannot send remote command - parent not authenticated");
                    return false;
                }

                command.Id = Guid.NewGuid().ToString();
                command.Timestamp = DateTime.UtcNow;
                command.DeviceId = deviceId;
                command.Status = CommandStatus.Pending;

                var commandRequest = new CommandRequest
                {
                    DeviceId = deviceId,
                    Command = command,
                    ParentId = await GetParentIdAsync()
                };

                var response = await SendCloudRequestAsync("/commands/send", commandRequest);
                
                if (response.Success)
                {
                    _logger.LogInformation($"Remote command sent to device {deviceId}: {command.Type}");
                    
                    // Store command locally for tracking
                    await StoreCommandLocallyAsync(command);
                }

                return response.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send remote command to device {deviceId}");
                return false;
            }
        }

        public async Task<List<RemoteCommand>> GetPendingCommandsAsync()
        {
            try
            {
                var response = await SendCloudRequestAsync("/commands/pending", new { });
                
                if (response.Success && !string.IsNullOrEmpty(response.Data))
                {
                    var commands = JsonSerializer.Deserialize<List<RemoteCommand>>(response.Data) ?? new();
                    
                    // Filter commands for this device
                    var deviceId = await GetCurrentDeviceIdAsync();
                    var deviceCommands = commands.Where(c => c.DeviceId == deviceId || c.DeviceId == "*").ToList();
                    
                    // Mark commands as received
                    foreach (var command in deviceCommands)
                    {
                        await MarkCommandAsReceivedAsync(command.Id);
                        CommandReceived?.Invoke(this, new RemoteCommandEventArgs { Command = command });
                    }
                    
                    return deviceCommands;
                }

                return new List<RemoteCommand>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get pending commands");
                return new List<RemoteCommand>();
            }
        }

        public async Task<bool> RegisterDeviceForRemoteControlAsync(string deviceId, string deviceType)
        {
            try
            {
                var registration = new DeviceRegistration
                {
                    DeviceId = deviceId,
                    DeviceType = deviceType,
                    Platform = GetCurrentPlatform(),
                    AppVersion = GetAppVersion(),
                    RegisteredAt = DateTime.UtcNow,
                    LastHeartbeat = DateTime.UtcNow
                };

                var response = await SendCloudRequestAsync("/devices/register", registration);
                
                if (response.Success)
                {
                    _logger.LogInformation($"Device {deviceId} registered for remote control");
                    
                    // Store registration locally
                    await StoreDeviceRegistrationAsync(registration);
                }

                return response.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to register device {deviceId} for remote control");
                return false;
            }
        }

        public async Task<bool> EnableRealTimeSyncAsync()
        {
            try
            {
                _isRealTimeSyncEnabled = true;
                
                // Increase sync frequency for real-time
                _syncTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(30));
                _commandTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(10));
                
                _logger.LogInformation("Real-time sync enabled");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enable real-time sync");
                return false;
            }
        }

        public async Task<bool> BackupAllDataAsync()
        {
            try
            {
                var backupData = new CloudBackup
                {
                    BackupId = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow,
                    DeviceProfiles = await GetAllDeviceProfilesAsync(),
                    WebFilteringRules = await GetWebFilteringRulesAsync(),
                    ParentSettings = await GetParentSettingsAsync(),
                    AccessLogs = await GetAccessLogsAsync()
                };

                var response = await SendCloudRequestAsync("/backup/create", backupData);
                
                if (response.Success)
                {
                    _logger.LogInformation($"Data backup created: {backupData.BackupId}");
                }

                return response.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backup data to cloud");
                return false;
            }
        }

        public async Task<bool> RestoreDataFromCloudAsync()
        {
            try
            {
                var response = await SendCloudRequestAsync("/backup/latest", new { });
                
                if (response.Success && !string.IsNullOrEmpty(response.Data))
                {
                    var backup = JsonSerializer.Deserialize<CloudBackup>(response.Data);
                    if (backup != null)
                    {
                        await RestoreFromBackupAsync(backup);
                        _logger.LogInformation($"Data restored from backup: {backup.BackupId}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore data from cloud");
                return false;
            }
        }

        public async Task<CloudSyncStatus> GetSyncStatusAsync()
        {
            try
            {
                var status = new CloudSyncStatus
                {
                    IsConnected = await TestCloudConnectionAsync(),
                    LastSyncTime = await GetLastSyncTimeAsync(),
                    PendingUploads = await CountPendingUploadsAsync(),
                    IsRealTimeSyncEnabled = _isRealTimeSyncEnabled,
                    DeviceRegistered = await IsDeviceRegisteredAsync()
                };

                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get sync status");
                return new CloudSyncStatus { IsConnected = false };
            }
        }

        // Private helper methods
        private async Task<CloudResponse> SendCloudRequestAsync<T>(string endpoint, T data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
                _httpClient.DefaultRequestHeaders.Add("X-Device-Id", await GetCurrentDeviceIdAsync());
                
                var response = await _httpClient.PostAsync($"{_cloudEndpoint}{endpoint}", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<CloudResponse>(responseContent) ?? new CloudResponse();
                }
                
                return new CloudResponse { Success = false, Message = $"HTTP {response.StatusCode}" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Cloud request failed: {endpoint}");
                return new CloudResponse { Success = false, Message = ex.Message };
            }
        }

        private async void PeriodicSync(object? state)
        {
            try
            {
                await SyncDeviceProfilesAsync();
                await SyncWebFilteringRulesAsync();
                await SyncParentSettingsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic sync failed");
            }
        }

        private async void CheckForCommands(object? state)
        {
            try
            {
                await GetPendingCommandsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Command check failed");
            }
        }

        private string GetDataHash(string data)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private async Task<string> GetCurrentDeviceIdAsync()
        {
            return Environment.MachineName + "_" + Environment.UserName;
        }

        private async Task<string> GetParentIdAsync()
        {
            // Get parent ID from authentication service
            return "parent_1"; // Placeholder
        }

        private string GetCurrentPlatform()
        {
#if WINDOWS
            return "Windows";
#elif ANDROID
            return "Android";
#elif IOS
            return "iOS";
#elif MACCATALYST
            return "macOS";
#else
            return "Unknown";
#endif
        }

        private string GetAppVersion()
        {
            return "1.0.0"; // Placeholder
        }

        // Additional placeholder methods
        private async Task DownloadDeviceUpdatesAsync(string data) { await Task.CompletedTask; }
        private async Task StoreCommandLocallyAsync(RemoteCommand command) { await Task.CompletedTask; }
        private async Task MarkCommandAsReceivedAsync(string commandId) { await Task.CompletedTask; }
        private async Task StoreDeviceRegistrationAsync(DeviceRegistration registration) { await Task.CompletedTask; }
        private async Task<List<DeviceProfile>> GetAllDeviceProfilesAsync() { return new List<DeviceProfile>(); }
        private async Task<WebFilteringRules> GetWebFilteringRulesAsync() { return new WebFilteringRules(); }
        private async Task<ParentSettings> GetParentSettingsAsync() { return new ParentSettings(); }
        private async Task<List<object>> GetAccessLogsAsync() { return new List<object>(); }
        private async Task RestoreFromBackupAsync(CloudBackup backup) { await Task.CompletedTask; }
        private async Task<bool> TestCloudConnectionAsync() { return true; }
        private async Task<DateTime> GetLastSyncTimeAsync() { return DateTime.Now; }
        private async Task<int> CountPendingUploadsAsync() { return 0; }
        private async Task<bool> IsDeviceRegisteredAsync() { return true; }

        public void Dispose()
        {
            _syncTimer?.Dispose();
            _commandTimer?.Dispose();
            _httpClient?.Dispose();
        }
    }

    // Data models
    public class DeviceProfile
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public string Hash { get; set; } = string.Empty;
    }

    public class WebFilteringRules
    {
        public List<string> BlockedCategories { get; set; } = new();
        public List<string> BlockedWebsites { get; set; } = new();
        public List<string> AllowedWebsites { get; set; } = new();
        public Dictionary<UserType, List<string>> UserTypeRestrictions { get; set; } = new();
    }

    public class ParentSettings
    {
        public string ParentId { get; set; } = string.Empty;
        public bool EnableNotifications { get; set; } = true;
        public bool StrictMode { get; set; } = false;
        public TimeSpan DefaultTimeLimit { get; set; } = TimeSpan.FromHours(2);
        public Dictionary<string, object> CustomSettings { get; set; } = new();
    }

    public class RemoteCommand
    {
        public string Id { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public CommandType Type { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        public DateTime Timestamp { get; set; }
        public CommandStatus Status { get; set; }
        public string Result { get; set; } = string.Empty;
    }

    public enum CommandType
    {
        BlockDevice,
        UnblockDevice,
        SetTimeLimit,
        BlockWebsite,
        UnblockWebsite,
        GetDeviceStatus,
        ForceSync,
        EmergencyBlock,
        GetScreenshot,
        SendMessage
    }

    public enum CommandStatus
    {
        Pending,
        Sent,
        Received,
        Executed,
        Failed
    }

    public class RemoteCommandEventArgs : EventArgs
    {
        public RemoteCommand Command { get; set; } = new();
    }

    public class SyncRequest
    {
        public string Type { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class CommandRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public RemoteCommand Command { get; set; } = new();
        public string ParentId { get; set; } = string.Empty;
    }

    public class DeviceRegistration
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public DateTime LastHeartbeat { get; set; }
    }

    public class CloudBackup
    {
        public string BackupId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<DeviceProfile> DeviceProfiles { get; set; } = new();
        public WebFilteringRules WebFilteringRules { get; set; } = new();
        public ParentSettings ParentSettings { get; set; } = new();
        public List<object> AccessLogs { get; set; } = new();
    }

    public class CloudResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    public class CloudSyncStatus
    {
        public bool IsConnected { get; set; }
        public DateTime LastSyncTime { get; set; }
        public int PendingUploads { get; set; }
        public bool IsRealTimeSyncEnabled { get; set; }
        public bool DeviceRegistered { get; set; }
    }
}

