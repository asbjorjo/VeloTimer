using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Models
{
    public class Rider
    {
        public long Id { get; private set; }
        public string Name { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public bool IsPublic { get; set; }

        public string UserId { get; set; }
        public IEnumerable<TransponderOwnership> Transponders { get; set; }

        public RiderWeb ToWeb()
        {
            return new RiderWeb
            {
                RiderDisplayName = Name,
                RiderFirstName = FirstName,
                RiderLastName = LastName,
                UserId = UserId,
                RiderIsPublic = IsPublic
            };
        }
    }
}
