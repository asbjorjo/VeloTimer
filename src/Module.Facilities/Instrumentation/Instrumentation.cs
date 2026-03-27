using System.Diagnostics;

namespace VeloTime.Module.Facilities;

internal class Instrumentation
{
    public static readonly ActivitySource Source = new("VeloTime.Module.Facilities");
}
