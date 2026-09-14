using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace LupexWallet.Api.Auth;

/// <summary>
/// Limits the rate of login attempts (docs/api/api-design.md: 429 on /auth/login,
/// protection against password brute-forcing). Uses the built-in
/// Microsoft.AspNetCore.RateLimiting — no additional packages.
/// </summary>
public static class RateLimiting
{
    public const string LoginPolicyName = "login";

    public static IServiceCollection AddLupexWalletRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddFixedWindowLimiter(LoginPolicyName, limiterOptions =>
            {
                limiterOptions.PermitLimit = 5;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueLimit = 0;
            });
        });

        return services;
    }
}
