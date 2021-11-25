﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class Track
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public double Length { get; set; }

        public IEnumerable<TrackSegment> Segments { get; set; }
        public IEnumerable<TimingLoop> TimingLoops { get; set; }
    }
}
