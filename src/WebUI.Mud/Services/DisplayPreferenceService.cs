using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;
using Generated = VeloTime.Module.Profiles.Client;

namespace VeloTime.WebUI.Mud.Services;

/// <summary>Uses the delegated Profiles client for server-interactive preference requests.</summary>
public sealed class DisplayPreferenceService(VeloTime.Module.Profiles.Client.IProfilesClient profiles) : IDisplayPreferenceService
{
    public async Task<DisplayPreferenceDTO> GetAsync(CancellationToken cancellationToken = default) =>
        Map(await profiles.GetCurrentDisplayPreferenceAsync(cancellationToken));

    public async Task<DisplayPreferenceDTO> SaveAsync(UpdateDisplayPreferenceRequest request,
        CancellationToken cancellationToken = default) =>
        Map(await profiles.UpdateDisplayPreferenceAsync(new Generated.UpdateDisplayPreferenceRequest
        {
            DistanceUnit = request.DistanceUnit,
            SpeedUnit = request.SpeedUnit,
            TimeMode = request.TimeMode,
            TimeZoneId = request.TimeZoneId
        }, cancellationToken));

    public async Task<IReadOnlyList<TimeZoneOptionDTO>> GetTimeZonesAsync(CancellationToken cancellationToken = default) =>
        (await profiles.GetSupportedTimeZonesAsync(cancellationToken))
            .Select(zone => new TimeZoneOptionDTO(zone.Id, zone.DisplayName)).ToArray();

    private static DisplayPreferenceDTO Map(Generated.DisplayPreferenceDTO preference) =>
        new(preference.DistanceUnit, preference.SpeedUnit, preference.TimeMode, preference.TimeZoneId);
}
