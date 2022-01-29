using VeloTimer.Shared.Data.Parameters;

namespace VeloTimer.Shared.Data
{
    public class PaginatedList<T> : List<T>
    {
        public Pagination Pagination { get; private set; }

        public PaginatedList(List<T> items, int count, int pageNumber, int pageSize)
        {
            Pagination = new Pagination
            {
                TotalCount = count,
                PageSize = pageSize,
                CurrentPage = pageNumber,
                TotalPages = (int)Math.Ceiling(count / (double)pageSize)
            };

            AddRange(items);
        }
    }
}
