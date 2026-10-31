using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Profiles.Service;

namespace VeloTime.Module.Profiles.Api.Test;

public sealed class ProfilesApiFactory : WebApplicationFactory<Program>
{
    public RecordingPreferences Preferences { get; } = new();
    public RecordingHybridCache Cache { get; } = new();
    public ICacheInvalidationOperations CacheInvalidation => new TestCacheInvalidationOperations(Cache);
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
                ["ConnectionStrings:velotimedb"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused",
                ["ConnectionStrings:cache"] = "localhost:6379"
            }));
        builder.ConfigureTestServices(services =>
        {
            foreach (var registration in services.Where(service => service.ServiceType == typeof(HybridCache)).ToArray())
                services.Remove(registration);
            services.AddSingleton<HybridCache>(Cache);
            foreach (var registration in services.Where(service => service.ServiceType == typeof(ICacheInvalidationOperations)).ToArray())
                services.Remove(registration);
            services.AddSingleton(CacheInvalidation);

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

public sealed record RecordedCacheRead(string Key, HybridCacheEntryOptions? Options);

public sealed class RecordingHybridCache : HybridCache
{
    private readonly object gate = new();
    private readonly Dictionary<string, object?> values = [];

    public List<RecordedCacheRead> Reads { get; } = [];
    public List<(string Key, CancellationToken Token)> Removals { get; } = [];
    public Exception? RemoveFailure { get; set; }

    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            Reads.Add(new RecordedCacheRead(key, options));
            if (values.TryGetValue(key, out var cached))
                return ValueTask.FromResult((T)cached!);
        }

        return CreateAsync();

        async ValueTask<T> CreateAsync()
        {
            var value = await factory(state, cancellationToken);
            lock (gate)
                values[key] = value;
            return value;
        }
    }

    public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        lock (gate)
            values[key] = value;
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            Removals.Add((key, cancellationToken));
            if (RemoveFailure is not null)
                return ValueTask.FromException(RemoveFailure);
            values.Remove(key);
        }
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}

internal sealed class TestCacheInvalidationOperations(RecordingHybridCache cache) : ICacheInvalidationOperations
{
    public bool Handles(HybridCache hybridCache) => false;
    public bool RequiresStrictHandling(HybridCache hybridCache) => false;
    public Task RemoveAsync(string key, CancellationToken cancellationToken) => cache.RemoveAsync(key, cancellationToken).AsTask();
    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken) => cache.RemoveByTagAsync(tag, cancellationToken).AsTask();
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
