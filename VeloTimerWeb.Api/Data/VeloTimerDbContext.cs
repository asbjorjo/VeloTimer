using Microsoft.EntityFrameworkCore;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Util;

namespace VeloTimerWeb.Api.Data
{
    public class VeloTimerDbContext : DbContext
    {
        public VeloTimerDbContext(DbContextOptions<VeloTimerDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.HasDefaultSchema("velotimer");

            base.OnModelCreating(builder);

            builder.Entity<Passing>(x =>
            {
                x.Property(p => p.Time)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc));
                x.HasAlternateKey(p => new { p.Time, p.TransponderId, p.LoopId });
                x.HasIndex(p => p.SourceId);
                x.HasIndex(p => p.Time);
            });

            builder.Entity<Rider>(x =>
            {
                x.HasAlternateKey(p => p.UserId);
            });

            //builder.Entity<SegmentRun>(x =>
            //{
            //    x.HasAlternateKey(k => new { k.SegmentId, k.StartId, k.EndId });
            //    x.HasOne(s => s.Start)
            //        .WithMany()
            //        .OnDelete(DeleteBehavior.Cascade);
            //    x.HasOne(s => s.End)
            //        .WithMany()
            //        .OnDelete(DeleteBehavior.Cascade);
            //    x.HasIndex(s => new { s.SegmentId, s.Time, s.StartId, s.EndId });
            //});

            builder.Entity<TimingLoop>(x =>
            {
                x.HasAlternateKey(t => new { t.TrackId, t.LoopId });
            });

            builder.Entity<Track>();

            builder.Entity<TrackSegment>(x =>
            {
                x.HasOne<Track>()
                    .WithMany();
                x.HasOne(s => s.Start)
                    .WithMany();
                x.HasOne(s => s.End)
                    .WithMany();
            });

            builder.Entity<Transponder>(x =>
            {
                x.Property(t => t.TimingSystem)
                    .HasConversion<string>();
                x.HasAlternateKey(t => new { t.TimingSystem, t.SystemId });
                x.HasOne(t => t.TimingSystemRelation)
                    .WithMany()
                    .HasForeignKey(t => t.TimingSystem);
            });

            builder.Entity<TransponderOwnership>(x =>
            {
                x.Property(p => p.OwnedFrom)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc));
                x.Property(p => p.OwnedUntil)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc));
            });

            builder.Entity<TransponderType>(x =>
            {
                x.Property(t => t.System)
                    .HasConversion<string>();
                x.HasKey(t => t.System);
            });

            builder.SnakeCaseModel();
        }
    }
}
