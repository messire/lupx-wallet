using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LupexWallet.Api.Auth;

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Выпуск JWT после успешной проверки единого пароля (POST /api/v1/auth/login,
/// docs/api/openapi.yaml). Токен не несет ролей/прав — приложение однопользовательское,
/// сам факт валидного токена означает полный доступ.
/// </summary>
public sealed class TokenService(IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    public IssuedToken IssueToken()
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(_options.TokenLifetime);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "lupex-wallet-user")],
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
