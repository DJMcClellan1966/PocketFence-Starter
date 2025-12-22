using Microsoft.Extensions.Logging;
using PocketFence.Models;
using System.Text.Json;

namespace PocketFence.Services
{
    public interface ITimeLimitsService
    {
        Task<bool> SetTimeLimitAsync(string deviceId, TimeSpan dailyLimit);
        Task<bool> SetAlwaysOnModeAsync(string deviceId, bool alwaysOn);
        Task<bool> SetTimeRestrictionAsync(string deviceId, TimeSpan startTime, TimeSpan endTime);
        Task<bool> GrantExtraTimeAsync(string deviceId, TimeSpan extraTime, string reason);
        Task<TimeUsageInfo> GetTimeUsageAsync(string deviceId);
        Task<List<TimeViolation>> GetTimeViolationsAsync(string deviceId, DateTime? since = null);
        Task<bool> ResetDailyTimersAsync();
        Task<bool> PauseTimeTrackingAsync(string deviceId, TimeSpan duration);
        Task<List<TimeLimitAlert>> GetTimeLimitAlertsAsync();
        Task<bool> SendTimeWarningAsync(string deviceId, TimeSpan remainingTime);
        event EventHandler<TimeLimitEventArgs> TimeLimitExceeded;
        event EventHandler<TimeLimitEventArgs> TimeWarningTriggered;
    }

    public class TimeLimitsService : ITimeLimitsService
    {
        private readonly ILogger<TimeLimitsService> _logger;
        private readonly IUniversalDeviceController _deviceController;
        private readonly IAISelfHealingService _selfHealingService;
        private readonly Timer _timeTrackingTimer;
        private readonly Dictionary<string, TimeTrackingData> _activeTracking = new();
        private readonly Dictionary<string, DateTime> _lastActivityCheck = new();
        private readonly string _configPath;
        private readonly List<TimeViolation> _violations = new();

        public event EventHandler<TimeLimitEventArgs>? TimeLimitExceeded;
        public event EventHandler<TimeLimitEventArgs>? TimeWarningTriggered;

        public TimeLimitsService(
            ILogger<TimeLimitsService> logger,
            IUniversalDeviceController deviceController,
            IAISelfHealingService selfHealingService)
        {
            _logger = logger;
            _deviceController = deviceController;
            _selfHealingService = selfHealingService;

            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PocketFence", "TimeLimits");
            Directory.CreateDirectory(_configPath);

            // Track time usage every minute
            _timeTrackingTimer = new Timer(TrackTimeUsage, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            
            // Load existing tracking data
            _ = LoadTrackingDataAsync();
        }

        public async Task<bool> SetTimeLimitAsync(string deviceId, TimeSpan dailyLimit)
        {
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                var device = devices.FirstOrDefault(d => d.Id == deviceId);
                if (device == null)
                {
                    _logger.LogWarning($"Device not found: {deviceId}");
                    return false;
                }

                device.DailyTimeLimit = dailyLimit;
                await _deviceController.SaveKnownDevicesAsync(devices);

                _logger.LogInformation($"Set daily time limit for {device.Name}: {dailyLimit}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to set time limit for device: {deviceId}");
                return false;
            }
        }

        public async Task<bool> SetAlwaysOnModeAsync(string deviceId, bool alwaysOn)
        {
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                var device = devices.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) return false;

                device.AlwaysOnMode = alwaysOn;
                
                if (alwaysOn)
                {
                    device.DailyTimeLimit = TimeSpan.FromDays(1); // Effectively unlimited
                    _logger.LogInformation($"Enabled always-on mode for {device.Name}");
                }
                else
                {
                    device.DailyTimeLimit = TimeSpan.FromHours(8); // Default limit
                    _logger.LogInformation($"Disabled always-on mode for {device.Name}");
                }

                await _deviceController.SaveKnownDevicesAsync(devices);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to set always-on mode for device: {deviceId}");
                return false;
            }
        }

        public async Task<bool> SetTimeRestrictionAsync(string deviceId, TimeSpan startTime, TimeSpan endTime)
        {
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                var device = devices.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) return false;

                device.TimeRestrictionStart = DateTime.Today.Add(startTime);
                device.TimeRestrictionEnd = DateTime.Today.Add(endTime);

                await _deviceController.SaveKnownDevicesAsync(devices);
                
                _logger.LogInformation($"Set time restriction for {device.Name}: {startTime} - {endTime}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to set time restriction for device: {deviceId}");
                return false;
            }
        }

        public async Task<bool> GrantExtraTimeAsync(string deviceId, TimeSpan extraTime, string reason)
        {
            try
            {
                if (!_activeTracking.TryGetValue(deviceId, out var tracking))
                {
                    tracking = new TimeTrackingData { DeviceId = deviceId };
                    _activeTracking[deviceId] = tracking;
                }

                tracking.ExtraTimeGranted += extraTime;
                tracking.ExtraTimeReason = reason;
                tracking.ExtraTimeGrantedAt = DateTime.Now;

                var devices = await _deviceController.LoadKnownDevicesAsync();
                var device = devices.FirstOrDefault(d => d.Id == deviceId);
                if (device != null)
                {
                    // Unblock device if it was blocked for time limit
                    if (device.IsBlocked && device.BlockReason == BlockReason.TimeLimit)
                    {
                        device.IsBlocked = false;
                        device.BlockReason = BlockReason.None;
                        await _deviceController.SaveKnownDevicesAsync(devices);
                    }
                }

                _logger.LogInformation($"Granted {extraTime} extra time to device {deviceId}. Reason: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to grant extra time to device: {deviceId}");
                return false;
            }
        }

        public async Task<TimeUsageInfo> GetTimeUsageAsync(string deviceId)
        {
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                var device = devices.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) 
                {
                    return new TimeUsageInfo { DeviceId = deviceId, DeviceName = "Unknown" };
                }

                var tracking = _activeTracking.GetValueOrDefault(deviceId, new TimeTrackingData { DeviceId = deviceId });

                var totalAllowed = device.DailyTimeLimit + tracking.ExtraTimeGranted;
                var remainingTime = totalAllowed - device.UsedTimeToday;

                return new TimeUsageInfo
                {
                    DeviceId = deviceId,
                    DeviceName = device.Name,
                    DailyLimit = device.DailyTimeLimit,
                    UsedToday = device.UsedTimeToday,
                    RemainingTime = remainingTime,
                    ExtraTimeGranted = tracking.ExtraTimeGranted,
                    IsAlwaysOn = device.AlwaysOnMode,
                    IsInRestrictedTime = IsInRestrictedTimeWindow(device),
                    NextRestrictionStart = GetNextRestrictionStart(device),
                    NextRestrictionEnd = GetNextRestrictionEnd(device)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to get time usage for device: {deviceId}");
                return new TimeUsageInfo { DeviceId = deviceId, DeviceName = "Error" };
            }
        }

        public async Task<List<TimeViolation>> GetTimeViolationsAsync(string deviceId, DateTime? since = null)
        {
            var sinceDate = since ?? DateTime.Today.AddDays(-7);
            return _violations
                .Where(v => v.DeviceId == deviceId && v.Timestamp >= sinceDate)
                .OrderByDescending(v => v.Timestamp)
                .ToList();
        }

        public async Task<bool> ResetDailyTimersAsync()
        {
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                
                foreach (var device in devices)
                {
                    device.UsedTimeToday = TimeSpan.Zero;
                    
                    // Reset extra time for non-always-on devices
                    if (!device.AlwaysOnMode && _activeTracking.TryGetValue(device.Id, out var tracking))
                    {
                        tracking.ExtraTimeGranted = TimeSpan.Zero;
                        tracking.ExtraTimeReason = string.Empty;
                    }
                    
                    // Unblock devices that were blocked for time limits
                    if (device.IsBlocked && device.BlockReason == BlockReason.TimeLimit)
                    {
                        device.IsBlocked = false;
                        device.BlockReason = BlockReason.None;
                    }
                }

                await _deviceController.SaveKnownDevicesAsync(devices);
                _logger.LogInformation("Daily timers reset for all devices");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset daily timers");
                return false;
            }
        }

        public async Task<bool> PauseTimeTrackingAsync(string deviceId, TimeSpan duration)
        {
            try
            {
                if (!_activeTracking.TryGetValue(deviceId, out var tracking))
                {
                    tracking = new TimeTrackingData { DeviceId = deviceId };
                    _activeTracking[deviceId] = tracking;
                }

                tracking.PausedUntil = DateTime.Now.Add(duration);
                _logger.LogInformation($"Paused time tracking for device {deviceId} for {duration}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to pause time tracking for device: {deviceId}");
                return false;
            }
        }

        public async Task<List<TimeLimitAlert>> GetTimeLimitAlertsAsync()
        {
            var alerts = new List<TimeLimitAlert>();
            
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                
                foreach (var device in devices.Where(d => d.IsConnected && !d.AlwaysOnMode))
                {
                    var usage = await GetTimeUsageAsync(device.Id);
                    var percentUsed = usage.DailyLimit.TotalMinutes > 0 ? 
                        (usage.UsedToday.TotalMinutes / usage.DailyLimit.TotalMinutes) * 100 : 0;

                    if (percentUsed >= 90) // 90% threshold
                    {
                        alerts.Add(new TimeLimitAlert
                        {
                            DeviceId = device.Id,
                            DeviceName = device.Name,
                            AlertType = TimeLimitAlertType.NearLimit,
                            Message = $"{device.Name} has used {percentUsed:F0}% of daily time limit",
                            RemainingTime = usage.RemainingTime,
                            Severity = percentUsed >= 100 ? AlertSeverity.Critical : AlertSeverity.High,
                            Timestamp = DateTime.Now
                        });
                    }

                    if (IsInRestrictedTimeWindow(device))
                    {
                        alerts.Add(new TimeLimitAlert
                        {
                            DeviceId = device.Id,
                            DeviceName = device.Name,
                            AlertType = TimeLimitAlertType.RestrictedTime,
                            Message = $"{device.Name} is active during restricted hours",
                            Severity = AlertSeverity.Medium,
                            Timestamp = DateTime.Now
                        });
                    }
                }

                return alerts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get time limit alerts");
                return alerts;
            }
        }

        public async Task<bool> SendTimeWarningAsync(string deviceId, TimeSpan remainingTime)
        {
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                var device = devices.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) return false;

                TimeWarningTriggered?.Invoke(this, new TimeLimitEventArgs
                {
                    DeviceId = deviceId,
                    DeviceName = device.Name,
                    RemainingTime = remainingTime,
                    EventType = TimeLimitEventType.Warning
                });

                _logger.LogInformation($"Time warning sent to {device.Name}: {remainingTime} remaining");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send time warning to device: {deviceId}");
                return false;
            }
        }

        // Private helper methods
        private async void TrackTimeUsage(object? state)
        {
            try
            {
                var devices = await _deviceController.LoadKnownDevicesAsync();
                var activeDevices = devices.Where(d => d.IsConnected && !d.IsBlocked).ToList();

                foreach (var device in activeDevices)
                {
                    await TrackDeviceTimeAsync(device);
                }

                await SaveTrackingDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Time tracking cycle failed");
            }
        }

        private async Task TrackDeviceTimeAsync(DeviceInfo device)
        {
            try
            {
                // Skip always-on devices
                if (device.AlwaysOnMode) return;

                // Check if time tracking is paused
                if (_activeTracking.TryGetValue(device.Id, out var tracking) && 
                    tracking.PausedUntil.HasValue && tracking.PausedUntil > DateTime.Now)
                {
                    return;
                }

                // Check if in restricted time window
                if (IsInRestrictedTimeWindow(device))
                {
                    await HandleRestrictedTimeViolation(device);
                    return;
                }

                // Increment usage time (1 minute per tracking cycle)
                device.UsedTimeToday = device.UsedTimeToday.Add(TimeSpan.FromMinutes(1));

                var totalAllowed = device.DailyTimeLimit;
                if (tracking != null)
                {
                    totalAllowed = totalAllowed.Add(tracking.ExtraTimeGranted);
                }

                // Check for time limit warnings
                var remainingTime = totalAllowed - device.UsedTimeToday;
                if (remainingTime.TotalMinutes <= 15 && remainingTime.TotalMinutes > 0)
                {
                    await SendTimeWarningAsync(device.Id, remainingTime);
                }

                // Check if time limit exceeded
                if (device.UsedTimeToday >= totalAllowed)
                {
                    await HandleTimeLimitExceeded(device);
                }

                _lastActivityCheck[device.Id] = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to track time for device: {device.Id}");
            }
        }

        private async Task HandleTimeLimitExceeded(DeviceInfo device)
        {
            try
            {
                device.IsBlocked = true;
                device.BlockReason = BlockReason.TimeLimit;

                var devices = await _deviceController.LoadKnownDevicesAsync();
                var deviceIndex = devices.FindIndex(d => d.Id == device.Id);
                if (deviceIndex >= 0)
                {
                    devices[deviceIndex] = device;
                    await _deviceController.SaveKnownDevicesAsync(devices);
                }

                // Log violation
                var violation = new TimeViolation
                {
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    ViolationType = TimeViolationType.DailyLimitExceeded,
                    Timestamp = DateTime.Now,
                    UsedTime = device.UsedTimeToday,
                    AllowedTime = device.DailyTimeLimit
                };
                _violations.Add(violation);

                TimeLimitExceeded?.Invoke(this, new TimeLimitEventArgs
                {
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    UsedTime = device.UsedTimeToday,
                    EventType = TimeLimitEventType.LimitExceeded
                });

                _logger.LogWarning($"Time limit exceeded for {device.Name}. Device blocked.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to handle time limit exceeded for device: {device.Id}");
            }
        }

        private async Task HandleRestrictedTimeViolation(DeviceInfo device)
        {
            try
            {
                var violation = new TimeViolation
                {
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    ViolationType = TimeViolationType.RestrictedTimeAccess,
                    Timestamp = DateTime.Now,
                    Details = "Device active during restricted hours"
                };
                _violations.Add(violation);

                _logger.LogWarning($"Restricted time violation for {device.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to handle restricted time violation for device: {device.Id}");
            }
        }

        private bool IsInRestrictedTimeWindow(DeviceInfo device)
        {
            if (!device.TimeRestrictionStart.HasValue || !device.TimeRestrictionEnd.HasValue)
                return false;

            var now = DateTime.Now.TimeOfDay;
            var start = device.TimeRestrictionStart.Value.TimeOfDay;
            var end = device.TimeRestrictionEnd.Value.TimeOfDay;

            if (start <= end)
            {
                return now >= start && now <= end;
            }
            else // Crosses midnight
            {
                return now >= start || now <= end;
            }
        }

        private DateTime? GetNextRestrictionStart(DeviceInfo device)
        {
            if (!device.TimeRestrictionStart.HasValue) return null;

            var today = DateTime.Today;
            var restrictionTime = device.TimeRestrictionStart.Value.TimeOfDay;
            var nextStart = today.Add(restrictionTime);

            if (nextStart <= DateTime.Now)
            {
                nextStart = nextStart.AddDays(1);
            }

            return nextStart;
        }

        private DateTime? GetNextRestrictionEnd(DeviceInfo device)
        {
            if (!device.TimeRestrictionEnd.HasValue) return null;

            var today = DateTime.Today;
            var restrictionTime = device.TimeRestrictionEnd.Value.TimeOfDay;
            var nextEnd = today.Add(restrictionTime);

            if (nextEnd <= DateTime.Now)
            {
                nextEnd = nextEnd.AddDays(1);
            }

            return nextEnd;
        }

        private async Task LoadTrackingDataAsync()
        {
            try
            {
                var trackingFile = Path.Combine(_configPath, "tracking_data.json");
                if (File.Exists(trackingFile))
                {
                    var json = await File.ReadAllTextAsync(trackingFile);
                    var data = JsonSerializer.Deserialize<Dictionary<string, TimeTrackingData>>(json);
                    if (data != null)
                    {
                        foreach (var kvp in data)
                        {
                            _activeTracking[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load tracking data");
            }
        }

        private async Task SaveTrackingDataAsync()
        {
            try
            {
                var trackingFile = Path.Combine(_configPath, "tracking_data.json");
                var json = JsonSerializer.Serialize(_activeTracking, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(trackingFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save tracking data");
            }
        }
    }

    // Supporting classes
    public class TimeTrackingData
    {
        public string DeviceId { get; set; } = string.Empty;
        public TimeSpan ExtraTimeGranted { get; set; } = TimeSpan.Zero;
        public string ExtraTimeReason { get; set; } = string.Empty;
        public DateTime? ExtraTimeGrantedAt { get; set; }
        public DateTime? PausedUntil { get; set; }
    }

    public class TimeUsageInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public TimeSpan DailyLimit { get; set; }
        public TimeSpan UsedToday { get; set; }
        public TimeSpan RemainingTime { get; set; }
        public TimeSpan ExtraTimeGranted { get; set; }
        public bool IsAlwaysOn { get; set; }
        public bool IsInRestrictedTime { get; set; }
        public DateTime? NextRestrictionStart { get; set; }
        public DateTime? NextRestrictionEnd { get; set; }
    }

    public class TimeViolation
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public TimeViolationType ViolationType { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan UsedTime { get; set; }
        public TimeSpan AllowedTime { get; set; }
        public string Details { get; set; } = string.Empty;
    }

    public class TimeLimitAlert
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public TimeLimitAlertType AlertType { get; set; }
        public string Message { get; set; } = string.Empty;
        public TimeSpan RemainingTime { get; set; }
        public AlertSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class TimeLimitEventArgs : EventArgs
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public TimeSpan UsedTime { get; set; }
        public TimeSpan RemainingTime { get; set; }
        public TimeLimitEventType EventType { get; set; }
    }

    public enum TimeViolationType
    {
        DailyLimitExceeded,
        RestrictedTimeAccess,
        UnauthorizedExtension
    }

    public enum TimeLimitAlertType
    {
        NearLimit,
        LimitExceeded,
        RestrictedTime,
        ExtraTimeGranted
    }

    public enum TimeLimitEventType
    {
        Warning,
        LimitExceeded,
        RestrictedTime
    }
}