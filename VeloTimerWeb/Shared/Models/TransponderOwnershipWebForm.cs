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
    public class TransponderOwnershipWebForm
    {
        private DateTime _ownedFrom = DateTime.Now.StartOfDay();
        private DateTime _ownedUntil = DateTime.Now.AddDays(365).EndOfDay();

        [Required]
        public string TransponderLabel { get; set; }
        [Required]
        public string Owner { get; set; }
        [Required]
        public DateTime? OwnedFrom
        {
            get => _ownedFrom;
            set
            {
                _ownedFrom = value.HasValue ? value.Value.StartOfDay() : DateTime.Now.StartOfDay();
            }
        }
        [EndDate(otherPropertyName = nameof(OwnedFrom), ErrorMessage = "End date has to be after start date.")]
        public DateTime? OwnedUntil { 
            get => _ownedUntil;
            set {
                _ownedUntil = value.HasValue ? value.Value.EndOfDay() : DateTime.Now.AddDays(365).EndOfDay();
            }
        }
    }
}
