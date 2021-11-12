﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Util
{
    public static class DateTimeUtils
    {
        public static DateTimeOffset StartOfMonth(this DateTimeOffset date)
        {
            return new DateTimeOffset(date.Year, date.Month, 1, 0, 0, 0, date.Offset);
        }

        public static DateTimeOffset EndOfMonth(this DateTimeOffset date)
        {
            return StartOfMonth(date).AddMonths(1).AddTicks(-1);
        }

        public static DateTimeOffset StartOfDay(this DateTimeOffset date)
        {
            return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, date.Offset);
        }

        public static DateTimeOffset EndOfDay(this DateTimeOffset date)
        {
            return StartOfDay(date).AddDays(1).AddTicks(-1);
        }
    }
}
