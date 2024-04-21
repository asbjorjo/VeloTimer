using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VeloTimer.Shared.Data.Models.Timing;

namespace PassingLoader.Services.Storage
{
    public class PassingRegisterStatus
    {
        public PassingRegister Passing { get; set; }
        public enum Status { Unsent, Sent, Registered };

        public PassingRegisterStatus(PassingRegister passing)
        {
            Passing = passing;
        }
    }
}
