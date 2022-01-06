using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimer.Shared.Models.Validation;
using VeloTimer.Shared.Util;

namespace VeloTimerWeb.Api.Models
{
    public class TransponderOwnership
    {
        public long Id { get; set; }

        public Transponder Transponder { get; set; }
        public Rider Owner { get; set; }
        public DateTime OwnedFrom { get; set; }
        public DateTime OwnedUntil { get; set; }
    }
}
