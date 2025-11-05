using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Mapping;

public static class TimingSystemMapper
{
    public static TimingSystemDto ToDto(this TimingSystem timingSystem)
    {
        return new TimingSystemDto
        {
            Id = timingSystem.Id,
            Name = timingSystem.Name,
        };
    }

    public static TimingSystem ToModel(this TimingSystemDto timingSystemDto)
    {
        return new TimingSystem
        {
            Id = timingSystemDto.Id,
            Name = timingSystemDto.Name,
        };
    }
}
