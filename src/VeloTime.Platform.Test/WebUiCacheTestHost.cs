using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Module.Common;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Services;
using ZiggyCreatures.Caching.Fusion;

namespace VeloTime.Platform.Test;

/// <summary>Real cache adapter and independently owned factory scopes, also used by legacy service tests.</summary>
internal sealed class WebUiCacheTestHost : IDisposable
{
    private readonly IHost host;
    internal WebUiCache Cache => host.Services.GetRequiredService<WebUiCache>();
    internal IServiceProvider Services => host.Services;
    internal MutableAuthenticationState Authentication { get; } = new();
    internal HttpContextAccessor Http { get; } = new();

    internal WebUiCacheTestHost(params object[] clients) : this(null, clients) { }

    internal WebUiCacheTestHost(string? redis, params object[] clients)
    {
        var builder = Host.CreateApplicationBuilder();
        if (redis is not null)
        {
            builder.Configuration["ConnectionStrings:cache"] = redis;
            builder.AddModuleCache();
        }
        else
        {
            builder.Services.AddMemoryCache();
            builder.Services.AddFusionCache().AsHybridCache();
            builder.Services.AddSingleton<FusionCacheCircuitBreakerState>();
            builder.Services.AddSingleton<ICacheInvalidationOperations, FusionCacheInvalidationOperations>();
        }
        Register(builder.Services);
        builder.Services.AddSingleton<IHttpContextAccessor>(Http);
        builder.Services.AddSingleton<AuthenticationStateProvider>(Authentication);
        foreach (var client in clients)
            foreach (var contract in client.GetType().GetInterfaces().Where(type =>
                         type.Name.EndsWith("Client") && type.Name.StartsWith("I")))
                builder.Services.AddSingleton(contract, client);
        host = builder.Build();
        Authentication.User = Principal();
    }

    internal static void Register(IServiceCollection services)
    {
        services.AddLogging();
        services.AddAdministrationAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<AuthenticationStateProvider, MutableAuthenticationState>();
        services.AddSingleton<WebUiCacheFactoryContext>();
        services.AddScoped<WebUiCache>();
        services.AddScoped<IDisplayPreferenceService, DisplayPreferenceService>();
        services.AddScoped<ILanguagePreferenceService, LanguagePreferenceService>();
    }

    internal static ClaimsPrincipal Principal(string? issuer = "https://issuer.test", string? subject = "rider",
        string? session = "session", bool admin = true)
    {
        List<Claim> claims = [];
        if (issuer is not null) claims.Add(new("iss", issuer));
        if (subject is not null) claims.Add(new("sub", subject));
        if (session is not null) claims.Add(new("sid", session));
        if (admin) claims.Add(new(AdministrationAuthorization.PermissionClaimType, AdministrationAuthorization.GlobalPermission));
        return new(new ClaimsIdentity(claims, "test"));
    }

    public void Dispose() => host.Dispose();

    internal sealed class MutableAuthenticationState : AuthenticationStateProvider
    {
        internal ClaimsPrincipal User { get; set; } = new(new ClaimsIdentity());
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
    }
}
