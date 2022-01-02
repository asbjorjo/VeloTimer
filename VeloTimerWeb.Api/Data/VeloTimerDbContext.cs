using Microsoft.EntityFrameworkCore;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Models;
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
                //x.HasMany(p => p.Elements)
                //    .WithOne(e => e.StatisticsItem);
                x.HasAlternateKey(x => x.Slug);
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
                x.HasMany(x => x.Layouts)
                    .WithOne(x => x.Track);
                x.HasAlternateKey(x => x.Slug);
            });

            builder.Entity<TrackLayout>(x =>
            {
                x.HasOne(x => x.Track)
                    .WithMany(x => x.Layouts)
                    .IsRequired();
                x.HasMany(x => x.Sectors)
                    .WithOne(x => x.Layout);
                x.Property(x => x.Name)
                    .IsRequired();
                x.HasAlternateKey("TrackId", "Name");
                x.HasAlternateKey(x => x.Slug);
            });

            builder.Entity<TrackLayoutPassing>(x =>
            {
                x.HasOne(x => x.TrackLayout)
                    .WithMany()
                    .IsRequired();
                x.HasOne(x => x.Transponder)
                    .WithMany()
                    .IsRequired();
                x.HasMany(x => x.Passings)
                    .WithMany(x => x.LayoutPassings);

                x.Property(p => p.StartTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc))
                    .IsRequired();
                x.Property(p => p.EndTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc))
                    .IsRequired();
                x.Property(x => x.Time)
                    .IsRequired();
            });

            builder.Entity<TrackLayoutSector>(x =>
            {
                x.HasOne(x => x.Layout)
                    .WithMany(x => x.Sectors)
                    .IsRequired();
                x.HasOne(x => x.Sector)
                    .WithMany()
                    .IsRequired();

                x.Property(x => x.Order)
                    .IsRequired();

                x.HasIndex("LayoutId", "SectorId", "Order")
                    .IsUnique();
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

            builder.Entity<TrackSector>(x =>
            {
                x.HasMany(x => x.Segments)
                    .WithOne(x => x.Sector);

            });

            builder.Entity<TrackSectorPassing>(x =>
            {
                x.HasOne(x => x.TrackSector)
                    .WithMany()
                    .IsRequired();
                x.HasOne(x => x.Transponder)
                    .WithMany()
                    .IsRequired();
                x.Property(x => x.StartTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc))
                    .IsRequired();
                x.Property(x => x.EndTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc))
                    .IsRequired();
                x.Property(x => x.Time)
                    .IsRequired();
            });

            builder.Entity<TrackSectorSegment>(x =>
            {
                x.HasOne(x => x.Sector)
                    .WithMany()
                    .IsRequired();
                x.HasOne(x => x.Segment)
                    .WithMany()
                    .IsRequired();
                x.Property(x => x.Order)
                    .IsRequired();
                x.HasKey("SectorId", "SegmentId");
            });

            builder.Entity<TrackSectorSegmentPassing>(x =>
            {
                x.HasOne(x => x.SectorPassing)
                    .WithMany(x => x.SegmentPassings)
                    .IsRequired();
                x.HasOne(x => x.SegmentPassing)
                    .WithMany()
                    .IsRequired();
                x.HasKey("SectorPassingId", "SegmentPassingId");
            });

            builder.Entity<TrackSegmentPassing>(x =>
            {
                x.Property(p => p.StartTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc))
                    .IsRequired();
                x.Property(p => p.EndTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc))
                    .IsRequired();
                x.Property(x => x.Time)
                    .IsRequired();
                x.HasOne(x => x.Transponder)
                    .WithMany()
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Restrict);
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

                x.HasIndex(x => new { x.StartTime, x.EndTime })
                    .IncludeProperties("TransponderId");
            });

            builder.Entity<TrackStatisticsItem>(x => {
                x.HasOne(x => x.StatisticsItem)
                    .WithMany()
                    .IsRequired();
                x.HasOne(x => x.Layout)
                    .WithMany();
                x.Property(x => x.Laps)
                    .IsRequired()
                    .HasDefaultValue(1);
                x.HasAlternateKey("StatisticsItemId", "LayoutId");
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
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc))
                    .IsRequired();
                x.Property(p => p.EndTime)
                    .HasConversion(
                        v => v,
                        v => new System.DateTime(v.Ticks, System.DateTimeKind.Utc))
                    .IsRequired();
                x.Property(x => x.Time)
                    .IsRequired();

                x.HasOne(x => x.Transponder)
                    .WithMany()
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Restrict);

                x.HasOne(x => x.StatisticsItem)
                    .WithMany()
                    .IsRequired();

                x.HasIndex(x => new { x.EndTime, x.StartTime })
                    .IncludeProperties("StatisticsItemId", "Time", "TransponderId");

                x.Ignore(x => x.LayoutPassings);
            });

            builder.Entity<TransponderStatisticsLayout>(x =>
            {
                x.HasOne(x => x.TransponderStatisticsItem).WithMany("LayoutPassingList").HasForeignKey("transponder_statistics_item_id");
                x.HasOne(x => x.LayoutPassing).WithMany().HasForeignKey("track_layout_passing_id");

                x.HasKey("transponder_statistics_item_id", "track_layout_passing_id");
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
