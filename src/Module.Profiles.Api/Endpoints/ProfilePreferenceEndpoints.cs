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
    private static readonly IReadOnlyList<TimeZoneOptionDTO> TimeZones = CreateTimeZoneCatalogue();
    private static readonly HashSet<string> TimeZoneIds = TimeZones.Select(zone => zone.Id).ToHashSet(StringComparer.Ordinal);

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

        endpoints.MapGet(ProfilePreferenceRoutes.CurrentDisplayPreference, GetCurrentDisplayPreference)
            .RequireAuthorization()
            .WithName("GetCurrentDisplayPreference")
            .WithSummary("Get the authenticated user's display preferences")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPut(ProfilePreferenceRoutes.UpdateDisplayPreference, UpdateDisplayPreference)
            .RequireAuthorization()
            .WithName("UpdateDisplayPreference")
            .WithSummary("Replace the authenticated user's display preferences")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapGet(ProfilePreferenceRoutes.SupportedTimeZoneCatalogue, GetTimeZones)
            .RequireAuthorization()
            .WithName("GetSupportedTimeZones")
            .WithSummary("List supported IANA time zones");
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
            return PreferenceProblem(
                "Invalid language preference",
                "Language must be exactly 'en' or 'nb-NO'.",
                ProfilePreferenceErrorCodes.InvalidLanguage);
        }

        var preference = await preferences.UpdateAsync(issuer, subject, request!.Language, cancellationToken);
        return TypedResults.Ok(preference);
    }

    private static async Task<Results<Ok<DisplayPreferenceDTO>, ProblemHttpResult>> GetCurrentDisplayPreference(
        ClaimsPrincipal user,
        DisplayPreferenceService preferences,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(user, out var issuer, out var subject))
        {
            return InvalidIdentity<DisplayPreferenceDTO>();
        }

        return TypedResults.Ok(await preferences.GetAsync(issuer, subject, cancellationToken));
    }

    private static async Task<Results<Ok<DisplayPreferenceDTO>, ProblemHttpResult>> UpdateDisplayPreference(
        ClaimsPrincipal user,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateDisplayPreferenceRequest? request,
        DisplayPreferenceService preferences,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(user, out var issuer, out var subject))
        {
            return InvalidIdentity<DisplayPreferenceDTO>();
        }

        var problem = ValidateDisplayPreference(request);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            return TypedResults.Ok(await preferences.SetAsync(issuer, subject, request!, cancellationToken));
        }
        catch (DisplayPreferenceValidationException exception)
        {
            return PreferenceProblem("Invalid display preference", exception.Message, exception.ErrorCode);
        }
    }

    private static Ok<IReadOnlyList<TimeZoneOptionDTO>> GetTimeZones() => TypedResults.Ok(TimeZones);

    private static ProblemHttpResult? ValidateDisplayPreference(UpdateDisplayPreferenceRequest? request)
    {
        if (request is null || request.DistanceUnit is not (ProfileDisplayPreferenceCodes.Metres or ProfileDisplayPreferenceCodes.Feet))
        {
            return PreferenceProblem("Invalid distance unit", "Distance unit must be 'm' or 'ft'.", ProfilePreferenceErrorCodes.InvalidDistanceUnit);
        }

        if (request.SpeedUnit is not (ProfileDisplayPreferenceCodes.KilometresPerHour or ProfileDisplayPreferenceCodes.MilesPerHour or ProfileDisplayPreferenceCodes.MetresPerSecond))
        {
            return PreferenceProblem("Invalid speed unit", "Speed unit must be 'km/h', 'mph', or 'm/s'.", ProfilePreferenceErrorCodes.InvalidSpeedUnit);
        }

        if (request.TimeMode is not (ProfileDisplayPreferenceCodes.BrowserTime or ProfileDisplayPreferenceCodes.UtcTime or ProfileDisplayPreferenceCodes.NamedTimeZone))
        {
            return PreferenceProblem("Invalid time mode", "Time mode must be 'browser', 'utc', or 'timeZone'.", ProfilePreferenceErrorCodes.InvalidTimeMode);
        }

        if (request.TimeMode == ProfileDisplayPreferenceCodes.NamedTimeZone)
        {
            if (string.IsNullOrWhiteSpace(request.TimeZoneId))
            {
                return PreferenceProblem("Time zone required", "A time-zone ID is required for timeZone mode.", ProfilePreferenceErrorCodes.TimeZoneRequired);
            }
            if (!TimeZoneIds.Contains(request.TimeZoneId))
            {
                return PreferenceProblem("Invalid time zone", "Time-zone ID must be a supported IANA identifier.", ProfilePreferenceErrorCodes.InvalidTimeZone);
            }
        }
        else if (request.TimeZoneId is not null)
        {
            return PreferenceProblem("Time zone not allowed", "A time-zone ID is only allowed for timeZone mode.", ProfilePreferenceErrorCodes.TimeZoneNotAllowed);
        }

        return null;
    }

    private static IReadOnlyList<TimeZoneOptionDTO> CreateTimeZoneCatalogue() => TimeZoneInfo.GetSystemTimeZones()
        .Select(zone =>
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(zone.Id, out _))
            {
                return (Id: zone.Id, Zone: zone);
            }

            return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var ianaId)
                ? (Id: ianaId, Zone: zone)
                : (Id: null, Zone: zone);
        })
        .Where(item => !string.IsNullOrWhiteSpace(item.Id))
        .GroupBy(item => item.Id!, StringComparer.Ordinal)
        .Select(group => group.OrderBy(item => item.Zone.Id, StringComparer.Ordinal).First())
        .Select(item => new TimeZoneOptionDTO(item.Id!, $"{item.Id} ({item.Zone.DisplayName})"))
        .OrderBy(zone => zone.Id, StringComparer.Ordinal)
        .ToArray();

    private static bool TryGetIdentity(ClaimsPrincipal user, out string issuer, out string subject)
    {
        issuer = user.FindFirstValue("iss") ?? string.Empty;
        subject = user.FindFirstValue("sub") ?? string.Empty;
        return !string.IsNullOrWhiteSpace(issuer) && !string.IsNullOrWhiteSpace(subject);
    }

    private static ProblemHttpResult InvalidIdentity() =>
        InvalidIdentity<object>();

    private static ProblemHttpResult InvalidIdentity<T>() => PreferenceProblem(
        "Invalid authenticated identity",
        "The validated identity must include non-blank issuer and subject claims.",
        ProfilePreferenceErrorCodes.InvalidIdentity,
        StatusCodes.Status403Forbidden);

    private static ProblemHttpResult PreferenceProblem(string title, string detail, string code, int statusCode = StatusCodes.Status400BadRequest) =>
        TypedResults.Problem(statusCode: statusCode, title: title, detail: detail, extensions: ErrorExtension(code));

    private static Dictionary<string, object?> ErrorExtension(string code) =>
        new() { ["code"] = code };
}
