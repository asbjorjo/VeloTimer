using System.Threading.Tasks;

namespace Client.Services
{
    public interface IRiderProfileService
    {
        Task<bool> HasActiveTransponder();
    }
}
