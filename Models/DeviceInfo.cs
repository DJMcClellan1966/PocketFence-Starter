using System.Text.Json.Serialization;

namespace PocketFence.Models
{
    public class DeviceInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public DateTime LastSeen { get; set; }
        public DateTime FirstSeen { get; set; } = DateTime.Now;
        public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
        public string DeviceModel { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        
        // Parental Control Properties
        public string AssignedUser { get; set; } = string.Empty;
        public UserType UserType { get; set; } = UserType.Unknown;
        public List<string> BlockedWebsites { get; set; } = new();
        public List<string> AllowedWebsites { get; set; } = new();
        public TimeSpan DailyTimeLimit { get; set; } = TimeSpan.FromHours(8);
        public TimeSpan UsedToday { get; set; } = TimeSpan.Zero;
        public bool IsBlocked { get; set; } = false;
        public BlockReason BlockReason { get; set; } = BlockReason.None;
        
        // AI Properties
        public DeviceBehaviorProfile BehaviorProfile { get; set; } = new();
        public double TrustScore { get; set; } = 0.5;
        public List<SecurityAlert> SecurityAlerts { get; set; } = new();
        
        // Always On and Time Management
        public bool AlwaysOnMode { get; set; } = false;
        public DateTime? TimeRestrictionStart { get; set; }
        public DateTime? TimeRestrictionEnd { get; set; }
        public TimeSpan UsedTimeToday { get; set; } = TimeSpan.Zero;
        public Dictionary<string, object> CustomSettings { get; set; } = new();
        
        // Computed Properties
        public string Status => IsBlocked ? "Blocked" : (IsConnected ? "Connected" : "Offline");
        public string StatusColor => IsBlocked ? "Red" : (IsConnected ? "Green" : "Gray");
        public bool IsTimeExceeded => UsedToday >= DailyTimeLimit;
        public TimeSpan RemainingTime => DailyTimeLimit - UsedToday;
    }

    public enum DeviceType
    {
        Unknown,
        Smartphone,
        Tablet,
        Laptop,
        Desktop,
        SmartTV,
        GameConsole,
        SmartSpeaker,
        IoTDevice,
        Router,
        Switch,
        AccessPoint,
        SmartWatch,
        SmartHome,
        Camera,
        Printer
    }

    public enum UserType
    {
        Unknown,
        Parent,
        Child,
        Teenager,
        Guest,
        Restricted
    }

    public enum BlockReason
    {
        None,
        TimeLimit,
        InappropriateContent,
        ParentBlocked,
        AIBlocked,
        ScheduleRestriction,
        SecurityThreat,
        Unknown
    }

    public class DeviceBehaviorProfile
    {
        public Dictionary<string, int> WebsiteCategories { get; set; } = new();
        public Dictionary<int, TimeSpan> HourlyUsage { get; set; } = new();
        public List<string> MostVisitedSites { get; set; } = new();
        public double AvgSessionDuration { get; set; }
        public DateTime LastBehaviorUpdate { get; set; } = DateTime.Now;
    }

    public class SecurityAlert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public bool IsResolved { get; set; } = false;
    }

    public enum AlertSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class AIInsight
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public InsightSeverity Severity { get; set; } = InsightSeverity.Info;
        public string Category { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public Dictionary<string, object> Data { get; set; } = new();
    }

    public enum InsightSeverity
    {
        Info,
        Warning,
        Alert,
        Critical
    }
}