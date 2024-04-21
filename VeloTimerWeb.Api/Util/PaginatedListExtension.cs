using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Data;

namespace Microsoft.EntityFrameworkCore
{
    public static class TimeOffsetListExtension
    {
        public static async Task<PaginatedList<TSource>> ToPaginatedListAsync<TSource>(this IQueryable<TSource> source, int pageNumber, int pageSize)
        {
            var count = await source.CountAsync();
            var items = await source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
            return new PaginatedList<TSource>(items, count, pageNumber, pageSize);
        }
    }
}
