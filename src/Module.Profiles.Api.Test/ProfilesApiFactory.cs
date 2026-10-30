using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Profiles.Service;

namespace VeloTime.Module.Profiles.Api.Test;

public sealed class ProfilesApiFactory : WebApplicationFactory<Program>
{
    public RecordingPreferences Preferences { get; } = new();
    public ClaimsPrincipal? Principal { get; set; } = Identity("https://issuer.example/realm", "subject");
    public bool UseRealBearer { get; init; }
    public bool UseRealPreferences { get; init; }

    public static ClaimsPrincipal Identity(string? issuer, string? subject) =>
        new(new ClaimsIdentity(
            (issuer is null ? Array.Empty<Claim>() : [new Claim("iss", issuer)])
            .Concat(subject is null ? [] : new[] { new Claim("sub", subject) }),
            "ContractTest"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["OIDC_AUDIENCE"] = "https://build.invalid",
                ["ConnectionStrings:keycloak"] = "https://issuer.example/realm",
                // Never used: the API contract tests replace the service, not the database provider.
                ["ConnectionStrings:profilesdb"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused"
            }));
        builder.ConfigureTestServices(services =>
        {
            if (!UseRealPreferences)
            {
                foreach (var registration in services.Where(s => s.ServiceType == typeof(ILanguagePreferenceService)).ToArray())
                    services.Remove(registration);
                services.AddSingleton<ILanguagePreferenceService>(Preferences);
            }
            if (!UseRealBearer)
            {
                services.AddSingleton(this);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "ContractTest";
                    options.DefaultChallengeScheme = "ContractTest";
                }).AddScheme<AuthenticationSchemeOptions, ContractAuthenticationHandler>("ContractTest", _ => { });
            }
        });
    }

    public HttpClient CreateContractClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}

public sealed class ContractAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    ProfilesApiFactory factory) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
        factory.Principal is null
            ? AuthenticateResult.NoResult()
            : AuthenticateResult.Success(new AuthenticationTicket(factory.Principal, Scheme.Name)));
}

public sealed class RecordingPreferences : ILanguagePreferenceService
{
    public List<(string Issuer, string Subject, string? Language, bool Write, CancellationToken Token)> Calls { get; } = [];
    public Func<CancellationToken, Task<LanguagePreferenceDTO>>? OnCall { get; set; }
    private readonly Dictionary<(string, string), string> saved = [];

    public Task<LanguagePreferenceDTO> GetAsync(string issuer, string subject, CancellationToken cancellationToken = default)
    {
        Calls.Add((issuer, subject, null, false, cancellationToken));
        return OnCall?.Invoke(cancellationToken)
            ?? Task.FromResult(new LanguagePreferenceDTO(saved.GetValueOrDefault((issuer, subject))));
    }

    public Task<LanguagePreferenceDTO> UpdateAsync(string issuer, string subject, string? language,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((issuer, subject, language, true, cancellationToken));
        if (OnCall is not null) return OnCall(cancellationToken);
        saved[(issuer, subject)] = language!;
        return Task.FromResult(new LanguagePreferenceDTO(language));
    }
}
