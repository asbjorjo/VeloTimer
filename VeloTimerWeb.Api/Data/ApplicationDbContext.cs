using IdentityServer4.EntityFramework.Options;
using Microsoft.AspNetCore.ApiAuthorization.IdentityServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Data
{
    public class ApplicationDbContext : ApiAuthorizationDbContext<User>
    {
        public ApplicationDbContext(
            DbContextOptions options,
            IOptions<OperationalStoreOptions> operationalStoreOptions) : base(options, operationalStoreOptions)
        {
        }

        public DbSet<Track> Tracks { get; set; }
        public DbSet<TimingLoop> TimingLoops { get; set; }
        public DbSet<Transponder> Transponders { get; set; }
        public DbSet<TransponderName> TransponderNames { get; set; }
        public DbSet<Passing> Passings { get; set; }
        public DbSet<Segment> Segments { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Passing>().HasAlternateKey(p => new { p.Time, p.TransponderId, p.LoopId });
            builder.Entity<Transponder>().Property(t => t.Id).ValueGeneratedNever();
            builder.Entity<TimingLoop>().HasAlternateKey(t => new { t.TrackId, t.LoopId });
            builder.Entity<Segment>().HasOne(s => s.Start).WithMany().OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Segment>().HasOne(s => s.End).WithMany().OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Intermediate>().HasKey(k => new { k.SegmentId, k.LoopId });

            base.OnModelCreating(builder);
        }
    }
}
