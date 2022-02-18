using System.Linq.Dynamic.Core;
using System.Reflection;
using System.Text;

namespace System.Linq
{
    public static class SortExtension
    {
        public static IQueryable<T> ApplySort<T>(this IQueryable<T> query, string orderByQueryString)
        {
            if (!query.Any())
                return query;
            if (string.IsNullOrWhiteSpace(orderByQueryString))
                return query;

            var orderParts = orderByQueryString.Trim().Split(',');
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var orderQueryString = new StringBuilder();

            foreach (var part in orderParts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                var propertyFromQuery = part.Split(':')[0];
                var typeProperty = properties.FirstOrDefault(x => x.Name.Equals(propertyFromQuery, System.StringComparison.InvariantCultureIgnoreCase));

                if (typeProperty == null)
                    continue;

                var sortingOrder = part.EndsWith(":desc") ? "descending" : "ascending";

                orderQueryString.Append($"{typeProperty.Name.ToString()} {sortingOrder}");
            }

            var orderQuery = orderQueryString.ToString().TrimEnd(',', ' ');

            return query.OrderBy(orderQuery);
        }
    }
}
