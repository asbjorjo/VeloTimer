using System;

namespace VeloTimer.Shared.Models
{
    public class TimeParameters
    {
        private DateTime _fromTime = DateTime.MinValue.ToUniversalTime();
        public DateTime FromTime
        {
            get
            {
                return _fromTime;
            }
            set
            {
                _fromTime = value.ToUniversalTime();
            }
        }
        private DateTime _toTime = DateTime.MaxValue.ToUniversalTime();
        public DateTime ToTime
        {
            get
            {
                return _toTime;
            }
            set
            {
                _toTime = value.ToUniversalTime();
            }
        }

        public string ToQueryString()
        {
            return $"FromTime={TimeFormatter(_fromTime)}&ToTime={TimeFormatter(_toTime)}";
        }

        private static string TimeFormatter(DateTime time)
        {
            return time.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
        }
    }
}
