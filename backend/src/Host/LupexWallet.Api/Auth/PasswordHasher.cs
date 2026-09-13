using System.Security.Cryptography;

namespace LupexWallet.Api.Auth;

/// <summary>
/// Hashes the application's single password (high-level-architecture.md, §8: single-user
/// application, no registration, but a single shared password). Uses PBKDF2-HMACSHA256 via
/// the built-in Rfc2898DeriveBytes — no third-party packages.
///
/// Hash format: "{iterations}.{salt-base64}.{hash-base64}" — self-contained, no need to
/// store parameters separately.
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
