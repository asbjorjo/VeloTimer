using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using VeloTime.WebUI.Mud.Services;

namespace VeloTime.WebUI.Mud.Localization;

internal sealed record LanguageSelection(string Language, string ReturnUrl);
internal sealed record LanguageSelectionState(string Language, bool PreferenceUnavailable);

internal static class LanguageSelectionEndpoints
{
    internal static void MapLanguageSelectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/language").AllowAnonymous();
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                try
                {
                    await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>()
                        .ValidateRequestAsync(context.HttpContext);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }
            }

            return await next(context);
        });

        group.MapGet("/state", async (HttpContext http) =>
        {
            return Results.Ok(await CultureResolver.ResolveAsync(http));
        });

        group.MapPost("/select", async ([FromBody] LanguageSelection? selection, HttpContext http,
            ILanguagePreferenceService preferences, CancellationToken cancellationToken) =>
        {
            if (selection is null || selection.Language is not ("en" or "nb-NO") ||
                !IsLocalReturnUrl(selection.ReturnUrl, http))
            {
                return Results.BadRequest();
            }

            if (http.User.Identity?.IsAuthenticated == true)
            {
                try
                {
                    var result = await preferences.SaveAsync(selection.Language, cancellationToken);
                    if (result.Language != selection.Language)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status502BadGateway);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    http.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("WebUI.LanguagePreference")
                        .LogError("Could not save the signed-in language preference ({ExceptionType}).", exception.GetType().Name);
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                http.Response.Cookies.Delete(CultureResolver.AnonymousPreferenceCookie);
            }
            else
            {
                http.Response.Cookies.Append(CultureResolver.AnonymousPreferenceCookie, selection.Language,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = http.Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        IsEssential = true,
                        MaxAge = TimeSpan.FromDays(365)
                    });
            }

            return Results.Ok(new { redirectUrl = selection.ReturnUrl });
        });
    }

    private static bool IsLocalReturnUrl(string? value, HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('/') || value.StartsWith("//") ||
            value.Contains('\\') || value.Contains('#') || value.Any(char.IsControl))
            return false;

        // A rooted path can parse as an absolute file URI. Validate its rooted
        // path/query shape above, then resolve against the origin instead.
        var origin = new Uri($"{http.Request.Scheme}://{http.Request.Host}");
        return Uri.TryCreate(origin, value, out var uri) &&
               uri.Scheme == origin.Scheme &&
               uri.Authority == origin.Authority &&
               string.IsNullOrEmpty(uri.UserInfo);
    }
}
