namespace LupexWallet.Api.Auth;

/// <summary>
/// Конфигурация единого пароля и JWT (секция "Auth" в appsettings/переменных окружения).
/// PasswordHash задается оператором при развертывании через PasswordHasher.Hash(...) —
/// регистрации через API не предусмотрено (раздел "Цель" бизнес-требований).
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
