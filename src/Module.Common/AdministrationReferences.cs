using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace VeloTime.Module.Common;

public sealed record EntityReference(Guid Id, Guid FacilityId);
public sealed record DependencySummary(int Count, string Description);

public interface IAdministrationReferences
{
    Task<EntityReference?> GetAsync(string module, string kind, Guid id, CancellationToken cancellationToken);
    Task<DependencySummary> GetDependenciesAsync(string module, string kind, Guid id, CancellationToken cancellationToken);
}

public sealed class ReferenceUnavailableException : Exception;
public sealed class AdministrationConflictException(string message) : Exception(message);

public sealed class AdministrationReferences(HttpClient client, IHttpContextAccessor accessor) : IAdministrationReferences
{
    public static IServiceCollection AddServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient<IAdministrationReferences, AdministrationReferences>();
        return services;
    }

    public Task<EntityReference?> GetAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
        ReadAsync<EntityReference>(module, $"{kind}/{id}", true, cancellationToken);

    public async Task<DependencySummary> GetDependenciesAsync(string module, string kind, Guid id, CancellationToken cancellationToken) =>
        await ReadAsync<DependencySummary>(module, $"{kind}/{id}/dependencies", false, cancellationToken) ?? throw new ReferenceUnavailableException();

    private async Task<TResponse?> ReadAsync<TResponse>(string module, string path, bool allowMissing, CancellationToken cancellationToken)
    {
        if (module is not ("facilities" or "timing" or "statistics")) throw new ArgumentException("Unknown module.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https+http://module-{module}-api/api/{module}/references/{path}");
        if (AuthenticationHeaderValue.TryParse(accessor.HttpContext?.Request.Headers.Authorization.ToString(), out var authorization))
            request.Headers.Authorization = authorization;
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (allowMissing && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw new ReferenceUnavailableException();
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
        }
        catch (HttpRequestException) { throw new ReferenceUnavailableException(); }
        catch (System.Text.Json.JsonException) { throw new ReferenceUnavailableException(); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ReferenceUnavailableException(); }
    }
}

public sealed class AdministrationDependencyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (ReferenceUnavailableException)
        {
            return Results.Problem(detail: "Dependencies could not be verified. No changes were saved; try again when the related services are available.", statusCode: 503);
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException exception) { return Results.Problem(detail: exception.Message, statusCode: 400); }
        catch (AdministrationConflictException exception) { return Results.Problem(detail: exception.Message, statusCode: 409); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException) { return Results.Problem(detail: "Related records changed while saving. Refresh and resolve the dependencies before trying again.", statusCode: 409); }
    }
}