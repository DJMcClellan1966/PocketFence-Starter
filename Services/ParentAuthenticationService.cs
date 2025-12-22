using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PocketFence.Services
{
    public interface IParentAuthenticationService
    {
        Task<bool> SetupParentAccountAsync(string email, string password, string securityQuestion, string securityAnswer);
        Task<AuthenticationResult> AuthenticateParentAsync(string email, string password);
        Task<bool> EnableBiometricAuthAsync();
        Task<bool> AuthenticateWithBiometricsAsync();
        Task<bool> VerifySecurityQuestionAsync(string answer);
        Task<bool> ResetPasswordAsync(string email, string securityAnswer, string newPassword);
        Task<bool> IsParentAuthenticatedAsync();
        Task<bool> RequireReauthenticationAsync();
        Task LogAuthenticationAttemptAsync(string email, bool successful, string method = "password");
        Task<List<AuthenticationAttempt>> GetAuthenticationHistoryAsync();
        Task<bool> EnableTwoFactorAuthAsync(string phoneNumber);
        Task<bool> VerifyTwoFactorCodeAsync(string code);
        Task<string> GenerateEmergencyBypassCodeAsync();
        Task<bool> UseEmergencyBypassCodeAsync(string code);
        Task LockoutAfterFailedAttemptsAsync(string email);
        Task<bool> IsAccountLockedAsync(string email);
    }

    public class ParentAuthenticationService : IParentAuthenticationService
    {
        private readonly ILogger<ParentAuthenticationService> _logger;
        private readonly IBiometricService _biometricService;
        private readonly ITwoFactorService _twoFactorService;
        private readonly string _authDataPath;
        private readonly Dictionary<string, DateTime> _lockouts = new();
        private readonly Dictionary<string, int> _failedAttempts = new();
        private bool _isAuthenticated = false;
        private DateTime _lastAuthTime = DateTime.MinValue;
        private readonly TimeSpan _authTimeout = TimeSpan.FromMinutes(30);

        public ParentAuthenticationService(
            ILogger<ParentAuthenticationService> logger,
            IBiometricService biometricService,
            ITwoFactorService twoFactorService)
        {
            _logger = logger;
            _biometricService = biometricService;
            _twoFactorService = twoFactorService;
            _authDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PocketFence", "Authentication");
            Directory.CreateDirectory(_authDataPath);
        }

        public async Task<bool> SetupParentAccountAsync(string email, string password, string securityQuestion, string securityAnswer)
        {
            try
            {
                var account = new ParentAccount
                {
                    Id = Guid.NewGuid().ToString(),
                    Email = email.ToLowerInvariant(),
                    PasswordHash = HashPassword(password),
                    Salt = GenerateSalt(),
                    SecurityQuestion = securityQuestion,
                    SecurityAnswerHash = HashPassword(securityAnswer.ToLowerInvariant()),
                    CreatedDate = DateTime.Now,
                    IsActive = true,
                    RequiresBiometric = false,
                    RequiresTwoFactor = false
                };

                var filePath = Path.Combine(_authDataPath, "parent_account.json");
                var json = JsonSerializer.Serialize(account, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);

                await LogAuthenticationAttemptAsync(email, true, "account_setup");
                _logger.LogInformation($"Parent account created for {email}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to setup parent account for {email}");
                return false;
            }
        }

        public async Task<AuthenticationResult> AuthenticateParentAsync(string email, string password)
        {
            try
            {
                var normalizedEmail = email.ToLowerInvariant();
                
                // Check if account is locked
                if (await IsAccountLockedAsync(normalizedEmail))
                {
                    await LogAuthenticationAttemptAsync(normalizedEmail, false, "account_locked");
                    return new AuthenticationResult { Success = false, Message = "Account is temporarily locked due to failed attempts" };
                }

                var account = await LoadParentAccountAsync();
                if (account == null || account.Email != normalizedEmail)
                {
                    await LogAuthenticationAttemptAsync(normalizedEmail, false, "invalid_email");
                    await IncrementFailedAttemptsAsync(normalizedEmail);
                    return new AuthenticationResult { Success = false, Message = "Invalid email or password" };
                }

                if (!VerifyPassword(password, account.PasswordHash, account.Salt))
                {
                    await LogAuthenticationAttemptAsync(normalizedEmail, false, "invalid_password");
                    await IncrementFailedAttemptsAsync(normalizedEmail);
                    return new AuthenticationResult { Success = false, Message = "Invalid email or password" };
                }

                // Reset failed attempts on successful authentication
                _failedAttempts.Remove(normalizedEmail);

                // Check if two-factor is required
                if (account.RequiresTwoFactor)
                {
                    var twoFactorCode = await _twoFactorService.SendCodeAsync(account.PhoneNumber);
                    return new AuthenticationResult 
                    { 
                        Success = false, 
                        RequiresTwoFactor = true, 
                        Message = "Two-factor authentication code sent" 
                    };
                }

                // Successful authentication
                _isAuthenticated = true;
                _lastAuthTime = DateTime.Now;
                account.LastLoginDate = DateTime.Now;
                await SaveParentAccountAsync(account);

                await LogAuthenticationAttemptAsync(normalizedEmail, true, "password");
                _logger.LogInformation($"Parent authenticated successfully: {normalizedEmail}");

                return new AuthenticationResult 
                { 
                    Success = true, 
                    Message = "Authentication successful",
                    Account = account
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Authentication failed for {email}");
                return new AuthenticationResult { Success = false, Message = "Authentication failed" };
            }
        }

        public async Task<bool> EnableBiometricAuthAsync()
        {
            try
            {
                if (!_isAuthenticated)
                {
                    _logger.LogWarning("Biometric setup attempted without authentication");
                    return false;
                }

                var account = await LoadParentAccountAsync();
                if (account == null) return false;

                var biometricEnabled = await _biometricService.SetupBiometricAsync();
                if (biometricEnabled)
                {
                    account.RequiresBiometric = true;
                    account.BiometricSetupDate = DateTime.Now;
                    await SaveParentAccountAsync(account);
                    
                    _logger.LogInformation("Biometric authentication enabled");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enable biometric authentication");
                return false;
            }
        }

        public async Task<bool> AuthenticateWithBiometricsAsync()
        {
            try
            {
                var account = await LoadParentAccountAsync();
                if (account == null || !account.RequiresBiometric) return false;

                var biometricResult = await _biometricService.AuthenticateAsync();
                if (biometricResult)
                {
                    _isAuthenticated = true;
                    _lastAuthTime = DateTime.Now;
                    account.LastLoginDate = DateTime.Now;
                    await SaveParentAccountAsync(account);

                    await LogAuthenticationAttemptAsync(account.Email, true, "biometric");
                    _logger.LogInformation("Biometric authentication successful");
                    return true;
                }

                await LogAuthenticationAttemptAsync(account.Email, false, "biometric");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Biometric authentication failed");
                return false;
            }
        }

        public async Task<bool> VerifySecurityQuestionAsync(string answer)
        {
            try
            {
                var account = await LoadParentAccountAsync();
                if (account == null) return false;

                var hashedAnswer = HashPassword(answer.ToLowerInvariant());
                var isValid = hashedAnswer == account.SecurityAnswerHash;

                await LogAuthenticationAttemptAsync(account.Email, isValid, "security_question");
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Security question verification failed");
                return false;
            }
        }

        public async Task<bool> ResetPasswordAsync(string email, string securityAnswer, string newPassword)
        {
            try
            {
                var account = await LoadParentAccountAsync();
                if (account == null || account.Email != email.ToLowerInvariant()) return false;

                if (!await VerifySecurityQuestionAsync(securityAnswer)) return false;

                account.PasswordHash = HashPassword(newPassword);
                account.Salt = GenerateSalt();
                account.PasswordResetDate = DateTime.Now;
                await SaveParentAccountAsync(account);

                await LogAuthenticationAttemptAsync(email, true, "password_reset");
                _logger.LogInformation($"Password reset successful for {email}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Password reset failed for {email}");
                return false;
            }
        }

        public async Task<bool> IsParentAuthenticatedAsync()
        {
            if (!_isAuthenticated) return false;

            // Check if authentication has expired
            if (DateTime.Now - _lastAuthTime > _authTimeout)
            {
                _isAuthenticated = false;
                _logger.LogInformation("Parent authentication expired");
                return false;
            }

            return true;
        }

        public async Task<bool> RequireReauthenticationAsync()
        {
            try
            {
                _isAuthenticated = false;
                _lastAuthTime = DateTime.MinValue;
                _logger.LogInformation("Parent reauthentication required");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to require reauthentication");
                return false;
            }
        }

        public async Task LogAuthenticationAttemptAsync(string email, bool successful, string method = "password")
        {
            try
            {
                var attempt = new AuthenticationAttempt
                {
                    Email = email,
                    Timestamp = DateTime.Now,
                    Successful = successful,
                    Method = method,
                    IpAddress = GetClientIpAddress(),
                    UserAgent = GetUserAgent()
                };

                var logFile = Path.Combine(_authDataPath, "auth_log.json");
                var history = new List<AuthenticationAttempt>();
                
                if (File.Exists(logFile))
                {
                    var json = await File.ReadAllTextAsync(logFile);
                    history = JsonSerializer.Deserialize<List<AuthenticationAttempt>>(json) ?? new();
                }
                
                history.Add(attempt);
                
                // Keep only last 100 attempts
                if (history.Count > 100)
                {
                    history = history.TakeLast(100).ToList();
                }
                
                var updatedJson = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(logFile, updatedJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log authentication attempt");
            }
        }

        public async Task<List<AuthenticationAttempt>> GetAuthenticationHistoryAsync()
        {
            try
            {
                var logFile = Path.Combine(_authDataPath, "auth_log.json");
                if (!File.Exists(logFile)) return new List<AuthenticationAttempt>();

                var json = await File.ReadAllTextAsync(logFile);
                return JsonSerializer.Deserialize<List<AuthenticationAttempt>>(json) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get authentication history");
                return new List<AuthenticationAttempt>();
            }
        }

        public async Task<bool> EnableTwoFactorAuthAsync(string phoneNumber)
        {
            try
            {
                if (!_isAuthenticated) return false;

                var account = await LoadParentAccountAsync();
                if (account == null) return false;

                var setupResult = await _twoFactorService.SetupTwoFactorAsync(phoneNumber);
                if (setupResult)
                {
                    account.RequiresTwoFactor = true;
                    account.PhoneNumber = phoneNumber;
                    account.TwoFactorSetupDate = DateTime.Now;
                    await SaveParentAccountAsync(account);
                    
                    _logger.LogInformation("Two-factor authentication enabled");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enable two-factor authentication");
                return false;
            }
        }

        public async Task<bool> VerifyTwoFactorCodeAsync(string code)
        {
            try
            {
                var account = await LoadParentAccountAsync();
                if (account == null || !account.RequiresTwoFactor) return false;

                var isValid = await _twoFactorService.VerifyCodeAsync(code);
                if (isValid)
                {
                    _isAuthenticated = true;
                    _lastAuthTime = DateTime.Now;
                    account.LastLoginDate = DateTime.Now;
                    await SaveParentAccountAsync(account);

                    await LogAuthenticationAttemptAsync(account.Email, true, "two_factor");
                    return true;
                }

                await LogAuthenticationAttemptAsync(account.Email, false, "two_factor");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Two-factor verification failed");
                return false;
            }
        }

        public async Task<string> GenerateEmergencyBypassCodeAsync()
        {
            try
            {
                if (!_isAuthenticated) return string.Empty;

                var account = await LoadParentAccountAsync();
                if (account == null) return string.Empty;

                var bypassCode = GenerateRandomCode(12);
                account.EmergencyBypassCode = HashPassword(bypassCode);
                account.BypassCodeGeneratedDate = DateTime.Now;
                await SaveParentAccountAsync(account);

                _logger.LogInformation("Emergency bypass code generated");
                return bypassCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate emergency bypass code");
                return string.Empty;
            }
        }

        public async Task<bool> UseEmergencyBypassCodeAsync(string code)
        {
            try
            {
                var account = await LoadParentAccountAsync();
                if (account == null || string.IsNullOrEmpty(account.EmergencyBypassCode)) return false;

                // Bypass codes expire after 24 hours
                if (DateTime.Now - account.BypassCodeGeneratedDate > TimeSpan.FromHours(24))
                {
                    account.EmergencyBypassCode = string.Empty;
                    await SaveParentAccountAsync(account);
                    return false;
                }

                var hashedCode = HashPassword(code);
                if (hashedCode == account.EmergencyBypassCode)
                {
                    _isAuthenticated = true;
                    _lastAuthTime = DateTime.Now;
                    account.EmergencyBypassCode = string.Empty; // Single use
                    account.LastLoginDate = DateTime.Now;
                    await SaveParentAccountAsync(account);

                    await LogAuthenticationAttemptAsync(account.Email, true, "emergency_bypass");
                    _logger.LogInformation("Emergency bypass code used");
                    return true;
                }

                await LogAuthenticationAttemptAsync(account.Email, false, "emergency_bypass");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Emergency bypass code verification failed");
                return false;
            }
        }

        public async Task LockoutAfterFailedAttemptsAsync(string email)
        {
            const int maxAttempts = 5;
            const int lockoutMinutes = 30;

            try
            {
                var normalizedEmail = email.ToLowerInvariant();
                
                if (!_failedAttempts.ContainsKey(normalizedEmail))
                    _failedAttempts[normalizedEmail] = 0;

                _failedAttempts[normalizedEmail]++;

                if (_failedAttempts[normalizedEmail] >= maxAttempts)
                {
                    _lockouts[normalizedEmail] = DateTime.Now.AddMinutes(lockoutMinutes);
                    _logger.LogWarning($"Account locked for {normalizedEmail} after {maxAttempts} failed attempts");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to handle lockout for {email}");
            }
        }

        public async Task<bool> IsAccountLockedAsync(string email)
        {
            try
            {
                var normalizedEmail = email.ToLowerInvariant();
                
                if (_lockouts.TryGetValue(normalizedEmail, out var lockoutTime))
                {
                    if (DateTime.Now < lockoutTime)
                    {
                        return true;
                    }
                    else
                    {
                        // Lockout expired, remove it
                        _lockouts.Remove(normalizedEmail);
                        _failedAttempts.Remove(normalizedEmail);
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to check lockout status for {email}");
                return false;
            }
        }

        private async Task IncrementFailedAttemptsAsync(string email)
        {
            await LockoutAfterFailedAttemptsAsync(email);
        }

        private async Task<ParentAccount?> LoadParentAccountAsync()
        {
            try
            {
                var filePath = Path.Combine(_authDataPath, "parent_account.json");
                if (!File.Exists(filePath)) return null;

                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<ParentAccount>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load parent account");
                return null;
            }
        }

        private async Task SaveParentAccountAsync(ParentAccount account)
        {
            try
            {
                var filePath = Path.Combine(_authDataPath, "parent_account.json");
                var json = JsonSerializer.Serialize(account, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save parent account");
            }
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private bool VerifyPassword(string password, string hash, string salt)
        {
            var passwordHash = HashPassword(password + salt);
            return passwordHash == hash;
        }

        private string GenerateSalt()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[32];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private string GenerateRandomCode(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            using var rng = RandomNumberGenerator.Create();
            var result = new char[length];
            for (int i = 0; i < length; i++)
            {
                var bytes = new byte[1];
                rng.GetBytes(bytes);
                result[i] = chars[bytes[0] % chars.Length];
            }
            return new string(result);
        }

        private string GetClientIpAddress()
        {
            // Placeholder for IP address detection
            return "127.0.0.1";
        }

        private string GetUserAgent()
        {
            // Placeholder for user agent detection
            return "PocketFence/1.0";
        }
    }

    public class ParentAccount
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public string SecurityQuestion { get; set; } = string.Empty;
        public string SecurityAnswerHash { get; set; } = string.Empty;
        public bool RequiresBiometric { get; set; }
        public bool RequiresTwoFactor { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string EmergencyBypassCode { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime LastLoginDate { get; set; }
        public DateTime PasswordResetDate { get; set; }
        public DateTime BiometricSetupDate { get; set; }
        public DateTime TwoFactorSetupDate { get; set; }
        public DateTime BypassCodeGeneratedDate { get; set; }
        public bool IsActive { get; set; }
    }

    public class AuthenticationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool RequiresTwoFactor { get; set; }
        public ParentAccount? Account { get; set; }
    }

    public class AuthenticationAttempt
    {
        public string Email { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool Successful { get; set; }
        public string Method { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
    }

    // Placeholder interfaces for biometric and two-factor services
    public interface IBiometricService
    {
        Task<bool> SetupBiometricAsync();
        Task<bool> AuthenticateAsync();
        Task<bool> IsAvailableAsync();
    }

    public interface ITwoFactorService
    {
        Task<bool> SetupTwoFactorAsync(string phoneNumber);
        Task<string> SendCodeAsync(string phoneNumber);
        Task<bool> VerifyCodeAsync(string code);
    }

    // Basic implementations
    public class BiometricService : IBiometricService
    {
        public async Task<bool> SetupBiometricAsync()
        {
            await Task.Delay(100);
            return true; // Placeholder
        }

        public async Task<bool> AuthenticateAsync()
        {
            await Task.Delay(100);
            return false; // Placeholder
        }

        public async Task<bool> IsAvailableAsync()
        {
            await Task.Delay(50);
            return false; // Placeholder
        }
    }

    public class TwoFactorService : ITwoFactorService
    {
        public async Task<bool> SetupTwoFactorAsync(string phoneNumber)
        {
            await Task.Delay(100);
            return true; // Placeholder
        }

        public async Task<string> SendCodeAsync(string phoneNumber)
        {
            await Task.Delay(100);
            return "123456"; // Placeholder
        }

        public async Task<bool> VerifyCodeAsync(string code)
        {
            await Task.Delay(100);
            return code == "123456"; // Placeholder
        }
    }
}

