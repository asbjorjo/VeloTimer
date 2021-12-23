using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class PaginationParameters
    {
        const int maxPageSize = 50;
        private int _pageNumber = 1;
        public int PageNumber
        {
            get
            {
                return _pageNumber;
            }
            set
            {
                _pageNumber = (value > 0) ? value : 1;
            }
        }
        private int _pageSize = 10;
        public int PageSize
        {
            get
            {
                return _pageSize;
            }
            set
            {
                _pageSize = (value > maxPageSize) ? maxPageSize : value;
            }
        }

        public string ToQueryString()
        {
            return $"PageNumber={PageNumber}&PageSize={PageSize}";
        }
    }
}
