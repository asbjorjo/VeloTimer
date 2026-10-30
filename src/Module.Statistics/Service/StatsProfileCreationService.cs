using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;

namespace VeloTime.Module.Statistics.Service;

/// <summary>Creates missing profiles without overwriting existing preferences.</summary>
/// <param name="storage">The Statistics-owned database context.</param>
public sealed class StatsProfileCreationService(StatisticsDbContext storage)
{
    /// <summary>Serializes profile creation with other API requests and Statistics processors.</summary>
    /// <param name="userId">The destination user's identity.</param>
    /// <param name="profileName">The name to use only when creating a profile.</param>
    /// <param name="isPublic">The visibility to use only when creating a profile.</param>
    /// <param name="cancellationToken">Cancels database operations.</param>
    /// <returns>The saved profile and whether this operation created it.</returns>
    public async Task<(StatsProfile Profile, bool Created)> EnsureAsync(
        Guid userId, string profileName, bool isPublic, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A valid user ID is required.", nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        await using var transaction = await storage.Database.BeginTransactionAsync(cancellationToken);
        if (storage.Database.IsNpgsql())
        {
            // This key must match StatisticsOwnershipService's processor-side profile lock.
            var key = $"statistics-profile:{userId:D}";
            await storage.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
        var profile = await storage.Set<StatsProfile>().AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
        var created = profile is null;
        if (profile is null)
        {
            profile = new StatsProfile { UserId = userId, ProfileName = profileName, IsPublic = isPublic };
            storage.Add(profile);
            await storage.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return (profile, created);
    }
}
