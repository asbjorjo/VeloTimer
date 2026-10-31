using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Profiles.Interface;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Profiles.Storage;

namespace VeloTime.Module.Profiles.Service;

/// <summary>Persists display preferences for an identity from a validated authentication token.</summary>
/// <remarks>
/// Issuer and subject are matched exactly, without trimming or case folding. A missing row is
/// represented by defaults and is not inserted until SetAsync is called.
/// </remarks>
public sealed class DisplayPreferenceService(ProfilesDbContext storage)
{
    private static readonly HashSet<string> SupportedIanaTimeZoneIds = TimeZoneInfo.GetSystemTimeZones()
        .Select(zone => TimeZoneInfo.TryConvertIanaIdToWindowsId(zone.Id, out _)
            ? zone.Id
            : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var ianaId) ? ianaId : null)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id!)
        .ToHashSet(StringComparer.Ordinal);

    public async Task<DisplayPreferenceDTO> GetAsync(
        string issuer, string subject, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(issuer, subject);

        var preference = await storage.DisplayPreferences.AsNoTracking()
            .Where(item => item.Issuer == issuer && item.Subject == subject)
            .Select(item => new DisplayPreferenceDTO(
                item.DistanceUnit, item.SpeedUnit, item.TimeMode, item.TimeZoneId))
            .SingleOrDefaultAsync(cancellationToken);

        return preference ?? new DisplayPreferenceDTO(
            ProfileDisplayPreferenceCodes.DefaultDistanceUnit,
            ProfileDisplayPreferenceCodes.DefaultSpeedUnit,
            ProfileDisplayPreferenceCodes.DefaultTimeMode,
            null);
    }

    public async Task<DisplayPreferenceDTO> SetAsync(
        string issuer,
        string subject,
        UpdateDisplayPreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(issuer, subject);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        // PostgreSQL arbitrates concurrent first writes using the composite primary key.
        // All interpolated values are SQL parameters; never read before insert or retry a unique violation.
        await storage.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO profiles.display_preference
            (issuer, subject, distance_unit, speed_unit, time_mode, time_zone_id)
        VALUES
            ({issuer}, {subject}, {request.DistanceUnit}, {request.SpeedUnit}, {request.TimeMode}, {request.TimeZoneId})
        ON CONFLICT (issuer, subject) DO UPDATE SET
            distance_unit = EXCLUDED.distance_unit,
            speed_unit = EXCLUDED.speed_unit,
            time_mode = EXCLUDED.time_mode,
            time_zone_id = EXCLUDED.time_zone_id", cancellationToken);

        // Return this operation's submitted value rather than rereading a possible later concurrent write.
        return new DisplayPreferenceDTO(
            request.DistanceUnit, request.SpeedUnit, request.TimeMode, request.TimeZoneId);
    }

    private static void ValidateRequest(UpdateDisplayPreferenceRequest request)
    {
        if (request.DistanceUnit is not (ProfileDisplayPreferenceCodes.Metres or ProfileDisplayPreferenceCodes.Feet))
        {
            throw Failure("Distance unit is not supported.", ProfilePreferenceErrorCodes.InvalidDistanceUnit,
                nameof(request.DistanceUnit));
        }

        if (request.SpeedUnit is not (
            ProfileDisplayPreferenceCodes.KilometresPerHour or
            ProfileDisplayPreferenceCodes.MilesPerHour or
            ProfileDisplayPreferenceCodes.MetresPerSecond))
        {
            throw Failure("Speed unit is not supported.", ProfilePreferenceErrorCodes.InvalidSpeedUnit,
                nameof(request.SpeedUnit));
        }

        if (request.TimeMode is not (
            ProfileDisplayPreferenceCodes.BrowserTime or
            ProfileDisplayPreferenceCodes.UtcTime or
            ProfileDisplayPreferenceCodes.NamedTimeZone))
        {
            throw Failure("Time mode is not supported.", ProfilePreferenceErrorCodes.InvalidTimeMode,
                nameof(request.TimeMode));
        }

        if (request.TimeMode == ProfileDisplayPreferenceCodes.NamedTimeZone)
        {
            if (string.IsNullOrWhiteSpace(request.TimeZoneId))
            {
                throw Failure("A time-zone ID is required for timeZone mode.",
                    ProfilePreferenceErrorCodes.TimeZoneRequired, nameof(request.TimeZoneId));
            }

            if (!SupportedIanaTimeZoneIds.Contains(request.TimeZoneId))
            {
                throw Failure("Time-zone ID must exactly match an IANA zone exposed by the installed .NET time-zone data.",
                    ProfilePreferenceErrorCodes.InvalidTimeZone, nameof(request.TimeZoneId));
            }
        }
        else if (request.TimeZoneId is not null)
        {
            throw Failure("A time-zone ID is not allowed unless time mode is timeZone.",
                ProfilePreferenceErrorCodes.TimeZoneNotAllowed, nameof(request.TimeZoneId));
        }
    }

    private static void ValidateIdentity(string issuer, string subject)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw Failure("Issuer must not be blank.", ProfilePreferenceErrorCodes.InvalidIdentity, nameof(issuer));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw Failure("Subject must not be blank.", ProfilePreferenceErrorCodes.InvalidIdentity, nameof(subject));
        }
    }

    private static DisplayPreferenceValidationException Failure(string message, string code, string parameter) =>
        new(message, code, parameter);
}
