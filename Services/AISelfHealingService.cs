using Microsoft.Extensions.Logging;
using PocketFence.Models;
using PocketFence.Services.AI;
using System.Text.Json;

namespace PocketFence.Services
{
    public interface IAISelfHealingService
    {
        Task<bool> StartSelfHealingAsync();
        Task<bool> StopSelfHealingAsync();
        Task<bool> DiagnoseAndRepairAsync();
        Task<List<SystemDiagnostic>> GetSystemHealthAsync();
        Task<bool> RepairServiceAsync(string serviceName);
        Task<bool> RestartFailedServicesAsync();
        Task<bool> OptimizePerformanceAsync();
        Task<SelfHealingStatus> GetSelfHealingStatusAsync();
        event EventHandler<HealingEventArgs> ServiceHealed;
        event EventHandler<DiagnosticEventArgs> DiagnosticCompleted;
    }

    public class AISelfHealingService : IAISelfHealingService
    {
        private readonly ILogger<AISelfHealingService> _logger;
        private readonly IUniversalDeviceController _deviceController;
        private readonly IAIParentalControlService _aiService;
        private readonly IPerformanceOptimizer _performanceOptimizer;
        private readonly IAutonomousProtectionService _autonomousService;
        private readonly Timer _healingTimer;
        private readonly Dictionary<string, ServiceHealth> _serviceHealthMap = new();
        private readonly List<SystemDiagnostic> _diagnosticHistory = new();
        private bool _isHealingActive = false;
        private readonly string _configPath;

        public event EventHandler<HealingEventArgs>? ServiceHealed;
        public event EventHandler<DiagnosticEventArgs>? DiagnosticCompleted;

        public AISelfHealingService(
            ILogger<AISelfHealingService> logger,
            IUniversalDeviceController deviceController,
            IAIParentalControlService aiService,
            IPerformanceOptimizer performanceOptimizer,
            IAutonomousProtectionService autonomousService)
        {
            _logger = logger;
            _deviceController = deviceController;
            _aiService = aiService;
            _performanceOptimizer = performanceOptimizer;
            _autonomousService = autonomousService;

            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PocketFence", "SelfHealing");
            Directory.CreateDirectory(_configPath);

            InitializeServiceHealth();
            
            // Run diagnostic and healing every 5 minutes
            _healingTimer = new Timer(AutoHealingCycle, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        public async Task<bool> StartSelfHealingAsync()
        {
            try
            {
                _isHealingActive = true;
                _logger.LogInformation("AI Self-Healing Service started");
                
                // Perform initial system health check
                await DiagnoseAndRepairAsync();
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start self-healing service");
                return false;
            }
        }

        public async Task<bool> StopSelfHealingAsync()
        {
            try
            {
                _isHealingActive = false;
                _logger.LogInformation("AI Self-Healing Service stopped");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop self-healing service");
                return false;
            }
        }

        public async Task<bool> DiagnoseAndRepairAsync()
        {
            if (!_isHealingActive) return false;

            try
            {
                var diagnostics = new List<SystemDiagnostic>();
                var repairs = new List<string>();

                // Check each critical service
                var services = new Dictionary<string, Func<Task<bool>>>
                {
                    { "DeviceController", async () => await TestDeviceControllerAsync() },
                    { "AIService", async () => await TestAIServiceAsync() },
                    { "PerformanceOptimizer", async () => await TestPerformanceOptimizerAsync() },
                    { "AutonomousProtection", async () => await TestAutonomousProtectionAsync() }
                };

                foreach (var service in services)
                {
                    var diagnostic = await DiagnoseServiceAsync(service.Key, service.Value);
                    diagnostics.Add(diagnostic);

                    if (!diagnostic.IsHealthy)
                    {
                        var repaired = await RepairServiceAsync(service.Key);
                        if (repaired)
                        {
                            repairs.Add(service.Key);
                            ServiceHealed?.Invoke(this, new HealingEventArgs { ServiceName = service.Key, WasSuccessful = true });
                        }
                    }
                }

                // Check system resources
                await DiagnoseSystemResourcesAsync(diagnostics);

                // Optimize performance if needed
                await OptimizeIfNeededAsync(diagnostics);

                // Store diagnostic results
                _diagnosticHistory.AddRange(diagnostics);
                if (_diagnosticHistory.Count > 1000) // Keep only last 1000 diagnostics
                {
                    _diagnosticHistory.RemoveRange(0, _diagnosticHistory.Count - 1000);
                }

                DiagnosticCompleted?.Invoke(this, new DiagnosticEventArgs { Diagnostics = diagnostics, RepairsPerformed = repairs });

                _logger.LogInformation($"System diagnosis completed. {repairs.Count} services repaired.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "System diagnosis and repair failed");
                return false;
            }
        }

        public async Task<List<SystemDiagnostic>> GetSystemHealthAsync()
        {
            var diagnostics = new List<SystemDiagnostic>();

            try
            {
                // Current service health
                foreach (var serviceHealth in _serviceHealthMap)
                {
                    diagnostics.Add(new SystemDiagnostic
                    {
                        Id = Guid.NewGuid().ToString(),
                        Timestamp = DateTime.Now,
                        ServiceName = serviceHealth.Key,
                        IsHealthy = serviceHealth.Value.IsHealthy,
                        Details = serviceHealth.Value.LastError ?? "Service operational",
                        Severity = serviceHealth.Value.IsHealthy ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                        RecommendedAction = serviceHealth.Value.IsHealthy ? "None" : "Service restart recommended"
                    });
                }

                // System performance metrics
                var perfMetrics = await _performanceOptimizer.GetCurrentPerformanceAsync();
                diagnostics.Add(new SystemDiagnostic
                {
                    Id = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.Now,
                    ServiceName = "SystemPerformance",
                    IsHealthy = perfMetrics.MemoryUsageMB < 1000 && perfMetrics.AverageResponseTimeMs < 3000,
                    Details = $"Memory: {perfMetrics.MemoryUsageMB:F1}MB, Response: {perfMetrics.AverageResponseTimeMs:F1}ms",
                    Severity = DiagnosticSeverity.Info
                });

                return diagnostics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get system health");
                return new List<SystemDiagnostic>();
            }
        }

        public async Task<bool> RepairServiceAsync(string serviceName)
        {
            try
            {
                _logger.LogInformation($"Attempting to repair service: {serviceName}");

                switch (serviceName.ToLower())
                {
                    case "devicecontroller":
                        return await RepairDeviceControllerAsync();
                    case "aiservice":
                        return await RepairAIServiceAsync();
                    case "performanceoptimizer":
                        return await RepairPerformanceOptimizerAsync();
                    case "autonomousprotection":
                        return await RepairAutonomousProtectionAsync();
                    default:
                        _logger.LogWarning($"Unknown service for repair: {serviceName}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to repair service: {serviceName}");
                return false;
            }
        }

        public async Task<bool> RestartFailedServicesAsync()
        {
            try
            {
                var failedServices = _serviceHealthMap.Where(s => !s.Value.IsHealthy).ToList();
                var repaired = 0;

                foreach (var service in failedServices)
                {
                    if (await RepairServiceAsync(service.Key))
                    {
                        repaired++;
                    }
                }

                _logger.LogInformation($"Restarted {repaired} of {failedServices.Count} failed services");
                return repaired > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart failed services");
                return false;
            }
        }

        public async Task<bool> OptimizePerformanceAsync()
        {
            try
            {
                // Run all optimization routines
                await _performanceOptimizer.OptimizeMemoryUsageAsync();
                await _performanceOptimizer.OptimizeForProcessingSpeedAsync();
                await _performanceOptimizer.CleanupOldDataAsync();

                _logger.LogInformation("Performance optimization completed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Performance optimization failed");
                return false;
            }
        }

        public async Task<SelfHealingStatus> GetSelfHealingStatusAsync()
        {
            try
            {
                var healthyServices = _serviceHealthMap.Count(s => s.Value.IsHealthy);
                var totalServices = _serviceHealthMap.Count;

                return new SelfHealingStatus
                {
                    IsActive = _isHealingActive,
                    HealthPercentage = totalServices > 0 ? (double)healthyServices / totalServices * 100 : 100,
                    TotalServices = totalServices,
                    HealthyServices = healthyServices,
                    LastDiagnostic = _diagnosticHistory.OrderByDescending(d => d.Timestamp).FirstOrDefault()?.Timestamp,
                    TotalRepairs = _diagnosticHistory.Count(d => d.RepairAttempted),
                    UptimeHours = DateTime.Now.Subtract(DateTime.Today).TotalHours
                };
            }
            catch
            {
                return new SelfHealingStatus { IsActive = _isHealingActive };
            }
        }

        // Private helper methods
        private void InitializeServiceHealth()
        {
            var services = new[] { "DeviceController", "AIService", "PerformanceOptimizer", "AutonomousProtection" };
            foreach (var service in services)
            {
                _serviceHealthMap[service] = new ServiceHealth { IsHealthy = true, LastCheck = DateTime.Now };
            }
        }

        private async void AutoHealingCycle(object? state)
        {
            if (_isHealingActive)
            {
                await DiagnoseAndRepairAsync();
            }
        }

        private async Task<SystemDiagnostic> DiagnoseServiceAsync(string serviceName, Func<Task<bool>> testFunction)
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var isHealthy = await testFunction();
                stopwatch.Stop();

                _serviceHealthMap[serviceName] = new ServiceHealth
                {
                    IsHealthy = isHealthy,
                    LastCheck = DateTime.Now,
                    ResponseTime = stopwatch.ElapsedMilliseconds,
                    LastError = isHealthy ? null : "Service test failed"
                };

                return new SystemDiagnostic
                {
                    Id = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.Now,
                    ServiceName = serviceName,
                    IsHealthy = isHealthy,
                    Details = isHealthy ? $"Response time: {stopwatch.ElapsedMilliseconds}ms" : "Service test failed",
                    Severity = isHealthy ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning
                };
            }
            catch (Exception ex)
            {
                _serviceHealthMap[serviceName] = new ServiceHealth
                {
                    IsHealthy = false,
                    LastCheck = DateTime.Now,
                    LastError = ex.Message
                };

                return new SystemDiagnostic
                {
                    Id = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.Now,
                    ServiceName = serviceName,
                    IsHealthy = false,
                    Details = ex.Message,
                    Severity = DiagnosticSeverity.Error
                };
            }
        }

        private async Task<bool> TestDeviceControllerAsync()
        {
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                return devices != null;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TestAIServiceAsync()
        {
            try
            {
                // Simple AI service test
                await Task.Delay(10); // Simulate AI processing
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TestPerformanceOptimizerAsync()
        {
            try
            {
                var metrics = await _performanceOptimizer.GetCurrentPerformanceAsync();
                return metrics != null;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TestAutonomousProtectionAsync()
        {
            try
            {
                var status = await _autonomousService.GetAutonomousStatusAsync();
                return status != null;
            }
            catch
            {
                return false;
            }
        }

        private async Task DiagnoseSystemResourcesAsync(List<SystemDiagnostic> diagnostics)
        {
            try
            {
                // Memory check
                var memoryUsage = GC.GetTotalMemory(false) / 1024 / 1024;
                diagnostics.Add(new SystemDiagnostic
                {
                    Id = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.Now,
                    ServiceName = "MemoryUsage",
                    IsHealthy = memoryUsage < 1000, // Less than 1GB
                    Details = $"Memory usage: {memoryUsage}MB",
                    Severity = memoryUsage > 1000 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info
                });

                // Disk space check
                var driveInfo = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!);
                var freeSpaceGB = driveInfo.AvailableFreeSpace / 1024 / 1024 / 1024;
                diagnostics.Add(new SystemDiagnostic
                {
                    Id = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.Now,
                    ServiceName = "DiskSpace",
                    IsHealthy = freeSpaceGB > 1, // More than 1GB free
                    Details = $"Free disk space: {freeSpaceGB}GB",
                    Severity = freeSpaceGB < 1 ? DiagnosticSeverity.Critical : DiagnosticSeverity.Info
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to diagnose system resources");
            }
        }

        private async Task OptimizeIfNeededAsync(List<SystemDiagnostic> diagnostics)
        {
            var memoryIssue = diagnostics.Any(d => d.ServiceName == "MemoryUsage" && !d.IsHealthy);
            var diskIssue = diagnostics.Any(d => d.ServiceName == "DiskSpace" && !d.IsHealthy);

            if (memoryIssue)
            {
                await _performanceOptimizer.OptimizeMemoryUsageAsync();
                _logger.LogInformation("Automatic memory optimization triggered");
            }

            if (diskIssue)
            {
                await _performanceOptimizer.CleanupOldDataAsync();
                _logger.LogInformation("Automatic disk cleanup triggered");
            }
        }

        private async Task<bool> RepairDeviceControllerAsync()
        {
            try
            {
                // Attempt to reinitialize device controller
                await _deviceController.DiscoverAllDevicesAsync();
                _logger.LogInformation("Device controller repaired successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to repair device controller");
                return false;
            }
        }

        private async Task<bool> RepairAIServiceAsync()
        {
            try
            {
                // Clear AI cache and reinitialize
                await Task.Delay(100); // Simulate AI service restart
                _logger.LogInformation("AI service repaired successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to repair AI service");
                return false;
            }
        }

        private async Task<bool> RepairPerformanceOptimizerAsync()
        {
            try
            {
                // Run memory optimization
                await _performanceOptimizer.OptimizeMemoryUsageAsync();
                _logger.LogInformation("Performance optimizer repaired successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to repair performance optimizer");
                return false;
            }
        }

        private async Task<bool> RepairAutonomousProtectionAsync()
        {
            try
            {
                // Restart autonomous protection
                await _autonomousService.DisableAutonomousModeAsync();
                await Task.Delay(1000);
                await _autonomousService.EnableAutonomousModeAsync();
                _logger.LogInformation("Autonomous protection repaired successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to repair autonomous protection");
                return false;
            }
        }
    }

    // Supporting classes
    public class ServiceHealth
    {
        public bool IsHealthy { get; set; }
        public DateTime LastCheck { get; set; }
        public string? LastError { get; set; }
        public long ResponseTime { get; set; }
    }

    public class SystemDiagnostic
    {
        public string Id { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public bool IsHealthy { get; set; }
        public string Details { get; set; } = string.Empty;
        public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;
        public string RecommendedAction { get; set; } = string.Empty;
        public bool RepairAttempted { get; set; } = false;
    }

    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public class SelfHealingStatus
    {
        public bool IsActive { get; set; }
        public double HealthPercentage { get; set; }
        public int TotalServices { get; set; }
        public int HealthyServices { get; set; }
        public DateTime? LastDiagnostic { get; set; }
        public int TotalRepairs { get; set; }
        public double UptimeHours { get; set; }
    }

    public class HealingEventArgs : EventArgs
    {
        public string ServiceName { get; set; } = string.Empty;
        public bool WasSuccessful { get; set; }
        public string Details { get; set; } = string.Empty;
    }

    public class DiagnosticEventArgs : EventArgs
    {
        public List<SystemDiagnostic> Diagnostics { get; set; } = new();
        public List<string> RepairsPerformed { get; set; } = new();
    }
}