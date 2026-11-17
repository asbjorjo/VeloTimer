using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;
using VeloTime.WebUI.Mud.Client.ViewModel;

namespace VeloTime.Platform.Test;

// A real Blazor renderer dispatches the rendered DOM event handlers. No page-private
// methods/fields or bUnit dependency: Mud's own input/debounce and button code runs.
public sealed partial class StatisticsAdminHeaderInteractionTests
{
    [Fact]
    public async Task HeaderClicksResetCursorToggleServerSortAndKeepCriteriaAcrossReloadActions()
    {
        await using var test = new Harness();
        await test.Start();
        await test.ClickSort("duration");
        Assert.Equal(AdminStatisticsEntriesSort.Duration, test.Last.Sort);
        Assert.Equal(AdminStatisticsEntriesSortOrder.Ascending, test.Last.Order);
        await test.Edit(0, "-00:00:01.0000001");
        await test.Settle();
        Assert.Equal(TimeSpan.FromTicks(-10000001), test.Last.MinDuration);
        await test.Edit(1, "00:00:02");
        await test.Settle();
        Assert.Equal(TimeSpan.FromSeconds(2), test.Last.MaxDuration);
        await test.Validity(false);
        await test.Edit(2, "10");
        await test.Settle();
        await test.Edit(3, "20");
        await test.Settle();
        var criteria = test.Last;
        await test.ClickText("Next results");
        Assert.NotNull(test.Last.Cursor);
        Assert.Equal(criteria with { Cursor = test.Last.Cursor, Direction = AdminStatisticsEntriesPageDirection.Next }, test.Last);
        await test.ClickText("Previous results");
        Assert.Equal(criteria with { Cursor = test.Last.Cursor, Direction = AdminStatisticsEntriesPageDirection.Previous }, test.Last);
        var paged = test.Last;
        await test.ClickText("Refresh");
        Assert.Equal(paged, test.Last);
        await test.Edit(0, "-00:00:02");
        await test.Settle();
        criteria = criteria with { MinDuration = TimeSpan.FromSeconds(-2) };
        Assert.Equal(criteria, test.Last); // header edit also resets a previous-page cursor
        await test.ClickSort("duration");
        Assert.Equal(criteria with { Order = AdminStatisticsEntriesSortOrder.Descending }, test.Last);
        await test.ClickSort("speed");
        Assert.Equal(criteria with { Sort = AdminStatisticsEntriesSort.Speed }, test.Last);
        Assert.Contains(test.Renderer.Frames(), frame => frame.FrameType == RenderTreeFrameType.Attribute &&
            frame.AttributeName == "aria-sort" && Equals(frame.AttributeValue, "ascending"));
        var before = test.Last;
        await test.ClickText("Restore automatic validation");
        Assert.Equal(1, test.Writes);
        Assert.Equal(before, test.Last);
        await test.ClickSort("speed");
        Assert.Equal(before with { Order = AdminStatisticsEntriesSortOrder.Descending }, test.Last);
        await test.ClickSort("completion");
        Assert.Equal(AdminStatisticsEntriesSort.TimeEnd, test.Last.Sort);
        Assert.Equal(AdminStatisticsEntriesSortOrder.Ascending, test.Last.Order);
    }

    [Theory]
    [InlineData("en-US", "mph", "22.369362920544", 10)]
    [InlineData("nb-NO", "km/h", "36,5", 36.5 / 3.6)]
    [InlineData("nb-NO", "m/s", "10,5", 10.5)]
    public async Task HeaderInputsDebounceConvertSiValidateClearAndIgnoreToolbarDrafts(string locale, string unit, string draft, double si)
    {
        await using var test = new Harness(locale, unit);
        await test.Start();
        var pendingItem = Guid.NewGuid();
        await test.Renderer.Dispatcher.InvokeAsync(() => test.Renderer.Components<MudSelect<Guid?>>().First().ValueChanged.InvokeAsync(pendingItem));
        var count = test.Queries.Count;
        await test.Edit(2, "1");
        await test.Edit(2, "2");
        await test.Edit(2, draft);
        Assert.Equal(count, test.Queries.Count); // oninput is not an immediate request
        await test.Settle();
        Assert.Equal(count + 1, test.Queries.Count);
        Assert.Equal(si, test.Last.MinSpeedMetresPerSecond!.Value, 9);
        Assert.Null(test.Last.StatisticsItemId); // unapplied toolbar selection is separate
        Assert.Equal(AdminStatisticsEntriesSort.TimeEnd, test.Last.Sort); // input did not bubble to sort
        var applied = test.Last;
        foreach (var invalid in new[] { "NaN", "Infinity", "-1", "oops" })
        {
            await test.Edit(2, invalid);
            await test.Settle();
            Assert.Equal(applied, test.Last);
            Assert.Equal(count + 1, test.Queries.Count);
        }
        await test.Validity(true);
        Assert.Equal(applied with { IsValid = true }, test.Last); // invalid draft does not block validity
        await test.Validity(false);
        Assert.False(test.Last.IsValid);
        await test.Validity(null);
        Assert.Null(test.Last.IsValid);
        var beforeInversion = test.Queries.Count;
        await test.Edit(2, draft);
        await test.Edit(3, "0"); // inverted finite speed range is not submitted
        await test.Settle();
        Assert.Null(test.Last.MaxSpeedMetresPerSecond);
        Assert.Equal(beforeInversion, test.Queries.Count);
        Assert.Equal(si, test.Last.MinSpeedMetresPerSecond!.Value, 9);
        await test.Edit(2, "");
        await test.Edit(3, "");
        await test.Settle();
        Assert.Null(test.Last.MinSpeedMetresPerSecond);
        await test.Edit(0, "00:00:02");
        await test.Settle();
        var validRange = test.Last;
        var beforeInvalidDuration = test.Queries.Count;
        foreach (var invalid in new[] { "-00:00:01", "00:", "bad" })
        {
            await test.Edit(1, invalid);
            await test.Settle();
            Assert.Equal(validRange, test.Last);
            Assert.Equal(beforeInvalidDuration, test.Queries.Count);
        }
        await test.Edit(0, "");
        await test.Edit(1, "");
        await test.Settle();
        Assert.Null(test.Last.MinDuration);
        Assert.Null(test.Last.MaxDuration);
        await test.Edit(0, "-00:00:01");
        await test.Settle();
        await test.ClickSort("speed");
        var headers = test.Last;
        await test.ClickText("Apply filters");
        Assert.Equal(headers with { StatisticsItemId = pendingItem }, test.Last);
        Assert.Equal(pendingItem, test.Last.StatisticsItemId);
        Assert.Null(test.Last.MinSpeedMetresPerSecond);
    }

    [Fact]
    public async Task HeaderControlsRemainKeyboardAccessibleAndMountedWhileLoadingAndEmpty()
    {
        await using var test = new Harness();
        await test.Start();
        test.AssertResponsiveHeader();
        test.AssertHeaderControls();

        var pending = new TaskCompletionSource<AdminStatisticsEntriesPageDTO>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Respond = (_, _) => pending.Task;
        var loading = test.ClickSort("duration");
        await test.WaitForQueries(2);
        test.AssertResponsiveHeader();
        test.AssertHeaderControls();
        Assert.All(test.Renderer.Components<MudTextField<string>>(), field => Assert.False(field.Disabled));
        Assert.Contains(test.Renderer.Frames(), frame => frame.FrameType == RenderTreeFrameType.Attribute &&
            frame.AttributeName == "role" && Equals(frame.AttributeValue, "status"));

        pending.SetResult(new([], null, null));
        await loading;
        await test.Settle();
        Assert.Empty(test.Renderer.Components<MudTable<AdminStatisticsEntryDTO>>().Single().Items);
        test.AssertResponsiveHeader();
        test.AssertHeaderControls();
        Assert.Contains(test.Renderer.Frames(), frame => frame.FrameType == RenderTreeFrameType.Attribute &&
            frame.AttributeName == "role" && Equals(frame.AttributeValue, "status"));
    }

    [Fact]
    public async Task BrowserLocaleAndSpeedUnitChangesReformatAppliedBoundsWithoutQueryOrCanonicalDrift()
    {
        await using var test = new Harness("en-US", "mph");
        await test.Start();
        await test.Edit(2, "10");
        await test.Edit(3, "20");
        await test.Settle();

        var applied = test.Last;
        Assert.Equal(UnitsNet.Speed.FromMilesPerHour(10).MetersPerSecond, applied.MinSpeedMetresPerSecond!.Value, 12);
        Assert.Equal(UnitsNet.Speed.FromMilesPerHour(20).MetersPerSecond, applied.MaxSpeedMetresPerSecond!.Value, 12);
        var queryCount = test.Queries.Count;

        await test.ChangeDisplaySettings("nb-NO", "km/h");
        var fields = test.Renderer.Components<MudTextField<string>>().ToArray();
        var culture = CultureInfo.GetCultureInfo("nb-NO");
        Assert.Contains(",", fields[2].Value);
        Assert.Contains(",", fields[3].Value);
        Assert.True(double.TryParse(fields[2].Value, NumberStyles.Float, culture, out var minimumInKmh));
        Assert.True(double.TryParse(fields[3].Value, NumberStyles.Float, culture, out var maximumInKmh));
        Assert.Equal(UnitsNet.Speed.FromMetersPerSecond(applied.MinSpeedMetresPerSecond.Value).KilometersPerHour, minimumInKmh, 12);
        Assert.Equal(UnitsNet.Speed.FromMetersPerSecond(applied.MaxSpeedMetresPerSecond.Value).KilometersPerHour, maximumInKmh, 12);
        Assert.EndsWith("(km/h)", fields[2].Label);
        Assert.EndsWith("(km/h)", fields[3].Label);
        Assert.Equal(queryCount, test.Queries.Count);
        Assert.Equal(applied, test.Last);

        await test.Edit(2, fields[2].Value);
        await test.Settle();
        Assert.Equal(queryCount, test.Queries.Count);
        Assert.Equal(applied, test.Last);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancellation")]
    public async Task RapidEditsDuringLoadingKeepHeaderInstancesAndLatestResultEvenWhenOldLoadFails(string oldOutcome)
    {
        await using var test = new Harness();
        await test.Start();
        var fields = test.Renderer.Components<MudTextField<string>>().ToArray();
        var old = new TaskCompletionSource<AdminStatisticsEntriesPageDTO>(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = new TaskCompletionSource<AdminStatisticsEntriesPageDTO>(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Respond = (_, _) => old.Task;
        var oldEvent = test.ClickSort("duration");
        await test.WaitForQueries(2);
        Assert.Equal(fields, test.Renderer.Components<MudTextField<string>>());
        Assert.All(fields, field => Assert.False(field.Disabled));
        test.Respond = (_, _) => latest.Task;
        await test.Edit(0, "-00:00:02");
        await test.WaitForQueries(3);
        Assert.True(test.Tokens[^2].IsCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(-2), test.Last.MinDuration);
        latest.SetResult(new([], null, null));
        await test.Settle();
        Assert.Equal(fields, test.Renderer.Components<MudTextField<string>>()); // empty result keeps controls mounted
        if (oldOutcome == "failure") old.SetException(new HttpRequestException("stale failure must not appear"));
        else if (oldOutcome == "cancellation") old.SetCanceled();
        else old.SetResult(test.Page(test.Queries[1]));
        await oldEvent;
        await test.Settle();
        Assert.Empty(test.Renderer.Components<MudTable<AdminStatisticsEntryDTO>>().Single().Items);
        Assert.DoesNotContain(test.Renderer.Components<MudAlert>(), alert => alert.Severity == Severity.Error);
        Assert.Equal("-00:00:02", fields[0].Value);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        public readonly EventRenderer Renderer;
        public readonly List<GetAdminStatisticsEntriesRequest> Queries = [];
        public readonly List<CancellationToken> Tokens = [];
        public GetAdminStatisticsEntriesRequest Last => Queries[^1];
        public Func<GetAdminStatisticsEntriesRequest, CancellationToken, Task<AdminStatisticsEntriesPageDTO>>? Respond;
        public int Writes;
        public readonly List<Guid> ContributionRequests = [];
        public readonly List<CancellationToken> ContributionTokens = [];
        public readonly List<Guid[]> NameRequests = [];
        public Func<Guid, CancellationToken, Task<StatisticsEntryContributionsDTO>>? ContributionsRespond;
        public Func<IReadOnlyList<Guid>, CancellationToken, Task<IReadOnlyDictionary<Guid, string>>>? NamesRespond;
        public readonly Guid StartPointId = Guid.NewGuid();
        public readonly Guid EndPointId = Guid.NewGuid();
        public AdminStatisticsEntryDTO Entry => entry;
        private EffectiveDisplaySettings settings;
        private readonly AdminStatisticsEntryDTO entry = StatisticsManualInvalidationTransportTests.Entry();
        public Harness(string locale = "en-US", string unit = "m/s")
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            services.AddSingleton<IJSRuntime, NoBrowser>();
            services.AddSingleton<NavigationManager, TestNavigation>();
            services.AddSingleton(PlatformServiceTests.TestProxy.Create<IFacilityService>((_, _) => new[] { new FacilityView { Id = entry.FacilityId, Name = "Admin facility" } }));
            services.AddSingleton(AsyncProxy.Create<IStatisticsAdministrationService>((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IStatisticsAdministrationService.GetItemsAsync): return Array.Empty<StatisticsItemDTO>();
                    case nameof(IStatisticsAdministrationService.GetConfigurationsAsync): return Array.Empty<StatisticsConfigurationDTO>();
                    case nameof(IStatisticsAdministrationService.ListEntriesAsync):
                        var request = (GetAdminStatisticsEntriesRequest)args[0]!;
                        var token = (CancellationToken)args[1]!;
                        Queries.Add(request); Tokens.Add(token);
                        return Respond is null ? Task.FromResult(Page(request)) : Respond(request, token);
                    case nameof(IStatisticsAdministrationService.GetEntryContributionsAsync):
                        var entryId = (Guid)args[0]!;
                        var contributionsToken = (CancellationToken)args[1]!;
                        ContributionRequests.Add(entryId); ContributionTokens.Add(contributionsToken);
                        return ContributionsRespond is null ? Task.FromResult(Contributions(entryId)) : ContributionsRespond(entryId, contributionsToken);
                    case nameof(IStatisticsAdministrationService.GetCoursePointNamesAsync):
                        var ids = (IReadOnlyList<Guid>)args[0]!;
                        var namesToken = (CancellationToken)args[1]!;
                        NameRequests.Add(ids.ToArray());
                        return NamesRespond is null ? Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>
                            { [StartPointId] = "Sample start", [EndPointId] = "Sample finish" }) : NamesRespond(ids, namesToken);
                    case nameof(IStatisticsAdministrationService.SetManualInvalidationAsync): Writes++; return entry;
                    default: throw new NotSupportedException(method.Name);
                }
            }));
            var dialog = PlatformServiceTests.TestProxy.Create<IDialogReference>((_, _) => DialogResult.Ok(new StatisticsEntryManualInvalidationRequest(false)));
            services.AddSingleton(PlatformServiceTests.TestProxy.Create<IDialogService>((_, _) => dialog));
            provider = services.BuildServiceProvider();
            Renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            settings = EffectiveDisplaySettings.Create(new("m", unit, "utc", null), "Europe/Oslo", locale, CultureInfo.GetCultureInfo(locale));
        }
        public AdminStatisticsEntriesPageDTO Page(GetAdminStatisticsEntriesRequest request)
        {
            var cursor = request.Sort switch
            {
                AdminStatisticsEntriesSort.Duration => new AdminStatisticsEntryCursorDTO(default, entry.Id, request.Sort, request.Order, entry.Duration),
                AdminStatisticsEntriesSort.Speed => new(default, entry.Id, request.Sort, request.Order, Speed: "25"),
                _ => new(entry.TimeEnd, entry.Id, request.Sort, request.Order)
            };
            return new([entry], cursor, cursor);
        }
        public StatisticsEntryContributionsDTO Contributions(Guid? entryId = null, StatisticsEntryPassingsStatus status = StatisticsEntryPassingsStatus.Available, bool samplesTruncated = false, bool passingsTruncated = false, IReadOnlyList<PassingDTO>? passings = null)
        {
            var from = new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);
            var to = from.AddMinutes(2);
            var sample = new AdminStatisticsSampleDTO(Guid.NewGuid(), from, to, StartPointId, EndPointId, 1500, TimeSpan.FromSeconds(30), 10, entry.FacilityId);
            passings ??= status == StatisticsEntryPassingsStatus.Available
                ? [new PassingDTO(Guid.NewGuid(), from.AddSeconds(5), Guid.NewGuid(), "Finish point", Guid.NewGuid(), "Finish stand", entry.FacilityId, true, false, true)]
                : null;
            return new(entryId ?? entry.Id, entry.TransponderId, from, to, [sample], samplesTruncated, passings, passingsTruncated, status);
        }
        public Task Start() => Renderer.Dispatcher.InvokeAsync(() => Renderer.Start(SettingsParameters()));
        private ParameterView SettingsParameters() => ParameterView.FromDictionary(new Dictionary<string, object?>
        { [nameof(StatisticsManualInvalidationPageTests.PageHost.Settings)] = settings });
        public Task ChangeDisplaySettings(string locale, string unit)
        {
            settings = EffectiveDisplaySettings.Create(new("m", unit, "utc", null), "Europe/Oslo", locale, CultureInfo.GetCultureInfo(locale));
            return Renderer.Dispatcher.InvokeAsync(() => Renderer.Update(SettingsParameters()));
        }
        public void AssertResponsiveHeader()
        {
            var region = Renderer.ElementAttributes("div", "role", "region");
            Assert.Equal("Statistics entries", region["aria-label"]);
            Assert.Equal("0", region["tabindex"]);
            Assert.Equal("overflow-x:auto; width:100%;", region["style"]);
            var table = Renderer.Components<MudTable<AdminStatisticsEntryDTO>>().Single();
            Assert.Equal(Breakpoint.None, table.Breakpoint);
            Assert.Contains("min-width:80rem", table.Style);
        }
        public void AssertHeaderControls()
        {
            var fields = Renderer.Components<MudTextField<string>>().ToArray();
            Assert.Collection(fields,
                field => Assert.Equal("Minimum duration", field.Label),
                field => Assert.Equal("Maximum duration", field.Label),
                field => Assert.Contains("Minimum speed", field.Label),
                field => Assert.Contains("Maximum speed", field.Label));
            Assert.Single(Renderer.Components<MudSelect<bool?>>());
            Assert.Equal(new bool?[] { null, true, false }, Renderer.Components<MudSelectItem<bool?>>().Select(item => item.Value));
            foreach (var label in new[] { "completion", "duration", "speed" })
            {
                var button = Renderer.Components<MudButton>().Single(button => button.UserAttributes?.TryGetValue("aria-label", out var value) == true &&
                    value.ToString()!.Contains(label, StringComparison.OrdinalIgnoreCase));
                Assert.True(Renderer.IsNativeButton(button));
            }
        }
        public Task ClickSort(string label) => Renderer.Dispatcher.InvokeAsync(() =>
        {
            var button = Renderer.Components<MudButton>().Single(button => button.UserAttributes?.TryGetValue("aria-label", out var value) == true &&
                value.ToString()!.Contains(label, StringComparison.OrdinalIgnoreCase));
            Assert.True(Renderer.IsNativeButton(button)); // native keyboard activation, not a clickable div
            return Renderer.Click(button);
        });
        public Task ClickText(string text) => Renderer.Dispatcher.InvokeAsync(() => Renderer.Click(Renderer.Components<MudButton>().Single(button =>
            Renderer.Text(button).Trim().Equals(text, StringComparison.OrdinalIgnoreCase))));
        public Task ClickToggle() => Renderer.Dispatcher.InvokeAsync(() => Renderer.Click(Renderer.Components<MudIconButton>().Single()));
        public Task ClickRow() => Renderer.Dispatcher.InvokeAsync(Renderer.ClickDataRow);
        public string AllText() => string.Concat(Renderer.Frames().Where(frame => frame.FrameType == RenderTreeFrameType.Text).Select(frame => frame.TextContent));
        public Task Edit(int field, string value) => Renderer.Dispatcher.InvokeAsync(() => Renderer.Input(Renderer.Components<MudTextField<string>>().ElementAt(field), value));
        // HtmlRenderer cannot open Mud's JS-positioned popover; invoke the rendered public
        // ValueChanged contract, not the page handler. The select options are asserted too.
        public Task Validity(bool? value) => Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var select = Renderer.Components<MudSelect<bool?>>().Single();
            Assert.Equal(new bool?[] { null, true, false }, Renderer.Components<MudSelectItem<bool?>>().Select(item => item.Value));
            await select.ValueChanged.InvokeAsync(value);
        });
        public async Task Settle() { await Task.Delay(500); await Renderer.Dispatcher.InvokeAsync(Renderer.Check); }
        public async Task WaitForContributionRequests(int count)
        {
            for (var i = 0; i < 100; i++)
            {
                if (await Renderer.Dispatcher.InvokeAsync(() => ContributionRequests.Count >= count)) return;
                await Task.Delay(20);
            }
            Assert.Fail($"Expected {count} contribution requests, got {ContributionRequests.Count}.");
        }
        public async Task WaitForQueries(int count)
        {
            for (var i = 0; i < 100; i++)
            {
                if (await Renderer.Dispatcher.InvokeAsync(() => Queries.Count >= count)) return;
                await Task.Delay(20);
            }
            Assert.Fail($"Expected {count} requests, got {Queries.Count}.");
        }
        public async ValueTask DisposeAsync() { await Renderer.DisposeAsync(); await provider.DisposeAsync(); }
    }

    public class AsyncProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = default!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = Handler(method!, args!);
            if (method!.ReturnType.IsInstanceOfType(result)) return result;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(method.ReturnType.GetGenericArguments()[0]).Invoke(null, [result]);
        }
        public static T Create<T>(Func<MethodInfo, object?[], object?> handler) where T : class
        {
            var proxy = Create<T, AsyncProxy>();
            ((AsyncProxy)(object)proxy).Handler = handler;
            return proxy;
        }
    }

    private sealed class EventRenderer(IServiceProvider services, ILoggerFactory logger) : Renderer(services, logger)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        private readonly List<Exception> errors = [];
        private int root;
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => errors.Add(exception);
        public void Check() => Assert.Empty(errors);
        public Task Start(ParameterView parameters)
        {
            root = AssignRootComponentId(new StatisticsManualInvalidationPageTests.PageHost());
            return RenderRootComponentAsync(root, parameters);
        }
        public Task Update(ParameterView parameters) => RenderRootComponentAsync(root, parameters);
        public IEnumerable<RenderTreeFrame> Frames(int? id = null)
        {
            var frames = GetCurrentRenderTreeFrames(id ?? root);
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames.Array[i];
                yield return frame;
                if (frame.FrameType == RenderTreeFrameType.Component)
                    foreach (var child in Frames(frame.ComponentId)) yield return child;
            }
        }
        public IEnumerable<T> Components<T>() => Frames().Where(frame => frame.FrameType == RenderTreeFrameType.Component).Select(frame => frame.Component).OfType<T>();
        private int Id(IComponent component) => Frames().Single(frame => frame.FrameType == RenderTreeFrameType.Component && ReferenceEquals(frame.Component, component)).ComponentId;
        public string Text(IComponent component) => string.Concat(Frames(Id(component)).Where(frame => frame.FrameType == RenderTreeFrameType.Text).Select(frame => frame.TextContent));
        public bool IsNativeButton(IComponent component) => Frames(Id(component)).Any(frame => frame.FrameType == RenderTreeFrameType.Element && frame.ElementName == "button");
        public IReadOnlyDictionary<string, object?> ElementAttributes(string elementName, string attributeName, object value)
        {
            var frames = Frames().ToArray();
            for (var i = 0; i < frames.Length; i++)
            {
                if (frames[i].FrameType != RenderTreeFrameType.Element || frames[i].ElementName != elementName) continue;
                var attributes = new Dictionary<string, object?>();
                for (var j = i + 1; j < frames.Length && frames[j].FrameType == RenderTreeFrameType.Attribute; j++)
                    attributes[frames[j].AttributeName] = frames[j].AttributeValue;
                if (Equals(attributes.GetValueOrDefault(attributeName), value)) return attributes;
            }
            throw new InvalidOperationException($"No {elementName} element with {attributeName}={value} was rendered.");
        }
        public Task Click(IComponent component) => Event(component, "onclick", new MouseEventArgs());
        public Task ClickDataRow()
        {
            var frames = Frames().ToArray();
            for (var i = 0; i < frames.Length; i++)
            {
                if (frames[i].FrameType != RenderTreeFrameType.Element || frames[i].ElementName != "tr") continue;
                for (var j = i + 1; j < frames.Length && frames[j].FrameType == RenderTreeFrameType.Attribute; j++)
                    if (frames[j].AttributeName == "onclick" && frames[j].AttributeEventHandlerId != 0)
                        return DispatchEventAsync(frames[j].AttributeEventHandlerId, null, new MouseEventArgs());
            }
            throw new InvalidOperationException("No clickable table data row was rendered.");
        }
        public Task Input(IComponent component, string value) => Event(component, "oninput", new ChangeEventArgs { Value = value });
        private Task Event(IComponent component, string name, EventArgs args)
        {
            var frame = Frames(Id(component)).First(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == name && frame.AttributeEventHandlerId != 0);
            return DispatchEventAsync(frame.AttributeEventHandlerId, null, args);
        }
    }
    private sealed class NoBrowser : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/statistics/entries");
    }
}
