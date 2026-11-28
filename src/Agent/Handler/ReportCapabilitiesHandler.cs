using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SlimMessageBus;
using VeloTime.Agent.Capabilities;
using VeloTime.Agent.Interface.Messages.Control;

namespace VeloTime.Agent.Handler;

public class ReportCapabilitiesHandler(CapabilityReporter reporter, IOptions<AgentOptions> options,
    ILogger<ReportCapabilitiesHandler> logger) : IConsumer<ReportCapabilitiesCommandV1>
{
    public Task OnHandle(ReportCapabilitiesCommandV1 message, CancellationToken cancellationToken)
    {
        if (!ControlCommandGuard.IsForThisAgent(message.AgentId, options.Value, logger, nameof(ReportCapabilitiesCommandV1)))
            return Task.CompletedTask;
        logger.LogInformation("Capability report requested ({RequestId}).", message.RequestId);
        reporter.RequestReport();
        return Task.CompletedTask;
    }
}
