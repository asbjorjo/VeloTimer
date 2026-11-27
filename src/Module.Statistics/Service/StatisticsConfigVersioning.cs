using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Service.Recompute;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>
/// Keeps each configuration's dated <see cref="StatisticsConfigVersion"/> history (plan §4.1–4.2, C3).
/// Definitions are edited from an effective instant onward: the version spanning it is closed there, and that
/// version and every later one are replaced by copies carrying the changed values. Values the edit does not
/// change keep whatever each later version had, and history before the instant is never touched. Every edit
/// marks the results from its instant dirty, so a backdated edit recomputes exactly the affected history.
/// Callers add to the caller's context and save in the same transaction as the definition change.
/// </summary>
public static class StatisticsConfigVersioning
{
    /// <summary>The current values of <paramref name="config"/> and its item, as a transient version.</summary>
    public static StatisticsConfigVersion Snapshot(StatisticsItem item, StatisticsItemConfig config) => new()
    {
        StatisticsItemConfigId = config.Id,
        StatisticsItemId = item.Id,
        Distance = item.Distance,
        DistanceMode = item.DistanceMode,
        ItemMinDuration = item.MinDuration,
        ItemMaxDuration = item.MaxDuration,
        MinDuration = config.MinDuration,
        MaxDuration = config.MaxDuration,
        CoursePointStart = (config as SimpleStatisticsItemConfig)?.CoursePointStart ?? Guid.Empty,
        CoursePointEnd = (config as SimpleStatisticsItemConfig)?.CoursePointEnd ?? Guid.Empty,
        RequiredCourseLayoutId = (config as SimpleStatisticsItemConfig)?.RequiredCourseLayoutId,
        RequiredCoursePointIds = (config as SimpleStatisticsItemConfig)?.RequiredCoursePointIds.ToArray() ?? [],
        ParentConfigId = (config as MultiStatisticsItemConfig)?.ParentConfigId,
        Repetitions = (config as MultiStatisticsItemConfig)?.Repetitions
    };

    /// <summary>
    /// Starts a new configuration's history at <paramref name="validFrom"/> (null: it counts all recorded
    /// history) with its current values, and marks that range dirty.
    /// </summary>
    public static StatisticsConfigVersion Create(StatisticsDbContext storage, StatisticsItem item, StatisticsItemConfig config,
        DateTime? validFrom, DateTime now)
    {
        var version = Initial(item, config, validFrom, now);
        storage.Add(version);
        StatisticsRecomputeScheduler.All(storage, RecomputeReason.Definition, config.Id, version.ValidFromUtc);
        return version;
    }

    /// <summary>A configuration's first version, open-ended from <paramref name="validFrom"/>; not yet added.</summary>
    public static StatisticsConfigVersion Initial(StatisticsItem item, StatisticsItemConfig config, DateTime? validFrom, DateTime now) =>
        Copy(Snapshot(item, config), Utc(validFrom), null, now);

    /// <summary>
    /// Applies the values that differ between <paramref name="before"/> and <paramref name="after"/> (snapshots
    /// of the same configuration) to its history from <paramref name="from"/> (null: all of it) onward, and marks
    /// that range dirty. Returns false, changing nothing, when no value differs.
    /// </summary>
    public static async Task<bool> ApplyFromAsync(StatisticsDbContext storage, StatisticsConfigVersion before,
        StatisticsConfigVersion after, DateTime? from, DateTime now, CancellationToken cancellationToken)
    {
        if (before.StatisticsItemConfigId != after.StatisticsItemConfigId)
            throw new ArgumentException("Both snapshots must describe the same configuration.", nameof(after));
        if (SameValues(before, after)) return false;
        from = Utc(from);
        var versions = await storage.Set<StatisticsConfigVersion>()
            .Where(v => v.StatisticsItemConfigId == after.StatisticsItemConfigId).ToListAsync(cancellationToken);
        foreach (var version in versions.Where(v => v.Overlaps(from, null)))
        {
            var patched = Patch(version, before, after);
            if (from is { } start && (version.ValidFromUtc is not { } begin || begin < start))
            {
                // Close the version spanning the instant; its remainder continues with the new values.
                var end = version.ValidToUtc;
                version.ValidToUtc = start;
                storage.Add(Copy(patched, start, end, now));
            }
            else
            {
                storage.Remove(version);
                storage.Add(Copy(patched, version.ValidFromUtc, version.ValidToUtc, now));
            }
        }
        StatisticsRecomputeScheduler.All(storage, RecomputeReason.Definition, after.StatisticsItemConfigId, from);
        return true;
    }

    /// <summary>
    /// Applies an item edit to every configuration of the item from <paramref name="from"/> onward.
    /// <paramref name="before"/> is the item as it was; the tracked <paramref name="item"/> holds the new values.
    /// </summary>
    public static async Task<bool> ApplyItemFromAsync(StatisticsDbContext storage, StatisticsItem before, StatisticsItem item,
        DateTime? from, DateTime now, CancellationToken cancellationToken)
    {
        var configs = await storage.Set<StatisticsItemConfig>().Where(c => c.StatisticsItemId == item.Id).ToListAsync(cancellationToken);
        var changed = false;
        foreach (var config in configs)
            changed |= await ApplyFromAsync(storage, Snapshot(before, config), Snapshot(item, config), from, now, cancellationToken);
        return changed;
    }

    /// <summary>A detached copy of an item's current values, for <see cref="ApplyItemFromAsync"/>.</summary>
    public static StatisticsItem Detach(StatisticsItem item) => new()
    {
        Id = item.Id, Name = item.Name, Description = item.Description, Distance = item.Distance,
        DistanceMode = item.DistanceMode, MinDuration = item.MinDuration, MaxDuration = item.MaxDuration
    };

    /// <summary>The version as a transient Simple configuration with its item, for derivation.</summary>
    public static SimpleStatisticsItemConfig ToSimpleConfig(this StatisticsConfigVersion version) => new()
    {
        Id = version.StatisticsItemConfigId,
        StatisticsItemId = version.StatisticsItemId,
        StatisticsItem = Item(version),
        MinDuration = version.MinDuration,
        MaxDuration = version.MaxDuration,
        CoursePointStart = version.CoursePointStart,
        CoursePointEnd = version.CoursePointEnd,
        RequiredCourseLayoutId = version.RequiredCourseLayoutId,
        RequiredCoursePointIds = version.RequiredCoursePointIds
    };

    /// <summary>The version as a transient Multi configuration with its item, for derivation.</summary>
    public static MultiStatisticsItemConfig ToMultiConfig(this StatisticsConfigVersion version) => new()
    {
        Id = version.StatisticsItemConfigId,
        StatisticsItemId = version.StatisticsItemId,
        StatisticsItem = Item(version),
        MinDuration = version.MinDuration,
        MaxDuration = version.MaxDuration,
        ParentConfig = null!,
        ParentConfigId = version.ParentConfigId ?? Guid.Empty,
        Repetitions = version.Repetitions ?? 0
    };

    public static StatisticsDurationBounds Bounds(this StatisticsConfigVersion version) =>
        StatisticsDurationBounds.Effective(version.ItemMinDuration, version.ItemMaxDuration, version.MinDuration, version.MaxDuration);

    private static StatisticsItem Item(StatisticsConfigVersion version) => new()
    {
        Id = version.StatisticsItemId,
        Distance = version.Distance,
        DistanceMode = version.DistanceMode,
        MinDuration = version.ItemMinDuration,
        MaxDuration = version.ItemMaxDuration
    };

    private static bool SameValues(StatisticsConfigVersion a, StatisticsConfigVersion b) =>
        a.Distance.Equals(b.Distance) && a.DistanceMode == b.DistanceMode && a.ItemMinDuration == b.ItemMinDuration
        && a.ItemMaxDuration == b.ItemMaxDuration && a.MinDuration == b.MinDuration && a.MaxDuration == b.MaxDuration
        && a.CoursePointStart == b.CoursePointStart && a.CoursePointEnd == b.CoursePointEnd
        && a.RequiredCourseLayoutId == b.RequiredCourseLayoutId && a.RequiredCoursePointIds.SequenceEqual(b.RequiredCoursePointIds)
        && a.ParentConfigId == b.ParentConfigId && a.Repetitions == b.Repetitions && a.StatisticsItemId == b.StatisticsItemId;

    /// <summary><paramref name="version"/> with every value that differs between before and after set to after's.</summary>
    private static StatisticsConfigVersion Patch(StatisticsConfigVersion version, StatisticsConfigVersion before, StatisticsConfigVersion after)
    {
        T Pick<T>(T current, T was, T now) => EqualityComparer<T>.Default.Equals(was, now) ? current : now;
        var points = before.RequiredCourseLayoutId == after.RequiredCourseLayoutId
            && before.RequiredCoursePointIds.SequenceEqual(after.RequiredCoursePointIds);
        return new StatisticsConfigVersion
        {
            StatisticsItemConfigId = version.StatisticsItemConfigId,
            StatisticsItemId = Pick(version.StatisticsItemId, before.StatisticsItemId, after.StatisticsItemId),
            Distance = Pick(version.Distance, before.Distance, after.Distance),
            DistanceMode = Pick(version.DistanceMode, before.DistanceMode, after.DistanceMode),
            ItemMinDuration = Pick(version.ItemMinDuration, before.ItemMinDuration, after.ItemMinDuration),
            ItemMaxDuration = Pick(version.ItemMaxDuration, before.ItemMaxDuration, after.ItemMaxDuration),
            MinDuration = Pick(version.MinDuration, before.MinDuration, after.MinDuration),
            MaxDuration = Pick(version.MaxDuration, before.MaxDuration, after.MaxDuration),
            CoursePointStart = Pick(version.CoursePointStart, before.CoursePointStart, after.CoursePointStart),
            CoursePointEnd = Pick(version.CoursePointEnd, before.CoursePointEnd, after.CoursePointEnd),
            // Required checkpoints and their layout are validated together, so they change together.
            RequiredCourseLayoutId = points ? version.RequiredCourseLayoutId : after.RequiredCourseLayoutId,
            RequiredCoursePointIds = points ? version.RequiredCoursePointIds : after.RequiredCoursePointIds.ToArray(),
            ParentConfigId = Pick(version.ParentConfigId, before.ParentConfigId, after.ParentConfigId),
            Repetitions = Pick(version.Repetitions, before.Repetitions, after.Repetitions)
        };
    }

    private static StatisticsConfigVersion Copy(StatisticsConfigVersion values, DateTime? from, DateTime? to, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        StatisticsItemConfigId = values.StatisticsItemConfigId,
        StatisticsItemId = values.StatisticsItemId,
        ValidFromUtc = from,
        ValidToUtc = to,
        CreatedAtUtc = now,
        Distance = values.Distance,
        DistanceMode = values.DistanceMode,
        ItemMinDuration = values.ItemMinDuration,
        ItemMaxDuration = values.ItemMaxDuration,
        MinDuration = values.MinDuration,
        MaxDuration = values.MaxDuration,
        CoursePointStart = values.CoursePointStart,
        CoursePointEnd = values.CoursePointEnd,
        RequiredCourseLayoutId = values.RequiredCourseLayoutId,
        RequiredCoursePointIds = values.RequiredCoursePointIds.ToArray(),
        ParentConfigId = values.ParentConfigId,
        Repetitions = values.Repetitions
    };

    private static DateTime? Utc(DateTime? value) =>
        value is { } time ? DateTime.SpecifyKind(time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time, DateTimeKind.Utc) : null;
}
