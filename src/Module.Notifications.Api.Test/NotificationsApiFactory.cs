using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VeloTime.Module.Notifications.Api.Identity;
using VeloTime.Module.Notifications.Interface.Identity;
using VeloTime.Module.Notifications.Storage;

namespace VeloTime.Module.Notifications.Api.Test;

public sealed class NotificationsApiFactory(PostgresFixture database) : WebApplicationFactory<Program>
{
    private readonly RSA assertionKey = RSA.Create(2048);
    public ClaimsPrincipal? Principal { get; set; }
    public NotificationReaderIdentity? ReaderIdentity { get; set; }
    public string AssertionPublicKey => assertionKey.ExportSubjectPublicKeyInfoPem();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["OIDC_AUDIENCE"] = "https://build.invalid",
                ["ConnectionStrings:keycloak"] = "https://issuer.example/realm",
                ["ConnectionStrings:velotimedb"] = database.ConnectionString,
                ["ConnectionStrings:cache"] = "localhost:6379,abortConnect=false",
                ["Aspire:StackExchange:Redis:DisableHealthChecks"] = "true",
                ["Notifications:SessionAssertion:Issuer"] = "webui-test",
                ["Notifications:SessionAssertion:Audience"] = "notifications-api-test",
                ["Notifications:SessionAssertion:PublicKeys:test-key"] = AssertionPublicKey,
                ["Notifications:SessionAssertion:MaximumLifetimeSeconds"] = "120",
                ["Notifications:SessionAssertion:ClockSkewSeconds"] = "0"
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

            services.RemoveAll<INotificationReaderIdentityAccessor>();
            services.AddSingleton<INotificationReaderIdentityAccessor>(new TestIdentityAccessor(this));
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());

            services.AddSingleton(this);
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "NotificationsTest";
                options.DefaultChallengeScheme = "NotificationsTest";
                options.DefaultForbidScheme = "NotificationsTest";
            }).AddScheme<AuthenticationSchemeOptions, NotificationsTestAuthenticationHandler>(
                "NotificationsTest", _ => { });
        });
    }

    public HttpClient CreateContractClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing) assertionKey.Dispose();
        base.Dispose(disposing);
    }

    private sealed class TestIdentityAccessor(NotificationsApiFactory factory) : INotificationReaderIdentityAccessor
    {
        public NotificationReaderIdentity? GetCurrent() => factory.ReaderIdentity;
    }
}

public sealed class NotificationsTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    NotificationsApiFactory factory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
        factory.Principal is null
            ? AuthenticateResult.NoResult()
            : AuthenticateResult.Success(new AuthenticationTicket(factory.Principal, Scheme.Name)));
}
