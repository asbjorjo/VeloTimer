using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace VeloTime.Agent;

public class Metrics
{
    private readonly Counter<int> _passingsProcessed;
    private readonly Histogram<double> _passingProcessingTime;

    public Metrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("VeloTime.Agent");

        _passingsProcessed = meter.CreateCounter<int>(
            name: "velotime.agent.passings_sent",
            unit: "passings",
            description: "Number of processed passings from the system");
        _passingProcessingTime = meter.CreateHistogram<double>(
            name: "velotime.agent.passings_sent_time",
            description: "Time taken to process a passing from the system");
    }
    public void ProcessedPassing(int count, Activity? activity = default)
    {
        _passingsProcessed.Add(count);
        if (activity == null) return;
        _passingProcessingTime.Record((DateTime.UtcNow - activity.StartTimeUtc).TotalMilliseconds);
    }
}