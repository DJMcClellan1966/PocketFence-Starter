using Microsoft.Extensions.Logging;

namespace PocketFence.Services
{
    public interface IVpnHelper
    {
        Task<List<string>?> GetDnsAsync();
        Task<bool> SetDnsAsync(List<string> dnsServers);
    }

    public class VpnHelper : IVpnHelper
    {
        private readonly ILogger<VpnHelper> _logger;
        private List<string> _currentDns = new() { "8.8.8.8", "8.8.4.4" };

        public VpnHelper(ILogger<VpnHelper> logger)
        {
            _logger = logger;
        }

        public async Task<List<string>?> GetDnsAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving current DNS servers");
                await Task.Delay(100); // Simulate operation
                return _currentDns;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve DNS servers");
                return null;
            }
        }

        public async Task<bool> SetDnsAsync(List<string> dnsServers)
        {
            if (dnsServers == null || !dnsServers.Any())
                return false;

            try
            {
                _logger.LogInformation("Setting DNS servers: {Servers}", string.Join(", ", dnsServers));
                await Task.Delay(200); // Simulate operation
                _currentDns = new List<string>(dnsServers);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set DNS servers");
                return false;
            }
        }
    }
}
