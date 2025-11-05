using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Mapping;

public static class InstallationMapper
{
    public static InstallationDto ToDto(this Installation installation)
    {
        return new InstallationDto
        {
            Id = installation.Id,
            Facility = installation.Facility,
            TimingPoints = [.. installation.TimingPoints.Select(tp => tp.ToDto())],
            TimingSystem = installation.TimingSystem.ToDto(),
            Description = installation.Description
        };
    }

    public static Installation ToModel(this InstallationDto installationDto)
    {
        return new Installation
        {
            Id = installationDto.Id,
            Facility = installationDto.Facility,
            TimingPoints = [.. installationDto.TimingPoints.Select(tp => tp.ToModel())],
            TimingSystem = installationDto.TimingSystem.ToModel(),
            Description = installationDto.Description
        };
    }
}
