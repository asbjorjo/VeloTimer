using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Mapping;

public static class TimingSystemMapper
{
    public static TimingSystemDTO ToDto(this TimingSystem timingSystem)
    {
        return new TimingSystemDTO
        {
            Name = timingSystem.ToString(),
        };
    }

    public static TimingSystem ToModel(this TimingSystemDTO timingSystemDto)
    {
        return Enum.Parse<TimingSystem>(timingSystemDto.Name);
    }
}
