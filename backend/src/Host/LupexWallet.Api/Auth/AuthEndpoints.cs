using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LupexWallet.Api.Auth;

public sealed record LoginRequest(string Password);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// POST /api/v1/auth/login — the only endpoint not protected by a Bearer token
/// (docs/api/openapi.yaml, docs/api/api-design.md, "Authentication" section).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", (
                [FromBody] LoginRequest request,
                IOptions<AuthOptions> authOptions,
                TokenService tokenService) =>
            {
                if (!PasswordHasher.Verify(request.Password, authOptions.Value.PasswordHash))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Invalid password",
                        type: "https://lupexwallet/errors/invalid-password");
                }

                var issued = tokenService.IssueToken();
                return Results.Ok(new LoginResponse(issued.Token, issued.ExpiresAt));
            })
            .RequireRateLimiting(RateLimiting.LoginPolicyName)
            .AllowAnonymous()
            .WithName("login")
            .WithOpenApi();

        return app;
    }
}
