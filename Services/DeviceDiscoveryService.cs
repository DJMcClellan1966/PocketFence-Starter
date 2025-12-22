using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;

namespace PocketFence.Services
{
    public interface IDeviceDiscoveryService
    {
        Task<List<Dictionary<string, object>>> ListDevicesAsync();
    }

    public class DeviceDiscoveryService : IDeviceDiscoveryService
    {
        private readonly ILogger<DeviceDiscoveryService> _logger;

        public DeviceDiscoveryService(ILogger<DeviceDiscoveryService> logger)
        {
            _logger = logger;
        }

        public async Task<List<Dictionary<string, object>>> ListDevicesAsync()
        {
            try
            {
                _logger.LogInformation("Starting device discovery");
                
                var devices = new List<Dictionary<string, object>>();
                
                // Simple ping-based discovery for local subnet
                await DiscoverLocalDevicesAsync(devices);
                
                _logger.LogInformation("Discovered {Count} devices", devices.Count);
                return devices;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover devices");
                return new List<Dictionary<string, object>>();
            }
        }

        private async Task DiscoverLocalDevicesAsync(List<Dictionary<string, object>> devices)
        {
            // Get local IP to determine subnet
            var localIP = GetLocalIPAddress();
            if (localIP == null) return;

            var baseIP = localIP.Substring(0, localIP.LastIndexOf('.'));
            var tasks = new List<Task>();

            // Ping common IP ranges
            for (int i = 1; i <= 20; i++)
            {
                var targetIP = $"{baseIP}.{i}";
                tasks.Add(PingDeviceAsync(devices, targetIP));
            }

            await Task.WhenAll(tasks);
        }

        private async Task PingDeviceAsync(List<Dictionary<string, object>> devices, string ipAddress)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ipAddress, 1000);
                
                if (reply.Status == IPStatus.Success)
                {
                    var device = new Dictionary<string, object>
                    {
                        {"ip", ipAddress},
                        {"hostname", await GetHostnameAsync(ipAddress)},
                        {"status", "online"},
                        {"lastSeen", DateTime.Now}
                    };
                    
                    lock (devices)
                    {
                        devices.Add(device);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to ping {IP}: {Message}", ipAddress, ex.Message);
            }
        }

        private async Task<string> GetHostnameAsync(string ip)
        {
            try
            {
                var hostEntry = await System.Net.Dns.GetHostEntryAsync(ip);
                return hostEntry.HostName;
            }
            catch
            {
                return "Unknown";
            }
        }

        private string? GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                return host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && 
                                         !System.Net.IPAddress.IsLoopback(ip))
                    ?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
