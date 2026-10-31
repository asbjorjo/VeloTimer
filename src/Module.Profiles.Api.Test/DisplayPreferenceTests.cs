using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VeloTime.Module.Profiles.Interface.Data;
using VeloTime.Module.Profiles.Service;
using VeloTime.Module.Profiles.Storage;
namespace VeloTime.Module.Profiles.Api.Test;
public sealed class DisplayPreferenceTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private const string Route = "/api/profiles/me/preferences/display";
    public static TheoryData<string, string> InvalidRequests => new()
    {
        { "null", "profiles.invalid_distance_unit" }, { "{}", "profiles.invalid_distance_unit" },
        { Json(null, "km/h", "browser", null), "profiles.invalid_distance_unit" },
        { Json("M", "km/h", "browser", null), "profiles.invalid_distance_unit" },
        { Json("ft ", "km/h", "browser", null), "profiles.invalid_distance_unit" },
        { Json("m", null, "browser", null), "profiles.invalid_speed_unit" },
        { Json("m", "MPH", "browser", null), "profiles.invalid_speed_unit" },
        { Json("m", "km/h", null, null), "profiles.invalid_time_mode" },
        { Json("m", "km/h", "timezone", null), "profiles.invalid_time_mode" },
        { Json("m", "km/h", "timeZone", null), "profiles.time_zone_required" },
        { Json("m", "km/h", "timeZone", " "), "profiles.time_zone_required" },
        { Json("m", "km/h", "timeZone", "europe/oslo"), "profiles.invalid_time_zone" },
        { Json("m", "km/h", "timeZone", "Europe/Oslo "), "profiles.invalid_time_zone" },
        { Json("m", "km/h", "timeZone", "Eastern Standard Time"), "profiles.invalid_time_zone" },
        { Json("m", "km/h", "utc", "UTC"), "profiles.time_zone_not_allowed" },
        { Json("m", "km/h", "browser", ""), "profiles.time_zone_not_allowed" }
    };
    private static string Json(string? distance, string? speed, string? mode, string? zone) =>
        JsonSerializer.Serialize(new { distanceUnit = distance, speedUnit = speed, timeMode = mode, timeZoneId = zone });
    [Theory] [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequests_HaveStableProblemCodes_BeforeDatabaseAccess(string json, string code)
    {
        using var factory = new ProfilesApiFactory(); using var client = factory.CreateContractClient();
        using var response = await client.PutAsync(Route, new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.Empty(factory.Preferences.Calls);
    }
    [Theory] [InlineData("GET", "/api/profiles/me/preferences/display")] [InlineData("PUT", "/api/profiles/me/preferences/display")] [InlineData("GET", "/api/profiles/me/preferences/time-zones")]
    public async Task AllDisplayRoutes_RequireAuthentication(string method, string route)
    {
        using var factory = new ProfilesApiFactory { Principal = null }; using var client = factory.CreateContractClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "PUT") request.Content = JsonContent.Create(new UpdateDisplayPreferenceRequest("m", "km/h", "browser", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }
    [Theory] [MemberData(nameof(PreferenceEndpointTests.InvalidIdentities), MemberType = typeof(PreferenceEndpointTests))]
    public async Task InvalidIdentity_IsForbiddenBeforeDisplayReadOrWrite(string? issuer, string? subject)
    {
        using var factory = new ProfilesApiFactory { Principal = ProfilesApiFactory.Identity(issuer, subject) }; using var client = factory.CreateContractClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route)).StatusCode);
        using var response = await client.PutAsJsonAsync(Route, new UpdateDisplayPreferenceRequest("m", "km/h", "browser", null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("profiles.invalid_identity", body.RootElement.GetProperty("code").GetString());
    }
    [PostgresFact]
    public async Task DefaultsNeverInsert_SeparateWritesAreDurable_AndIdentityIsExact()
    {
        var issuer = "https://Issuer/" + Guid.NewGuid();
        await using var context = database.CreateContext(); var display = new DisplayPreferenceService(context); var language = new LanguagePreferenceService(context);
        Assert.Equal(new("m", "km/h", "browser", null), await display.GetAsync(issuer, "User"));
        Assert.Equal(new("m", "km/h", "browser", null), await display.GetAsync(issuer, "User"));
        Assert.False(await context.DisplayPreferences.AnyAsync(x => x.Issuer == issuer)); Assert.Empty(context.ChangeTracker.Entries());
        await language.UpdateAsync(issuer, "User", "nb-NO");
        var expected = new DisplayPreferenceDTO("ft", "mph", "timeZone", "Europe/Oslo");
        Assert.Equal(expected, await display.SetAsync(issuer, "User", new("ft", "mph", "timeZone", "Europe/Oslo")));
        await language.UpdateAsync(issuer, "User", "en"); Assert.Equal(expected, await display.GetAsync(issuer, "User"));
        await display.SetAsync(issuer, "User", new("m", "m/s", "utc", null)); Assert.Equal("en", (await language.GetAsync(issuer, "User")).Language);
        foreach (var identity in new[] { (issuer, "user"), (issuer.ToUpperInvariant(), "User"), (issuer + "/", "User"), (issuer, " User ") })
            Assert.Equal(new("m", "km/h", "browser", null), await display.GetAsync(identity.Item1, identity.Item2));
        await using var fresh = database.CreateContext(); await fresh.Database.MigrateAsync(); await fresh.Database.MigrateAsync();
        Assert.Empty(await fresh.Database.GetPendingMigrationsAsync()); Assert.False(fresh.Database.HasPendingModelChanges());
        Assert.Equal(new("m", "m/s", "utc", null), await new DisplayPreferenceService(fresh).GetAsync(issuer, "User"));
    }
    [PostgresFact]
    public async Task SupportedCombinations_AndCatalogue_AreCompatibleWithRealApi()
    {
        using var root = new ProfilesApiFactory { UseRealPreferences = true, Principal = ProfilesApiFactory.Identity("api-display", Guid.NewGuid().ToString()) };
        using var factory = root.WithWebHostBuilder(b => b.ConfigureTestServices(ConfigureStorage)); using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var initial = await client.GetAsync(Route); using var shape = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "distanceUnit", "speedUnit", "timeMode", "timeZoneId" }, shape.RootElement.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.Equal(JsonValueKind.Null, shape.RootElement.GetProperty("timeZoneId").ValueKind);
        var zones = (await client.GetFromJsonAsync<List<TimeZoneOptionDTO>>("/api/profiles/me/preferences/time-zones"))!;
        Assert.Contains(zones, x => x.Id == "Europe/Oslo"); Assert.Equal(zones.Count, zones.Select(x => x.Id).Distinct().Count()); Assert.All(zones, x => Assert.False(string.IsNullOrWhiteSpace(x.DisplayName)));
        foreach (var distance in new[] { "m", "ft" }) foreach (var speed in new[] { "km/h", "mph", "m/s" }) foreach (var mode in new[] { "browser", "utc", "timeZone" })
        {
            var zone = mode == "timeZone" ? "Europe/Oslo" : null;
            using var response = await client.PutAsJsonAsync(Route + "?issuer=attacker&subject=victim", new UpdateDisplayPreferenceRequest(distance, speed, mode, zone));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new(distance, speed, mode, zone), await response.Content.ReadFromJsonAsync<DisplayPreferenceDTO>());
            Assert.Equal(new(distance, speed, mode, zone), await client.GetFromJsonAsync<DisplayPreferenceDTO>(Route));
        }
        using var language = await client.GetAsync("/api/profiles/me/preferences"); using var languageBody = JsonDocument.Parse(await language.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "language" }, languageBody.RootElement.EnumerateObject().Select(x => x.Name).ToArray()); Assert.Equal(JsonValueKind.Null, languageBody.RootElement.GetProperty("language").ValueKind);
    }
    [PostgresTheory] [InlineData("bad", "km/h", "browser", null)] [InlineData("m", "bad", "browser", null)] [InlineData("m", "mph", "bad", null)] [InlineData("m", "mph", "utc", "UTC")] [InlineData("m", "mph", "timeZone", null)]
    public async Task DatabaseChecks_RejectDirectInvalidWrites(string distance, string speed, string mode, string? zone)
    {
        await using var context = database.CreateContext(); var subject = Guid.NewGuid().ToString();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO profiles.display_preference (issuer, subject, distance_unit, speed_unit, time_mode, time_zone_id) VALUES ({"constraint"}, {subject}, {distance}, {speed}, {mode}, {zone})"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }
    [PostgresFact]
    public async Task ConcurrentFirstWrites_ReturnOwnSnapshot_AndCreateOneRow()
    {
        for (var round = 0; round < 3; round++)
        {
            var subject = Guid.NewGuid().ToString(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var ready = 0;
            var tasks = Enumerable.Range(0, 12).Select(async i =>
            {
                await using var context = database.CreateContext(); await context.Database.OpenConnectionAsync();
                if (Interlocked.Increment(ref ready) == 12) start.SetResult(); await start.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var request = new UpdateDisplayPreferenceRequest(i % 2 == 0 ? "m" : "ft", "mph", "utc", null);
                Assert.Equal(new(request.DistanceUnit, request.SpeedUnit, request.TimeMode, null), await new DisplayPreferenceService(context).SetAsync("race", subject, request));
            }).ToArray(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
            await using var verify = database.CreateContext(); Assert.Equal(1, await verify.DisplayPreferences.CountAsync(x => x.Issuer == "race" && x.Subject == subject));
        }
    }
    [PostgresFact]
    public async Task CancelledDisplayOperations_DoNotPersistDefaultsOrChanges()
    {
        var subject = Guid.NewGuid().ToString(); await using var context = database.CreateContext();
        var service = new DisplayPreferenceService(context); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync("cancel-display", subject, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetAsync("cancel-display", subject, new("ft", "mph", "utc", null), cancelled.Token));
        Assert.False(await context.DisplayPreferences.AnyAsync(x => x.Issuer == "cancel-display" && x.Subject == subject));
        await service.SetAsync("cancel-display", subject, new("m", "m/s", "utc", null));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetAsync("cancel-display", subject, new("ft", "mph", "utc", null), cancelled.Token));
        await using var fresh = database.CreateContext(); Assert.Equal(new("m", "m/s", "utc", null), await new DisplayPreferenceService(fresh).GetAsync("cancel-display", subject));
    }
    [PostgresFact]
    public async Task DisplayPreferenceSurvivesApiHostRecreation_AndOtherIdentityStillSeesDefaults()
    {
        var subject = "user-" + Guid.NewGuid();
        using (var root = new ProfilesApiFactory { UseRealPreferences = true, Principal = ProfilesApiFactory.Identity("restart-display", subject) })
        using (var factory = root.WithWebHostBuilder(b => b.ConfigureTestServices(ConfigureStorage)))
        using (var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") }))
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Route, new UpdateDisplayPreferenceRequest("ft", "mph", "timeZone", "Europe/Oslo"))).StatusCode);
        using var restartedRoot = new ProfilesApiFactory { UseRealPreferences = true, Principal = ProfilesApiFactory.Identity("restart-display", subject) };
        using var restarted = restartedRoot.WithWebHostBuilder(b => b.ConfigureTestServices(ConfigureStorage));
        using var read = restarted.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(new("ft", "mph", "timeZone", "Europe/Oslo"), await read.GetFromJsonAsync<DisplayPreferenceDTO>(Route));
        restartedRoot.Principal = ProfilesApiFactory.Identity("restart-display", subject.ToUpperInvariant());
        // Subject casing is deliberately a different exact identity; do not normalize it.
        Assert.Equal(new("m", "km/h", "browser", null), await read.GetFromJsonAsync<DisplayPreferenceDTO>(Route));
    }

    [Theory] [MemberData(nameof(InvalidRequests))]
    public async Task ServiceRejectsInvalidSnapshot_BeforeOpeningUnavailableDatabase(string json, string code)
    {
        var request = JsonSerializer.Deserialize<UpdateDisplayPreferenceRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var context = new ProfilesDbContext(new DbContextOptionsBuilder<ProfilesDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused").Options);
        if (request is null)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => new DisplayPreferenceService(context).SetAsync("issuer", "subject", null!));
            return;
        }
        var error = await Assert.ThrowsAsync<DisplayPreferenceValidationException>(() => new DisplayPreferenceService(context).SetAsync("issuer", "subject", request));
        Assert.Equal(code, error.ErrorCode); Assert.Empty(context.ChangeTracker.Entries());
    }
    [PostgresTheory] [InlineData("distance_unit")] [InlineData("speed_unit")] [InlineData("time_mode")] [InlineData("issuer")] [InlineData("subject")]
    public async Task RequiredColumns_RejectNullAtDatabaseBoundary(string column)
    {
        // Identifier comes exclusively from the fixed theory inventory; values are SQL parameters.
        var subject = Guid.NewGuid().ToString(); await using var context = database.CreateContext();
        var columns = new[] { "issuer", "subject", "distance_unit", "speed_unit", "time_mode" };
        Assert.Contains(column, columns);
        var values = columns.Select(name => name == column ? null : name switch { "issuer" => "not-null", "subject" => subject, "distance_unit" => "m", "speed_unit" => "km/h", _ => "browser" }).Cast<object>().ToArray();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("INSERT INTO profiles.display_preference (issuer, subject, distance_unit, speed_unit, time_mode) VALUES ({0}, {1}, {2}, {3}, {4})", values));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, failure.SqlState);
    }
    private void ConfigureStorage(IServiceCollection services)
    {
        var types = new[] { typeof(ProfilesDbContext), typeof(DbContextOptions<ProfilesDbContext>), typeof(IDbContextOptionsConfiguration<ProfilesDbContext>) };
        foreach (var registration in services.Where(s => types.Contains(s.ServiceType)).ToArray()) services.Remove(registration);
        services.AddDbContext<ProfilesDbContext>(o => o.UseNpgsql(database.ConnectionString, pg => pg.MigrationsHistoryTable("__ef_migrations_history", "ef")).UseSnakeCaseNamingConvention());
    }
}
