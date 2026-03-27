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
            AgentId = installation.AgentId,
            TimingPoints = [.. installation.TimingPoints.Select(tp => tp.ToDto())],
            TimingSystem = installation.TimingSystem.ToDto(),
            Description = installation.Description
        };
    }

    public static Installation ToModel(this InstallationDTO installationDto)
    {
        return new Installation
        {
            Id = installationDto.Id,
            AgentId = installationDto.AgentId,
            TimingPoints = [.. installationDto.TimingPoints.Select(tp => tp.ToModel())],
            TimingSystem = installationDto.TimingSystem.ToModel(),
            Description = installationDto.Description
        };
    }
}
