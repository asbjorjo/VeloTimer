using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Models.Validation;

namespace VeloTimer.Shared.Models
{
    public class TransponderNameWeb
    {
        private DateTimeOffset _validUntil = DateTimeOffset.Now.Date + TimeSpan.FromDays(1) - TimeSpan.FromMilliseconds(1);

        [Required]
        public string TransponderLabel { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.Now.Date;
        [EndDate(otherPropertyName = nameof(ValidFrom), ErrorMessage = "End date has to be after start date.")]
        public DateTimeOffset ValidUntil 
        { 
            get => _validUntil;
            set {
                _validUntil = value.Date + TimeSpan.FromDays(1) - TimeSpan.FromMilliseconds(1);
            }
        }
    }
}
