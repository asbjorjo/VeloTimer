using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VeloTime.Module.Profiles.Interface;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Profiles.Service;

namespace VeloTime.Module.Profiles.Api.Endpoints;

internal static class ProfilePreferenceEndpoints
{
    internal static void MapProfilePreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ProfilePreferenceRoutes.CurrentLanguage, GetCurrentLanguagePreference)
            .RequireAuthorization()
            .WithName("GetCurrentLanguagePreference")
            .WithSummary("Get the authenticated user's language preference")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPut(ProfilePreferenceRoutes.UpdateLanguage, UpdateLanguagePreference)
            .RequireAuthorization()
            .WithName("UpdateLanguagePreference")
            .WithSummary("Update the authenticated user's language preference")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<Results<Ok<LanguagePreferenceDTO>, ProblemHttpResult>> GetCurrentLanguagePreference(
        ClaimsPrincipal user,
        ILanguagePreferenceService preferences,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(user, out var issuer, out var subject))
        {
            return InvalidIdentity();
        }

        var preference = await preferences.GetAsync(issuer, subject, cancellationToken);
        return TypedResults.Ok(preference);
    }

    private static async Task<Results<Ok<LanguagePreferenceDTO>, ProblemHttpResult>> UpdateLanguagePreference(
        ClaimsPrincipal user,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateLanguagePreferenceRequest? request,
        ILanguagePreferenceService preferences,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(user, out var issuer, out var subject))
        {
            return InvalidIdentity();
        }

        if (!ProfileLanguageCodes.IsSupportedPreference(request?.Language))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid language preference",
                detail: "Language must be exactly 'en' or 'nb-NO'.",
                extensions: ErrorExtension(ProfilePreferenceErrorCodes.InvalidLanguage));
        }

        var preference = await preferences.UpdateAsync(issuer, subject, request!.Language, cancellationToken);
        return TypedResults.Ok(preference);
    }

    private static bool TryGetIdentity(ClaimsPrincipal user, out string issuer, out string subject)
    {
        issuer = user.FindFirstValue("iss") ?? string.Empty;
        subject = user.FindFirstValue("sub") ?? string.Empty;
        return !string.IsNullOrWhiteSpace(issuer) && !string.IsNullOrWhiteSpace(subject);
    }

    private static ProblemHttpResult InvalidIdentity() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Invalid authenticated identity",
            detail: "The validated identity must include non-blank issuer and subject claims.",
            extensions: ErrorExtension(ProfilePreferenceErrorCodes.InvalidIdentity));

    private static Dictionary<string, object?> ErrorExtension(string code) =>
        new() { ["code"] = code };
}
