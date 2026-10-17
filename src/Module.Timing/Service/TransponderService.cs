using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

public class TransponderService(TimingDbContext storage, HybridCache cache)
{
    public async Task<IEnumerable<Transponder>> ListAllTransponders(CancellationToken cancellationToken = default)
    {
        return await storage.Set<Transponder>().AsNoTracking().ToListAsync(cancellationToken: cancellationToken);
    }

    public async Task<Transponder?> GetTransponderById(Guid transponderId, CancellationToken cancellationToken = default)
    {
        return await storage.Set<Transponder>().Include(t => t.Owners).AsNoTracking().FirstOrDefaultAsync(t => t.Id == transponderId, cancellationToken: cancellationToken);
    }

    public async Task Update(Transponder transponder)
    {
        storage.Set<Transponder>().Update(transponder);
        await storage.SaveChangesAsync();
    }
}
