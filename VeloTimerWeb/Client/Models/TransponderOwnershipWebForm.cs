using MudBlazor;
using System;
using System.ComponentModel.DataAnnotations;
using VeloTimer.Shared.Models;
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
        public DateRange ValidPeriod { get; set; } = new DateRange(DateTime.Now.StartOfDay(), DateTime.Now.AddDays(365).EndOfDay());

        public static TransponderOwnershipWebForm CreateFromOwnership(TransponderOwnershipWeb transponderOwnership)
        {
            if (transponderOwnership == null) throw new ArgumentNullException(nameof(transponderOwnership));
            if (transponderOwnership.Owner == null) throw new ArgumentNullException("Missing transponder Owner");

            var form = new TransponderOwnershipWebForm { Owner = transponderOwnership.Owner.UserId};

            if (!string.IsNullOrWhiteSpace(transponderOwnership.Transponder?.Label))
            {
                form.TransponderLabel = transponderOwnership.Transponder.Label;
            }

            if (transponderOwnership.OwnedFrom != default && transponderOwnership.OwnedUntil != default)
            {
                form.ValidPeriod = new DateRange(transponderOwnership.OwnedFrom.DateTime, transponderOwnership.OwnedUntil.DateTime);
            }

            return form;
        }
    }
}
