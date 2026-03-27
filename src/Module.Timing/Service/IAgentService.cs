using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Service;

public interface IAgentService
{
    Task<Passing> RegisterPassingAsync(
        string agentId,
        string transponderId,
        string loopId,
        DateTime time,
        string transponderType,
        CancellationToken cancellationToken = default);
    Task<Sample?> RegisterSampleAsync(
        Passing passing,
        CancellationToken cancellationToken = default);
}
