using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AegisQuant.UI.Services;

/// <summary>
/// 配置文件加密服务 - 使用 AES 加密敏感信息
/// Requirements: 11.6
/// </summary>
public class ConfigEncryptionService
{
    private static ConfigEncryptionService? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// 单例实例
    /// </summary>
    public static ConfigEncryptionService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ConfigEncryptionService();
                }
            }
            return _instance;
        }
    }

    // AES 密钥长度 (256 bits)
    private const int KeySize = 256;
    private const int BlockSize = 128;
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int Iterations = 10000;

    private readonly byte[] _masterKey;

    private ConfigEncryptionService()
    {
        _masterKey = GetOrCreateMasterKey();
    }

    /// <summary>
    /// 加密字符串
    /// </summary>
    /// <param name="plainText">明文</param>
    /// <returns>Base64 编码的密文</returns>
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // 生成随机 Salt 和 IV
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);

        // 从主密钥派生加密密钥
        using var keyDerivation = new Rfc2898DeriveBytes(_masterKey, salt, Iterations, HashAlgorithmName.SHA256);
        aes.Key = keyDerivation.GetBytes(KeySize / 8);
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // 组合: Salt + IV + CipherText
        var result = new byte[SaltSize + IvSize + cipherBytes.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(iv, 0, result, SaltSize, IvSize);
        Buffer.BlockCopy(cipherBytes, 0, result, SaltSize + IvSize, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// 解密字符串
    /// </summary>
    /// <param name="cipherText">Base64 编码的密文</param>
    /// <returns>明文</returns>
    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        try
        {
            var fullCipher = Convert.FromBase64String(cipherText);

            if (fullCipher.Length < SaltSize + IvSize)
                throw new CryptographicException("Invalid cipher text");

            // 提取 Salt, IV, CipherText
            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            var cipher = new byte[fullCipher.Length - SaltSize - IvSize];

            Buffer.BlockCopy(fullCipher, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(fullCipher, SaltSize, iv, 0, IvSize);
            Buffer.BlockCopy(fullCipher, SaltSize + IvSize, cipher, 0, cipher.Length);

            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.BlockSize = BlockSize;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // 从主密钥派生解密密钥
            using var keyDerivation = new Rfc2898DeriveBytes(_masterKey, salt, Iterations, HashAlgorithmName.SHA256);
            aes.Key = keyDerivation.GetBytes(KeySize / 8);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Decryption failed", ex);
        }
    }

    /// <summary>
    /// 加密配置对象
    /// </summary>
    /// <typeparam name="T">配置类型</typeparam>
    /// <param name="config">配置对象</param>
    /// <returns>加密后的 JSON 字符串</returns>
    public string EncryptConfig<T>(T config) where T : class
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        return Encrypt(json);
    }

    /// <summary>
    /// 解密配置对象
    /// </summary>
    /// <typeparam name="T">配置类型</typeparam>
    /// <param name="encryptedConfig">加密的配置字符串</param>
    /// <returns>配置对象</returns>
    public T? DecryptConfig<T>(string encryptedConfig) where T : class
    {
        var json = Decrypt(encryptedConfig);
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// 保存加密配置到文件
    /// </summary>
    /// <typeparam name="T">配置类型</typeparam>
    /// <param name="config">配置对象</param>
    /// <param name="filePath">文件路径</param>
    public void SaveEncryptedConfig<T>(T config, string filePath) where T : class
    {
        var encrypted = EncryptConfig(config);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(filePath, encrypted);
    }

    /// <summary>
    /// 从文件加载加密配置
    /// </summary>
    /// <typeparam name="T">配置类型</typeparam>
    /// <param name="filePath">文件路径</param>
    /// <returns>配置对象</returns>
    public T? LoadEncryptedConfig<T>(string filePath) where T : class
    {
        if (!File.Exists(filePath))
            return null;

        var encrypted = File.ReadAllText(filePath);
        return DecryptConfig<T>(encrypted);
    }

    /// <summary>
    /// 加密敏感字段 (如 API Key)
    /// </summary>
    /// <param name="sensitiveValue">敏感值</param>
    /// <returns>加密后的值 (带前缀标识)</returns>
    public string EncryptSensitive(string sensitiveValue)
    {
        if (string.IsNullOrEmpty(sensitiveValue))
            return string.Empty;

        // 已加密的值不再加密
        if (sensitiveValue.StartsWith("ENC:"))
            return sensitiveValue;

        return "ENC:" + Encrypt(sensitiveValue);
    }

    /// <summary>
    /// 解密敏感字段
    /// </summary>
    /// <param name="encryptedValue">加密的值</param>
    /// <returns>明文值</returns>
    public string DecryptSensitive(string encryptedValue)
    {
        if (string.IsNullOrEmpty(encryptedValue))
            return string.Empty;

        // 未加密的值直接返回
        if (!encryptedValue.StartsWith("ENC:"))
            return encryptedValue;

        return Decrypt(encryptedValue[4..]);
    }

    /// <summary>
    /// 获取或创建主密钥
    /// </summary>
    private static byte[] GetOrCreateMasterKey()
    {
        var keyPath = GetMasterKeyPath();
        var dir = Path.GetDirectoryName(keyPath);

        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(keyPath))
        {
            // 读取现有密钥
            var keyData = File.ReadAllBytes(keyPath);
            return ProtectedData.Unprotect(keyData, null, DataProtectionScope.CurrentUser);
        }
        else
        {
            // 生成新密钥
            var key = RandomNumberGenerator.GetBytes(32);
            var protectedKey = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(keyPath, protectedKey);
            
            // 设置文件属性为隐藏
            File.SetAttributes(keyPath, FileAttributes.Hidden);
            
            return key;
        }
    }

    private static string GetMasterKeyPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AegisQuant",
            ".keystore"
        );
    }
}

/// <summary>
/// 敏感配置模型
/// </summary>
public class SensitiveConfig
{
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? BrokerEndpoint { get; set; }
    public string? AccountId { get; set; }
}
