using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface ITransponderManagementService
{
    Task<IReadOnlyList<TransponderDTO>> GetTranspondersAsync(bool administration, CancellationToken cancellationToken = default);
    Task RegisterAsync(string code, bool administration, CancellationToken cancellationToken = default);
    Task SaveOwnershipAsync(Guid? id, SaveTransponderOwnershipRequest request, CancellationToken cancellationToken = default);
    Task EndOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default);
    Task DeleteOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default);
    Task SaveTransponderAsync(Guid id, string label, CancellationToken cancellationToken = default);
    Task DeleteTransponderAsync(Guid id, CancellationToken cancellationToken = default);
}