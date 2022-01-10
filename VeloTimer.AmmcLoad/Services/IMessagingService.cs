using System.Threading.Tasks;
using VeloTimer.AmmcLoad.Models;

namespace VeloTimer.AmmcLoad.Services
{
    public interface IMessagingService
    {
        Task SubmitPassing(PassingAmmc passing);
    }
}
