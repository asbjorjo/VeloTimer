using System.Diagnostics;

namespace VeloTime.Module.Statistics;

internal class Instrumentation
{
    public static readonly ActivitySource Source = new("VeloTime.Facilities.Statistics");
}
