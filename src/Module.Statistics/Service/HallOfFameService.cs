using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>Executes one item's all-time, per-profile aggregation and deterministic ranking in PostgreSQL.</summary>
public sealed class HallOfFameService(StatisticsDbContext storage)
{
    public async Task<IReadOnlyList<HallOfFameProfileResult>> GetRankingAsync(
        Guid statisticsItemId,
        bool rankByPassingCount,
        int rowCount,
        CancellationToken cancellationToken = default)
    {
        if (!storage.Database.IsNpgsql())
            throw new NotSupportedException("Hall of Fame ranking requires PostgreSQL.");

        return await storage.Database.SqlQuery<HallOfFameProfileResult>($"""
            WITH eligible AS (
                SELECT entry.id,
                       entry.stats_profile_user_id AS profile_id,
                       profile.profile_name,
                       entry.duration,
                       entry.speed,
                       entry.facility_id,
                       entry.time_end
                FROM statistics.statistics_entry AS entry
                INNER JOIN statistics.stats_profile AS profile
                    ON profile.user_id = entry.stats_profile_user_id
                WHERE entry.statistics_item_id = {statisticsItemId}
                  AND entry.stats_profile_user_id IS NOT NULL
                  AND profile.is_public = TRUE
            ),
            passing_counts AS (
                SELECT profile_id, profile_name, COUNT(*)::bigint AS passing_count
                FROM eligible
                GROUP BY profile_id, profile_name
            ),
            best_results AS (
                SELECT DISTINCT ON (profile_id)
                       profile_id, id AS entry_id, duration AS best_duration,
                       speed AS best_speed, facility_id AS best_facility_id,
                       time_end AS best_time_end
                FROM eligible
                ORDER BY profile_id, duration ASC, time_end ASC, facility_id ASC, id ASC
            ),
            ranked AS (
                SELECT passing_counts.profile_id,
                       passing_counts.profile_name,
                       passing_counts.passing_count,
                       best_results.entry_id,
                       best_results.best_duration,
                       best_results.best_speed,
                       best_results.best_facility_id,
                       best_results.best_time_end,
                       RANK() OVER (
                           ORDER BY
                               CASE WHEN {rankByPassingCount} THEN passing_counts.passing_count END DESC,
                               CASE WHEN NOT {rankByPassingCount} THEN best_results.best_duration END ASC
                       ) AS rank,
                       ROW_NUMBER() OVER (
                           ORDER BY
                               CASE WHEN {rankByPassingCount} THEN passing_counts.passing_count END DESC,
                               CASE WHEN NOT {rankByPassingCount} THEN best_results.best_duration END ASC,
                               passing_counts.profile_name COLLATE "C" ASC,
                               passing_counts.profile_id ASC
                       ) AS row_number
                FROM passing_counts
                INNER JOIN best_results USING (profile_id)
            )
            SELECT profile_id,
                   profile_name,
                   passing_count,
                   entry_id,
                   best_duration,
                   best_speed,
                   best_facility_id,
                   best_time_end,
                   rank
            FROM ranked
            WHERE row_number <= {rowCount}
            ORDER BY row_number
            """).ToListAsync(cancellationToken);
    }
}

/// <summary>Internal query projection. Profile and entry identifiers are never mapped to public DTOs.</summary>
public sealed class HallOfFameProfileResult
{
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public long PassingCount { get; set; }
    public Guid EntryId { get; set; }
    public TimeSpan BestDuration { get; set; }
    public double BestSpeed { get; set; }
    public Guid BestFacilityId { get; set; }
    public DateTime BestTimeEnd { get; set; }
    public long Rank { get; set; }
}
