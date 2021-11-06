using IdentityServer4.EntityFramework;
using IdentityServer4.EntityFramework.Entities;
using IdentityServer4.EntityFramework.Extensions;
using IdentityServer4.EntityFramework.Interfaces;
using IdentityServer4.EntityFramework.Options;
using Microsoft.AspNetCore.ApiAuthorization.IdentityServer;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Data
{
    public class ApplicationDbContext : IdentityDbContext<User, Role, string>, IPersistedGrantDbContext
    {
        private readonly IOptions<OperationalStoreOptions> _operationalStoreOptions;

        public ApplicationDbContext(
            DbContextOptions options,
            IOptions<OperationalStoreOptions> operationalStoreOptions) : base(options)
        {
            _operationalStoreOptions = operationalStoreOptions;
        }

        public DbSet<Track> Tracks { get; set; }
        public DbSet<TimingLoop> TimingLoops { get; set; }
        public DbSet<Transponder> Transponders { get; set; }
        public DbSet<TransponderName> TransponderNames { get; set; }
        public DbSet<Passing> Passings { get; set; }
        public DbSet<Segment> Segments { get; set; }

        public DbSet<PersistedGrant> PersistedGrants { get; set; }
        public DbSet<DeviceFlowCodes> DeviceFlowCodes { get; set; }

        Task<int> IPersistedGrantDbContext.SaveChangesAsync() => base.SaveChangesAsync();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.ConfigurePersistedGrantContext(_operationalStoreOptions.Value);

            builder.Entity<Passing>().HasAlternateKey(p => new { p.Time, p.TransponderId, p.LoopId });
            builder.Entity<Transponder>().Property(t => t.Id).ValueGeneratedNever();
            builder.Entity<TimingLoop>().HasAlternateKey(t => new { t.TrackId, t.LoopId });
            builder.Entity<Segment>().HasOne(s => s.Start).WithMany().OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Segment>().HasOne(s => s.End).WithMany().OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Intermediate>().HasKey(k => new { k.SegmentId, k.LoopId });
        }
    }
}
