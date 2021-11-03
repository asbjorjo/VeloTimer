using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Util
{
    public class DateTimeUtils
    {
        public static DateTimeOffset StartOfMonth(DateTimeOffset date)
        {
            return new DateTimeOffset(date.Year, date.Month, 1, 0, 0, 0, date.Offset);
        }

        public static DateTimeOffset EndOfMonth(DateTimeOffset date)
        {
            return StartOfMonth(date).AddMonths(1).AddTicks(-1);
        }
    }
}
