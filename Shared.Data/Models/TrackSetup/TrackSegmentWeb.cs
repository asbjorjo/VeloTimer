﻿namespace VeloTimer.Shared.Data.Models.TrackSetup
{
    public class TrackSegmentWeb
    {
        public TimingLoopWeb Start { get; set; }
        public TimingLoopWeb End { get; set; }

        public double Length { get; set; }
    }
}
