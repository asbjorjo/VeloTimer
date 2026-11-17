using Microsoft.AspNetCore.Components.RenderTree;
using MudBlazor;
using VeloTime.Module.Statistics.Interface.Data;
using VeloTime.Module.Timing.Interface.Data;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

public sealed partial class StatisticsAdminHeaderInteractionTests
{
    [Fact]
    public async Task RowAndAccessibleToggleExpandCollapseCacheAndRefreshWhileActionsStayIndependent()
    {
        await using var test = new Harness("nb-NO", "km/h");
        await test.Start();
        var entry = test.Entry;
        var panelId = $"statistics-contributions-{entry.Id:N}";
        var button = test.Renderer.ElementAttributes("button", "aria-controls", panelId);
        Assert.Equal("false", button["aria-expanded"]);
        Assert.Equal(panelId, button["aria-controls"]);
        var toggle = test.Renderer.Components<MudIconButton>().Single();
        Assert.True(test.Renderer.IsNativeButton(toggle));

        await test.ClickRow();
        Assert.True(test.Renderer.ElementAttributes("button", "aria-controls", panelId)["aria-expanded"]!.Equals("true"));
        Assert.Equal(new[] { entry.Id }, test.ContributionRequests);
        Assert.Equal(new[] { test.StartPointId, test.EndPointId }, test.NameRequests.Single());
        var rendered = test.AllText();
        Assert.Contains("Sample start", rendered);
        Assert.Contains("Sample finish", rendered);
        Assert.Contains("Admin facility", rendered);
        Assert.Contains("Finish point", rendered);
        Assert.Contains("Finish stand", rendered);
        Assert.Contains("km/h", rendered);
        Assert.Contains("36,000", rendered);
        Assert.Contains("08:00", rendered);
        Assert.Contains("[UTC]", rendered);
        Assert.Contains("Yes", rendered);
        Assert.Contains("No", rendered);
        Assert.Contains("1500", rendered);

        await test.ClickRow();
        Assert.Equal("false", test.Renderer.ElementAttributes("button", "aria-controls", panelId)["aria-expanded"]);
        await test.ClickToggle();
        Assert.Equal("true", test.Renderer.ElementAttributes("button", "aria-controls", panelId)["aria-expanded"]);
        await test.ClickToggle();
        Assert.Equal("false", test.Renderer.ElementAttributes("button", "aria-controls", panelId)["aria-expanded"]);
        Assert.Equal(1, test.ContributionRequests.Count); // page-lifetime cache avoids a second fetch
        Assert.Single(test.NameRequests);

        Assert.Contains(test.Renderer.Frames(), frame => frame.FrameType == RenderTreeFrameType.Attribute &&
            frame.AttributeName.Contains("stopPropagation", StringComparison.OrdinalIgnoreCase) && Equals(frame.AttributeValue, true));
        await test.ClickText("Restore automatic validation");
        Assert.Equal(1, test.Writes);
        Assert.Equal("false", test.Renderer.ElementAttributes("button", "aria-controls", panelId)["aria-expanded"]);

        await test.ClickText("Refresh");
        await test.ClickToggle();
        Assert.Equal(2, test.ContributionRequests.Count); // refresh clears the per-entry result cache
        Assert.Equal(2, test.NameRequests.Count);
    }

    [Theory]
    [InlineData(StatisticsEntryPassingsStatus.Available, "Finish point")]
    [InlineData(StatisticsEntryPassingsStatus.Forbidden, "permission to view Timing passings")]
    [InlineData(StatisticsEntryPassingsStatus.WindowTooLarge, "over seven days")]
    [InlineData(StatisticsEntryPassingsStatus.Unavailable, "not an empty result")]
    public async Task ContributionStatusesRemainDistinctAndTruncationIsVisible(StatisticsEntryPassingsStatus status, string expected)
    {
        await using var test = new Harness();
        test.ContributionsRespond = (id, _) => Task.FromResult(test.Contributions(id, status,
            samplesTruncated: true, passingsTruncated: status == StatisticsEntryPassingsStatus.Available));
        await test.Start();
        await test.ClickToggle();
        var text = test.AllText();
        Assert.Contains(expected, text);
        Assert.Contains("additional records were omitted", text);
        Assert.Contains("Sample start", text);
        Assert.DoesNotContain("No matching Timing passings were found", text);
        if (status != StatisticsEntryPassingsStatus.Available) Assert.DoesNotContain("Finish point", text);
    }

    [Fact]
    public async Task EmptyContributionsAndSeparateCoursePointLookupFailureCanRetryWithoutLosingSamples()
    {
        await using var test = new Harness();
        var empty = test.Contributions(status: StatisticsEntryPassingsStatus.Available) with { Samples = [], Passings = [] };
        test.ContributionsRespond = (_, _) => Task.FromResult(empty);
        await test.Start();
        await test.ClickToggle();
        Assert.Contains("No Statistics samples contributed", test.AllText());
        Assert.Contains("No matching Timing passings were found", test.AllText());
        Assert.Equal(1, test.ContributionRequests.Count);

        await test.ClickText("Refresh");
        var namesAttempt = 0;
        test.ContributionsRespond = (id, _) => Task.FromResult(test.Contributions(id));
        test.NamesRespond = (ids, _) => ++namesAttempt == 1
            ? Task.FromException<IReadOnlyDictionary<Guid, string>>(new HttpRequestException("lookup failed", null, System.Net.HttpStatusCode.BadGateway))
            : Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>
                { [test.StartPointId] = "Retried start", [test.EndPointId] = "Retried finish" });
        await test.ClickToggle();
        var failed = test.AllText();
        Assert.Contains("Course-point names could not be loaded", failed);
        Assert.Contains("Course-point name unavailable", failed);
        Assert.Contains("Finish point", failed); // passing and Statistics details survive lookup failure
        Assert.Equal(2, test.ContributionRequests.Count);
        await test.ClickText("Retry");
        Assert.Contains("Retried start", test.AllText());
        Assert.Contains("Retried finish", test.AllText());
        Assert.Equal(2, test.ContributionRequests.Count);
        Assert.Equal(3, test.NameRequests.Count);
    }

    [Fact]
    public async Task ContributionRequestErrorHasEntryChangedMessageAndAnIndependentRetry()
    {
        await using var test = new Harness();
        var attempts = 0;
        test.ContributionsRespond = (id, _) => ++attempts == 1
            ? Task.FromException<StatisticsEntryContributionsDTO>(new HttpRequestException("missing", null, System.Net.HttpStatusCode.NotFound))
            : Task.FromResult(test.Contributions(id));
        await test.Start();
        await test.ClickToggle();
        Assert.Contains("This entry changed or is no longer available", test.AllText());
        Assert.DoesNotContain("Sample start", test.AllText());
        await test.ClickText("Retry");
        Assert.Contains("Sample start", test.AllText());
        Assert.Equal(2, test.ContributionRequests.Count);
        Assert.Single(test.NameRequests);
    }

    [Fact]
    public async Task RefreshCancelsInFlightExpansionAndIgnoresLateStaleResponse()
    {
        await using var test = new Harness();
        var stale = new TaskCompletionSource<StatisticsEntryContributionsDTO>(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        test.ContributionsRespond = (id, _) => Interlocked.Increment(ref call) == 1
            ? stale.Task
            : Task.FromResult(test.Contributions(id));
        await test.Start();
        var oldClick = test.ClickToggle();
        await test.WaitForContributionRequests(1);
        Assert.Contains("Loading entry contributions", test.AllText());
        Assert.Contains(test.Renderer.Frames(), frame => frame.FrameType == RenderTreeFrameType.Attribute &&
            frame.AttributeName == "role" && Equals(frame.AttributeValue, "status"));
        var canceled = test.ContributionTokens[0];
        await test.ClickText("Refresh");
        Assert.True(canceled.IsCancellationRequested);
        stale.SetResult(test.Contributions(samplesTruncated: true) with
        {
            Samples = [new AdminStatisticsSampleDTO(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                Guid.NewGuid(), Guid.NewGuid(), 999999, TimeSpan.FromDays(1), 99, Guid.NewGuid())]
        });
        await oldClick;
        Assert.DoesNotContain("999999", test.AllText());
        await test.ClickToggle();
        Assert.Equal(2, test.ContributionRequests.Count);
        Assert.Contains("Sample start", test.AllText());
        Assert.DoesNotContain("999999", test.AllText());
    }
}
