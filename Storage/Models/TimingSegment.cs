﻿using VeloTime.Storage.Models.TrackSetup;

namespace VeloTime.Storage.Models
{
    public class TimingSegment
    {
        public long Id { get; private set; }

        public int Order { get; set; }
        public TimingLoop Loop { get; set; }
        public TimingElement Element { get; set; }
    }
}