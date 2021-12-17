using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class RiderWeb
    {
        public string UserId { get; set; }
        public string RiderDisplayName { get; set; }
        public string RiderFirstName { get; set; }
        public string RiderLastName { get; set; }
        public bool RiderIsPublic { get; set; }

        public static RiderWeb Create(Rider rider)
        {
            return new RiderWeb
            {
                UserId = rider.UserId,
                RiderDisplayName = rider.Name,
                RiderFirstName = rider.FirstName,
                RiderLastName = rider.LastName,
                RiderIsPublic = rider.IsPublic
            };
        }
    }
}
