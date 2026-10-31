using System.Security.Claims;
using Duende.AccessTokenManagement.OpenIdConnect;

namespace VeloTime.WebUI.Mud.Services;

/// <summary>Execution-local identity for independently owned cache factory scopes.</summary>
public sealed class WebUiCacheFactoryContext
{
    private readonly AsyncLocal<ClaimsPrincipal?> _principal = new();

    internal ClaimsPrincipal? Principal => _principal.Value;

    internal async Task<T> RunAsync<TClient, T>(
        IServiceScopeFactory scopeFactory,
        ClaimsPrincipal principal,
        Func<TClient, CancellationToken, Task<T>> factory,
        CancellationToken token) where TClient : notnull
    {
        var previous = _principal.Value;
        _principal.Value = principal;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await factory(scope.ServiceProvider.GetRequiredService<TClient>(), token);
        }
        finally
        {
            _principal.Value = previous;
        }
    }
}

/// <summary>
/// Retains Duende's normal BFF/circuit accessor outside cache factories. Inside a
/// factory the handler must use the same detached principal that formed the key,
/// not an ambient request or a possibly disposed initiating circuit.
/// </summary>
internal sealed class WebUiCacheUserAccessor(
    WebUiCacheFactoryContext context,
    [FromKeyedServices("webui-original-user-accessor")] IUserAccessor original) : IUserAccessor
{
    public Task<ClaimsPrincipal> GetCurrentUserAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return context.Principal is { } principal
            ? Task.FromResult(principal)
            : original.GetCurrentUserAsync(ct);
    }

    internal static void Register(IServiceCollection services)
    {
        // Preserve the installed library registration without depending on its
        // internal implementation type or reimplementing its circuit integration.
        var original = services.Last(descriptor => descriptor.ServiceType == typeof(IUserAccessor));
        var implementation = original.ImplementationType
            ?? throw new InvalidOperationException("The token user accessor must have a type registration.");
        services.Add(ServiceDescriptor.DescribeKeyed(
            typeof(IUserAccessor), "webui-original-user-accessor", implementation, original.Lifetime));
        services.AddSingleton<WebUiCacheFactoryContext>();
        services.AddScoped<IUserAccessor, WebUiCacheUserAccessor>();
    }
}
