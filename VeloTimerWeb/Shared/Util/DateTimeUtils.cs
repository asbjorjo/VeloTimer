using System;

namespace VeloTimer.Shared.Util
{
    public static class DateTimeUtils
    {
        public static DateTimeOffset StartOfYear(this DateTimeOffset date)
        {
            return new DateTimeOffset(date.Year, 1, 1, 0, 0, 0, date.Offset);
        }

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

        public static DateTime StartOfDay(this DateTime date)
        {
            return new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, date.Kind);
        }

        public static DateTime EndOfDay(this DateTime date)
        {
            return StartOfDay(date).AddDays(1).AddTicks(-1);
        }
    }
}
