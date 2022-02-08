using VeloTime.Storage.Models.Timing;

namespace VeloTime.Storage.Models.Statistics
{
    public class Session
    {
        public long Id { get; set; }

        public Activity Activity { get; private set; }
        public DateTime Start { get; private set; }
        public DateTime End { get; private set; }

        public bool UpdateEnd(Passing end)
        {
            if (end == null || end.Transponder != Activity.Transponder || end.Time < End)
                return false;

            End = end.Time;
            return true;
        }

        public static Session Create(Passing passing, Activity activity)
        {
            var session = new Session
            {
                Start = passing.Time,
                End = passing.Time,
                Activity = activity
            };

            return session;
        }
    }
}
