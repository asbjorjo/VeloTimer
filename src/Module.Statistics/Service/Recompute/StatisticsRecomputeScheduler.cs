using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service.Recompute;

/// <summary>
/// Marks ranges dirty (plan §3.3). Adds the request to the caller's context so it commits in the same
/// transaction as the change that caused it; the caller saves.
/// </summary>
public static class StatisticsRecomputeScheduler
{
    /// <summary>One transponder over [from, to]; the worker expands it to whole sessions.</summary>
    public static RecomputeRequest Transponder(StatisticsDbContext storage, Guid transponderId, DateTime? from, DateTime? to,
        RecomputeReason reason, Guid? causationId = null) =>
        Add(storage, new RecomputeRequest
        {
            TransponderId = transponderId, FromUtc = from, ToUtc = to, Reason = reason, CausationId = causationId,
            CreatedAtUtc = DateTime.UtcNow
        });

    /// <summary>Every transponder with facts in [from, to] (null bounds are unbounded).</summary>
    public static RecomputeRequest All(StatisticsDbContext storage, RecomputeReason reason, Guid? causationId = null,
        DateTime? from = null, DateTime? to = null) =>
        Add(storage, new RecomputeRequest
        {
            FromUtc = from, ToUtc = to, Reason = reason, CausationId = causationId, CreatedAtUtc = DateTime.UtcNow
        });

    /// <summary>Every transponder with facts recorded at the installation in [from, to].</summary>
    public static RecomputeRequest Installation(StatisticsDbContext storage, Guid installationId, RecomputeReason reason,
        Guid? causationId = null, DateTime? from = null, DateTime? to = null) =>
        Add(storage, new RecomputeRequest
        {
            InstallationId = installationId, FromUtc = from, ToUtc = to, Reason = reason, CausationId = causationId,
            CreatedAtUtc = DateTime.UtcNow
        });

    private static RecomputeRequest Add(StatisticsDbContext storage, RecomputeRequest request)
    {
        storage.Add(request);
        return request;
    }
}
