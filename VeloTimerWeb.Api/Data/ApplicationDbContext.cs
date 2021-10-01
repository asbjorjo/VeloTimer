using Microsoft.EntityFrameworkCore;
using VeloTimer.Shared.Models;

namespace VeloTimerWeb.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(
            DbContextOptions options) : base(options)
        {
        }

        public DbSet<Track> Tracks { get; set; }
        public DbSet<TimingLoop> TimingLoops { get; set; }
        public DbSet<Transponder> Transponders { get; set; }
        public DbSet<Passing> Passings { get; set; }
        public DbSet<Segment> Segments { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            _ = builder.Entity<Passing>().HasAlternateKey(p => new { p.Time, p.TransponderId, p.LoopId });
            _ = builder.Entity<Transponder>().Property(t => t.Id).ValueGeneratedNever();

            base.OnModelCreating(builder);
        }
    }
}
