using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VeloTime.Module.Timing.Client;
using VeloTime.Module.Timing.Interface;
using VeloTime.Users.Migration.Services;
using VeloTimer.Shared.Data.Models.Timing;

namespace VeloTime.Workers.Test;

public sealed class OwnershipMigrationTests
{
    private static readonly DateTime Start = new(2026, 3, 28, 12, 34, 56, DateTimeKind.Utc);
    private const string SystemId = "106187718";

    [Fact]
    public void EmptySourceHasNoRegistrationsOrIssues()
    {
        var plan = OwnershipPreflight.Create([]);
        Assert.Empty(plan.Registrations);
        Assert.Empty(plan.Issues);
        Assert.Equal(0, plan.SourceCount);
        Assert.Equal(0, plan.NormalizedCount);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    public void GapThresholdIsStrictlyFifteenMinutes(long extraTicks, int expectedCount)
    {
        var end = Start.AddHours(1);
        var secondStart = end.AddMinutes(15).AddTicks(extraTicks);
        var plan = OwnershipPreflight.Create([
            Row(2, secondStart, secondStart.AddHours(1)),
            Row(1, Start, end)
        ]);

        Assert.Empty(plan.Issues);
        Assert.Equal(expectedCount, plan.Registrations.Count);
        Assert.Equal(new DateTimeOffset(Start), plan.Registrations[0].OwnedFrom);
        Assert.Equal(new DateTimeOffset((expectedCount == 1 ? secondStart.AddHours(1) : end).AddTicks(10)),
            plan.Registrations[0].OwnedTo);
    }

    [Fact]
    public void UnsortedDuplicatesOverlapsTouchingNestedAndChainedPeriodsMerge()
    {
        var rows = new[]
        {
            Row(6, Start.AddMinutes(70), Start.AddMinutes(90)),
            Row(3, Start.AddMinutes(10), Start.AddMinutes(20)),
            Row(1, Start, Start.AddMinutes(30)),
            Row(5, Start.AddMinutes(50), Start.AddMinutes(60)),
            Row(4, Start.AddMinutes(30), Start.AddMinutes(40)),
            Row(2, Start, Start.AddMinutes(30))
        };
        var plan = OwnershipPreflight.Create(rows);

        Assert.Empty(plan.Issues);
        var period = Assert.Single(plan.Registrations);
        Assert.Equal("HT-24422", period.Code);
        Assert.Equal(new DateTimeOffset(Start), period.OwnedFrom);
        Assert.Equal(new DateTimeOffset(Start.AddMinutes(90).AddTicks(10)), period.OwnedTo);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, period.SourceIds);
        Assert.Equal(6, plan.SourceCount);
        Assert.Equal(1, plan.NormalizedCount);
        Assert.Equal(Start.AddMinutes(30), rows[2].OwnedUntil);
    }

    [Fact]
    public void OpenEndAbsorbsAllLaterPeriodsWithoutLosingEarlierStart()
    {
        var plan = OwnershipPreflight.Create([
            Row(3, Start.AddYears(1), Start.AddYears(2)),
            Row(2, Start.AddMinutes(5), null),
            Row(1, Start, Start.AddMinutes(10))
        ]);

        var period = Assert.Single(plan.Registrations);
        Assert.Empty(plan.Issues);
        Assert.Equal(new DateTimeOffset(Start), period.OwnedFrom);
        Assert.Null(period.OwnedTo);
        Assert.Equal(3, period.SourceIds.Count);
    }

    [Fact]
    public void DifferentRidersAndTranspondersStaySeparateAndNumericIdsAreCanonical()
    {
        var plan = OwnershipPreflight.Create([
            Row(1, Start, Start.AddHours(1)) with { SystemId = "0106187718" },
            Row(2, Start.AddMinutes(30), Start.AddHours(2)),
            Row(3, Start, null) with { SystemId = "106187719" },
            Row(4, Start.AddDays(1), null) with { RiderId = 2 }
        ]);

        Assert.Empty(plan.Issues);
        Assert.Equal(3, plan.Registrations.Count);
        Assert.Equal(2, plan.Registrations.Count(period => period.RiderId == 1));
        Assert.Equal(2, plan.Registrations.Single(period => period.RiderId == 1 && period.Code == "HT-24422").SourceIds.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossOwnerOverlapBlocksBothGroupsIncludingOtherPeriods(bool introducedByMerge)
    {
        var firstEnd = introducedByMerge ? Start.AddHours(1) : Start.AddHours(2);
        var plan = OwnershipPreflight.Create([
            Row(1, Start, firstEnd),
            Row(2, Start.AddMinutes(70), Start.AddHours(2)),
            Row(3, Start.AddMinutes(62), Start.AddMinutes(68)) with { RiderId = 2 },
            Row(4, Start.AddDays(1), Start.AddDays(2)),
            Row(5, Start, null) with { SystemId = "106187719" }
        ]);

        Assert.Single(plan.Issues);
        Assert.Contains("conflicting riders", plan.Issues[0]);
        var independent = Assert.Single(plan.Registrations);
        Assert.Equal("HT-24423", independent.Code);
        Assert.Equal(new long[] { 5 }, independent.SourceIds);
    }

    [Fact]
    public void InclusiveLegacyEndpointCannotBeSharedByDifferentOwners()
    {
        var plan = OwnershipPreflight.Create([
            Row(1, Start, Start.AddHours(1)),
            Row(2, Start.AddHours(1), null) with { RiderId = 2 }
        ]);

        Assert.Empty(plan.Registrations);
        Assert.Single(plan.Issues);
    }

    [Fact]
    public void SeparateOwnersWithDisjointCoverageAreNotBlocked()
    {
        var plan = OwnershipPreflight.Create([
            Row(1, Start, Start.AddHours(1)),
            Row(2, Start.AddHours(1).AddTicks(10), null) with { RiderId = 2 }
        ]);
        Assert.Empty(plan.Issues);
        Assert.Equal(2, plan.Registrations.Count);
    }

    [Theory]
    [InlineData("zero-start")]
    [InlineData("equal-end")]
    [InlineData("reversed-end")]
    [InlineData("unspecified-start")]
    [InlineData("unspecified-end")]
    [InlineData("overflow-end")]
    [InlineData("bad-id")]
    [InlineData("out-of-range-id")]
    [InlineData("unsupported-system")]
    public void InvalidGroupIsReportedAndWithheld(string invalid)
    {
        var row = Row(1, Start, Start.AddHours(1));
        row = invalid switch
        {
            "zero-start" => row with { OwnedFrom = default },
            "equal-end" => row with { OwnedUntil = Start },
            "reversed-end" => row with { OwnedUntil = Start.AddTicks(-1) },
            "unspecified-start" => row with { OwnedFrom = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified) },
            "unspecified-end" => row with { OwnedUntil = DateTime.SpecifyKind(Start.AddHours(1), DateTimeKind.Unspecified) },
            "overflow-end" => row with { OwnedUntil = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) },
            "bad-id" => row with { SystemId = "not-numeric" },
            "out-of-range-id" => row with { SystemId = long.MaxValue.ToString() },
            "unsupported-system" => row with { System = (TransponderType.TimingSystem)999 },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        var plan = OwnershipPreflight.Create([row, row with { RowId = 2, OwnedFrom = Start, OwnedUntil = null }]);

        Assert.Empty(plan.Registrations);
        Assert.Single(plan.Issues);
        Assert.Contains("source rows [1, 2]", plan.Issues[0]);
    }

    [Fact]
    public async Task WriterSubmitsOnlyAdminRegistrationWithMergedDatesAndExplicitOwner()
    {
        var plan = OwnershipPreflight.Create([
            Row(1, Start.AddTicks(3), Start.AddHours(1)),
            Row(2, Start.AddMinutes(70), Start.AddHours(2))
        ]);
        using var handler = new RegistrationHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new("http://timing/") };
        var writer = Writer(http, plan);
        var ownerId = Guid.NewGuid();

        writer.LogPreflight();
        await writer.RegisterRiderAsync(1, ownerId, CancellationToken.None);
        writer.Complete();

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/admin/ownerships/register", request.Path);
        Assert.Equal("HT-24422", request.Body.GetProperty("code").GetString());
        Assert.Equal(ownerId, request.Body.GetProperty("ownerId").GetGuid());
        Assert.Equal(new DateTimeOffset(Start.AddTicks(3)), request.Body.GetProperty("ownedFrom").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(Start.AddHours(2).AddTicks(10)), request.Body.GetProperty("ownedTo").GetDateTimeOffset());
        Assert.True(!request.Body.TryGetProperty("confirmationToken", out var confirmation) || confirmation.ValueKind == JsonValueKind.Null);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    public async Task UnresolvedRegistrationContinuesIndependentPeriodsButFailsCompletion(int status)
    {
        var plan = TwoPeriods();
        using var handler = new RegistrationHandler((HttpStatusCode)status, HttpStatusCode.OK)
        {
            RequireConfirmation = status == 200
        };
        using var http = new HttpClient(handler) { BaseAddress = new("http://timing/") };
        var writer = Writer(http, plan);

        await writer.RegisterRiderAsync(1, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var failure = Assert.Throws<AggregateException>(writer.Complete);
        var issue = Assert.Single(failure.InnerExceptions);
        Assert.Contains("source rows [1]", issue.Message);
        Assert.Contains(status == 200 ? "shortening requires review" : $"HTTP {status}", issue.Message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task AuthenticationAndServerFailuresStopImmediately(int status)
    {
        using var handler = new RegistrationHandler((HttpStatusCode)status, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new("http://timing/") };
        var writer = Writer(http, TwoPeriods());

        var failure = await Assert.ThrowsAsync<ApiException>(() => writer.RegisterRiderAsync(1, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(status, failure.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CancellationIsNotReportedAsAnOrdinaryConflict()
    {
        using var handler = new RegistrationHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new("http://timing/") };
        var writer = Writer(http, TwoPeriods());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.RegisterRiderAsync(1, Guid.NewGuid(), cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SourceConflictsAreWithheldBeforeAnyRegistrationWrites()
    {
        var plan = OwnershipPreflight.Create([
            Row(1, Start, null),
            Row(2, Start.AddDays(1), null) with { RiderId = 2 }
        ]);
        using var handler = new RegistrationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://timing/") };
        var writer = Writer(http, plan);

        writer.LogPreflight();
        await writer.RegisterRiderAsync(1, Guid.NewGuid(), CancellationToken.None);
        await writer.RegisterRiderAsync(2, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.Throws<AggregateException>(writer.Complete);
    }

    [Fact]
    public void SourceIssuesAndMissingIdentityFailCompletion()
    {
        using var handler = new RegistrationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://timing/") };
        var writer = Writer(http, TwoPeriods() with { Issues = ["source conflict"] });
        writer.MissingOwner(1);

        var failure = Assert.Throws<AggregateException>(writer.Complete);
        Assert.Equal(3, failure.InnerExceptions.Count);
        Assert.Contains(failure.InnerExceptions, issue => issue.Message == "source conflict");
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ExtractedCodeConversionRetainsKnownMappings()
    {
        Assert.Equal("HT-24422", MylapsX2Code.IdToCode(106187718));
        Assert.Equal(106187718, MylapsX2Code.CodeToId("HT-24422"));
    }

    private static SourceOwnership Row(long id, DateTime from, DateTime? until) =>
        new(1, id, TransponderType.TimingSystem.Mylaps_X2, SystemId, from, until);

    private static OwnershipPreflightResult TwoPeriods() => OwnershipPreflight.Create([
        Row(1, Start, Start.AddHours(1)),
        Row(2, Start.AddDays(1), null)
    ]);

    private static OwnershipMigrationWriter Writer(HttpClient http, OwnershipPreflightResult plan) =>
        new(new TimingClient(http), NullLogger.Instance, plan);

    private sealed class RegistrationHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        internal List<(string Path, JsonElement Body)> Requests { get; } = [];
        internal bool RequireConfirmation { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/admin/ownerships/register", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add((request.RequestUri.AbsolutePath, json.RootElement.Clone()));
            var status = statuses[Requests.Count - 1];
            var confirmation = RequireConfirmation && Requests.Count == 1;
            return new(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK
                    ? JsonSerializer.Serialize(new { requiresConfirmation = confirmation, confirmationToken = confirmation ? "review-token" : null, existingOwnerships = Array.Empty<object>() })
                    : "{\"detail\":\"Registration rejected\"}", Encoding.UTF8, "application/json")
            };
        }
    }
}
