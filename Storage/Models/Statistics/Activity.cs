using VeloTime.Storage.Models.Riders;
using VeloTime.Storage.Models.Timing;
using VeloTime.Storage.Models.TrackSetup;

namespace VeloTime.Storage.Models.Statistics
{
    public class Activity
    {
        public long Id { get; set; }
        public Track Track { get; set; }
        public Transponder Transponder { get; set; }
        public Rider? Rider { get; set; }

        public List<Session> Sessions { get; set; } = new();

        public static Activity Create(Passing passing)
        {
            var activity = new Activity();
            activity.Track = passing.Loop.Track;
            activity.Transponder = passing.Transponder;

            activity.Sessions.Add(Session.Create(passing, activity));

            return activity;
        }
    }
}
