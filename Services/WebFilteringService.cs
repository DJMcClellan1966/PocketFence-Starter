using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PocketFence.Models;

namespace PocketFence.Services
{
    public interface IWebFilteringService
    {
        Task<bool> IsWebsiteAllowedAsync(string url, DeviceInfo device);
        Task<WebsiteCategory> CategorizeWebsiteAsync(string url);
        Task<double> AnalyzeContentSafetyAsync(string content);
        Task<List<string>> DetectInappropriateContentAsync(string content);
        Task BlockWebsiteAsync(string url, string deviceId, string reason);
        Task UnblockWebsiteAsync(string url, string deviceId);
        Task<List<WebsiteAccess>> GetWebsiteHistoryAsync(string deviceId, DateTime? since = null);
        Task LogWebsiteAccessAsync(string deviceId, string url, bool wasBlocked, string reason = "");
        Task<bool> IsPhishingAttemptAsync(string url);
        Task<List<string>> GetSuggestedBlocksAsync(DeviceInfo device);
        Task<List<string>> GetActiveFiltersAsync();
    }

    public class WebFilteringService : IWebFilteringService
    {
        private readonly ILogger<WebFilteringService> _logger;
        private readonly IAIContentAnalyzer _aiAnalyzer;
        private readonly Dictionary<string, WebsiteCategory> _categoryCache = new();
        private readonly Dictionary<string, double> _safetyScoreCache = new();
        private readonly List<string> _phishingPatterns;
        private readonly Dictionary<WebsiteCategory, List<string>> _categoryKeywords;
        private readonly string _accessLogPath;

        public WebFilteringService(ILogger<WebFilteringService> logger, IAIContentAnalyzer aiAnalyzer)
        {
            _logger = logger;
            _aiAnalyzer = aiAnalyzer;
            _accessLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PocketFence", "WebAccess");
            Directory.CreateDirectory(_accessLogPath);
            
            InitializeFilteringRules();
            _ = LoadCacheAsync();
        }

        private void InitializeFilteringRules()
        {
            _phishingPatterns = new List<string>
            {
                @"[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}", // IP addresses
                @"[a-z0-9-]+\.(tk|ml|ga|cf)", // Suspicious TLDs
                @"(amazon|paypal|google|microsoft|apple).*[0-9]+", // Brand impersonation
                @"secure.*[0-9]{3,}", // Fake secure sites
                @"[a-z]{10,}\.(com|net|org)", // Random character domains
            };

            _categoryKeywords = new Dictionary<WebsiteCategory, List<string>>
            {
                [WebsiteCategory.Adult] = new() { "adult", "porn", "sex", "xxx", "nude", "erotic" },
                [WebsiteCategory.Gambling] = new() { "casino", "poker", "bet", "gambling", "lottery" },
                [WebsiteCategory.Violence] = new() { "weapon", "gun", "violence", "fight", "war" },
                [WebsiteCategory.Drugs] = new() { "drug", "marijuana", "cocaine", "cannabis", "weed" },
                [WebsiteCategory.Social] = new() { "facebook", "instagram", "twitter", "tiktok", "snapchat" },
                [WebsiteCategory.Gaming] = new() { "game", "gaming", "steam", "xbox", "playstation" },
                [WebsiteCategory.Education] = new() { "edu", "school", "university", "learn", "study" },
                [WebsiteCategory.News] = new() { "news", "cnn", "bbc", "reuters", "ap" },
                [WebsiteCategory.Shopping] = new() { "shop", "buy", "amazon", "ebay", "store" },
                [WebsiteCategory.Streaming] = new() { "youtube", "netflix", "video", "stream", "watch" }
            };
        }

        public async Task<bool> IsWebsiteAllowedAsync(string url, DeviceInfo device)
        {
            try
            {
                var normalizedUrl = NormalizeUrl(url);
                
                // Check explicit allow/block lists first
                if (device.BlockedWebsites.Any(blocked => normalizedUrl.Contains(blocked)))
                {
                    await LogWebsiteAccessAsync(device.Id, url, true, "Explicitly blocked");
                    return false;
                }

                if (device.AllowedWebsites.Any(allowed => normalizedUrl.Contains(allowed)))
                {
                    await LogWebsiteAccessAsync(device.Id, url, false, "Explicitly allowed");
                    return true;
                }

                // Check for phishing attempts
                if (await IsPhishingAttemptAsync(url))
                {
                    await LogWebsiteAccessAsync(device.Id, url, true, "Phishing detected");
                    return false;
                }

                // AI-powered content analysis
                var category = await CategorizeWebsiteAsync(url);
                var isBlocked = ShouldBlockCategory(category, device.UserType);

                if (isBlocked)
                {
                    await LogWebsiteAccessAsync(device.Id, url, true, $"Category blocked: {category}");
                    return false;
                }

                // Time-based restrictions
                if (device.IsTimeExceeded)
                {
                    await LogWebsiteAccessAsync(device.Id, url, true, "Time limit exceeded");
                    return false;
                }

                // AI safety analysis for unknown content
                if (category == WebsiteCategory.Unknown)
                {
                    var safetyScore = await AnalyzeContentSafetyAsync(url);
                    if (safetyScore < GetSafetyThreshold(device.UserType))
                    {
                        await LogWebsiteAccessAsync(device.Id, url, true, $"AI safety check failed: {safetyScore:F2}");
                        return false;
                    }
                }

                await LogWebsiteAccessAsync(device.Id, url, false, "Allowed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking website access for {url}");
                // Fail safe - block on error for child devices
                return device.UserType == UserType.Parent;
            }
        }

        private bool ShouldBlockCategory(WebsiteCategory category, UserType userType)
        {
            return userType switch
            {
                UserType.Child => category is WebsiteCategory.Adult or WebsiteCategory.Gambling 
                    or WebsiteCategory.Violence or WebsiteCategory.Drugs or WebsiteCategory.Social,
                UserType.Teenager => category is WebsiteCategory.Adult or WebsiteCategory.Gambling 
                    or WebsiteCategory.Drugs,
                UserType.Guest => category is WebsiteCategory.Adult or WebsiteCategory.Gambling 
                    or WebsiteCategory.Violence or WebsiteCategory.Drugs,
                _ => false
            };
        }

        private double GetSafetyThreshold(UserType userType)
        {
            return userType switch
            {
                UserType.Child => 0.8,
                UserType.Teenager => 0.6,
                UserType.Guest => 0.7,
                _ => 0.3
            };
        }

        public async Task<WebsiteCategory> CategorizeWebsiteAsync(string url)
        {
            var normalizedUrl = NormalizeUrl(url);
            
            if (_categoryCache.TryGetValue(normalizedUrl, out var cachedCategory))
            {
                return cachedCategory;
            }

            try
            {
                // Keyword-based classification
                foreach (var kvp in _categoryKeywords)
                {
                    if (kvp.Value.Any(keyword => normalizedUrl.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    {
                        _categoryCache[normalizedUrl] = kvp.Key;
                        return kvp.Key;
                    }
                }

                // AI-powered classification for unknown content
                var category = await _aiAnalyzer.ClassifyWebsiteAsync(url);
                _categoryCache[normalizedUrl] = category;
                
                return category;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to categorize website {url}");
                return WebsiteCategory.Unknown;
            }
        }

        public async Task<double> AnalyzeContentSafetyAsync(string content)
        {
            var contentHash = GetContentHash(content);
            
            if (_safetyScoreCache.TryGetValue(contentHash, out var cachedScore))
            {
                return cachedScore;
            }

            try
            {
                var score = await _aiAnalyzer.AnalyzeContentSafetyAsync(content);
                _safetyScoreCache[contentHash] = score;
                return score;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze content safety");
                return 0.5; // Default neutral score
            }
        }

        public async Task<List<string>> DetectInappropriateContentAsync(string content)
        {
            try
            {
                return await _aiAnalyzer.DetectInappropriateContentAsync(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to detect inappropriate content");
                return new List<string>();
            }
        }

        public async Task<bool> IsPhishingAttemptAsync(string url)
        {
            try
            {
                var normalizedUrl = NormalizeUrl(url);
                
                // Pattern-based detection
                foreach (var pattern in _phishingPatterns)
                {
                    if (Regex.IsMatch(normalizedUrl, pattern, RegexOptions.IgnoreCase))
                    {
                        _logger.LogWarning($"Phishing pattern detected in URL: {url}");
                        return true;
                    }
                }

                // AI-powered phishing detection
                return await _aiAnalyzer.IsPhishingAttemptAsync(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking for phishing attempt: {url}");
                return false;
            }
        }

        public async Task BlockWebsiteAsync(string url, string deviceId, string reason)
        {
            try
            {
                await LogWebsiteAccessAsync(deviceId, url, true, $"Manually blocked: {reason}");
                _logger.LogInformation($"Website {url} blocked for device {deviceId}: {reason}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to block website {url} for device {deviceId}");
            }
        }

        public async Task UnblockWebsiteAsync(string url, string deviceId)
        {
            try
            {
                await LogWebsiteAccessAsync(deviceId, url, false, "Manually unblocked");
                _logger.LogInformation($"Website {url} unblocked for device {deviceId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to unblock website {url} for device {deviceId}");
            }
        }

        public async Task<List<WebsiteAccess>> GetWebsiteHistoryAsync(string deviceId, DateTime? since = null)
        {
            try
            {
                var history = new List<WebsiteAccess>();
                var logFile = Path.Combine(_accessLogPath, $"{deviceId}.json");
                
                if (!File.Exists(logFile))
                    return history;

                var json = await File.ReadAllTextAsync(logFile);
                var allAccess = JsonSerializer.Deserialize<List<WebsiteAccess>>(json) ?? new();
                
                var filterDate = since ?? DateTime.Today;
                return allAccess.Where(a => a.Timestamp >= filterDate).OrderByDescending(a => a.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to get website history for device {deviceId}");
                return new List<WebsiteAccess>();
            }
        }

        public async Task LogWebsiteAccessAsync(string deviceId, string url, bool wasBlocked, string reason = "")
        {
            try
            {
                var access = new WebsiteAccess
                {
                    DeviceId = deviceId,
                    Url = url,
                    Timestamp = DateTime.Now,
                    WasBlocked = wasBlocked,
                    Reason = reason
                };

                var logFile = Path.Combine(_accessLogPath, $"{deviceId}.json");
                var history = new List<WebsiteAccess>();
                
                if (File.Exists(logFile))
                {
                    var json = await File.ReadAllTextAsync(logFile);
                    history = JsonSerializer.Deserialize<List<WebsiteAccess>>(json) ?? new();
                }
                
                history.Add(access);
                
                // Keep only last 30 days
                var cutoff = DateTime.Now.AddDays(-30);
                history = history.Where(h => h.Timestamp >= cutoff).ToList();
                
                var updatedJson = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(logFile, updatedJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to log website access for device {deviceId}");
            }
        }

        public async Task<List<string>> GetSuggestedBlocksAsync(DeviceInfo device)
        {
            try
            {
                var suggestions = new List<string>();
                var history = await GetWebsiteHistoryAsync(device.Id, DateTime.Today.AddDays(-7));
                
                // AI-powered suggestions based on usage patterns
                var frequentSites = history.GroupBy(h => ExtractDomain(h.Url))
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => g.Key)
                    .ToList();

                foreach (var site in frequentSites)
                {
                    var category = await CategorizeWebsiteAsync(site);
                    if (ShouldBlockCategory(category, device.UserType) && !device.BlockedWebsites.Contains(site))
                    {
                        suggestions.Add(site);
                    }
                }

                return suggestions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to get suggested blocks for device {device.Id}");
                return new List<string>();
            }
        }

        private string NormalizeUrl(string url)
        {
            try
            {
                var uri = new Uri(url.StartsWith("http") ? url : $"https://{url}");
                return uri.Host.ToLowerInvariant();
            }
            catch
            {
                return url.ToLowerInvariant();
            }
        }

        private string ExtractDomain(string url)
        {
            try
            {
                var uri = new Uri(url.StartsWith("http") ? url : $"https://{url}");
                return uri.Host;
            }
            catch
            {
                return url;
            }
        }

        private string GetContentHash(string content)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private async Task LoadCacheAsync()
        {
            try
            {
                var cacheFile = Path.Combine(_accessLogPath, "category_cache.json");
                if (File.Exists(cacheFile))
                {
                    var json = await File.ReadAllTextAsync(cacheFile);
                    var cache = JsonSerializer.Deserialize<Dictionary<string, WebsiteCategory>>(json);
                    if (cache != null)
                    {
                        foreach (var kvp in cache)
                        {
                            _categoryCache[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load website category cache");
            }
        }

        public async Task<List<string>> GetActiveFiltersAsync()
        {
            try
            {
                var filters = new List<string>();
                
                // Add all category keywords
                if (_categoryKeywords != null)
                {
                    filters.AddRange(_categoryKeywords.SelectMany(kvp => kvp.Value));
                }
                
                // Add phishing patterns  
                if (_phishingPatterns != null)
                {
                    filters.AddRange(_phishingPatterns);
                }
                
                return filters.Distinct().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get active filters");
                return new List<string>();
            }
        }
    }

    public enum WebsiteCategory
    {
        Unknown,
        Adult,
        Gambling,
        Violence,
        Drugs,
        Social,
        Gaming,
        Education,
        News,
        Shopping,
        Streaming,
        Work,
        Safe
    }

    public class WebsiteAccess
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool WasBlocked { get; set; }
        public string Reason { get; set; } = string.Empty;
        public WebsiteCategory Category { get; set; }
    }

    public interface IAIContentAnalyzer
    {
        Task<WebsiteCategory> ClassifyWebsiteAsync(string url);
        Task<double> AnalyzeContentSafetyAsync(string content);
        Task<List<string>> DetectInappropriateContentAsync(string content);
        Task<bool> IsPhishingAttemptAsync(string url);
    }

    public class AIContentAnalyzer : IAIContentAnalyzer
    {
        private readonly ILogger<AIContentAnalyzer> _logger;
        
        public AIContentAnalyzer(ILogger<AIContentAnalyzer> logger)
        {
            _logger = logger;
        }

        public async Task<WebsiteCategory> ClassifyWebsiteAsync(string url)
        {
            // Placeholder for AI classification logic
            // In production, this would use ML models or APIs
            await Task.Delay(100); // Simulate AI processing
            return WebsiteCategory.Unknown;
        }

        public async Task<double> AnalyzeContentSafetyAsync(string content)
        {
            // Placeholder for AI safety analysis
            await Task.Delay(100);
            return 0.7; // Safe by default
        }

        public async Task<List<string>> DetectInappropriateContentAsync(string content)
        {
            // Placeholder for content analysis
            await Task.Delay(100);
            return new List<string>();
        }

        public async Task<bool> IsPhishingAttemptAsync(string url)
        {
            // Placeholder for phishing detection
            await Task.Delay(50);
            return false;
        }
    }
}
