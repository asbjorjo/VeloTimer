using System.Diagnostics.Metrics;

namespace VeloTime.Module.Statistics;

public class Metrics
{
    private readonly Counter<long> _passingsRecorded;
    private readonly Counter<long> _recomputes;
    private readonly Histogram<double> _recomputeDuration;
    private readonly Counter<long> _recomputeUnresolved;
    private readonly Counter<long> _recomputeRejections;
    private readonly Counter<long> _recomputeRowsChanged;
    private readonly Counter<long> _recomputeFailures;
    private readonly Counter<long> _manualDecisionReviews;
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
        _passingsRecorded = meter.CreateCounter<long>("velotime.module.statistics.passings_recorded", description: "Passing facts received, by whether they were new");
        _recomputes = meter.CreateCounter<long>("velotime.module.statistics.recompute.runs", description: "Committed transponder recomputes");
        _recomputeDuration = meter.CreateHistogram<double>("velotime.module.statistics.recompute.duration", unit: "ms", description: "Duration of committed transponder recomputes");
        _recomputeUnresolved = meter.CreateCounter<long>("velotime.module.statistics.recompute.unresolved_passings", description: "Passing facts without a facility or course point at their time (plan §5.3)");
        _recomputeRejections = meter.CreateCounter<long>("velotime.module.statistics.recompute.rejections", description: "Sample or entry candidates rejected by derivation rules");
        _recomputeRowsChanged = meter.CreateCounter<long>("velotime.module.statistics.recompute.rows_changed", description: "Derived rows inserted, updated or deleted by recomputes");
        _recomputeFailures = meter.CreateCounter<long>("velotime.module.statistics.recompute.failures", description: "Failed (rolled back) recompute attempts");
        _manualDecisionReviews = meter.CreateCounter<long>("velotime.module.statistics.recompute.manual_decision_reviews", description: "Manual invalidations surfaced for review by a recompute");
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

    public void PassingRecorded(bool inserted) =>
        _passingsRecorded.Add(1, new KeyValuePair<string, object?>("inserted", inserted));

    public void RecomputeCommitted(double milliseconds, int unresolved, int rejections, int rowsChanged, int reviews)
    {
        _recomputes.Add(1);
        _recomputeDuration.Record(milliseconds);
        _recomputeUnresolved.Add(unresolved);
        _recomputeRejections.Add(rejections);
        _recomputeRowsChanged.Add(rowsChanged);
        _manualDecisionReviews.Add(reviews);
    }

    public void RecomputeFailed() => _recomputeFailures.Add(1);
}
