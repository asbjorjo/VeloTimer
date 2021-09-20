using System.Collections.Generic;

namespace VeloTimerWeb.Shared.Models
{
    public class Transponder : Entity
    {
        public string? Name { get; set; }

        public List<Passing> Passings { get; set; }
    }
}