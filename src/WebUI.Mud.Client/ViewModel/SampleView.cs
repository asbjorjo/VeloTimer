namespace VeloTime.WebUI.Mud.Client.ViewModel;

public class SampleView
{ 
    public DateTime Time { get; init; }
    public required string TransponderLabel { get; init; }
    public required string StartPoint { get; init; }
    public required string EndPoint { get; init; }
    public double Distance { get; init; }
    public TimeSpan Duration { get; init; }
    public double Speed { get; init; }
}
