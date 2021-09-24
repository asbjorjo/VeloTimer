using IdentityServer4.EntityFramework.Options;
using Microsoft.AspNetCore.ApiAuthorization.IdentityServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VeloTimerWeb.Server.Models;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Server.Data
{
    public class ApplicationDbContext : ApiAuthorizationDbContext<ApplicationUser>
    {
        public ApplicationDbContext(
            DbContextOptions options,
            IOptions<OperationalStoreOptions> operationalStoreOptions) : base(options, operationalStoreOptions)
        {
        }

        public DbSet<Track> Tracks { get; set; }
        public DbSet<TimingLoop> TimingLoops { get; set; }
        public DbSet<Transponder> Transponders { get; set; }
        public DbSet<Passing> Passings { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            _ = builder.Entity<Passing>().HasKey(p => new { p.Time, p.TransponderId, p.LoopId });
            _ = builder.Entity<Transponder>().Property(t => t.Id).ValueGeneratedNever();

            base.OnModelCreating(builder);
        }
    }
}
