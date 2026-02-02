using VeloTime.Module.Timing.Model;

namespace VeloTime.Bootstrap;

public static class TimingData
{
    public static Installation Installation = new Installation { AgentId = "x2-sola-dev" };
    public static List<TimingPoint> timingPoints = new List<TimingPoint>
    {
        new TimingPoint{ Description = "Finish", SystemId = "0" },
        new TimingPoint{ Description = "200m", SystemId = "1" },
        new TimingPoint{ Description = "100m", SystemId = "2" },
        new TimingPoint{ Description = "Red", SystemId = "3" },
        new TimingPoint{ Description = "Green", SystemId = "4" }
    };
}
