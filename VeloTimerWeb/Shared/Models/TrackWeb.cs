using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloTimer.Shared.Models
{
    public class TrackWeb
    {
        public string Name { get; set; }
        public string Slug { get; set; }
        public double Length { get; set; }
        public List<TrackLayoutWeb> Layouts { get => _layouts; set { _layouts = value; _layouts.ForEach(x => x.UpdateTrack(this)); } }

        private List<TrackLayoutWeb> _layouts = new List<TrackLayoutWeb>();

        public static TrackWeb Create(string name, string slug, double length)
        {
            TrackWeb trackWeb = new TrackWeb
            {
                Name = name,
                Slug = slug,
                Length = length
            };

            return trackWeb;
        }

        public static TrackWeb Create(string name, string slug, double length, IEnumerable<TrackLayoutWeb> layouts)
        {
            TrackWeb trackWeb = new TrackWeb
            {
                Name = name,
                Slug = slug,
                Length = length,
                Layouts = layouts.ToList()
            };

            return trackWeb;
        }

    }

    public class TrackLayoutWeb
    {
        public string Name { get; set; }
        public string Slug { get; set; }
        public double Length { get; set; }
        public TrackWeb Track { get; set; }

        public void UpdateTrack(TrackWeb track)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            Track = track;
        }

        public static TrackLayoutWeb Create(string name, string slug, double length)
        {
            TrackLayoutWeb trackLayoutWeb = new TrackLayoutWeb
            {
                Name = name,
                Slug = slug,
                Length = length
            };

            return trackLayoutWeb;
        }
    }
}
