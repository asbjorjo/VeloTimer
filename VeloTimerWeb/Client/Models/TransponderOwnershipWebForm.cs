using MudBlazor;
using System;
using System.ComponentModel.DataAnnotations;
using VeloTimer.Shared.Util;

namespace VeloTimerWeb.Client.Models
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
        public DateRange ValidPeriod { get; set; } = new DateRange(DateTime.Now.StartOfDay(), DateTime.Now.AddDays(365).EndOfDay());
    }
}
