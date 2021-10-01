using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class TransponderName : Entity
    {
        public Transponder Transponder { get; set; }
        public string Name { get; set; }
        public DateTimeOffset ValidFrom { get; set; }
        public DateTimeOffset ValidUntil { get; set; }
    }
}
