using System.Collections.Generic;
using System.Linq;

namespace VeloTimerWeb.Api.Models
{
    public class TimingElement
    {
        public long Id { get; set; }

        public string Label { get; set; }

        public IEnumerable<TimingSegment> Segments { get; set; }

        public double Distance => End.Distance > Start.Distance ? End.Distance - Start.Distance : End.Distance - Start.Distance + Start.Track.Length;
        public TimingLoop Start => Segments.OrderBy(s => s.Order).Select(s => s.Loop).First();
        public TimingLoop End => Segments.OrderBy(s => s.Order).Select(s => s.Loop).Last();
        public IReadOnlyList<TimingLoop> Intermediates => Segments.OrderBy(s => s.Order).Select(s => s.Loop).Skip(1).SkipLast(1).ToList().AsReadOnly();
    }
}
