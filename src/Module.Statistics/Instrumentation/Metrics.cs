using System.Diagnostics.Metrics;

namespace VeloTime.Module.Statistics;

public class Metrics
{
    private Counter<int> _timingSamplesProcessed;
    private Histogram<double> _timingSampleProcessingTime;
    private Counter<int> _samplesCompleted;
    private Histogram<double> _samplesCompleteProcessingTime;

    public Metrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("VeloTime.Module.Statistics");
        _samplesCompleted = meter.CreateCounter<int>("velotime.module.statistics.samples_completed", description: "Number of samples completed");
        _samplesCompleteProcessingTime = meter.CreateHistogram<double>("velotime.module.statistics.sample_complete_processing_time", description: "Processing time for completed samples in seconds");
        _timingSamplesProcessed = meter.CreateCounter<int>("velotime.module.statistics.timing_samples_processed", description: "Number of timing samples processed");
        _timingSampleProcessingTime = meter.CreateHistogram<double>("velotime.module.statistics.timing_sample_processing_time", description: "Processing time for timing samples in seconds");
    }

    public void SampleCompleted(double time)
    {
        _samplesCompleted.Add(1);
        _samplesCompleteProcessingTime.Record(time);
    }

    public void TimingSampleProcessed(double time)
    {
        _timingSamplesProcessed.Add(1);
        _timingSampleProcessingTime.Record(time);
    }
}