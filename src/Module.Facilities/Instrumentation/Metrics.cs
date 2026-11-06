using System.Diagnostics.Metrics;

namespace VeloTime.Module.Facilities;

public class Metrics
{
    public Metrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("VeloTime.Module.Facilities");
    }
}