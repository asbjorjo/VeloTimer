using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public class TransponderService(TimingDbContext storage, HybridCache cache, ILogger<TransponderService>? logger = null,
    ICacheInvalidationOperations? evictionOperations = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<TransponderService>.Instance;

    public async Task<IEnumerable<Transponder>> ListAllTransponders(CancellationToken cancellationToken = default)
    {
        return await storage.Set<Transponder>().AsNoTracking().ToListAsync(cancellationToken: cancellationToken);
    }

    public async Task<Transponder?> GetTransponderById(Guid transponderId, CancellationToken cancellationToken = default)
    {
        return await storage.Set<Transponder>().Include(t => t.Owners).FirstOrDefaultAsync(t => t.Id == transponderId, cancellationToken: cancellationToken);
    }

    public async Task Update(Transponder transponder, CancellationToken cancellationToken = default)
    {
        var oldIdentity = await storage.Set<Transponder>().AsNoTracking()
            .Where(item => item.Id == transponder.Id)
            .Select(item => (TimingTransponderCacheIdentity?)new TimingTransponderCacheIdentity(item.System, item.SystemId))
            .SingleOrDefaultAsync(cancellationToken);
        var newIdentity = new TimingTransponderCacheIdentity(transponder.System, transponder.SystemId);
        storage.Set<Transponder>().Update(transponder);
        await storage.SaveChangesAsync(cancellationToken);
        await TimingCacheInvalidation.InvalidateAsync(cache, _logger,
            oldIdentity is { } previous ? [previous.Key, newIdentity.Key] : [newIdentity.Key],
            oldIdentity is { } oldValue ? [oldValue.Tag, newIdentity.Tag] : [newIdentity.Tag], evictionOperations);
    }
}
