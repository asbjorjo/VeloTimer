using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.WebUI.Mud.Endpoints;

internal static class BffEndpoints
{
    internal static void MapBffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme
            });
        api.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
                {
                    await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
                }

                return await next(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var status = exception switch
                {
                    Module.Facilities.Client.ApiException upstream => upstream.StatusCode,
                    Module.Timing.Client.ApiException upstream => upstream.StatusCode,
                    Module.Statistics.Client.ApiException upstream => upstream.StatusCode,
                    Keycloak.ApiException upstream => upstream.StatusCode,
                    HttpRequestException { StatusCode: { } statusCode } => (int)statusCode,
                    _ => StatusCodes.Status502BadGateway
                };
                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("WebUI.Bff").LogError("BFF request failed for {Path}: {ExceptionType}, status {Status}",
                        http.Request.Path, exception.GetType().Name, status);
                return Results.Problem(
                    detail: exception is HttpRequestException && status is 400 or 409 or 422 or 503 ? exception.Message : null,
                    statusCode: status is 400 or 401 or 403 or 404 or 409 or 422 or 503 ? status : 502);
            }
        });

        api.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            TypedResults.Ok(antiforgery.GetAndStoreTokens(context).RequestToken));

        api.MapGet("/facilities", async (IFacilityService service, CancellationToken cancellationToken) =>
            TypedResults.Ok((await service.GetFacilitiesAsync(cancellationToken)).ToArray())).AllowAnonymous();
        api.MapGet("/facilities/{facilityId:guid}/layouts", async (Guid facilityId,
            IFacilityService service, CancellationToken cancellationToken) =>
            TypedResults.Ok((await service.GetFacilityLayoutsAsync(facilityId, cancellationToken)).ToArray())).AllowAnonymous();
        api.MapGet("/layouts/{layoutId:guid}", async (Guid layoutId,
            IFacilityService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetCourseLayoutDetailAsync(layoutId, cancellationToken))).AllowAnonymous();
        api.MapGet("/facilities/{facilityId:guid}/installations", async (Guid facilityId,
            IFacilityService service, CancellationToken cancellationToken) =>
            TypedResults.Ok((await service.GetInstallationsAsync(facilityId, cancellationToken)).ToArray()));

        api.MapGet("/timing/installations", async (ITimingService service, CancellationToken cancellationToken) =>
            TypedResults.Ok((await service.GetInstallationsAsync(cancellationToken)).ToArray()));
        api.MapGet("/timing/installations/{id:guid}", async (Guid id,
            ITimingService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetInstallationAsync(id, cancellationToken)));
        api.MapPut("/timing/installations/{id:guid}", async (Guid id, InstallationView installation,
            ITimingService service, CancellationToken cancellationToken) =>
        {
            if (id != installation.Id)
            {
                return Results.BadRequest();
            }

            await service.UpdateInstallationAsync(installation, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization(VeloTime.Module.Common.AdministrationAuthorization.TimingPolicy);

        api.MapStatisticsEndpoints();
        api.MapStatisticsAdministrationEndpoints();
        api.MapTransponderManagementEndpoints();
        api.MapTransponderOwnerEndpoints();
        api.MapInfrastructureAdministrationEndpoints();

        api.MapGet("/debug/auth/token", async (IAuthDebugService service, CancellationToken cancellationToken) =>
            await service.GetAccessTokenInfoAsync(cancellationToken) is { } info ? Results.Ok(info) : Results.NotFound());

        api.MapGet("/profile/username", async (IProfileService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetUsernameAsync(cancellationToken)));
        api.MapGet("/profile/accounts", async (IProfileService service, CancellationToken cancellationToken) =>
            TypedResults.Ok((await service.GetLinkedAccountsAsync(cancellationToken)).ToArray()));
        api.MapPost("/profile/accounts/{provider}/link", async (string provider, [Microsoft.AspNetCore.Mvc.FromBody] string redirectUri,
            HttpContext context, IProfileService service, CancellationToken cancellationToken) =>
        {
            var origin = new Uri($"{context.Request.Scheme}://{context.Request.Host}");
            if (string.IsNullOrWhiteSpace(redirectUri) || redirectUri.Contains('\\') ||
                !Uri.TryCreate(origin, redirectUri, out var redirect) ||
                redirect.Scheme != origin.Scheme || redirect.Authority != origin.Authority ||
                !string.IsNullOrEmpty(redirect.UserInfo))
            {
                return Results.BadRequest();
            }

            return Results.Ok(await service.GetLinkAccountAsync(redirect.AbsoluteUri, provider, cancellationToken));
        });
        api.MapDelete("/profile/accounts/{provider}", async (string provider,
            IProfileService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteLinkedAccountAsync(provider, cancellationToken);
            return TypedResults.NoContent();
        });
    }
}