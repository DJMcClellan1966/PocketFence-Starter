using Microsoft.Extensions.Logging;
using PocketFence.Models;
using PocketFence.Services.AI;
using System.Text.Json;
using DeviceType = PocketFence.Models.DeviceType;

namespace PocketFence.Services
{
    public interface IAutonomousProtectionService
    {
        Task<bool> EnableAutonomousModeAsync();
        Task<bool> DisableAutonomousModeAsync();
        Task<bool> IsAutonomousModeEnabledAsync();
        Task<AutoProtectionAction> EvaluateDeviceAsync(DeviceInfo device);
        Task<bool> ApplyAutoProtectionAsync(DeviceInfo device, AutoProtectionAction action);
        Task<List<AutoProtectionRule>> GetActiveRulesAsync();
        Task<bool> AddCustomRuleAsync(AutoProtectionRule rule);
        Task<bool> UpdateLearningModelAsync(DeviceInfo device, string activity, bool wasAllowed);
        Task<AutonomousStatus> GetAutonomousStatusAsync();
        Task<bool> SetAutonomousLevelAsync(AutonomousLevel level);
    }

    public class AutonomousProtectionService : IAutonomousProtectionService
    {
        private readonly ILogger<AutonomousProtectionService> _logger;
        private readonly IAIParentalControlService _aiService;
        private readonly IUniversalDeviceController _deviceController;
        private readonly IWebFilteringService _webFilter;
        private readonly string _configPath;
        private readonly Timer _protectionTimer;
        private readonly List<AutoProtectionRule> _rules = new();
        private readonly Dictionary<string, LearningData> _learningModel = new();
        private bool _isAutonomousEnabled = false;
        private AutonomousLevel _autonomousLevel = AutonomousLevel.Moderate;

        public AutonomousProtectionService(
            ILogger<AutonomousProtectionService> logger,
            IAIParentalControlService aiService,
            IUniversalDeviceController deviceController,
            IWebFilteringService webFilter)
        {
            _logger = logger;
            _aiService = aiService;
            _deviceController = deviceController;
            _webFilter = webFilter;
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PocketFence", "Autonomous");
            Directory.CreateDirectory(_configPath);

            // Initialize protection rules and learning model
            _ = LoadConfigurationAsync();
            InitializeDefaultRules();

            // Setup autonomous protection timer (runs every 30 seconds)
            _protectionTimer = new Timer(RunAutonomousProtection, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        public async Task<bool> EnableAutonomousModeAsync()
        {
            try
            {
                _isAutonomousEnabled = true;
                await SaveConfigurationAsync();
                
                _logger.LogInformation("Autonomous protection mode enabled");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enable autonomous mode");
                return false;
            }
        }

        public async Task<bool> DisableAutonomousModeAsync()
        {
            try
            {
                _isAutonomousEnabled = false;
                await SaveConfigurationAsync();
                
                _logger.LogInformation("Autonomous protection mode disabled");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to disable autonomous mode");
                return false;
            }
        }

        public async Task<bool> IsAutonomousModeEnabledAsync()
        {
            return _isAutonomousEnabled;
        }

        public async Task<AutoProtectionAction> EvaluateDeviceAsync(DeviceInfo device)
        {
            try
            {
                var action = new AutoProtectionAction
                {
                    DeviceId = device.Id,
                    Timestamp = DateTime.Now,
                    ConfidenceScore = 0.0
                };

                // Check if device should be auto-blocked based on AI analysis
                if (await _aiService.ShouldBlockNewDeviceAsync(device))
                {
                    action.Action = ProtectionActionType.Block;
                    action.Reason = "AI detected high-risk device";
                    action.ConfidenceScore = 0.8;
                    return action;
                }

                // Check trust score
                var trustScore = await _aiService.CalculateTrustScoreAsync(device);
                if (trustScore < GetTrustThreshold())
                {
                    action.Action = ProtectionActionType.Restrict;
                    action.Reason = $"Low trust score: {trustScore:F2}";
                    action.ConfidenceScore = 1.0 - trustScore;
                    return action;
                }

                // Check security threats
                var threats = await _aiService.DetectSecurityThreatsAsync(device);
                var highSeverityThreats = threats.Where(t => t.Severity >= AlertSeverity.High).ToList();
                if (highSeverityThreats.Any())
                {
                    action.Action = ProtectionActionType.Block;
                    action.Reason = $"Security threats detected: {highSeverityThreats.Count}";
                    action.ConfidenceScore = 0.9;
                    return action;
                }

                // Check learning model for patterns
                if (await ShouldApplyLearningBasedActionAsync(device, action))
                {
                    return action;
                }

                // Apply autonomous level rules
                action = await ApplyAutonomousLevelRulesAsync(device);
                
                return action;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to evaluate device {device.Id}");
                return new AutoProtectionAction 
                { 
                    DeviceId = device.Id, 
                    Action = ProtectionActionType.Allow,
                    Reason = "Evaluation failed - defaulting to allow",
                    ConfidenceScore = 0.1
                };
            }
        }

        public async Task<bool> ApplyAutoProtectionAsync(DeviceInfo device, AutoProtectionAction action)
        {
            try
            {
                switch (action.Action)
                {
                    case ProtectionActionType.Block:
                        await _deviceController.BlockDeviceAsync(device.Id, BlockReason.AIBlocked);
                        _logger.LogInformation($"Auto-blocked device {device.Name}: {action.Reason}");
                        break;
                        
                    case ProtectionActionType.Restrict:
                        await ApplyRestrictionsAsync(device);
                        _logger.LogInformation($"Auto-restricted device {device.Name}: {action.Reason}");
                        break;
                        
                    case ProtectionActionType.Monitor:
                        await EnableMonitoringAsync(device);
                        _logger.LogInformation($"Monitoring device {device.Name}: {action.Reason}");
                        break;
                        
                    case ProtectionActionType.Allow:
                        // Device is allowed, no action needed
                        break;
                }

                // Log action for learning
                await LogActionForLearningAsync(device, action);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to apply auto protection for device {device.Id}");
                return false;
            }
        }

        public async Task<List<AutoProtectionRule>> GetActiveRulesAsync()
        {
            return _rules.Where(r => r.IsActive).ToList();
        }

        public async Task<bool> AddCustomRuleAsync(AutoProtectionRule rule)
        {
            try
            {
                rule.Id = Guid.NewGuid().ToString();
                rule.CreatedAt = DateTime.Now;
                _rules.Add(rule);
                
                await SaveConfigurationAsync();
                _logger.LogInformation($"Added custom protection rule: {rule.Name}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add custom rule");
                return false;
            }
        }

        public async Task<bool> UpdateLearningModelAsync(DeviceInfo device, string activity, bool wasAllowed)
        {
            try
            {
                var deviceKey = device.MacAddress;
                
                if (!_learningModel.ContainsKey(deviceKey))
                {
                    _learningModel[deviceKey] = new LearningData();
                }

                var learningData = _learningModel[deviceKey];
                learningData.Activities.Add(new ActivityLearning
                {
                    Activity = activity,
                    WasAllowed = wasAllowed,
                    Timestamp = DateTime.Now,
                    UserType = device.UserType,
                    DeviceType = device.DeviceType
                });

                // Keep only last 1000 activities per device
                if (learningData.Activities.Count > 1000)
                {
                    learningData.Activities = learningData.Activities.TakeLast(1000).ToList();
                }

                learningData.LastUpdated = DateTime.Now;
                await SaveLearningDataAsync();
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update learning model");
                return false;
            }
        }

        public async Task<AutonomousStatus> GetAutonomousStatusAsync()
        {
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                var protectedDevices = devices.Where(d => d.IsBlocked || d.TrustScore > 0.7).ToList();
                var monitoredDevices = devices.Where(d => d.TrustScore >= 0.4 && d.TrustScore <= 0.7).ToList();
                var blockedDevices = devices.Where(d => d.IsBlocked).ToList();

                return new AutonomousStatus
                {
                    IsEnabled = _isAutonomousEnabled,
                    Level = _autonomousLevel,
                    TotalDevices = devices.Count,
                    ProtectedDevices = protectedDevices.Count,
                    MonitoredDevices = monitoredDevices.Count,
                    BlockedDevices = blockedDevices.Count,
                    ActiveRules = _rules.Count(r => r.IsActive),
                    LearningEntries = _learningModel.Values.Sum(l => l.Activities.Count),
                    LastAction = DateTime.Now // Placeholder
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get autonomous status");
                return new AutonomousStatus { IsEnabled = false };
            }
        }

        public async Task<bool> SetAutonomousLevelAsync(AutonomousLevel level)
        {
            try
            {
                _autonomousLevel = level;
                await SaveConfigurationAsync();
                
                _logger.LogInformation($"Autonomous protection level set to: {level}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set autonomous level");
                return false;
            }
        }

        // Private helper methods
        private void InitializeDefaultRules()
        {
            _rules.AddRange(new[]
            {
                new AutoProtectionRule
                {
                    Id = "default-unknown-block",
                    Name = "Block Unknown Devices",
                    Description = "Automatically block devices that cannot be identified",
                    Condition = "DeviceType == Unknown",
                    Action = ProtectionActionType.Block,
                    IsActive = true,
                    Priority = 1
                },
                new AutoProtectionRule
                {
                    Id = "default-low-trust",
                    Name = "Restrict Low Trust Devices",
                    Description = "Apply restrictions to devices with low trust scores",
                    Condition = "TrustScore < 0.3",
                    Action = ProtectionActionType.Restrict,
                    IsActive = true,
                    Priority = 2
                },
                new AutoProtectionRule
                {
                    Id = "default-night-block",
                    Name = "Night Time Protection",
                    Description = "Block new devices during night hours",
                    Condition = "Hour >= 22 OR Hour <= 6",
                    Action = ProtectionActionType.Block,
                    IsActive = false,
                    Priority = 3
                }
            });
        }

        private async void RunAutonomousProtection(object? state)
        {
            if (!_isAutonomousEnabled) return;

            try
            {
                var devices = await _deviceController.DiscoverAllDevicesAsync();
                
                foreach (var device in devices)
                {
                    var action = await EvaluateDeviceAsync(device);
                    if (action.Action != ProtectionActionType.Allow)
                    {
                        await ApplyAutoProtectionAsync(device, action);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Autonomous protection cycle failed");
            }
        }

        private double GetTrustThreshold() => _autonomousLevel switch
        {
            AutonomousLevel.Conservative => 0.8,
            AutonomousLevel.Moderate => 0.5,
            AutonomousLevel.Aggressive => 0.3,
            _ => 0.5
        };

        private async Task<bool> ShouldApplyLearningBasedActionAsync(DeviceInfo device, AutoProtectionAction action)
        {
            if (!_learningModel.TryGetValue(device.MacAddress, out var learningData))
                return false;

            // Analyze patterns to suggest actions
            var recentActivities = learningData.Activities.TakeLast(100).ToList();
            var allowedRate = recentActivities.Count(a => a.WasAllowed) / (double)recentActivities.Count;

            if (allowedRate < 0.3)
            {
                action.Action = ProtectionActionType.Block;
                action.Reason = "Learning model suggests blocking";
                action.ConfidenceScore = 1.0 - allowedRate;
                return true;
            }

            return false;
        }

        private async Task<AutoProtectionAction> ApplyAutonomousLevelRulesAsync(DeviceInfo device)
        {
            var action = new AutoProtectionAction
            {
                DeviceId = device.Id,
                Timestamp = DateTime.Now,
                Action = ProtectionActionType.Allow,
                ConfidenceScore = 0.5
            };

            switch (_autonomousLevel)
            {
                case AutonomousLevel.Conservative:
                    // Only allow well-known, trusted devices
                    if (device.UserType == UserType.Unknown || device.TrustScore < 0.8)
                    {
                        action.Action = ProtectionActionType.Block;
                        action.Reason = "Conservative mode - unknown or low trust device";
                    }
                    break;

                case AutonomousLevel.Moderate:
                    // Allow with monitoring for moderate risk devices
                    if (device.TrustScore < 0.5)
                    {
                        action.Action = ProtectionActionType.Monitor;
                        action.Reason = "Moderate mode - monitoring low trust device";
                    }
                    break;

                case AutonomousLevel.Aggressive:
                    // Allow most devices but monitor closely
                    if (device.TrustScore < 0.2)
                    {
                        action.Action = ProtectionActionType.Restrict;
                        action.Reason = "Aggressive mode - restricting very low trust device";
                    }
                    break;
            }

            return action;
        }

        private async Task ApplyRestrictionsAsync(DeviceInfo device)
        {
            // Apply time restrictions
            await _deviceController.SetTimeRestrictionAsync(device.Id, TimeSpan.FromHours(1));
            
            // Block high-risk categories
            var riskCategories = await _aiService.PredictHighRiskCategoriesAsync(device);
            foreach (var category in riskCategories)
            {
                // Add category restrictions (would need web filtering integration)
            }
        }

        private async Task EnableMonitoringAsync(DeviceInfo device)
        {
            // Enhanced monitoring for the device
            await Task.CompletedTask; // Placeholder
        }

        private async Task LogActionForLearningAsync(DeviceInfo device, AutoProtectionAction action)
        {
            await UpdateLearningModelAsync(device, action.Reason, action.Action == ProtectionActionType.Allow);
        }

        private async Task LoadConfigurationAsync()
        {
            try
            {
                var configFile = Path.Combine(_configPath, "autonomous_config.json");
                if (File.Exists(configFile))
                {
                    var json = await File.ReadAllTextAsync(configFile);
                    var config = JsonSerializer.Deserialize<AutonomousConfig>(json);
                    if (config != null)
                    {
                        _isAutonomousEnabled = config.IsEnabled;
                        _autonomousLevel = config.Level;
                        _rules.AddRange(config.CustomRules);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load autonomous configuration");
            }
        }

        private async Task SaveConfigurationAsync()
        {
            try
            {
                var config = new AutonomousConfig
                {
                    IsEnabled = _isAutonomousEnabled,
                    Level = _autonomousLevel,
                    CustomRules = _rules.Where(r => !r.Id.StartsWith("default-")).ToList()
                };

                var configFile = Path.Combine(_configPath, "autonomous_config.json");
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(configFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save autonomous configuration");
            }
        }

        private async Task SaveLearningDataAsync()
        {
            try
            {
                var learningFile = Path.Combine(_configPath, "learning_data.json");
                var json = JsonSerializer.Serialize(_learningModel, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(learningFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save learning data");
            }
        }
    }

    // Supporting classes and enums
    public enum AutonomousLevel
    {
        Conservative, // Block unknown, allow only trusted
        Moderate,     // Monitor unknown, allow with restrictions
        Aggressive    // Allow most, monitor closely
    }

    public enum ProtectionActionType
    {
        Allow,
        Monitor,
        Restrict,
        Block
    }

    public class AutoProtectionAction
    {
        public string DeviceId { get; set; } = string.Empty;
        public ProtectionActionType Action { get; set; }
        public string Reason { get; set; } = string.Empty;
        public double ConfidenceScore { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AutoProtectionRule
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;
        public ProtectionActionType Action { get; set; }
        public bool IsActive { get; set; } = true;
        public int Priority { get; set; } = 5;
        public DateTime CreatedAt { get; set; }
    }

    public class LearningData
    {
        public List<ActivityLearning> Activities { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }

    public class ActivityLearning
    {
        public string Activity { get; set; } = string.Empty;
        public bool WasAllowed { get; set; }
        public DateTime Timestamp { get; set; }
        public UserType UserType { get; set; }
        public DeviceType DeviceType { get; set; }
    }

    public class AutonomousConfig
    {
        public bool IsEnabled { get; set; }
        public AutonomousLevel Level { get; set; }
        public List<AutoProtectionRule> CustomRules { get; set; } = new();
    }

    public class AutonomousStatus
    {
        public bool IsEnabled { get; set; }
        public AutonomousLevel Level { get; set; }
        public int TotalDevices { get; set; }
        public int ProtectedDevices { get; set; }
        public int MonitoredDevices { get; set; }
        public int BlockedDevices { get; set; }
        public int ActiveRules { get; set; }
        public int LearningEntries { get; set; }
        public DateTime LastAction { get; set; }
    }
}

