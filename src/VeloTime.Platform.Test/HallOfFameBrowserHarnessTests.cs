using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed partial class PublicStatisticsBrowserHarnessTests
{
    [HallOfFameBrowserFact]
    public async Task HallOfFame_RealRouterEditorAndPublicPagePreserveOrderedSettingsAndConflicts()
    {
        await using var app = await CreateAppAsync(new HallOfFameBrowserStore());
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var start = new ProcessStartInfo("node")
            {
                WorkingDirectory = FindRepository(), UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("--test");
            start.ArgumentList.Add("src/VeloTime.Platform.Test/HallOfFame.browser.test.mjs");
            start.Environment["VELOTIMER_HALL_OF_FAME_BROWSER_URL"] = address;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Node unavailable");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("Hall of Fame browser test exceeded three minutes.");
            }
            Assert.True(process.ExitCode == 0, $"Browser exit {process.ExitCode}\n{await stdout}\n{await stderr}");
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await app.StopAsync(timeout.Token);
        }
    }

    private sealed class HallAdministratorState : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "hall-admin"),
                 new Claim(AdministrationAuthorization.PermissionClaimType, AdministrationAuthorization.StatisticsPermission)], "test"))));
    }

    private sealed class HallAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var state = await new HallAdministratorState().GetAuthenticationStateAsync();
            return AuthenticateResult.Success(new AuthenticationTicket(state.User, Scheme.Name));
        }
    }

    private sealed class HallOfFameBrowserStore
    {
        private readonly object gate = new();
        internal bool LoadFails { get; set; }
        internal HallOfFameSettingsDTO Settings { get; private set; } = new(7,
        [
            new(HallOfFameTransportTests.Item, HallOfFameRankingMode.MostPassings, true, 10,
                [HallOfFameColumn.PassingCount, HallOfFameColumn.SpeedOfBestResult, HallOfFameColumn.DateOfBestResult]),
            new(HallOfFameTransportTests.Item, HallOfFameRankingMode.BestTime, true, 10,
                [HallOfFameColumn.BestTime])
        ]);
        internal void AdvanceVersion() { lock (gate) Settings = Settings with { Version = Settings.Version + 1 }; }
        internal IStatisticsAdministrationService Administration => HarnessProxy.Create<IStatisticsAdministrationService>((method, args) =>
        {
            lock (gate)
            {
                switch (method.Name)
                {
                    case nameof(IStatisticsAdministrationService.GetItemsAsync):
                        if (LoadFails) throw new HttpRequestException("Test load failed", null, HttpStatusCode.ServiceUnavailable);
                        return (IReadOnlyList<StatisticsItemDTO>)new[] { new StatisticsItemDTO(HallOfFameTransportTests.Item, "Browser lap", "", 250) };
                    case nameof(IStatisticsAdministrationService.GetHallOfFameSettingsAsync):
                        if (LoadFails) throw new HttpRequestException("Test load failed", null, HttpStatusCode.ServiceUnavailable);
                        return Settings;
                    case nameof(IStatisticsAdministrationService.SaveHallOfFameSettingsAsync):
                        var request = (SaveHallOfFameSettingsRequest)args[0]!;
                        if (request.ExpectedVersion != Settings.Version)
                            throw new HttpRequestException("Settings changed", null, HttpStatusCode.Conflict);
                        if (HallOfFameQuery.Validate(request).Count != 0)
                            throw new HttpRequestException("Invalid draft", null, HttpStatusCode.BadRequest);
                        return Settings = new(Settings.Version + 1, request.Sections);
                    default: throw new NotSupportedException(method.Name);
                }
            }
        });
        internal IStatisticsService Statistics => HarnessProxy.Create<IStatisticsService>((method, _) =>
        {
            Assert.Equal(nameof(IStatisticsService.GetHallOfFameAsync), method.Name);
            lock (gate)
                return new HallOfFameDTO(Settings.Sections.Where(section => section.IsEnabled).Select(section =>
                    new HallOfFameSectionDTO(section.StatisticsItemId, "Browser lap", section.RankingMode,
                        section.RowCount, section.Columns, [SelectColumns(section.Columns)])).ToArray());
        });

        private static HallOfFameEntryDTO SelectColumns(IReadOnlyList<HallOfFameColumn> columns)
        {
            var entry = HallOfFameTransportTests.FullEntry;
            return entry with
            {
                BestTime = columns.Contains(HallOfFameColumn.BestTime) ? entry.BestTime : null,
                SpeedOfBestResult = columns.Contains(HallOfFameColumn.SpeedOfBestResult) ? entry.SpeedOfBestResult : null,
                PassingCount = columns.Contains(HallOfFameColumn.PassingCount) ? entry.PassingCount : null,
                FacilityOfBestResult = columns.Contains(HallOfFameColumn.FacilityOfBestResult) ? entry.FacilityOfBestResult : null,
                DateOfBestResult = columns.Contains(HallOfFameColumn.DateOfBestResult) ? entry.DateOfBestResult : null
            };
        }
    }
}

public sealed class HallOfFameBrowserFactAttribute : FactAttribute
{
    public HallOfFameBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VELOTIMER_HALL_OF_FAME_BROWSER") != "1")
            Skip = "Opt in with VELOTIMER_HALL_OF_FAME_BROWSER=1 after Node, Playwright and Chromium are available.";
    }
}
