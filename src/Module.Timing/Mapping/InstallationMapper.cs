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

public static class PassingReplayRequestMapper
{
    public static PassingReplayRequestDTO ToDto(this PassingReplayRequest replay) => new()
    {
        RequestId = replay.Id,
        InstallationId = replay.InstallationId,
        AgentId = replay.AgentId,
        Mode = replay.Mode,
        FromUtc = replay.FromUtc,
        ToUtc = replay.ToUtc,
        LoopIds = [.. replay.LoopIds],
        RequestedByUserId = replay.RequestedByUserId,
        Note = replay.Note,
        CreatedAt = replay.CreatedAt,
        ExpiresAt = replay.ExpiresAt,
        State = replay.State.ToString(),
        Reason = replay.Reason,
        StatusReportedAt = replay.StatusReportedAt,
        UpdatedAt = replay.UpdatedAt,
        Read = replay.Read,
        Published = replay.Published,
        NewInAgentStore = replay.NewInAgentStore,
        Recorded = replay.Recorded,
        Duplicate = replay.Duplicate,
        Conflict = replay.Conflict,
        Rejected = replay.Rejected
    };
}
