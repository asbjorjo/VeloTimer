namespace VeloTime.Timing.Contracts
{
    public record TrackPassing
    {
        public DateTimeOffset Time { get; init; }
        public string FieldSystem { get; init; } = string.Empty;
        public string Track { get; init; } = string.Empty;
        public string Transponder { get; init; } = string.Empty;
        public long PassingPoint { get; init; }
        public string SourceId { get; init; } = string.Empty;
        public bool LowBattery { get; init; }
        public bool LowSignal { get; init; }

    }
}
