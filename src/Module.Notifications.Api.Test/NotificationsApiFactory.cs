using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VeloTime.Module.Notifications.Storage;
using System.IdentityModel.Tokens.Jwt;

namespace VeloTime.Module.Notifications.Api.Test;

public sealed class NotificationsApiFactory(PostgresFixture database) : WebApplicationFactory<Program>
{
    private readonly RSA signingKey = RSA.Create(2048);
    public const string Issuer = "https://issuer.example/realm";
    public const string Audience = "https://build.invalid";
    public string EnvironmentName { get; set; } = "Production";

    public string CreateToken(params Claim[] claims)
    {
        var credentials = new SigningCredentials(
            new RsaSecurityKey(signingKey) { KeyId = "notifications-test-key" }, SecurityAlgorithms.RsaSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience, claims, now.AddMinutes(-1), now.AddMinutes(5), credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Authenticate(HttpClient client, params Claim[] claims) =>
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(claims));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["OIDC_AUDIENCE"] = Audience,
                ["ConnectionStrings:keycloak"] = Issuer,
                ["ConnectionStrings:velotimedb"] = database.ConnectionString,
                ["ConnectionStrings:cache"] = "localhost:6379,abortConnect=false",
                ["Aspire:StackExchange:Redis:DisableHealthChecks"] = "true"
            }));
        builder.ConfigureTestServices(services =>
        {
            var contextRegistrations = new[]
            {
                typeof(NotificationsDbContext), typeof(DbContextOptions<NotificationsDbContext>),
                typeof(IDbContextOptionsConfiguration<NotificationsDbContext>)
            };
            foreach (var registration in services.Where(service => contextRegistrations.Contains(service.ServiceType)).ToArray())
                services.Remove(registration);
            services.AddDbContext<NotificationsDbContext>(options => options
                .UseNpgsql(database.ConnectionString, postgres =>
                    postgres.MigrationsHistoryTable("__ef_migrations_history", "ef"))
                .UseSnakeCaseNamingConvention());

            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.MetadataAddress = string.Empty;
                options.ConfigurationManager = null;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(signingKey.ExportParameters(false))
                        { KeyId = "notifications-test-key" },
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        });
    }

    public HttpClient CreateContractClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing) signingKey.Dispose();
        base.Dispose(disposing);
    }
}
