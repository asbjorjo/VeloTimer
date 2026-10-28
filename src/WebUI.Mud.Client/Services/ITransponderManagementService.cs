using VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public interface ITransponderManagementService
{
    Task<IReadOnlyList<TransponderDTO>> GetTranspondersAsync(bool administration, CancellationToken cancellationToken = default);
    Task RegisterAsync(string code, bool administration, CancellationToken cancellationToken = default);
    /// <summary>Registers the selected ownership period or returns a shortening proposal requiring confirmation.</summary>
    /// <param name="request">The code, dates, optional administrator-selected owner, and confirmed snapshot.</param>
    /// <param name="administration">Whether to use the timing-administrator registration route.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The saved outcome or a confirmation proposal.</returns>
    Task<RegisterTransponderResult> RegisterOwnershipAsync(RegisterTransponderRequest request, bool administration, CancellationToken cancellationToken = default);
    Task SaveOwnershipAsync(Guid? id, SaveTransponderOwnershipRequest request, CancellationToken cancellationToken = default);
    Task EndOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default);
    Task DeleteOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default);
    Task SaveTransponderAsync(Guid id, string label, CancellationToken cancellationToken = default);
    Task DeleteTransponderAsync(Guid id, CancellationToken cancellationToken = default);
}