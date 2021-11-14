﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    [DbContext(typeof(VeloTimerDbContext))]
    [Migration("20211112110045_SegmentRun")]
    partial class SegmentRun
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("velotimer")
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("ProductVersion", "5.0.12")
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("VeloTimer.Shared.Models.Intermediate", b =>
                {
                    b.Property<long>("SegmentId")
                        .HasColumnType("bigint")
                        .HasColumnName("segment_id");

                    b.Property<long>("LoopId")
                        .HasColumnType("bigint")
                        .HasColumnName("loop_id");

                    b.HasKey("SegmentId", "LoopId")
                        .HasName("pk_intermediate");

                    b.HasIndex("LoopId")
                        .HasDatabaseName("ix_intermediate_loop_id");

                    b.ToTable("intermediate");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Passing", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<long>("LoopId")
                        .HasColumnType("bigint")
                        .HasColumnName("loop_id");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("source");

                    b.Property<DateTimeOffset>("Time")
                        .HasColumnType("datetimeoffset")
                        .HasColumnName("time");

                    b.Property<long>("TransponderId")
                        .HasColumnType("bigint")
                        .HasColumnName("transponder_id");

                    b.HasKey("Id")
                        .HasName("pk_passings");

                    b.HasAlternateKey("Time", "TransponderId", "LoopId")
                        .HasName("ak_passings_time_transponder_id_loop_id");

                    b.HasIndex("LoopId")
                        .HasDatabaseName("ix_passings_loop_id");

                    b.HasIndex("TransponderId")
                        .HasDatabaseName("ix_passings_transponder_id");

                    b.ToTable("passings");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Rider", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("name");

                    b.Property<string>("UserId")
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("user_id");

                    b.HasKey("Id")
                        .HasName("pk_riders");

                    b.ToTable("riders");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Segment", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<bool>("DisplayIntermediates")
                        .HasColumnType("bit")
                        .HasColumnName("display_intermediates");

                    b.Property<long>("EndId")
                        .HasColumnType("bigint")
                        .HasColumnName("end_id");

                    b.Property<string>("Label")
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("label");

                    b.Property<long>("MaxTime")
                        .HasColumnType("bigint")
                        .HasColumnName("max_time");

                    b.Property<long>("MinTime")
                        .HasColumnType("bigint")
                        .HasColumnName("min_time");

                    b.Property<bool>("RequireIntermediates")
                        .HasColumnType("bit")
                        .HasColumnName("require_intermediates");

                    b.Property<long>("StartId")
                        .HasColumnType("bigint")
                        .HasColumnName("start_id");

                    b.HasKey("Id")
                        .HasName("pk_segments");

                    b.HasIndex("EndId")
                        .HasDatabaseName("ix_segments_end_id");

                    b.HasIndex("StartId")
                        .HasDatabaseName("ix_segments_start_id");

                    b.ToTable("segments");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.SegmentRun", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<long>("EndId")
                        .HasColumnType("bigint")
                        .HasColumnName("end_id");

                    b.Property<long>("SegmentId")
                        .HasColumnType("bigint")
                        .HasColumnName("segment_id");

                    b.Property<long>("StartId")
                        .HasColumnType("bigint")
                        .HasColumnName("start_id");

                    b.Property<double>("Time")
                        .HasColumnType("float")
                        .HasColumnName("time");

                    b.HasKey("Id")
                        .HasName("pk_segment_runs");

                    b.HasAlternateKey("SegmentId", "StartId", "EndId")
                        .HasName("ak_segment_runs_segment_id_start_id_end_id");

                    b.HasIndex("EndId")
                        .HasDatabaseName("ix_segment_runs_end_id");

                    b.HasIndex("StartId")
                        .HasDatabaseName("ix_segment_runs_start_id");

                    b.ToTable("segment_runs");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TimingLoop", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Description")
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("description");

                    b.Property<double>("Distance")
                        .HasColumnType("float")
                        .HasColumnName("distance");

                    b.Property<long>("LoopId")
                        .HasColumnType("bigint")
                        .HasColumnName("loop_id");

                    b.Property<long>("TrackId")
                        .HasColumnType("bigint")
                        .HasColumnName("track_id");

                    b.HasKey("Id")
                        .HasName("pk_timing_loops");

                    b.HasAlternateKey("TrackId", "LoopId")
                        .HasName("ak_timing_loops_track_id_loop_id");

                    b.ToTable("timing_loops");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Track", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<double>("Length")
                        .HasColumnType("float")
                        .HasColumnName("length");

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("name");

                    b.HasKey("Id")
                        .HasName("pk_tracks");

                    b.ToTable("tracks");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Transponder", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Label")
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("label");

                    b.Property<string>("SystemId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)")
                        .HasColumnName("system_id");

                    b.Property<string>("TimingSystem")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)")
                        .HasColumnName("timing_system");

                    b.HasKey("Id")
                        .HasName("pk_transponders");

                    b.HasAlternateKey("TimingSystem", "SystemId")
                        .HasName("ak_transponders_timing_system_system_id");

                    b.ToTable("transponders");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderName", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("name");

                    b.Property<long>("TransponderId")
                        .HasColumnType("bigint")
                        .HasColumnName("transponder_id");

                    b.Property<DateTimeOffset>("ValidFrom")
                        .HasColumnType("datetimeoffset")
                        .HasColumnName("valid_from");

                    b.Property<DateTimeOffset>("ValidUntil")
                        .HasColumnType("datetimeoffset")
                        .HasColumnName("valid_until");

                    b.HasKey("Id")
                        .HasName("pk_transponder_names");

                    b.HasIndex("TransponderId")
                        .HasDatabaseName("ix_transponder_names_transponder_id");

                    b.ToTable("transponder_names");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderOwnership", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<DateTimeOffset>("OwnedFrom")
                        .HasColumnType("datetimeoffset")
                        .HasColumnName("owned_from");

                    b.Property<DateTimeOffset>("OwnedUntil")
                        .HasColumnType("datetimeoffset")
                        .HasColumnName("owned_until");

                    b.Property<long>("OwnerId")
                        .HasColumnType("bigint")
                        .HasColumnName("owner_id");

                    b.Property<long>("TransponderId")
                        .HasColumnType("bigint")
                        .HasColumnName("transponder_id");

                    b.HasKey("Id")
                        .HasName("pk_transponders_ownerships");

                    b.HasIndex("OwnerId")
                        .HasDatabaseName("ix_transponders_ownerships_owner_id");

                    b.HasIndex("TransponderId")
                        .HasDatabaseName("ix_transponders_ownerships_transponder_id");

                    b.ToTable("transponders_ownerships");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderType", b =>
                {
                    b.Property<string>("System")
                        .HasColumnType("nvarchar(450)")
                        .HasColumnName("system");

                    b.HasKey("System")
                        .HasName("pk_transponder_type");

                    b.ToTable("transponder_type");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Intermediate", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TimingLoop", "Loop")
                        .WithMany()
                        .HasForeignKey("LoopId")
                        .HasConstraintName("fk_intermediate_timing_loops_loop_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Segment", "Segment")
                        .WithMany("Intermediates")
                        .HasForeignKey("SegmentId")
                        .HasConstraintName("fk_intermediate_segments_segment_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Loop");

                    b.Navigation("Segment");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Passing", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TimingLoop", "Loop")
                        .WithMany("Passings")
                        .HasForeignKey("LoopId")
                        .HasConstraintName("fk_passings_timing_loops_loop_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Transponder", "Transponder")
                        .WithMany("Passings")
                        .HasForeignKey("TransponderId")
                        .HasConstraintName("fk_passings_transponders_transponder_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Loop");

                    b.Navigation("Transponder");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Segment", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TimingLoop", "End")
                        .WithMany()
                        .HasForeignKey("EndId")
                        .HasConstraintName("fk_segments_timing_loops_end_id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.TimingLoop", "Start")
                        .WithMany()
                        .HasForeignKey("StartId")
                        .HasConstraintName("fk_segments_timing_loops_start_id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("End");

                    b.Navigation("Start");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.SegmentRun", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.Passing", "End")
                        .WithMany()
                        .HasForeignKey("EndId")
                        .HasConstraintName("fk_segment_runs_passings_end_id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Segment", "Segment")
                        .WithMany()
                        .HasForeignKey("SegmentId")
                        .HasConstraintName("fk_segment_runs_segments_segment_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Passing", "Start")
                        .WithMany()
                        .HasForeignKey("StartId")
                        .HasConstraintName("fk_segment_runs_passings_start_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("End");

                    b.Navigation("Segment");

                    b.Navigation("Start");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TimingLoop", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.Track", "Track")
                        .WithMany("TimingLoops")
                        .HasForeignKey("TrackId")
                        .HasConstraintName("fk_timing_loops_tracks_track_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Track");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Transponder", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TransponderType", "TimingSystemRelation")
                        .WithMany()
                        .HasForeignKey("TimingSystem")
                        .HasConstraintName("fk_transponders_transponder_type_timing_system")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("TimingSystemRelation");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderName", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.Transponder", "Transponder")
                        .WithMany("Names")
                        .HasForeignKey("TransponderId")
                        .HasConstraintName("fk_transponder_names_transponders_transponder_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Transponder");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderOwnership", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.Rider", "Owner")
                        .WithMany("Transponders")
                        .HasForeignKey("OwnerId")
                        .HasConstraintName("fk_transponders_ownerships_riders_owner_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Transponder", "Transponder")
                        .WithMany("Owners")
                        .HasForeignKey("TransponderId")
                        .HasConstraintName("fk_transponders_ownerships_transponders_transponder_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Owner");

                    b.Navigation("Transponder");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Rider", b =>
                {
                    b.Navigation("Transponders");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Segment", b =>
                {
                    b.Navigation("Intermediates");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TimingLoop", b =>
                {
                    b.Navigation("Passings");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Track", b =>
                {
                    b.Navigation("TimingLoops");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Transponder", b =>
                {
                    b.Navigation("Names");

                    b.Navigation("Owners");

                    b.Navigation("Passings");
                });
#pragma warning restore 612, 618
        }
    }
}
