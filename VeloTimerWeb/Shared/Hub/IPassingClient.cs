using System.Threading.Tasks;
using VeloTimer.Shared.Models.Timing;

namespace VeloTimer.Shared.Hub
{
    public interface IPassingClient
    {
        Task RegisterPassing(PassingWeb passing);
        Task NewPassings();
        Task LastPassing(PassingWeb passing);
    }
}
