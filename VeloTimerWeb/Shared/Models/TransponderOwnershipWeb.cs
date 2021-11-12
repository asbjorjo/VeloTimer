using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Models.Validation;
using VeloTimer.Shared.Util;

namespace VeloTimer.Shared.Models
{
    public class TransponderOwnershipWeb
    {
        private DateTimeOffset _ownedFrom = DateTimeOffset.Now.StartOfDay();
        private DateTimeOffset _ownedUntil = DateTimeOffset.Now.EndOfDay().Date.AddYears(1);

        [Required]
        public string TransponderLabel { get; set; }
        [Required]
        public string Owner { get; set; }
        [Required]
        public DateTimeOffset OwnedFrom
        {
            get => _ownedFrom;
            set
            {
                _ownedFrom = value.StartOfDay();
            }
        }
        [EndDate(otherPropertyName = nameof(OwnedFrom), ErrorMessage = "End date has to be after start date.")]
        public DateTimeOffset OwnedUntil { 
            get => _ownedUntil;
            set {
                _ownedUntil = value.EndOfDay();
            }
        }
    }
}
