using System.Diagnostics;

namespace VeloTime.Agent;

internal class Instrumentation
{
    public static readonly ActivitySource Source = new("VeloTime.Agent", "2.0.0");
}
