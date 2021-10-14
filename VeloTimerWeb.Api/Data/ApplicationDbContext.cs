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
        public DbSet<TransponderName> TransponderNames { get; set; }
        public DbSet<Passing> Passings { get; set; }
        public DbSet<Segment> Segments { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Passing>().HasAlternateKey(p => new { p.Time, p.TransponderId, p.LoopId });
            builder.Entity<Transponder>().Property(t => t.Id).ValueGeneratedNever();
            builder.Entity<TimingLoop>().HasAlternateKey(t => new { t.TrackId, t.LoopId });
            builder.Entity<Segment>().HasMany(s => s.Intermediates).WithMany("segment");
            builder.Entity<Segment>().HasOne(s => s.Start).WithMany().OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Segment>().HasOne(s => s.End).WithMany().OnDelete(DeleteBehavior.Restrict);
        }
    }
}
