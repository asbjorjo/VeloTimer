using System.Diagnostics.Metrics;

namespace VeloTime.Module.Statistics;

public class Metrics
{
    private Counter<int> _timingSamplesProcessed;
    private Histogram<double> _timingSampleProcessingTime;
    private Counter<int> _samplesCompleted;
    private Histogram<double> _samplesCompleteProcessingTime;
    private readonly Counter<long> _revalidationBatches;
    private readonly Counter<long> _revalidationEntriesScanned;
    private readonly Counter<long> _revalidationEntriesReclassified;
    private readonly Counter<long> _revalidationDependentItemsScheduled;
    private readonly Counter<long> _revalidationUnprovenLegacyLineage;
    private readonly Counter<long> _revalidationInconsistentLineage;
    private readonly Counter<long> _revalidationFailures;

    public Metrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("VeloTime.Module.Statistics");
        _samplesCompleted = meter.CreateCounter<int>("velotime.module.statistics.samples_completed", description: "Number of samples completed");
        _samplesCompleteProcessingTime = meter.CreateHistogram<double>("velotime.module.statistics.sample_complete_processing_time", description: "Processing time for completed samples in seconds");
        _timingSamplesProcessed = meter.CreateCounter<int>("velotime.module.statistics.timing_samples_processed", description: "Number of timing samples processed");
        _timingSampleProcessingTime = meter.CreateHistogram<double>("velotime.module.statistics.timing_sample_processing_time", description: "Processing time for timing samples in seconds");
        _revalidationBatches = meter.CreateCounter<long>("velotime.module.statistics.revalidation.batches", description: "Committed validity revalidation batches");
        _revalidationEntriesScanned = meter.CreateCounter<long>("velotime.module.statistics.revalidation.entries_scanned", description: "Entries scanned by committed revalidation batches");
        _revalidationEntriesReclassified = meter.CreateCounter<long>("velotime.module.statistics.revalidation.entries_reclassified", description: "Entries whose stored validity changed during revalidation");
        _revalidationDependentItemsScheduled = meter.CreateCounter<long>("velotime.module.statistics.revalidation.dependent_items_scheduled", description: "Dependent Multi item jobs scheduled by parent validity changes");
        _revalidationUnprovenLegacyLineage = meter.CreateCounter<long>("velotime.module.statistics.revalidation.unproven_legacy_lineage", description: "Legacy Multi entries classified InvalidContributor because contributors could not be proven");
        _revalidationInconsistentLineage = meter.CreateCounter<long>("velotime.module.statistics.revalidation.inconsistent_lineage", description: "Multi entries classified InvalidContributor because recorded lineage was inconsistent");
        _revalidationFailures = meter.CreateCounter<long>("velotime.module.statistics.revalidation.failures", description: "Failed (rolled back) revalidation attempts, by transient flag");
    }

    public void RevalidationBatchCommitted(int scanned, int reclassified, int dependentItemsScheduled, int unprovenLegacy, int inconsistentLineage)
    {
        _revalidationBatches.Add(1);
        _revalidationEntriesScanned.Add(scanned);
        _revalidationEntriesReclassified.Add(reclassified);
        _revalidationDependentItemsScheduled.Add(dependentItemsScheduled);
        _revalidationUnprovenLegacyLineage.Add(unprovenLegacy);
        _revalidationInconsistentLineage.Add(inconsistentLineage);
    }

    public void RevalidationFailed(bool transient) =>
        _revalidationFailures.Add(1, new KeyValuePair<string, object?>("transient", transient));

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