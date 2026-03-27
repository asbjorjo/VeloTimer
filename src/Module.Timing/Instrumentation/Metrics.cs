using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace VeloTime.Module.Timing;

public class Metrics
{
    private readonly Counter<int> _passingProcessed;
    private readonly Histogram<double> _passingProcessingTime;
    private readonly UpDownCounter<int> _timingPointCacheHits;

    public Metrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("VeloTime.Module.Timing");
        _passingProcessed = meter.CreateCounter<int>("velotime.module.timing.passing_processed", description: "Number of passing events processed");
        _passingProcessingTime = meter.CreateHistogram<double>("velotime.module.timing.passing_processing_time", description: "Processing time for passing events in seconds");
        _timingPointCacheHits = meter.CreateUpDownCounter<int>("velotime.module.timing.timing_point_cache_hits", description: "Number of timing point cache hits");
    }

    public void TimingLoopCacheHit(bool hit = true)
    {
        _timingPointCacheHits.Add(hit ? 1 : -1);
    }

    public void PassingProcessed(string Agent, Activity? activity, bool duplicate = false)
    {
        _passingProcessed.Add(1,
            new KeyValuePair<string, object?>("agent", Agent),
            new KeyValuePair<string, object?>("duplicate", duplicate));
        if (activity == null) return;
        _passingProcessingTime.Record((DateTime.UtcNow - activity.StartTimeUtc).TotalMilliseconds);
    }
}