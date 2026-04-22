namespace VeloTime.Module.Timing.Model;

public class TransponderOwner
{
    public Guid TransponderId { get; set; }
    public Guid OwnerId { get; set; }

    public Transponder Transponder { get; set; } = null!;
    public DateTime OwnedFrom { get; set; }
    public DateTime? OwnedTo { get; set; }
}
