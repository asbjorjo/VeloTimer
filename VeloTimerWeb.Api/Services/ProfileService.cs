using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityModel;
using System.Threading.Tasks;

namespace VeloTimerWeb.Api.Services
{
    public class ProfileService : IProfileService
    {
        public ProfileService()
        {
        }

        public async Task GetProfileDataAsync(ProfileDataRequestContext context)
        {
            var nameClaim = context.Subject.FindAll(JwtClaimTypes.Name);
            context.IssuedClaims.AddRange(nameClaim);

            var roleClaims = context.Subject.FindAll(JwtClaimTypes.Role);
            context.IssuedClaims.AddRange(roleClaims);

            await Task.CompletedTask;
        }

        public async Task IsActiveAsync(IsActiveContext context)
        {
            await Task.CompletedTask;
        }
    }
}
