using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Models;

namespace VeloTimerWeb.Api.Services
{
    public class VeloTimeUserManager<TUser> : UserManager<TUser> where TUser : IdentityUser<Guid>
    {
        private readonly VeloTimerDbContext _context;

        public VeloTimeUserManager(VeloTimerDbContext applicationDbContext,
                                   IUserStore<TUser> store,
                                   IOptions<IdentityOptions> optionsAccessor,
                                   IPasswordHasher<TUser> passwordHasher,
                                   IEnumerable<IUserValidator<TUser>> userValidators,
                                   IEnumerable<IPasswordValidator<TUser>> passwordValidators,
                                   ILookupNormalizer keyNormalizer,
                                   IdentityErrorDescriber errors,
                                   IServiceProvider services,
                                   ILogger<UserManager<TUser>> logger) : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
        {
            _context = applicationDbContext;
        }

        public async override Task<IdentityResult> CreateAsync(TUser user)
        {
            var created = await base.CreateAsync(user);

            if (created.Succeeded)
            {
                var newuser = await Store.FindByNameAsync(user.NormalizedUserName, CancellationToken);
                await _context.Set<Rider>().AddAsync(
                    new Rider
                    {
                        UserId = newuser.Id.ToString()
                    });
                await _context.SaveChangesAsync();
            }

            return created;
        }

        public async override Task<IdentityResult> DeleteAsync(TUser user)
        {
            var deleted = await base.DeleteAsync(user);

            if (deleted.Succeeded)
            {
                var rider = await _context.Set<Rider>().Where(r => r.UserId.Equals(user.Id)).SingleOrDefaultAsync();

                if (rider != null)
                {
                    _context.Set<Rider>().Remove(rider);
                    await _context.SaveChangesAsync();
                }
            }

            return deleted;
        }

        public async override Task<IdentityResult> AddClaimAsync(TUser user, Claim claim)
        {
            var added = await base.AddClaimAsync(user, claim);

            if (added.Succeeded && claim.Type == ClaimTypes.Name)
            {
                var rider = await _context.Set<Rider>().SingleOrDefaultAsync(r => r.UserId == user.Id.ToString());
                if (rider != null && rider.Name != claim.Value)
                {
                    rider.Name = claim.Value;
                    await _context.SaveChangesAsync();
                }
            }
            if (added.Succeeded && claim.Type == ClaimTypes.GivenName)
            {
                var rider = await _context.Set<Rider>().SingleOrDefaultAsync(r => r.UserId == user.Id.ToString());
                if (rider != null && rider.FirstName != claim.Value)
                {
                    rider.FirstName = claim.Value;
                    await _context.SaveChangesAsync();
                }
            }
            if (added.Succeeded && claim.Type == ClaimTypes.Surname)
            {
                var rider = await _context.Set<Rider>().SingleOrDefaultAsync(r => r.UserId == user.Id.ToString());
                if (rider != null && rider.LastName != claim.Value)
                {
                    rider.LastName = claim.Value;
                    await _context.SaveChangesAsync();
                }
            }

            return added;
        }
    }
}
