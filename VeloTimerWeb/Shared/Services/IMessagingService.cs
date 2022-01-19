using System.Collections.Generic;
using System.Threading.Tasks;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.Shared.Services
{
    public interface IMessagingService
    {
        Task SubmitPassing(PassingRegister passing);
        Task SubmitPassings(IEnumerable<PassingRegister> passings);
    }
}
