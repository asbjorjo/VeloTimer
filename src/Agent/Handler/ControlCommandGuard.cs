using Microsoft.Extensions.Logging;
using VeloTime.Agent.Capabilities;

namespace VeloTime.Agent.Handler;

internal static class ControlCommandGuard
{
    /// <summary>
    /// The subscription's correlation filter already limits delivery to this agent; this check guards against a
    /// misconfigured filter. A command for another agent is logged and acknowledged, never acted on.
    /// </summary>
    public static bool IsForThisAgent(string commandAgentId, AgentOptions options, ILogger logger, string command)
    {
        if (string.IsNullOrEmpty(options.AgentId) || string.Equals(commandAgentId, options.AgentId, StringComparison.Ordinal))
            return true;
        logger.LogWarning("Ignoring {Command} addressed to agent {CommandAgentId}; this agent is {AgentId}.",
            command, commandAgentId, options.AgentId);
        return false;
    }
}
