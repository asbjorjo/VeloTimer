extern alias FacilitiesClientAssembly;

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Model;
using VeloTime.Module.Facilities.Storage;
using Client = FacilitiesClientAssembly::VeloTime.Module.Facilities.Client;

namespace VeloTime.Module.Facilities.Api.Test;

public sealed class TimingAssociationConcurrencyResponseTests
{
    private static readonly DateTimeOffset T = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TimingAssociationPostgresFact]
    public async Task DatabaseFailures_RollBackAndReturnFreshConfirmableContractWithoutEvents()
    {
        foreach (var sqlState in new[] { "23P01", "23514", "23503", "40P01", "40001" })
        {
            var fault = new RollbackFault();
            await using var factory = new FacilitiesApiFactory
            {
                ConfigureTestServices = services => services.AddDbContext<FacilityDbContext>(
                    options => options.AddInterceptors(fault))
            };
            await factory.InitializeDatabaseAsync();
            var source = Guid.NewGuid();
            var occupant = Guid.NewGuid();
            var destinationTiming = Guid.NewGuid();
            var originalSourceTiming = Guid.NewGuid();
            var freshSourceTiming = Guid.NewGuid();
            var facility = Guid.NewGuid();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
                var start = new CoursePoint { Id = source };
                var end = new CoursePoint { Id = occupant };
                storage.Add(new Facility { Id = facility, Name = "Track" });
                storage.Add(new CourseLayout
                {
                    Id = Guid.NewGuid(), FacilityId = facility,
                    Segments = [new CourseSegment { Id = Guid.NewGuid(), Order = 0, Start = start, End = end, Length = 250 }]
                });
                storage.AddRange(
                    new CoursePointTimingAssociation
                    {
                        Id = Guid.NewGuid(), CoursePointId = source, TimingPointId = originalSourceTiming
                    },
                    new CoursePointTimingAssociation
                    {
                        Id = Guid.NewGuid(), CoursePointId = occupant, TimingPointId = destinationTiming,
                        ValidFromUtc = T
                    });
                await storage.SaveChangesAsync();
            }
            factory.References.FacilityId = facility;
            using var client = factory.CreateClient();
            var route = $"/api/facilities/coursepoint/{source}/timing-association";
            var request = new SetCoursePointTimingAssociationRequest
            {
                TimingPointId = destinationTiming,
                ValidFromUtc = T.ToOffset(TimeSpan.FromHours(2)).AddTicks(9),
                ValidToUtc = T.AddDays(2)
            };
            var original = await PreviewAsync(client, route, request, "destination-occupied");

            // Non-cooperating writer commits after the locked source/destination reads.
            // Then fail on the real database AFTER EF saved/accepted the edit, before enqueue.
            fault.BeforeSave = async (storage, cancellationToken) =>
            {
                await using var connection = new NpgsqlConnection(storage.Database.GetConnectionString());
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("""
                    UPDATE facility.course_point_timing_association
                    SET timing_point_id = @timing WHERE course_point_id = @source;
                    UPDATE facility.course_point_timing_association
                    SET valid_from_utc = @from WHERE course_point_id = @occupant;
                    """, connection);
                command.Parameters.AddWithValue("timing", freshSourceTiming);
                command.Parameters.AddWithValue("source", source);
                command.Parameters.AddWithValue("occupant", occupant);
                command.Parameters.AddWithValue("from", T.AddDays(1));
                await command.ExecuteNonQueryAsync(cancellationToken);
            };
            fault.SqlState = sqlState;
            var fresh = await PreviewAsync(client, route,
                request with { ConfirmationToken = original.ConfirmationToken }, "confirmation-stale");
            Assert.Equal(sqlState, fault.ObservedSqlState);
            Assert.Equal(new TimingAssociationRequestedChange(source, destinationTiming, T, T.AddDays(2)), fresh.Requested);
            var conflict = Assert.Single(fresh.Conflicts);
            Assert.Equal(T.AddDays(1), conflict.Association.ValidFromUtc);
            Assert.Equal(T.AddDays(1), conflict.OverlapValidFromUtc);
            Assert.Equal(T.AddDays(2), conflict.OverlapValidToUtc);
            Assert.Empty(factory.Events.Enqueued);
            Assert.Equal(0, factory.OutputCache.EvictionCalls);
            var sourceHistory = await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
                $"/api/facilities/coursepoint/{source}/timing-associations");
            Assert.Equal(new CoursePointTimingAssociationSnapshot(freshSourceTiming, source, null, null),
                Assert.Single(sourceHistory!));
            var destinationHistory = await client.GetFromJsonAsync<CoursePointTimingAssociationSnapshot[]>(
                $"/api/facilities/coursepoint/{occupant}/timing-associations");
            Assert.Equal(new CoursePointTimingAssociationSnapshot(destinationTiming, occupant, T.AddDays(1), null),
                Assert.Single(destinationHistory!));

            // Fresh approval cannot be reused for another point, target, or interval.
            foreach (var changed in new[]
            {
                request with { TimingPointId = Guid.NewGuid() },
                request with { ValidToUtc = T.AddDays(3) }
            })
                await PreviewAsync(client, route, changed with { ConfirmationToken = fresh.ConfirmationToken }, "confirmation-stale");
            await PreviewAsync(client, $"/api/facilities/coursepoint/{occupant}/timing-association",
                request with { ConfirmationToken = fresh.ConfirmationToken }, "confirmation-stale");
            Assert.Empty(factory.Events.Enqueued);

            // A later source-only edit invalidates the token even if visible conflicts match.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var storage = scope.ServiceProvider.GetRequiredService<FacilityDbContext>();
                await storage.Set<CoursePointTimingAssociation>().Where(a => a.CoursePointId == source)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.TimingPointId, originalSourceTiming));
            }
            var latest = await PreviewAsync(client, route,
                request with { ConfirmationToken = fresh.ConfirmationToken }, "confirmation-stale");
            Assert.Equal(fresh.Conflicts, latest.Conflicts);
            using var accepted = await client.PutAsJsonAsync(route,
                request with { ConfirmationToken = latest.ConfirmationToken });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var result = (await accepted.Content.ReadFromJsonAsync<SetCoursePointTimingAssociationResponse>())!;
            Assert.NotNull(result.ChangeId);
            var message = Assert.Single(factory.Events.Enqueued);
            Assert.Equal(result.ChangeId, message.ChangeId);
            Assert.Equal(result.Changes, message.Changes);
            Assert.Contains(result.Changes, change =>
                change.Before?.TimingPointId == originalSourceTiming &&
                change.After?.TimingPointId == destinationTiming &&
                change.After.ValidFromUtc == T && change.After.ValidToUtc == T.AddDays(2));

            // Even an unconfirmed unlink with no destination conflicts must return a
            // state-bound stale preview, not rerun the write or invent a destination conflict.
            factory.Events.Reset();
            var unlink = request with { TimingPointId = null, ConfirmationToken = null };
            fault.SqlState = sqlState;
            var unlinkPreview = await PreviewAsync(client, route, unlink, "confirmation-stale");
            Assert.Null(unlinkPreview.Requested.TimingPointId);
            Assert.Empty(unlinkPreview.Conflicts);
            Assert.Empty(factory.Events.Enqueued);
            using var unlinked = await client.PutAsJsonAsync(route,
                unlink with { ConfirmationToken = unlinkPreview.ConfirmationToken });
            Assert.Equal(HttpStatusCode.OK, unlinked.StatusCode);
            Assert.Single(factory.Events.Enqueued);
        }
    }

    private static async Task<TimingAssociationConflictPreview> PreviewAsync(HttpClient client, string route,
        SetCoursePointTimingAssociationRequest request, string code)
    {
        // Exercise the actual OpenAPI-generated consumer, not just permissive DTO parsing.
        var api = new Client.FacilitiesClient(client);
        var exception = await Assert.ThrowsAsync<Client.ApiException<Client.TimingAssociationConflictPreview>>(
            () => api.SetCoursePointTimingAssociationAsync(Guid.Parse(route.Split('/')[4]),
                new Client.SetCoursePointTimingAssociationRequest
                {
                    TimingPointId = request.TimingPointId, ValidFromUtc = request.ValidFromUtc,
                    ValidToUtc = request.ValidToUtc, ConfirmationToken = request.ConfirmationToken
                }));
        Assert.Equal(409, exception.StatusCode);
        var result = exception.Result;
        Assert.NotNull(result);
        Assert.NotNull(result.Requested);
        Assert.NotNull(result.Conflicts);
        var preview = new TimingAssociationConflictPreview(result.Code,
            new TimingAssociationRequestedChange(result.Requested.CoursePointId, result.Requested.TimingPointId,
                result.Requested.ValidFromUtc, result.Requested.ValidToUtc),
            result.Conflicts.Select(conflict => new TimingAssociationDestinationConflict(
                new CoursePointTimingAssociationSnapshot(conflict.Association.TimingPointId,
                    conflict.Association.CoursePointId, conflict.Association.ValidFromUtc, conflict.Association.ValidToUtc),
                conflict.OverlapValidFromUtc, conflict.OverlapValidToUtc)).ToArray(),
            result.ConfirmationToken);
        Assert.Equal(code, preview.Code);
        Assert.NotNull(preview.Requested);
        Assert.NotNull(preview.Conflicts);
        Assert.False(string.IsNullOrWhiteSpace(preview.ConfirmationToken));
        return preview;
    }

    private sealed class RollbackFault : SaveChangesInterceptor
    {
        public Func<FacilityDbContext, CancellationToken, Task>? BeforeSave { get; set; }
        public string? SqlState { get; set; }
        public string? ObservedSqlState { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeSave is { } before)
            {
                BeforeSave = null;
                await before((FacilityDbContext)eventData.Context!, cancellationToken);
            }
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (SqlState is not { } code) return result;
            SqlState = null;
            var sql = code switch
            {
                "23P01" => "DO $$ BEGIN RAISE EXCEPTION 'rollback regression' USING ERRCODE = '23P01'; END $$;",
                "23514" => "DO $$ BEGIN RAISE EXCEPTION 'rollback regression' USING ERRCODE = '23514'; END $$;",
                "23503" => "DO $$ BEGIN RAISE EXCEPTION 'rollback regression' USING ERRCODE = '23503'; END $$;",
                "40P01" => "DO $$ BEGIN RAISE EXCEPTION 'rollback regression' USING ERRCODE = '40P01'; END $$;",
                "40001" => "DO $$ BEGIN RAISE EXCEPTION 'rollback regression' USING ERRCODE = '40001'; END $$;",
                _ => throw new InvalidOperationException("Unsupported database fault.")
            };
            try
            {
                // Fixed test inputs only; executes on the edit's actual PostgreSQL transaction.
                await eventData.Context!.Database.ExecuteSqlRawAsync(
                    sql,
                    cancellationToken);
            }
            catch (PostgresException exception)
            {
                ObservedSqlState = exception.SqlState;
                throw;
            }
            throw new InvalidOperationException("Database fault did not fire.");
        }
    }
}
