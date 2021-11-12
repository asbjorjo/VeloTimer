using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Models.Validation;

namespace VeloTimer.Shared.Models
{
    public class TransponderOwnership : Entity
    {
        public long TransponderId { get; set; }
        public long OwnerId { get; set; }

        public Transponder Transponder { get; set; }
        public Rider Owner { get; set; }
        [Required]
        public DateTimeOffset OwnedFrom { get; set; }
        [EndDate(otherPropertyName = nameof(OwnedFrom))]
        public DateTimeOffset OwnedUntil { get; set; }
    }
}
