using System.Security.Cryptography;
using System.Text;

namespace AegisQuant.UI.Services;

/// <summary>
/// 用户角色
/// </summary>
public enum UserRole
{
    /// <summary>查看者 - 只读权限</summary>
    Viewer,
    /// <summary>交易员 - 可执行交易</summary>
    Trader,
    /// <summary>管理员 - 完全权限</summary>
    Admin
}

/// <summary>
/// 用户信息
/// </summary>
public class UserInfo
{
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public DateTime LastLogin { get; set; }
}

/// <summary>
/// 认证服务 - 管理用户登录和权限
/// Requirements: 14.1, 14.2, 14.3, 14.4, 14.6
/// </summary>
public class AuthenticationService
{
    private static AuthenticationService? _instance;
    private static readonly object _lock = new();

    public static AuthenticationService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new AuthenticationService();
                }
            }
            return _instance;
        }
    }

    private readonly Dictionary<string, (string PasswordHash, UserRole Role)> _users = new();
    private UserInfo? _currentUser;

    public UserInfo? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser != null;

    private AuthenticationService()
    {
        LoadUsers();
    }

    /// <summary>
    /// 验证用户凭据
    /// </summary>
    public bool ValidateCredentials(string username, string password)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return false;

        if (!_users.TryGetValue(username.ToLower(), out var userData))
            return false;

        var hash = HashPassword(password);
        if (hash != userData.PasswordHash)
            return false;

        _currentUser = new UserInfo
        {
            Username = username,
            Role = userData.Role,
            LastLogin = DateTime.Now
        };

        return true;
    }

    /// <summary>
    /// 登出
    /// </summary>
    public void Logout()
    {
        if (_currentUser != null)
        {
            AuditLogService.Instance.LogLogout();
            _currentUser = null;
        }
        ClearRememberToken();
    }

    /// <summary>
    /// 检查是否有指定权限
    /// </summary>
    public bool HasPermission(UserRole requiredRole)
    {
        if (_currentUser == null)
            return false;

        return _currentUser.Role >= requiredRole;
    }

    /// <summary>
    /// 检查是否可以执行交易
    /// </summary>
    public bool CanTrade()
    {
        return HasPermission(UserRole.Trader);
    }

    /// <summary>
    /// 检查是否是管理员
    /// </summary>
    public bool IsAdmin()
    {
        return HasPermission(UserRole.Admin);
    }

    /// <summary>
    /// 保存记住我 token
    /// </summary>
    public void SaveRememberToken(string username)
    {
        try
        {
            var token = GenerateToken(username);
            var encrypted = ConfigEncryptionService.Instance.Encrypt(token);
            var path = GetTokenPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, encrypted);
        }
        catch { }
    }

    /// <summary>
    /// 获取记住我 token
    /// </summary>
    public string? GetRememberToken()
    {
        try
        {
            var path = GetTokenPath();
            if (!File.Exists(path))
                return null;

            var encrypted = File.ReadAllText(path);
            return ConfigEncryptionService.Instance.Decrypt(encrypted);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 验证记住我 token
    /// </summary>
    public string? ValidateRememberToken(string token)
    {
        try
        {
            // Token 格式: username:timestamp:hash
            var parts = token.Split(':');
            if (parts.Length != 3)
                return null;

            var username = parts[0];
            var timestamp = long.Parse(parts[1]);
            var hash = parts[2];

            // 检查 token 是否过期 (7 天)
            var tokenTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            if (DateTimeOffset.Now - tokenTime > TimeSpan.FromDays(7))
                return null;

            // 验证 hash
            var expectedHash = ComputeTokenHash(username, timestamp);
            if (hash != expectedHash)
                return null;

            // 验证用户存在
            if (!_users.ContainsKey(username.ToLower()))
                return null;

            // 设置当前用户
            _currentUser = new UserInfo
            {
                Username = username,
                Role = _users[username.ToLower()].Role,
                LastLogin = DateTime.Now
            };

            return username;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 清除记住我 token
    /// </summary>
    public void ClearRememberToken()
    {
        try
        {
            var path = GetTokenPath();
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    /// <summary>
    /// 添加用户 (仅管理员)
    /// </summary>
    public bool AddUser(string username, string password, UserRole role)
    {
        if (!IsAdmin())
            return false;

        var hash = HashPassword(password);
        _users[username.ToLower()] = (hash, role);
        SaveUsers();
        return true;
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    public bool ChangePassword(string username, string oldPassword, string newPassword)
    {
        if (!ValidateCredentials(username, oldPassword))
            return false;

        var role = _users[username.ToLower()].Role;
        var hash = HashPassword(newPassword);
        _users[username.ToLower()] = (hash, role);
        SaveUsers();
        return true;
    }

    private void LoadUsers()
    {
        try
        {
            var path = GetUsersPath();
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length == 3)
                    {
                        var username = parts[0];
                        var hash = parts[1];
                        var role = Enum.Parse<UserRole>(parts[2]);
                        _users[username.ToLower()] = (hash, role);
                    }
                }
            }
            else
            {
                // 创建默认管理员账户
                _users["admin"] = (HashPassword("admin123"), UserRole.Admin);
                _users["trader"] = (HashPassword("trader123"), UserRole.Trader);
                _users["viewer"] = (HashPassword("viewer123"), UserRole.Viewer);
                SaveUsers();
            }
        }
        catch
        {
            // 使用默认账户
            _users["admin"] = (HashPassword("admin123"), UserRole.Admin);
        }
    }

    private void SaveUsers()
    {
        try
        {
            var path = GetUsersPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var lines = _users.Select(u => $"{u.Key}|{u.Value.PasswordHash}|{u.Value.Role}");
            File.WriteAllLines(path, lines);
        }
        catch { }
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "AegisQuantSalt"));
        return Convert.ToBase64String(bytes);
    }

    private static string GenerateToken(string username)
    {
        var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        var hash = ComputeTokenHash(username, timestamp);
        return $"{username}:{timestamp}:{hash}";
    }

    private static string ComputeTokenHash(string username, long timestamp)
    {
        using var sha256 = SHA256.Create();
        var data = $"{username}:{timestamp}:AegisQuantTokenSecret";
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(bytes)[..16];
    }

    private static string GetUsersPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AegisQuant", "users.dat"
        );
    }

    private static string GetTokenPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AegisQuant", ".remember"
        );
    }
}
