using VeloTime.Agent.Interface.Messages;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Mapping;

public static class InstallationMapper
{
    public static InstallationDTO ToDto(this Installation installation)
    {
        return new InstallationDTO
        {
            Id = installation.Id,
            FacilityId = installation.Facility,
            AgentId = installation.AgentId,
            LastSeen = installation.LastSeen,
            TimingPoints = [.. installation.TimingPoints.Select(tp => tp.ToDto())],
            TimingSystem = installation.TimingSystem.ToDto(),
            Description = installation.Description,
            Capabilities = installation.ToCapabilitiesDto()
        };
    }

    /// <summary>The latest agent capability report, or null when the agent never sent one.</summary>
    public static AgentCapabilitiesDTO? ToCapabilitiesDto(this Installation installation) =>
        installation.CapabilitiesReportedAt is not { } reportedAt ? null : new AgentCapabilitiesDTO
        {
            ReportedAtUtc = reportedAt,
            AgentType = installation.AgentType ?? string.Empty,
            AgentVersion = installation.AgentVersion ?? string.Empty,
            ControlProtocolVersion = installation.ControlProtocolVersion ?? 0,
            Capabilities = [.. installation.AgentCapabilities.Select(capability => new AgentCapabilityDTO
            {
                Name = capability.Name,
                PassingReplay = capability.PassingReplay is not { } replay ? null : new PassingReplayCapabilityDTO
                {
                    Mode = replay.Mode,
                    OldestAvailableUtc = replay.OldestAvailableUtc,
                    MaxRangeSeconds = replay.MaxRangeSeconds,
                    SupportsLoopFilter = replay.SupportsLoopFilter
                }
            })]
        };

    public static Installation ToModel(this InstallationDTO installationDto)
    {
        return new Installation
        {
            Id = installationDto.Id,
            Facility = installationDto.FacilityId,
            AgentId = installationDto.AgentId,
            LastSeen = installationDto.LastSeen,
            TimingPoints = [.. installationDto.TimingPoints.Select(tp => tp.ToModel())],
            TimingSystem = installationDto.TimingSystem.ToModel(),
            Description = installationDto.Description
        };
    }
}
