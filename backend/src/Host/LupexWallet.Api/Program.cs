using System.Text;
using LupexWallet.Api.Auth;
using LupexWallet.Audit.Api;
using LupexWallet.Audit.Infrastructure;
using LupexWallet.BalanceHistory.Api;
using LupexWallet.BalanceHistory.Infrastructure;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Api;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.Operations.Api;
using LupexWallet.Operations.Infrastructure;
using LupexWallet.ReferenceData.Api;
using LupexWallet.ReferenceData.Infrastructure;
using LupexWallet.Reporting.Api;
using LupexWallet.Reporting.Infrastructure;
using LupexWallet.Wallets.Api;
using LupexWallet.Wallets.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Auth (Host — сквозная забота, не DDD-модуль: high-level-architecture.md, §8) ----
builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateDataAnnotations();

if (!builder.Environment.IsDevelopment())
{
    var authSection = builder.Configuration.GetSection(AuthOptions.SectionName);
    if (string.IsNullOrWhiteSpace(authSection["PasswordHash"]) || string.IsNullOrWhiteSpace(authSection["JwtSigningKey"]))
    {
        throw new InvalidOperationException(
            "Auth:PasswordHash и Auth:JwtSigningKey обязательны вне Development — задайте их через " +
            "переменные окружения (Auth__PasswordHash, Auth__JwtSigningKey) или secret manager. См. README.md.");
    }

    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("LupexWallet")))
    {
        throw new InvalidOperationException(
            "ConnectionStrings:LupexWallet обязательна вне Development — задайте через переменную окружения " +
            "ConnectionStrings__LupexWallet или secret manager. См. README.md.");
    }
}

builder.Services.AddSingleton<TokenService>();
builder.Services.AddLupexWalletRateLimiting();

var jwtSigningKey = builder.Configuration[$"{AuthOptions.SectionName}:JwtSigningKey"] ?? string.Empty;
var jwtIssuer = builder.Configuration[$"{AuthOptions.SectionName}:JwtIssuer"] ?? "LupexWallet";
var jwtAudience = builder.Configuration[$"{AuthOptions.SectionName}:JwtAudience"] ?? "LupexWallet";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                string.IsNullOrEmpty(jwtSigningKey) ? "development-only-placeholder-key-not-secure!!" : jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// ---- MediatR + сквозные behaviors (high-level-architecture.md, §4) ----
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(
        typeof(LupexWallet.Wallets.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.ReferenceData.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.Operations.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.BalanceHistory.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.ExchangeRates.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.Audit.Application.AssemblyMarker).Assembly,
        typeof(LupexWallet.Reporting.Application.AssemblyMarker).Assembly);

    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
});
builder.Services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
// ADR-0010: кто выполняет текущую цепочку обработки (User/System) — читают обработчики
// Audit.Infrastructure и фоновые сервисы BalanceHistory/ExchangeRates.
builder.Services.AddScoped<IAuditActorAccessor, AuditActorAccessor>();

// ---- Композиция модулей (ddd-model.md bounded contexts = модули) ----
builder.Services.AddWalletsModule(builder.Configuration);
builder.Services.AddReferenceDataModule(builder.Configuration);
builder.Services.AddOperationsModule(builder.Configuration);
builder.Services.AddBalanceHistoryModule(builder.Configuration);
builder.Services.AddExchangeRatesModule(builder.Configuration);
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddReportingModule(builder.Configuration);

// ---- CORS: разрешённые origin'ы фронтенда — из конфига (Cors:AllowedOrigins), +
// localhost:4200 в Development. Список пуст в проде, пока не задан деплойментом
// (Railway/Vercel — разные origin для prod/staging) через Cors__AllowedOrigins__0 и т.д.
const string FrontendCorsPolicy = "FrontendCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (builder.Environment.IsDevelopment())
{
    allowedOrigins = [.. allowedOrigins, "http://localhost:4200"];
}
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// ---- API-инфраструктура ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "LupexWallet API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

var app = builder.Build();

// ---- За обратным прокси (Railway) исходный протокол/хост приходят в заголовках,
// а не в самом запросе — без этого UseHttpsRedirection примет проксированный HTTP
// за нешифрованный и уйдёт в редирект-петлю.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// ---- Применение EF Core-миграций всех модулей при старте (каждый — своя схема, ADR-0006) ----
// До этого места ни одна миграция не применялась к реальной БД в продакшен-хосте (только в
// тестах через LupexWalletApiFactory.MigrateAllAsync) — без этого блока приложение падало бы
// на первом же запросе с "relation ... does not exist".
using (var migrationScope = app.Services.CreateScope())
{
    var services = migrationScope.ServiceProvider;
    await services.GetRequiredService<LupexWallet.Wallets.Infrastructure.WalletsDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<LupexWallet.ReferenceData.Infrastructure.ReferenceDataDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<LupexWallet.Operations.Infrastructure.OperationsDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<LupexWallet.BalanceHistory.Infrastructure.BalanceHistoryDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<LupexWallet.ExchangeRates.Infrastructure.ExchangeRatesDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<LupexWallet.Audit.Infrastructure.AuditDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// /health исключён из HTTPS-редиректа: Railway опрашивает его напрямую по HTTP внутри
// приватной сети, минуя edge-прокси, где не будет X-Forwarded-Proto.
app.UseWhen(
    context => context.Request.Path != "/health",
    branch => branch.UseHttpsRedirection());

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseCors(FrontendCorsPolicy);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// /auth/login — единственный анонимный эндпоинт; все остальные требуют Bearer-токен
// (docs/api/api-design.md, §"Аутентификация").
app.MapAuthEndpoints();

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapWalletsEndpoints();
api.MapReferenceDataEndpoints();
api.MapOperationsEndpoints();
api.MapBalanceHistoryEndpoints();
api.MapExchangeRatesEndpoints();
api.MapAuditEndpoints();
api.MapReportingEndpoints();

app.Run();

public partial class Program;
