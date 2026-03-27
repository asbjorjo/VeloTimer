namespace VeloTime.Module.Timing.Model;

internal class RegisterPassingResponse
{
    public required Passing Passing { get; init; }
    public bool IsDuplicate { get; init; } = false;
}
