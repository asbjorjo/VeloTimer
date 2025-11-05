using VeloTime.Module.Timing.Interface.Data;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Mapping;

public static class TimingPointMapper
{
    public static TimingPointDto ToDto(this TimingPoint timingPoint)
    {
        return new TimingPointDto
        {
            Id = timingPoint.Id,
            Description = timingPoint.Description,
            SystemId = timingPoint.SystemId
        };
    }

    public static TimingPoint ToModel(this TimingPointDto timingPointDto)
    {
        return new TimingPoint
        {
            Id = timingPointDto.Id,
            Description = timingPointDto.Description,
            SystemId = timingPointDto.SystemId
        };
    }
}
