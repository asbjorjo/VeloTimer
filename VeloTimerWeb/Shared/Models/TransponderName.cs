using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Models.Validation;

namespace VeloTimer.Shared.Models
{
    public class TransponderName : Entity
    {
        [Required]
        public long TransponderId { get; set; }
        public Transponder Transponder { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public DateTimeOffset ValidFrom { get; set; }
        [EndDate(otherPropertyName = nameof(ValidFrom))]
        public DateTimeOffset ValidUntil { get; set; }
    }
}
