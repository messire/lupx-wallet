using System.Security.Cryptography;

namespace LupexWallet.Api.Auth;

/// <summary>
/// Хеширование единого пароля приложения (high-level-architecture.md, §8: однопользовательское
/// приложение, без регистрации, но с единым паролем). PBKDF2-HMACSHA256 через встроенный
/// Rfc2898DeriveBytes — без сторонних пакетов.
///
/// Формат хеша: "{iterations}.{salt-base64}.{hash-base64}" — самодостаточен, не требует
/// хранить параметры отдельно.
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000; // OWASP-рекомендация для PBKDF2-HMACSHA256 (2023+)
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encodedHash)
    {
        var parts = encodedHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expectedHash = Convert.FromBase64String(parts[2]);

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
