namespace VeloTime.Module.Timing.Model
{
    public class Transponder
    {
        public required Guid Id { get; init; }
        public required TransponderType Type { get; init; }
        public required string SystemId { get; init; }
    }
}