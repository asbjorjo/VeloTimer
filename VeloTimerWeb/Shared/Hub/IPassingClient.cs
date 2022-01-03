﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimer.Shared.Hub
{
    public interface IPassingClient
    {
        Task RegisterPassing(PassingWeb passing);
        Task NewPassings();
        Task LastPassing(PassingWeb passing);
    }
}
