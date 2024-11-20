using MudBlazor;
using System;
using System.ComponentModel.DataAnnotations;
using VeloTimer.Shared.Data.Models.Riders;
using VeloTimer.Shared.Util;

namespace VeloTimerWeb.Client.Models
{
    public class TransponderOwnershipWebForm
    {
        [Required]
        public string TransponderLabel { get; set; }
        [Required]
        public string Owner { get; set; }
        [Required]
        public DateTime? OwnedFrom { get; set; } = DateTime.Now.StartOfDay();

        public static TransponderOwnershipWebForm CreateFromOwnership(TransponderOwnershipWeb transponderOwnership)
        {
            if (transponderOwnership == null) throw new ArgumentNullException(nameof(transponderOwnership));
            if (transponderOwnership.Owner == null) throw new ArgumentNullException("Missing transponder Owner");

            var form = new TransponderOwnershipWebForm { Owner = transponderOwnership.Owner.UserId };

            if (!string.IsNullOrWhiteSpace(transponderOwnership.Transponder?.Label))
            {
                form.TransponderLabel = transponderOwnership.Transponder.Label;
            }

            if (transponderOwnership.OwnedFrom != default)
            {
                form.OwnedFrom = transponderOwnership.OwnedFrom.DateTime;
            }

            return form;
        }
    }
}
