﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace VeloTimerWeb.Shared.Models
{
    public abstract class Entity
    {
        public long Id { get; set; }
    }
}
