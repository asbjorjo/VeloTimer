using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Facilities.Api;
using VeloTime.Module.Facilities.Api.Test;
using VeloTime.Module.Facilities.Interface.Data;
using VeloTime.Module.Facilities.Interface.Messages;
using VeloTime.Module.Facilities.Storage;

namespace VeloTime.Module.Facilities.Test;

public sealed class TimingAssociationMessagingTests
{
    internal const string TestBusConnection = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingConnectionFailsExplicitlyEvenInDevelopment(string? connectionString)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MessageBus"] = connectionString });
        Assert.Contains("ConnectionStrings:MessageBus is required",
            Assert.Throws<InvalidOperationException>(() => builder.AddCourseConfigMessaging()).Message);
    }

    [Fact]
    public void RegisteredOutboxRetainsDispatchedRowsAndUsesFacilitySchema()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:MessageBus"] = TestBusConnection;
        builder.Services.AddDbContext<FacilityDbContext>(options => options.UseNpgsql("Host=localhost;Database=unused"));
        builder.AddCourseConfigMessaging();
        using var host = builder.Build(); // Do not start: no database or live bus.
        var settings = host.Services.GetRequiredService<PostgreSqlOutboxSettings>();
        Assert.Equal("facility", settings.PostgreSqlSettings.DatabaseSchemaName);
        Assert.False(settings.MessageCleanup.Enabled);
        Assert.True(settings.MaintainSequence);
        Assert.Equal(1, settings.PollBatchSize);
        Assert.Equal(int.MaxValue, settings.MaxDeliveryAttempts);
        Assert.Equal("velotime-facilities", MessagingExtensions.CourseConfigTopic);
        Assert.Contains(builder.Services, registration => registration.ServiceType == typeof(ICourseConfigEventPublisher)
            && registration.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void RegisteredSerializerRoundTripsStableIdsLosslessDeltasAndUtcNullBounds()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:MessageBus"] = TestBusConnection;
        builder.AddCourseConfigMessaging();
        using var host = builder.Build();
        var serializer = host.Services.GetRequiredService<JsonMessageSerializer>();
        var original = Event();
        var prepared = CourseConfigEventPublisher.Prepare(original);
        var json = JsonSerializer.Serialize(prepared, serializer.Options);
        var restored = JsonSerializer.Deserialize<CourseConfigChangedV1>(json, serializer.Options)!;
        Assert.Equal(original.EventId, restored.EventId);
        Assert.Equal(original.ChangeId, restored.ChangeId);
        Assert.Equal(original.FacilityId, restored.FacilityId);
        Assert.Equal(original.OccurredAtUtc, restored.OccurredAtUtc);
        Assert.Equal(TimeSpan.Zero, restored.OccurredAtUtc.Offset);
        Assert.Equal(CourseConfigChangeKind.TimingAssociation, restored.Kind);
        Assert.Contains("\"kind\":\"TimingAssociation\"", json);
        Assert.Equal(prepared.AssociationChanges, restored.AssociationChanges);
        Assert.Null(restored.AssociationChanges[0].Before!.ValidFromUtc);
        Assert.Null(restored.AssociationChanges[1].After!.ValidToUtc);
        Assert.Null(restored.AssociationChanges[2].Before);
        Assert.Null(restored.AssociationChanges[3].After);
        Assert.All(restored.AssociationChanges.SelectMany(change => new[] { change.Before, change.After })
            .OfType<CoursePointTimingAssociationSnapshot>(), snapshot =>
        {
            if (snapshot.ValidFromUtc is { } from) Assert.Equal(TimeSpan.Zero, from.Offset);
            if (snapshot.ValidToUtc is { } to) Assert.Equal(TimeSpan.Zero, to.Offset);
        });
        Assert.Equal(json, JsonSerializer.Serialize(CourseConfigEventPublisher.Prepare(prepared), serializer.Options));
        Assert.NotEqual(TimeSpan.Zero, original.OccurredAtUtc.Offset); // Caller data wasn't mutated.
    }

    [Fact]
    public void MalformedEventsAreRejectedRatherThanDropped()
    {
        Assert.Throws<ArgumentException>(() => CourseConfigEventPublisher.Prepare(Event() with { EventId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => CourseConfigEventPublisher.Prepare(Event() with { ChangeId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => CourseConfigEventPublisher.Prepare(Event() with { FacilityId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => CourseConfigEventPublisher.Prepare(Event() with { AssociationChanges = null! }));
        Assert.Throws<ArgumentException>(() => CourseConfigEventPublisher.Prepare(Event() with { AssociationChanges = [] }));
        Assert.Throws<ArgumentException>(() => CourseConfigEventPublisher.Prepare(Event() with { AssociationChanges = [new(null, null)] }));
        Assert.Throws<ArgumentException>(() => CourseConfigEventPublisher.Prepare(Event() with
            { Kind = CourseConfigChangeKind.LayoutRevision, CourseLayoutId = null }));
        Assert.Throws<ArgumentException>(() => CourseConfigEventPublisher.Prepare(Event() with
            { AffectedFromUtc = DateTimeOffset.UnixEpoch, AffectedToUtc = DateTimeOffset.UnixEpoch }));
    }

    [Fact]
    public async Task TestOnlyPublisherRecordsAndPropagatesFailuresAndCancellation()
    {
        var spy = new RecordingTimingAssociationPublisher();
        var message = Event();
        await spy.EnqueueAsync(message);
        Assert.Same(message, Assert.Single(spy.Enqueued));
        spy.Failure = new InvalidOperationException("injected enqueue failure");
        Assert.Same(spy.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() => spy.EnqueueAsync(message)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => spy.EnqueueAsync(message, new CancellationToken(true)));
        Assert.Single(spy.Enqueued);
    }

    internal static CourseConfigChangedV1 Event()
    {
        var instant = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.FromHours(2));
        var point = Guid.NewGuid();
        var timing = Guid.NewGuid();
        return new(Guid.NewGuid(), Guid.NewGuid(), instant, Guid.NewGuid(), CourseConfigChangeKind.TimingAssociation, null, null, null,
        [
            new(new(timing, point, null, instant), new(timing, point, null, instant.AddHours(1))),
            new(new(timing, point, instant, null), new(timing, point, instant.AddHours(1), null)),
            new(null, new(timing, point, null, null)),
            new(new(timing, point, null, null), null)
        ]);
    }
}
