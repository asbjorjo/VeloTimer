using System.Collections.Generic;

namespace VeloTimer.Shared.Models
{
    public class Transponder : Entity
    {
        public string Label { get; set; }

        public List<Passing> Passings { get; set; }
    }
}