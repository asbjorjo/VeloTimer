﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using VeloTimerWeb.Api.Data;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    [DbContext(typeof(VeloTimerDbContext))]
    [Migration("20211206222853_TrackStatsAltKey")]
    partial class TrackStatsAltKey
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("velotimer")
                .HasAnnotation("Relational:MaxIdentifierLength", 63)
                .HasAnnotation("ProductVersion", "5.0.12")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            modelBuilder.Entity("VeloTimer.Shared.Models.Passing", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<long>("LoopId")
                        .HasColumnType("bigint")
                        .HasColumnName("loop_id");

                    b.Property<string>("SourceId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("source_id");

                    b.Property<DateTime>("Time")
                        .HasColumnType("timestamp without time zone")
                        .HasColumnName("time");

                    b.Property<long>("TransponderId")
                        .HasColumnType("bigint")
                        .HasColumnName("transponder_id");

                    b.HasKey("Id")
                        .HasName("pk_passing");

                    b.HasAlternateKey("Time", "TransponderId", "LoopId")
                        .HasName("ak_passing_time_transponder_id_loop_id");

                    b.HasIndex("LoopId")
                        .HasDatabaseName("ix_passing_loop_id");

                    b.HasIndex("SourceId")
                        .HasDatabaseName("ix_passing_source_id");

                    b.HasIndex("Time")
                        .HasDatabaseName("ix_passing_time");

                    b.HasIndex("TransponderId")
                        .HasDatabaseName("ix_passing_transponder_id");

                    b.ToTable("passing");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Rider", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<string>("FirstName")
                        .HasColumnType("text")
                        .HasColumnName("first_name");

                    b.Property<string>("LastName")
                        .HasColumnType("text")
                        .HasColumnName("last_name");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.HasKey("Id")
                        .HasName("pk_rider");

                    b.HasAlternateKey("UserId")
                        .HasName("ak_rider_user_id");

                    b.ToTable("rider");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.StatisticsItem", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<double>("Distance")
                        .HasColumnType("double precision")
                        .HasColumnName("distance");

                    b.Property<string>("Label")
                        .HasColumnType("text")
                        .HasColumnName("label");

                    b.HasKey("Id")
                        .HasName("pk_statistics_item");

                    b.ToTable("statistics_item");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TimingLoop", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<string>("Description")
                        .HasColumnType("text")
                        .HasColumnName("description");

                    b.Property<double>("Distance")
                        .HasColumnType("double precision")
                        .HasColumnName("distance");

                    b.Property<int>("LoopId")
                        .HasColumnType("integer")
                        .HasColumnName("loop_id");

                    b.Property<long>("TrackId")
                        .HasColumnType("bigint")
                        .HasColumnName("track_id");

                    b.HasKey("Id")
                        .HasName("pk_timing_loop");

                    b.HasAlternateKey("TrackId", "LoopId")
                        .HasName("ak_timing_loop_track_id_loop_id");

                    b.ToTable("timing_loop");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Track", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<double>("Length")
                        .HasColumnType("double precision")
                        .HasColumnName("length");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.HasKey("Id")
                        .HasName("pk_track");

                    b.ToTable("track");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackLayout", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<long>("TrackId")
                        .HasColumnType("bigint")
                        .HasColumnName("track_id");

                    b.HasKey("Id")
                        .HasName("pk_track_layout");

                    b.HasAlternateKey("TrackId", "Name")
                        .HasName("ak_track_layout_track_id_name");

                    b.ToTable("track_layout");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackLayoutPassing", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<DateTime>("EndTime")
                        .HasColumnType("timestamp without time zone")
                        .HasColumnName("end_time");

                    b.Property<DateTime>("StartTime")
                        .HasColumnType("timestamp without time zone")
                        .HasColumnName("start_time");

                    b.Property<double>("Time")
                        .HasColumnType("double precision")
                        .HasColumnName("time");

                    b.Property<long>("TrackLayoutId")
                        .HasColumnType("bigint")
                        .HasColumnName("track_layout_id");

                    b.Property<long>("TransponderId")
                        .HasColumnType("bigint")
                        .HasColumnName("transponder_id");

                    b.HasKey("Id")
                        .HasName("pk_track_layout_passing");

                    b.HasIndex("TrackLayoutId")
                        .HasDatabaseName("ix_track_layout_passing_track_layout_id");

                    b.HasIndex("TransponderId")
                        .HasDatabaseName("ix_track_layout_passing_transponder_id");

                    b.ToTable("track_layout_passing");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackLayoutSegment", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<long>("LayoutId")
                        .HasColumnType("bigint")
                        .HasColumnName("layout_id");

                    b.Property<int>("Order")
                        .HasColumnType("integer")
                        .HasColumnName("order");

                    b.Property<long>("SegmentId")
                        .HasColumnType("bigint")
                        .HasColumnName("segment_id");

                    b.HasKey("Id")
                        .HasName("pk_track_layout_segment");

                    b.HasIndex("SegmentId")
                        .HasDatabaseName("ix_track_layout_segment_segment_id");

                    b.HasIndex("LayoutId", "SegmentId", "Order")
                        .IsUnique()
                        .HasDatabaseName("ix_track_layout_segment_layout_id_segment_id_order");

                    b.ToTable("track_layout_segment");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackSegment", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<long>("EndId")
                        .HasColumnType("bigint")
                        .HasColumnName("end_id");

                    b.Property<double>("Length")
                        .HasColumnType("double precision")
                        .HasColumnName("length");

                    b.Property<long>("StartId")
                        .HasColumnType("bigint")
                        .HasColumnName("start_id");

                    b.HasKey("Id")
                        .HasName("pk_track_segment");

                    b.HasAlternateKey("StartId", "EndId")
                        .HasName("ak_track_segment_start_id_end_id");

                    b.HasIndex("EndId")
                        .HasDatabaseName("ix_track_segment_end_id");

                    b.ToTable("track_segment");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackSegmentPassing", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<long>("EndId")
                        .HasColumnType("bigint")
                        .HasColumnName("end_id");

                    b.Property<DateTime>("EndTime")
                        .HasColumnType("timestamp without time zone")
                        .HasColumnName("end_time");

                    b.Property<long>("StartId")
                        .HasColumnType("bigint")
                        .HasColumnName("start_id");

                    b.Property<DateTime>("StartTime")
                        .HasColumnType("timestamp without time zone")
                        .HasColumnName("start_time");

                    b.Property<double>("Time")
                        .HasColumnType("double precision")
                        .HasColumnName("time");

                    b.Property<long?>("TrackLayoutPassingId")
                        .HasColumnType("bigint")
                        .HasColumnName("track_layout_passing_id");

                    b.Property<long>("TrackSegmentId")
                        .HasColumnType("bigint")
                        .HasColumnName("track_segment_id");

                    b.Property<long>("TransponderId")
                        .HasColumnType("bigint")
                        .HasColumnName("transponder_id");

                    b.HasKey("Id")
                        .HasName("pk_track_segment_passing");

                    b.HasIndex("EndId")
                        .HasDatabaseName("ix_track_segment_passing_end_id");

                    b.HasIndex("StartId")
                        .HasDatabaseName("ix_track_segment_passing_start_id");

                    b.HasIndex("TrackLayoutPassingId")
                        .HasDatabaseName("ix_track_segment_passing_track_layout_passing_id");

                    b.HasIndex("TrackSegmentId")
                        .HasDatabaseName("ix_track_segment_passing_track_segment_id");

                    b.HasIndex("TransponderId")
                        .HasDatabaseName("ix_track_segment_passing_transponder_id");

                    b.HasIndex("StartTime", "EndTime")
                        .HasDatabaseName("ix_track_segment_passing_start_time_end_time")
                        .HasAnnotation("SqlServer:Include", new[] { "TransponderId" });

                    b.ToTable("track_segment_passing");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackStatisticsItem", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<int>("Laps")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(1)
                        .HasColumnName("laps");

                    b.Property<long?>("LayoutId")
                        .IsRequired()
                        .HasColumnType("bigint")
                        .HasColumnName("layout_id");

                    b.Property<long>("StatisticsItemId")
                        .HasColumnType("bigint")
                        .HasColumnName("statistics_item_id");

                    b.HasKey("Id")
                        .HasName("pk_track_statistics_item");

                    b.HasAlternateKey("StatisticsItemId", "LayoutId")
                        .HasName("ak_track_statistics_item_statistics_item_id_layout_id");

                    b.HasIndex("LayoutId")
                        .HasDatabaseName("ix_track_statistics_item_layout_id");

                    b.ToTable("track_statistics_item");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Transponder", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<string>("Label")
                        .HasColumnType("text")
                        .HasColumnName("label");

                    b.Property<string>("SystemId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("system_id");

                    b.Property<string>("TimingSystem")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("timing_system");

                    b.HasKey("Id")
                        .HasName("pk_transponder");

                    b.HasAlternateKey("TimingSystem", "SystemId")
                        .HasName("ak_transponder_timing_system_system_id");

                    b.ToTable("transponder");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderOwnership", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<DateTime>("OwnedFrom")
                        .HasColumnType("timestamp without time zone")
                        .HasColumnName("owned_from");

                    b.Property<DateTime>("OwnedUntil")
                        .HasColumnType("timestamp without time zone")
                        .HasColumnName("owned_until");

                    b.Property<long?>("OwnerId")
                        .HasColumnType("bigint")
                        .HasColumnName("owner_id");

                    b.Property<long?>("TransponderId")
                        .HasColumnType("bigint")
                        .HasColumnName("transponder_id");

                    b.HasKey("Id")
                        .HasName("pk_transponder_ownership");

                    b.HasIndex("OwnerId")
                        .HasDatabaseName("ix_transponder_ownership_owner_id");

                    b.HasIndex("TransponderId")
                        .HasDatabaseName("ix_transponder_ownership_transponder_id");

                    b.ToTable("transponder_ownership");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderStatisticsItem", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("id")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

                    b.Property<DateTime>("EndTime")
                        .HasColumnType("timestamp without time zone")
                        .HasColumnName("end_time");

                    b.Property<DateTime>("StartTime")
                        .HasColumnType("timestamp without time zone")
                        .HasColumnName("start_time");

                    b.Property<long>("StatisticsItemId")
                        .HasColumnType("bigint")
                        .HasColumnName("statistics_item_id");

                    b.Property<double>("Time")
                        .HasColumnType("double precision")
                        .HasColumnName("time");

                    b.Property<long>("TransponderId")
                        .HasColumnType("bigint")
                        .HasColumnName("transponder_id");

                    b.HasKey("Id")
                        .HasName("pk_transponder_statistics_item");

                    b.HasIndex("StatisticsItemId")
                        .HasDatabaseName("ix_transponder_statistics_item_statistics_item_id");

                    b.HasIndex("TransponderId")
                        .HasDatabaseName("ix_transponder_statistics_item_transponder_id");

                    b.HasIndex("EndTime", "StartTime")
                        .HasDatabaseName("ix_transponder_statistics_item_end_time_start_time")
                        .HasAnnotation("SqlServer:Include", new[] { "StatisticsItemId", "Time", "TransponderId" });

                    b.ToTable("transponder_statistics_item");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderStatisticsLayout", b =>
                {
                    b.Property<long>("transponder_statistics_item_id")
                        .HasColumnType("bigint")
                        .HasColumnName("transponder_statistics_item_id");

                    b.Property<long>("track_layout_passing_id")
                        .HasColumnType("bigint")
                        .HasColumnName("track_layout_passing_id");

                    b.HasKey("transponder_statistics_item_id", "track_layout_passing_id")
                        .HasName("pk_transponder_statistics_layout");

                    b.HasIndex("track_layout_passing_id")
                        .HasDatabaseName("ix_transponder_statistics_layout_track_layout_passing_id");

                    b.ToTable("transponder_statistics_layout");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderType", b =>
                {
                    b.Property<string>("System")
                        .HasColumnType("text")
                        .HasColumnName("system");

                    b.HasKey("System")
                        .HasName("pk_transponder_type");

                    b.ToTable("transponder_type");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Passing", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TimingLoop", "Loop")
                        .WithMany("Passings")
                        .HasForeignKey("LoopId")
                        .HasConstraintName("fk_passing_timing_loop_loop_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Transponder", "Transponder")
                        .WithMany("Passings")
                        .HasForeignKey("TransponderId")
                        .HasConstraintName("fk_passing_transponder_transponder_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Loop");

                    b.Navigation("Transponder");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TimingLoop", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.Track", "Track")
                        .WithMany("TimingLoops")
                        .HasForeignKey("TrackId")
                        .HasConstraintName("fk_timing_loop_track_track_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Track");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackLayout", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.Track", "Track")
                        .WithMany("Layouts")
                        .HasForeignKey("TrackId")
                        .HasConstraintName("fk_track_layout_track_track_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Track");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackLayoutPassing", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TrackLayout", "TrackLayout")
                        .WithMany()
                        .HasForeignKey("TrackLayoutId")
                        .HasConstraintName("fk_track_layout_passing_track_layout_track_layout_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Transponder", "Transponder")
                        .WithMany()
                        .HasForeignKey("TransponderId")
                        .HasConstraintName("fk_track_layout_passing_transponder_transponder_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("TrackLayout");

                    b.Navigation("Transponder");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackLayoutSegment", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TrackLayout", "Layout")
                        .WithMany("Segments")
                        .HasForeignKey("LayoutId")
                        .HasConstraintName("fk_track_layout_segment_track_layout_layout_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.TrackSegment", "Segment")
                        .WithMany()
                        .HasForeignKey("SegmentId")
                        .HasConstraintName("fk_track_layout_segment_track_segment_segment_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Layout");

                    b.Navigation("Segment");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackSegment", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TimingLoop", "End")
                        .WithMany()
                        .HasForeignKey("EndId")
                        .HasConstraintName("fk_track_segment_timing_loop_end_id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.TimingLoop", "Start")
                        .WithMany()
                        .HasForeignKey("StartId")
                        .HasConstraintName("fk_track_segment_timing_loop_start_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("End");

                    b.Navigation("Start");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackSegmentPassing", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.Passing", "End")
                        .WithMany()
                        .HasForeignKey("EndId")
                        .HasConstraintName("fk_track_segment_passing_passing_end_id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Passing", "Start")
                        .WithMany()
                        .HasForeignKey("StartId")
                        .HasConstraintName("fk_track_segment_passing_passing_start_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.TrackLayoutPassing", null)
                        .WithMany("Passings")
                        .HasForeignKey("TrackLayoutPassingId")
                        .HasConstraintName("fk_track_segment_passing_track_layout_passing_track_layout_pas~");

                    b.HasOne("VeloTimer.Shared.Models.TrackSegment", "TrackSegment")
                        .WithMany()
                        .HasForeignKey("TrackSegmentId")
                        .HasConstraintName("fk_track_segment_passing_track_segment_track_segment_id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Transponder", "Transponder")
                        .WithMany()
                        .HasForeignKey("TransponderId")
                        .HasConstraintName("fk_track_segment_passing_transponder_transponder_id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("End");

                    b.Navigation("Start");

                    b.Navigation("TrackSegment");

                    b.Navigation("Transponder");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackStatisticsItem", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TrackLayout", "Layout")
                        .WithMany()
                        .HasForeignKey("LayoutId")
                        .HasConstraintName("fk_track_statistics_item_track_layout_layout_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.StatisticsItem", "StatisticsItem")
                        .WithMany()
                        .HasForeignKey("StatisticsItemId")
                        .HasConstraintName("fk_track_statistics_item_statistics_item_statistics_item_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Layout");

                    b.Navigation("StatisticsItem");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Transponder", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TransponderType", "TimingSystemRelation")
                        .WithMany()
                        .HasForeignKey("TimingSystem")
                        .HasConstraintName("fk_transponder_transponder_type_timing_system")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("TimingSystemRelation");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderOwnership", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.Rider", "Owner")
                        .WithMany("Transponders")
                        .HasForeignKey("OwnerId")
                        .HasConstraintName("fk_transponder_ownership_rider_owner_id");

                    b.HasOne("VeloTimer.Shared.Models.Transponder", "Transponder")
                        .WithMany("Owners")
                        .HasForeignKey("TransponderId")
                        .HasConstraintName("fk_transponder_ownership_transponder_transponder_id");

                    b.Navigation("Owner");

                    b.Navigation("Transponder");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderStatisticsItem", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TrackStatisticsItem", "StatisticsItem")
                        .WithMany()
                        .HasForeignKey("StatisticsItemId")
                        .HasConstraintName("fk_transponder_statistics_item_track_statistics_item_statistic~")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.Transponder", "Transponder")
                        .WithMany()
                        .HasForeignKey("TransponderId")
                        .HasConstraintName("fk_transponder_statistics_item_transponder_transponder_id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("StatisticsItem");

                    b.Navigation("Transponder");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderStatisticsLayout", b =>
                {
                    b.HasOne("VeloTimer.Shared.Models.TrackLayoutPassing", "LayoutPassing")
                        .WithMany()
                        .HasForeignKey("track_layout_passing_id")
                        .HasConstraintName("fk_transponder_statistics_layout_track_layout_passing_track_la~")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("VeloTimer.Shared.Models.TransponderStatisticsItem", "TransponderStatisticsItem")
                        .WithMany("LayoutPassingList")
                        .HasForeignKey("transponder_statistics_item_id")
                        .HasConstraintName("fk_transponder_statistics_layout_transponder_statistics_item_t~")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("LayoutPassing");

                    b.Navigation("TransponderStatisticsItem");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Rider", b =>
                {
                    b.Navigation("Transponders");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TimingLoop", b =>
                {
                    b.Navigation("Passings");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Track", b =>
                {
                    b.Navigation("Layouts");

                    b.Navigation("TimingLoops");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackLayout", b =>
                {
                    b.Navigation("Segments");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TrackLayoutPassing", b =>
                {
                    b.Navigation("Passings");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.Transponder", b =>
                {
                    b.Navigation("Owners");

                    b.Navigation("Passings");
                });

            modelBuilder.Entity("VeloTimer.Shared.Models.TransponderStatisticsItem", b =>
                {
                    b.Navigation("LayoutPassingList");
                });
#pragma warning restore 612, 618
        }
    }
}
