﻿using Microsoft.EntityFrameworkCore;
using VeloTimer.Shared.Models;
using VeloTimerWeb.Api.Util;

namespace VeloTimerWeb.Api.Data
{
    public class VeloTimerDbContext : DbContext
    {
        public VeloTimerDbContext(DbContextOptions<VeloTimerDbContext> options) : base(options)
        {
        }

        public DbSet<Passing> Passings { get; set; }
        public DbSet<Rider> Riders { get; set; }
        public DbSet<Segment> Segments { get; set; }
        public DbSet<SegmentRun> SegmentRuns { get; set; }
        public DbSet<TimingLoop> TimingLoops { get; set; }
        public DbSet<Track> Tracks { get; set; }
        public DbSet<Transponder> Transponders { get; set; }
        public DbSet<TransponderName> TransponderNames { get; set; }
        public DbSet<TransponderOwnership> TranspondersOwnerships { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.HasDefaultSchema("velotimer");

            base.OnModelCreating(builder);

            builder.Entity<Passing>()
                .HasAlternateKey(p => new { p.Time, p.TransponderId, p.LoopId });

            builder.Entity<Segment>()
                .HasOne(s => s.Start)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Segment>()
                .HasOne(s => s.End)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Intermediate>()
                .HasKey(k => new { k.SegmentId, k.LoopId });

            builder.Entity<SegmentRun>()
                .HasAlternateKey(k => new { k.SegmentId, k.StartId, k.EndId });
            builder.Entity<SegmentRun>()
                .HasOne(s => s.Segment)
                .WithMany();
            builder.Entity<SegmentRun>()
                .HasOne(s => s.Start)
                .WithMany()
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<SegmentRun>()
                .HasOne(s => s.End)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            //builder.Entity<SegmentPassing>()
            //    .HasKey(s => new { s.PassingId, s.SegmentId });

            builder.Entity<TimingLoop>()
                .HasAlternateKey(t => new { t.TrackId, t.LoopId });

            builder.Entity<Transponder>()
                .HasAlternateKey(t => new { t.TimingSystem, t.SystemId });
            builder.Entity<Transponder>()
                .Property(t => t.TimingSystem)
                .HasConversion<string>();
            builder.Entity<Transponder>()
                .HasOne(t => t.TimingSystemRelation)
                .WithMany()
                .HasForeignKey(t => t.TimingSystem);

            builder.Entity<TransponderType>()
                .Property(t => t.System)
                .HasConversion<string>();
            builder.Entity<TransponderType>()
                .HasKey(t => t.System);

            builder.SnakeCaseModel();
        }
    }
}