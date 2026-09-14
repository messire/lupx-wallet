using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LupexWallet.Api.Auth;

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues a JWT after successful verification of the single password (POST
/// /api/v1/auth/login, docs/api/openapi.yaml). The token carries no roles/claims — the
/// application is single-user, so a valid token alone grants full access.
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
