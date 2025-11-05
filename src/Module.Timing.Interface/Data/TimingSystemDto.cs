namespace VeloTime.Module.Timing.Interface.Data;

public class TimingSystemDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
}
