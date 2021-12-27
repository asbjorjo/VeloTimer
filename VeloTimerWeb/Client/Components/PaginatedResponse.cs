using System.Collections.Generic;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Client.Components
{
    public class PaginatedResponse<T>
    {
        public List<T> Items { get; set; }
        public Pagination Pagination { get; set; }
    }
}
