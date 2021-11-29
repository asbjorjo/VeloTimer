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
                x.Property(x => x.SourceId)
                    .IsRequired();
                x.Property(p => p.Time)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc));
                x.HasOne(p => p.Transponder)
                    .WithMany(t => t.Passings)
                    .HasForeignKey(p => p.TransponderId)
                    .IsRequired();
                x.HasOne(p => p.Loop)
                    .WithMany(t => t.Passings)
                    .HasForeignKey(p => p.LoopId)
                    .IsRequired();
                x.HasAlternateKey(p => new { p.Time, p.TransponderId, p.LoopId });
                x.HasIndex(p => p.SourceId);
                x.HasIndex(p => p.Time);
            });

            builder.Entity<Rider>(x =>
            {
                x.HasAlternateKey(p => p.UserId);
            });

            builder.Entity<StatisticsItem>(x =>
            {
                x.HasMany(p => p.Elements)
                    .WithOne(e => e.StatisticsItem);
            });

            //builder.Entity<TimingElement>(x => 
            //{
            //    x.HasOne(p => p.StartLoop)
            //        .WithMany()
            //        .IsRequired();
            //    x.HasOne(p => p.EndLoop)
            //        .WithMany()
            //        .IsRequired()
            //        .OnDelete(DeleteBehavior.Restrict);
            //    x.HasMany<TimingPoint>("IntermediateLoops")
            //        .WithMany(x => x.)
            //});

            builder.Entity<TimingLoop>(x =>
            {
                x.Property(p => p.LoopId)
                    .IsRequired();
                x.Property(p => p.Distance)
                    .IsRequired();
                x.HasOne(p => p.Track)
                    .WithMany(t => t.TimingLoops)
                    .IsRequired();
                x.HasAlternateKey(t => new { t.TrackId, t.LoopId });
            });

            builder.Entity<Track>(x =>
            {
            });

            builder.Entity<TrackSegment>(x =>
            {
                x.Property(p => p.Length)
                    .IsRequired();
                x.HasOne(s => s.Start)
                    .WithMany()
                    .IsRequired();
                x.HasOne(s => s.End)
                    .WithMany()
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Restrict);
                x.HasAlternateKey(k => new { k.StartId, k.EndId });
            });

            builder.Entity<TrackSegmentPassing>(x =>
            {
                x.Property(p => p.StartTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc));
                x.Property(p => p.EndTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc));
                x.Property(x => x.Time)
                    .IsRequired();
                x.HasOne(x => x.TrackSegment)
                    .WithMany()
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Restrict);
                x.HasOne(x => x.Start)
                    .WithMany()
                    .IsRequired();
                x.HasOne(x => x.End)
                    .WithMany()
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<TrackStatisticsItem>(x => {
                x.HasOne(x => x.StatisticsItem)
                    .WithMany(i => i.Elements)
                    .IsRequired();
                x.HasMany(x => x.Segments)
                    .WithOne(s => s.Element)
                    .IsRequired();

                x.Ignore(x => x.Start);
                x.Ignore(x => x.End);
                x.Ignore(x => x.Intermediates);
            });

            builder.Entity<TrackStatisticsSegment>(x =>
            {
                x.Property(x => x.Order)
                    .IsRequired();
                x.HasOne(x => x.Element)
                    .WithMany(x => x.Segments)
                    .IsRequired();
                x.HasOne(x => x.Segment)
                    .WithMany()
                    .IsRequired();
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

            builder.Entity<TransponderStatisticsItem>(x =>
            {
                x.Property(p => p.StartTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc));
                x.Property(p => p.EndTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc));

                x.HasOne(x => x.StatisticsItem)
                    .WithMany()
                    .IsRequired();

                x.Ignore(x => x.SegmentPassings);
            });

            builder.Entity<TransponderStatisticsSegment>(x =>
            {
                x.HasOne(x => x.TransponderStatisticsItem).WithMany("segmentpassinglist").HasForeignKey("transponder_statistics_item_id");
                x.HasOne(x => x.SegmentPassing).WithMany().HasForeignKey("track_segment_passing_id");

                x.HasKey("transponder_statistics_item_id", "track_segment_passing_id");
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
