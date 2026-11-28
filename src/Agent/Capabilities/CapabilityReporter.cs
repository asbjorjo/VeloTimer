using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SlimMessageBus;
using VeloTime.Agent.Interface;
using VeloTime.Agent.Interface.Messages.Events;

namespace VeloTime.Agent.Capabilities;

/// <summary>
/// Sends <see cref="AgentCapabilitiesV1"/> through the agent outbox on startup, whenever a provider reports a change,
/// and when <see cref="RequestReport"/> is called (for example by <c>ReportCapabilitiesCommandV1</c>). Requests that
/// arrive while a report is pending are coalesced into it.
/// </summary>
public sealed class CapabilityReporter : BackgroundService
{
    private readonly IReadOnlyList<IAgentCapabilityProvider> providers;
    private readonly IServiceScopeFactory scopes;
    private readonly AgentOptions options;
    private readonly TimeProvider time;
    private readonly ILogger<CapabilityReporter> log;
    private readonly Channel<bool> requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public CapabilityReporter(IEnumerable<IAgentCapabilityProvider> providers, IServiceScopeFactory scopes,
        IOptions<AgentOptions> options, ILogger<CapabilityReporter> log, TimeProvider? time = null)
    {
        this.providers = providers.ToArray();
        this.scopes = scopes;
        this.options = options.Value;
        this.log = log;
        this.time = time ?? TimeProvider.System;
    }

    /// <summary>Asks for a new report to be sent soon. Never blocks.</summary>
    public void RequestReport() => requests.Writer.TryWrite(true);

    private void OnCapabilityChanged(object? sender, EventArgs e) => RequestReport();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.ReportCapabilities)
        {
            log.LogInformation("Capability reporting is disabled.");
            return;
        }
        foreach (var provider in providers) provider.CapabilityChanged += OnCapabilityChanged;
        try
        {
            RequestReport();
            while (await requests.Reader.WaitToReadAsync(stoppingToken))
            {
                requests.Reader.TryRead(out _);
                try
                {
                    await SendReportAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    log.LogWarning(exception, "Could not enqueue the capability report; retrying in {Delay}.", options.ReportRetryDelay);
                    await Task.Delay(options.ReportRetryDelay, time, stoppingToken);
                    RequestReport();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var provider in providers) provider.CapabilityChanged -= OnCapabilityChanged;
        }
    }

    /// <summary>Builds the current report from every provider.</summary>
    public async Task<AgentCapabilitiesV1> BuildReportAsync(CancellationToken cancellationToken)
    {
        var capabilities = new List<AgentCapability>();
        foreach (var provider in providers)
            capabilities.AddRange(await provider.GetCapabilitiesAsync(cancellationToken));
        return new AgentCapabilitiesV1(
            ReportedAtUtc: time.GetUtcNow().UtcDateTime,
            AgentType: options.AgentType,
            AgentVersion: options.AgentVersion ?? DefaultVersion(),
            ControlProtocolVersion: AgentControl.ProtocolVersion,
            Capabilities: capabilities);
    }

    private async Task SendReportAsync(CancellationToken cancellationToken)
    {
        var report = await BuildReportAsync(cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.Publish(report, cancellationToken: cancellationToken);
        log.LogInformation("Enqueued capability report with {Count} capabilities: {Names}.",
            report.Capabilities.Count, string.Join(", ", report.Capabilities.Select(c => c.Name)));
    }

    private static string DefaultVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(CapabilityReporter).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";
        // Drop SourceLink build metadata ("+<commit>") so the report stays readable.
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }
}
