﻿using System.Collections.Generic;

namespace VeloTimerWeb.Shared.Models
{
    public class TimingLoop : Entity
    {
        public int LoopId { get; set; }
        public string? Description { get; set; }

        public List<Passing> Passings { get; set; }
    }
}