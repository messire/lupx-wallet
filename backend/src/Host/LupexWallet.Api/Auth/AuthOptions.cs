namespace LupexWallet.Api.Auth;

/// <summary>
/// Configuration for the single application password and JWT (the "Auth" section in
/// appsettings/environment variables). PasswordHash is set by the operator at deployment
/// time via PasswordHasher.Hash(...) — there is no API-based registration (see the "Goal"
/// section of the business requirements).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public required string PasswordHash { get; init; }
    public required string JwtSigningKey { get; init; }
    public string JwtIssuer { get; init; } = "LupexWallet";
    public string JwtAudience { get; init; } = "LupexWallet";
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromHours(12);
}
