# 登录认证详解

## 概述

登录认证是交易系统安全的第一道防线。本文档详细说明如何实现用户认证、角色权限和记住我功能。

## 问题分析

### 无认证的风险

1. **未授权访问**: 任何人都能操作系统
2. **权限混乱**: 无法区分查看者和交易员
3. **审计困难**: 不知道谁执行了操作
4. **合规风险**: 无法满足监管要求

### 设计目标

- 用户名密码认证
- 三级角色权限 (Viewer/Trader/Admin)
- 记住我功能 (7 天有效)
- 密码加盐哈希存储


## 解决方案

### 用户角色定义

```csharp
/// <summary>
/// 用户角色
/// </summary>
public enum UserRole
{
    Viewer,   // 查看者 - 只读权限
    Trader,   // 交易员 - 可执行交易
    Admin     // 管理员 - 完全权限
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
```

### 认证服务实现

```csharp
// AuthenticationService.cs
public class AuthenticationService
{
    private readonly Dictionary<string, (string PasswordHash, UserRole Role)> _users = new();
    private UserInfo? _currentUser;

    public UserInfo? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser != null;

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
    public bool CanTrade() => HasPermission(UserRole.Trader);

    /// <summary>
    /// 检查是否是管理员
    /// </summary>
    public bool IsAdmin() => HasPermission(UserRole.Admin);
}
```

### 密码哈希

```csharp
/// <summary>
/// 密码加盐哈希
/// </summary>
private static string HashPassword(string password)
{
    using var sha256 = SHA256.Create();
    var bytes = sha256.ComputeHash(
        Encoding.UTF8.GetBytes(password + "AegisQuantSalt"));
    return Convert.ToBase64String(bytes);
}
```

### 记住我功能

```csharp
/// <summary>
/// 保存记住我 token
/// </summary>
public void SaveRememberToken(string username)
{
    var token = GenerateToken(username);
    var encrypted = ConfigEncryptionService.Instance.Encrypt(token);
    File.WriteAllText(GetTokenPath(), encrypted);
}

/// <summary>
/// 验证记住我 token
/// </summary>
public string? ValidateRememberToken(string token)
{
    // Token 格式: username:timestamp:hash
    var parts = token.Split(':');
    if (parts.Length != 3) return null;

    var username = parts[0];
    var timestamp = long.Parse(parts[1]);
    var hash = parts[2];

    // 检查 token 是否过期 (7 天)
    var tokenTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
    if (DateTimeOffset.Now - tokenTime > TimeSpan.FromDays(7))
        return null;

    // 验证 hash
    var expectedHash = ComputeTokenHash(username, timestamp);
    if (hash != expectedHash) return null;

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
```

### 用户管理

```csharp
/// <summary>
/// 添加用户 (仅管理员)
/// </summary>
public bool AddUser(string username, string password, UserRole role)
{
    if (!IsAdmin()) return false;

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
```

## 使用示例

```csharp
// 登录
if (AuthenticationService.Instance.ValidateCredentials("trader", "password"))
{
    Console.WriteLine("登录成功");
    
    // 保存记住我
    AuthenticationService.Instance.SaveRememberToken("trader");
}

// 检查权限
if (AuthenticationService.Instance.CanTrade())
{
    // 执行交易操作
}

// 自动登录
var token = AuthenticationService.Instance.GetRememberToken();
if (token != null)
{
    var username = AuthenticationService.Instance.ValidateRememberToken(token);
    if (username != null)
        Console.WriteLine($"自动登录: {username}");
}

// 登出
AuthenticationService.Instance.Logout();
```

## 面试话术

### Q: 为什么使用加盐哈希？

**A**: 防止彩虹表攻击：
1. **盐值**: 相同密码产生不同哈希
2. **单向**: 无法从哈希反推密码
3. **抗碰撞**: SHA256 碰撞概率极低

### Q: 记住我 token 如何保证安全？

**A**: 三重保护：
1. **时间戳**: 7 天过期
2. **哈希签名**: 防止篡改
3. **加密存储**: 使用 DPAPI 加密

### Q: 角色权限如何设计？

**A**: 层级递进：
- `Viewer < Trader < Admin`
- 高级角色自动拥有低级权限
- 使用 `>=` 比较简化权限检查
