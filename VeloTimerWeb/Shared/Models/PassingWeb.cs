﻿using System;

namespace VeloTimer.Shared.Models
{
    public class PassingWeb
    {
        public DateTime Time { get; set; }
        public TransponderWeb Transponder { get; set; }
        public TrackWeb Track { get; set; }
        public string LoopDescription { get; set; }
        public string SourceId { get; set; }
    }
}
