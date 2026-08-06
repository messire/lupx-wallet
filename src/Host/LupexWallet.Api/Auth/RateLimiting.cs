using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace LupexWallet.Api.Auth;

/// <summary>
/// Ограничение частоты попыток входа (docs/api/api-design.md: 429 на /auth/login,
/// защита от подбора пароля). Встроенный Microsoft.AspNetCore.RateLimiting — без
/// дополнительных пакетов.
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
