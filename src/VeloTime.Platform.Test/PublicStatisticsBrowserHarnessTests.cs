using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.WebUI.Mud.Client;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;
using Contracts = VeloTime.Module.Statistics.Interface.Data;

namespace VeloTime.Platform.Test;

/// <summary>
/// Bounded Kestrel + real client Router/MainLayout/NavMenu/Results, mocked services.
/// Opt in after the integrated build and Node/Playwright/browser handoff.
/// </summary>
public sealed class PublicStatisticsBrowserHarnessTests
{
    [PublicStatisticsBrowserFact]
    public async Task RealInteractivePage_RestoresRefreshHistoryFiltersSelectionsAndCursorNavigation()
    {
        var repository = FindRepository();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development", ContentRootPath = repository
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services.Configure<RequestLocalizationOptions>(options =>
            options.SetDefaultCulture("en").AddSupportedCultures("en", "nb-NO").AddSupportedUICultures("en", "nb-NO"));
        builder.Services.AddMudServices();
        builder.Services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, AnonymousState>();
        builder.Services.AddScoped<UnsavedChangesTracker>();
        var state = new HarnessState();
        builder.Services.AddSingleton(CreateStatistics(state));
        builder.Services.AddSingleton(PlatformServiceTests.TestProxy.Create<IFacilityService>((method, _) =>
            method.Name == nameof(IFacilityService.GetFacilitiesAsync)
                ? state.Mode == "no-facilities" ? Array.Empty<FacilityView>()
                    : new[] { new FacilityView { Id = Id(2), Name = "Zulu" },
                        new FacilityView { Id = Id(14), Name = "Arena" }, new FacilityView { Id = Id(1), Name = "Arena" } }
                : throw new NotSupportedException(method.Name)));
        await using var app = builder.Build();
        app.UseRequestLocalization();
        app.UseAntiforgery();
        app.MapPost("/__test/scenario/{mode}", (string mode) =>
        {
            if (!new[] { "normal", "no-facilities", "no-lap", "no-configurations", "discovery-error",
                "results-error", "denied", "delayed" }.Contains(mode)) return Results.BadRequest();
            state.Mode = mode;
            return Results.Ok();
        });
        app.MapGet("/__test/evidence", () => Results.Json(new
        {
            state.Requests, state.Cancelled, state.CompletedDelayed
        }));
        // Serve locally cached package assets, never remote fonts/CDNs.
        MapPackageAsset(app, "MudBlazor", "MudBlazor.min.js");
        MapPackageAsset(app, "MudBlazor", "MudBlazor.min.css");
        MapPackageAsset(app, "Microsoft.AspNetCore.App.Internal.Assets", "blazor.web.js", "/_framework/blazor.web.js");
        app.MapRazorComponents<HarnessDocument>().AddInteractiveServerRenderMode()
            .AddAdditionalAssemblies(typeof(Routes).Assembly);
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var readiness = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await readiness.GetAsync(address + "/statistics");
            Assert.True(response.IsSuccessStatusCode, $"Harness readiness returned {(int)response.StatusCode}.");

            var start = new ProcessStartInfo("node")
            {
                WorkingDirectory = repository, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("--test");
            start.ArgumentList.Add("src/VeloTime.Platform.Test/PublicStatistics.browser.test.mjs");
            start.Environment["VELOTIMER_PUBLIC_STATISTICS_BROWSER_URL"] = address;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node browser regression.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("Public statistics browser regression exceeded its two-minute bound.");
            }
            Assert.True(process.ExitCode == 0, $"Browser exit {process.ExitCode}\n{await stdout}\n{await stderr}");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    public sealed class HarnessDocument : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "html");
            builder.AddAttribute(1, "lang", "en");
            builder.OpenElement(2, "head");
            builder.AddMarkupContent(3, """
                <meta charset="utf-8"><base href="/">
                <link rel="stylesheet" href="/_content/MudBlazor/MudBlazor.min.css">
                """);
            builder.CloseElement();
            builder.OpenElement(4, "body");
            builder.OpenComponent<Routes>(5);
            builder.AddComponentRenderMode(new InteractiveServerRenderMode(prerender: false));
            builder.CloseComponent();
            builder.AddMarkupContent(6, """
                <script>window.velotimeLanguage={state:()=>({language:"en",preferenceUnavailable:false})};</script>
                <script src="/_content/MudBlazor/MudBlazor.min.js"></script>
                <script src="/_framework/blazor.web.js"></script>
                """);
            builder.CloseElement();
            builder.CloseElement();
        }
    }

    public sealed class AnonymousState : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class HarnessState
    {
        public string Mode = "normal";
        public int Requests;
        public int Cancelled;
        public int CompletedDelayed;
    }

    private static IStatisticsService CreateStatistics(HarnessState state)
    {
        var configs = new[]
        {
            new Contracts.PublicStatisticsConfigurationDTO(Id(4), Id(3), Id(1), Contracts.StatisticsConfigurationType.Simple,
                Id(10), "Start", Id(11), "Finish", null, null),
            new Contracts.PublicStatisticsConfigurationDTO(Id(5), Id(3), Id(2), Contracts.StatisticsConfigurationType.Simple,
                Id(12), "Other start", Id(13), "Other finish", null, null),
            new Contracts.PublicStatisticsConfigurationDTO(Id(9), Id(3), Id(1), Contracts.StatisticsConfigurationType.Multi,
                Id(10), "Start", Id(11), "Finish", Id(4), 3),
            new Contracts.PublicStatisticsConfigurationDTO(Id(8), Id(7), Id(1), Contracts.StatisticsConfigurationType.Simple,
                Id(10), "Sprint start", Id(11), "Sprint finish", null, null)
        };
        var discovery = new Contracts.PublicStatisticsDiscoveryDTO(
            [new(Id(6), "Lap", "duplicate", 250), new(Id(7), "Sprint", "100m", 100),
                new(Id(3), "Lap", "250m", 250)], configs.Reverse().ToArray(), []);
        var completion = new DateTimeOffset(2026, 10, 31, 8, 47, 54, TimeSpan.Zero).AddTicks(1_234_560);
        var rows = configs.SelectMany(config => Enumerable.Range(1, 60).Select(index =>
            new Contracts.PublicStatisticsResultDTO(Id(100 + index), $"Rider {index:00} Group {(index % 2 == 0 ? "A" : "B")}",
                config.StatisticsItemId, config.Id, config.FacilityId,
                TimeSpan.FromSeconds(index), 36 + index, completion.AddSeconds(index)))).ToArray();
        return HarnessProxy.Create<IStatisticsService>((method, args) =>
        {
            if (method.Name == nameof(IStatisticsService.GetPublicDiscoveryAsync))
            {
                if (state.Mode == "discovery-error") throw new HttpRequestException("secret discovery failure");
                return state.Mode switch
                {
                    "no-lap" => discovery with { Items = discovery.Items.Where(item => item.Name != "Lap").ToArray() },
                    "no-configurations" => discovery with { Configurations = [] },
                    _ => discovery
                };
            }
            if (method.Name == nameof(IStatisticsService.GetBestResultsAsync)) return Array.Empty<BestStatisticsResultView>();
            if (method.Name != nameof(IStatisticsService.GetPublicResultsAsync)) throw new NotSupportedException(method.Name);
            var request = (Contracts.GetPublicStatisticsResultsRequest)args[0]!;
            ((CancellationToken)args[1]!).ThrowIfCancellationRequested();
            Interlocked.Increment(ref state.Requests);
            if (state.Mode is "results-error" or "denied")
                throw new HttpRequestException("secret result failure", null,
                    state.Mode == "denied" ? System.Net.HttpStatusCode.Forbidden : System.Net.HttpStatusCode.ServiceUnavailable);
            if (state.Mode == "delayed" && request.Name == "slow")
                return DelayedAsync(state, (CancellationToken)args[1]!);
            // Only the mocked backend operates on this synthetic array. The real UI still
            // receives one bounded page and performs no filtering/paging on stored results.
            var filtered = rows.Where(row => row.FacilityId == request.FacilityId &&
                row.StatisticsItemId == request.StatisticsItemId && row.StatisticsItemConfigId == request.StatisticsItemConfigId &&
                (request.Name is null || row.ProfileName.Contains(request.Name, StringComparison.OrdinalIgnoreCase)) &&
                (!request.MinDuration.HasValue || row.Duration >= request.MinDuration) &&
                (!request.MaxDuration.HasValue || row.Duration <= request.MaxDuration) &&
                (!request.MinSpeedKmh.HasValue || row.SpeedKmh >= request.MinSpeedKmh) &&
                (!request.MaxSpeedKmh.HasValue || row.SpeedKmh <= request.MaxSpeedKmh))
                .OrderByDescending(row => row.TimeEnd).ToArray();
            var eligible = request.Cursor is null ? filtered :
                filtered.Where(row => request.Direction == Contracts.PublicStatisticsPageDirection.Previous
                    ? row.TimeEnd > request.Cursor.TimeEnd : row.TimeEnd < request.Cursor.TimeEnd).ToArray();
            var page = request.Direction == Contracts.PublicStatisticsPageDirection.Previous
                ? eligible.TakeLast(25).ToArray() : eligible.Take(25).ToArray();
            if (page.Length == 0) return new Contracts.PublicStatisticsResultsPageDTO([], null, null);
            return new Contracts.PublicStatisticsResultsPageDTO(page,
                filtered.Any(row => row.TimeEnd > page[0].TimeEnd) ? new(page[0].TimeEnd, page[0].Id) : null,
                filtered.Any(row => row.TimeEnd < page[^1].TimeEnd) ? new(page[^1].TimeEnd, page[^1].Id) : null);
        });
    }

    private static async Task<Contracts.PublicStatisticsResultsPageDTO> DelayedAsync(HarnessState state, CancellationToken token)
    {
        // Deliberately return a stale payload even when cancellation is requested:
        // real Results must both cancel and ignore a non-cooperating dependency.
        using var registration = token.Register(() => Interlocked.Increment(ref state.Cancelled));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Interlocked.Increment(ref state.CompletedDelayed);
        return new([new(Id(999), "Stale secret", Id(3), Id(4), Id(1),
            TimeSpan.FromDays(1) + TimeSpan.FromMilliseconds(5), 12.5, DateTimeOffset.UtcNow)], null, null);
    }

    public class HarnessProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = Handler(method!, args ?? []);
            if (result is Task) return result;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(method!.ReturnType.GetGenericArguments()[0]).Invoke(null, [result]);
        }
        public static T Create<T>(Func<MethodInfo, object?[], object?> handler) where T : class
        {
            var service = DispatchProxy.Create<T, HarnessProxy>();
            ((HarnessProxy)(object)service).Handler = handler;
            return service;
        }
    }

    private static Guid Id(int index) => Guid.Parse($"{index:x8}-0000-0000-0000-000000000001");
    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "VeloTimer.sln"))) return directory.FullName;
        throw new InvalidOperationException("Could not locate repository browser regression.");
    }
    private static void MapPackageAsset(WebApplication app, string package, string asset, string? route = null)
    {
        var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var folder = Path.Combine(root, package.ToLowerInvariant());
        var path = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, asset, SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (path is null) throw new InvalidOperationException($"Missing restored {package}/{asset} browser asset.");
        app.MapGet(route ?? $"/_content/{package}/{asset}", () =>
            Microsoft.AspNetCore.Http.Results.File(path, asset.EndsWith(".css") ? "text/css" : "text/javascript"));
    }
}

public sealed class PublicStatisticsBrowserFactAttribute : FactAttribute
{
    public PublicStatisticsBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VELOTIMER_PUBLIC_STATISTICS_BROWSER") != "1")
            Skip = "Opt in with VELOTIMER_PUBLIC_STATISTICS_BROWSER=1 after Node, Playwright and Chromium are available.";
    }
}
