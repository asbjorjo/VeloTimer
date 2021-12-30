using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Models.Validation;

namespace VeloTimer.Shared.Models
{
    public class TransponderOwnership
    {
        public long Id { get; set; }

        public Transponder Transponder { get; set; }
        public Rider Owner { get; set; }
        public DateTime OwnedFrom { get; set; }
        public DateTime OwnedUntil { get; set; }

        public TransponderOwnershipWeb ToWeb()
        {
            return new TransponderOwnershipWeb
            {
                Owner = Owner.UserId,
                TransponderLabel = Transponder.SystemId,
                OwnedFrom = OwnedFrom,
                OwnedUntil = OwnedUntil
            };
        }
    }
}
