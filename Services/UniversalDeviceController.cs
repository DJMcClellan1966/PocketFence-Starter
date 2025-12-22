using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Management;
using Microsoft.Extensions.Logging;
using PocketFence.Models;
using PocketFence.Services.AI;
using DeviceType = PocketFence.Models.DeviceType;

namespace PocketFence.Services
{
    public interface IUniversalDeviceController
    {
        Task<List<DeviceInfo>> DiscoverAllDevicesAsync();
        Task<bool> BlockDeviceAsync(string deviceId, BlockReason reason = BlockReason.ParentBlocked);
        Task<bool> UnblockDeviceAsync(string deviceId);
        Task<bool> SetTimeRestrictionAsync(string deviceId, TimeSpan dailyLimit);
        Task<bool> AddWebsiteRestrictionAsync(string deviceId, string website, bool isBlocked);
        Task<DeviceInfo?> IdentifyDeviceAsync(string macAddress);
        Task<bool> UpdateDeviceUserAsync(string deviceId, string userName, UserType userType);
        Task SaveDeviceProfileAsync(DeviceInfo device);
        Task<List<DeviceInfo>> LoadKnownDevicesAsync();
    }

    public class UniversalDeviceController : IUniversalDeviceController
    {
        private readonly ILogger<UniversalDeviceController> _logger;
        private readonly IAIParentalControlService _aiService;
        private readonly IWebFilteringService _webFilter;
        private readonly Dictionary<string, DeviceInfo> _knownDevices = new();
        private readonly string _deviceStoragePath;

        public UniversalDeviceController(
            ILogger<UniversalDeviceController> logger,
            IAIParentalControlService aiService,
            IWebFilteringService webFilter)
        {
            _logger = logger;
            _aiService = aiService;
            _webFilter = webFilter;
            _deviceStoragePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PocketFence", "DeviceProfiles");
            Directory.CreateDirectory(_deviceStoragePath);
            _ = LoadKnownDevicesAsync();
        }

        public async Task<List<DeviceInfo>> DiscoverAllDevicesAsync()
        {
            _logger.LogInformation("Starting comprehensive device discovery");
            var devices = new List<DeviceInfo>();

            try
            {
                // Network scanning for all connected devices
                await DiscoverNetworkDevicesAsync(devices);
                
                // Platform-specific device discovery
#if WINDOWS
                await DiscoverWindowsDevicesAsync(devices);
#elif ANDROID
                await DiscoverAndroidDevicesAsync(devices);
#endif
                
                // IoT and smart device discovery
                await DiscoverIoTDevicesAsync(devices);
                
                // Update with known device profiles
                await EnhanceWithKnownProfilesAsync(devices);
                
                // AI-powered device classification
                await ClassifyDevicesWithAIAsync(devices);

                _logger.LogInformation($"Discovered {devices.Count} devices");
                return devices;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover devices");
                return new List<DeviceInfo>();
            }
        }

        private async Task DiscoverNetworkDevicesAsync(List<DeviceInfo> devices)
        {
            var localIp = GetLocalIPAddress();
            if (string.IsNullOrEmpty(localIp)) return;

            var subnet = localIp.Substring(0, localIp.LastIndexOf('.'));
            var tasks = new List<Task>();

            for (int i = 1; i <= 254; i++)
            {
                string ip = $"{subnet}.{i}";
                tasks.Add(Task.Run(async () =>
                {
                    var device = await ScanDeviceAsync(ip);
                    if (device != null)
                    {
                        lock (devices)
                        {
                            devices.Add(device);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }

        private async Task<DeviceInfo?> ScanDeviceAsync(string ip)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 1000);
                
                if (reply.Status == IPStatus.Success)
                {
                    var device = new DeviceInfo
                    {
                        IpAddress = ip,
                        IsConnected = true,
                        LastSeen = DateTime.Now
                    };

                    // Get MAC address and hostname
                    await EnhanceDeviceInfoAsync(device);
                    
                    return device;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Failed to ping {ip}: {ex.Message}");
            }
            
            return null;
        }

        private async Task EnhanceDeviceInfoAsync(DeviceInfo device)
        {
            try
            {
                // Get MAC address from ARP table
                device.MacAddress = GetMacAddress(device.IpAddress);
                
                // Get hostname
                try
                {
                    var host = await System.Net.Dns.GetHostEntryAsync(device.IpAddress);
                    device.Name = host.HostName ?? device.IpAddress;
                }
                catch
                {
                    device.Name = device.IpAddress;
                }

                // Detect device type from network signatures
                await DetectDeviceTypeAsync(device);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Failed to enhance device info for {device.IpAddress}: {ex.Message}");
            }
        }

        private async Task DetectDeviceTypeAsync(DeviceInfo device)
        {
            // Port scanning and service detection for device classification
            var commonPorts = new[] { 22, 23, 80, 135, 445, 554, 8080, 9100 };
            var openPorts = new List<int>();

            foreach (var port in commonPorts)
            {
                if (await IsPortOpenAsync(device.IpAddress, port))
                {
                    openPorts.Add(port);
                }
            }

            // Device classification based on open ports and network behavior
            device.DeviceType = ClassifyDeviceByPorts(openPorts);
            
            // Enhanced classification using AI
            if (_aiService != null)
            {
                try
                {
                    device.DeviceType = await _aiService.ClassifyDeviceTypeAsync(device.Name, device.IpAddress);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"AI classification failed: {ex.Message}");
                }
            }
        }

        private DeviceType ClassifyDeviceByPorts(List<int> openPorts)
        {
            if (openPorts.Contains(9100)) return DeviceType.Printer;
            if (openPorts.Contains(554)) return DeviceType.Camera;
            if (openPorts.Contains(8080) && openPorts.Contains(80)) return DeviceType.SmartHome;
            if (openPorts.Contains(23)) return DeviceType.Router;
            if (openPorts.Contains(135) || openPorts.Contains(445)) return DeviceType.Desktop;
            return DeviceType.Unknown;
        }

        private async Task<bool> IsPortOpenAsync(string ip, int port)
        {
            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect(ip, port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
                return success && client.Connected;
            }
            catch
            {
                return false;
            }
        }

#if WINDOWS
        private async Task DiscoverWindowsDevicesAsync(List<DeviceInfo> devices)
        {
            try
            {
                // Use WMI to discover Windows devices on network
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PingStatus WHERE Address LIKE '%'");
                // Additional Windows-specific discovery logic
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Windows device discovery failed: {ex.Message}");
            }
        }
#endif

#if ANDROID
        private async Task DiscoverAndroidDevicesAsync(List<DeviceInfo> devices)
        {
            try
            {
                // Android-specific device discovery using WiFi manager
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Android device discovery failed: {ex.Message}");
            }
        }
#endif

        private async Task DiscoverIoTDevicesAsync(List<DeviceInfo> devices)
        {
            try
            {
                // mDNS discovery for smart devices
                // UPnP discovery for media devices
                // Bluetooth LE scanning for wearables
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"IoT device discovery failed: {ex.Message}");
            }
        }

        private async Task EnhanceWithKnownProfilesAsync(List<DeviceInfo> devices)
        {
            foreach (var device in devices)
            {
                if (_knownDevices.TryGetValue(device.MacAddress, out var knownDevice))
                {
                    // Merge with known profile
                    device.Id = knownDevice.Id;
                    device.AssignedUser = knownDevice.AssignedUser;
                    device.UserType = knownDevice.UserType;
                    device.BlockedWebsites = knownDevice.BlockedWebsites;
                    device.AllowedWebsites = knownDevice.AllowedWebsites;
                    device.DailyTimeLimit = knownDevice.DailyTimeLimit;
                    device.BehaviorProfile = knownDevice.BehaviorProfile;
                    device.TrustScore = knownDevice.TrustScore;
                    device.FirstSeen = knownDevice.FirstSeen;
                }
            }
        }

        private async Task ClassifyDevicesWithAIAsync(List<DeviceInfo> devices)
        {
            foreach (var device in devices)
            {
                try
                {
                    if (_aiService != null)
                    {
                        // AI-powered device classification and risk assessment
                        device.TrustScore = await _aiService.CalculateTrustScoreAsync(device);
                        
                        // Check for security threats
                        var threats = await _aiService.DetectSecurityThreatsAsync(device);
                        device.SecurityAlerts.AddRange(threats);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"AI classification failed for device {device.Name}: {ex.Message}");
                }
            }
        }

        public async Task<bool> BlockDeviceAsync(string deviceId, BlockReason reason = BlockReason.ParentBlocked)
        {
            try
            {
                var device = _knownDevices.Values.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) return false;

                device.IsBlocked = true;
                device.BlockReason = reason;

                // Apply network-level blocking
                await ApplyNetworkBlockAsync(device);
                
                // Save updated profile
                await SaveDeviceProfileAsync(device);
                
                _logger.LogInformation($"Device {device.Name} ({deviceId}) blocked for reason: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to block device {deviceId}");
                return false;
            }
        }

        public async Task<bool> UnblockDeviceAsync(string deviceId)
        {
            try
            {
                var device = _knownDevices.Values.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) return false;

                device.IsBlocked = false;
                device.BlockReason = BlockReason.None;

                // Remove network-level blocking
                await RemoveNetworkBlockAsync(device);
                
                // Save updated profile
                await SaveDeviceProfileAsync(device);
                
                _logger.LogInformation($"Device {device.Name} ({deviceId}) unblocked");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to unblock device {deviceId}");
                return false;
            }
        }

        private async Task ApplyNetworkBlockAsync(DeviceInfo device)
        {
            // Implement network-level blocking (firewall rules, router API, etc.)
            await Task.CompletedTask;
        }

        private async Task RemoveNetworkBlockAsync(DeviceInfo device)
        {
            // Remove network-level blocking
            await Task.CompletedTask;
        }

        public async Task<bool> SetTimeRestrictionAsync(string deviceId, TimeSpan dailyLimit)
        {
            try
            {
                var device = _knownDevices.Values.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) return false;

                device.DailyTimeLimit = dailyLimit;
                await SaveDeviceProfileAsync(device);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to set time restriction for device {deviceId}");
                return false;
            }
        }

        public async Task<bool> AddWebsiteRestrictionAsync(string deviceId, string website, bool isBlocked)
        {
            try
            {
                var device = _knownDevices.Values.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) return false;

                if (isBlocked)
                {
                    if (!device.BlockedWebsites.Contains(website))
                        device.BlockedWebsites.Add(website);
                }
                else
                {
                    if (!device.AllowedWebsites.Contains(website))
                        device.AllowedWebsites.Add(website);
                }

                await SaveDeviceProfileAsync(device);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to add website restriction for device {deviceId}");
                return false;
            }
        }

        public async Task<DeviceInfo?> IdentifyDeviceAsync(string macAddress)
        {
            if (_knownDevices.TryGetValue(macAddress, out var device))
            {
                return device;
            }

            // Enhanced identification using AI and network analysis
            return null;
        }

        public async Task<bool> UpdateDeviceUserAsync(string deviceId, string userName, UserType userType)
        {
            try
            {
                var device = _knownDevices.Values.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) return false;

                device.AssignedUser = userName;
                device.UserType = userType;
                
                // Apply default restrictions based on user type
                ApplyDefaultRestrictions(device, userType);
                
                await SaveDeviceProfileAsync(device);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update device user for {deviceId}");
                return false;
            }
        }

        private void ApplyDefaultRestrictions(DeviceInfo device, UserType userType)
        {
            switch (userType)
            {
                case UserType.Child:
                    device.DailyTimeLimit = TimeSpan.FromHours(2);
                    device.BlockedWebsites.AddRange(new[] { "facebook.com", "instagram.com", "tiktok.com" });
                    break;
                case UserType.Teenager:
                    device.DailyTimeLimit = TimeSpan.FromHours(4);
                    break;
                case UserType.Parent:
                    device.DailyTimeLimit = TimeSpan.FromHours(24);
                    break;
                case UserType.Guest:
                    device.DailyTimeLimit = TimeSpan.FromHours(1);
                    break;
            }
        }

        public async Task SaveDeviceProfileAsync(DeviceInfo device)
        {
            try
            {
                var filePath = Path.Combine(_deviceStoragePath, $"{device.Id}.json");
                var json = System.Text.Json.JsonSerializer.Serialize(device, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(filePath, json);
                
                _knownDevices[device.MacAddress] = device;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to save device profile for {device.Id}");
            }
        }

        public async Task<List<DeviceInfo>> LoadKnownDevicesAsync()
        {
            var devices = new List<DeviceInfo>();
            
            try
            {
                if (!Directory.Exists(_deviceStoragePath))
                    return devices;

                var files = Directory.GetFiles(_deviceStoragePath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var device = System.Text.Json.JsonSerializer.Deserialize<DeviceInfo>(json);
                        if (device != null)
                        {
                            devices.Add(device);
                            _knownDevices[device.MacAddress] = device;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to load device profile from {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load known devices");
            }

            return devices;
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                return host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                    ?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetMacAddress(string ipAddress)
        {
            try
            {
                // Platform-specific MAC address resolution
#if WINDOWS
                var output = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = $"-a {ipAddress}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                })?.StandardOutput.ReadToEnd();

                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains(ipAddress))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                return parts[1];
                            }
                        }
                    }
                }
#endif
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
