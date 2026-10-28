using System.Net;
using System.Text.Json;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using Contracts = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Services;

public sealed class TransponderManagementService(ITimingClient client) : ITransponderManagementService
{
    public Task<IReadOnlyList<Contracts.TransponderDTO>> GetTranspondersAsync(bool administration, CancellationToken cancellationToken = default) =>
        Execute<IReadOnlyList<Contracts.TransponderDTO>>(async () =>
        {
            var transponders = administration ? await client.TranspondersAllAsync(cancellationToken) : await client.ListMyTranspondersAsync(cancellationToken);
            return transponders.Select(transponder => new Contracts.TransponderDTO(transponder.Id, transponder.System, transponder.SystemId, transponder.Label,
                transponder.Ownerships.Select(owner => new Contracts.TransponderOwnershipDTO(owner.Id, owner.TransponderId, owner.OwnerId, owner.OwnedFrom, owner.OwnedTo)).ToList()))
                .OrderBy(transponder => transponder.Label).ToList();
        });

    public Task RegisterAsync(string code, bool administration, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        if (administration) await client.TranspondersPOSTAsync(null!, code, "X2", cancellationToken);
        else await client.ClaimMyTransponderAsync(new ClaimTransponderRequest { Code = code }, cancellationToken);
        return true;
    });

    /// <inheritdoc />
    public Task<Contracts.RegisterTransponderResult> RegisterOwnershipAsync(Contracts.RegisterTransponderRequest request, bool administration,
        CancellationToken cancellationToken = default) => Execute(async () =>
    {
        var body = new RegisterTransponderRequest
        {
            Code = request.Code, OwnedFrom = request.OwnedFrom, OwnedTo = request.OwnedTo,
            OwnerId = request.OwnerId, ConfirmationToken = request.ConfirmationToken
        };
        var result = administration
            ? await client.RegisterTransponderOwnershipAsync(body, cancellationToken)
            : await client.RegisterMyTransponderAsync(body, cancellationToken);
        return new Contracts.RegisterTransponderResult(result.RequiresConfirmation, result.ConfirmationToken,
            result.ExistingOwnerships.Select(owner => new Contracts.TransponderOwnershipDTO(owner.Id, owner.TransponderId, owner.OwnerId, owner.OwnedFrom, owner.OwnedTo)).ToList());
    });

    public Task SaveOwnershipAsync(Guid? id, Contracts.SaveTransponderOwnershipRequest request, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        var body = new SaveTransponderOwnershipRequest { TransponderId = request.TransponderId, OwnerId = request.OwnerId, OwnedFrom = request.OwnedFrom, OwnedTo = request.OwnedTo };
        if (id.HasValue) await client.UpdateTransponderOwnershipAsync(id.Value, body, cancellationToken);
        else await client.CreateTransponderOwnershipAsync(body, cancellationToken);
        return true;
    });

    public Task EndOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        if (administration) await client.EndTransponderOwnershipAsync(id, cancellationToken);
        else await client.EndMyTransponderOwnershipAsync(id, cancellationToken);
        return true;
    });

    public Task DeleteOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        if (administration) await client.DeleteTransponderOwnershipAsync(id, cancellationToken);
        else await client.DeleteMyTransponderOwnershipAsync(id, cancellationToken);
        return true;
    });

    public Task SaveTransponderAsync(Guid id, string label, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await client.UpdateTransponderAsync(id, new SaveTransponderRequest { Label = label }, cancellationToken);
        return true;
    });

    public Task DeleteTransponderAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await client.DeleteTransponderAsync(id, cancellationToken);
        return true;
    });

    private static async Task<TResult> Execute<TResult>(Func<Task<TResult>> action)
    {
        try { return await action(); }
        catch (ApiException exception) when (exception.StatusCode is 400 or 401 or 403 or 404 or 409 or 422 or 503)
        {
            var detail = "The request could not be completed.";
            try
            {
                using var document = JsonDocument.Parse(exception.Response);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                    detail = string.Join(" ", document.RootElement.EnumerateArray().Where(element => element.ValueKind == JsonValueKind.String).Select(element => element.GetString()));
                else if (document.RootElement.TryGetProperty("detail", out var property) && property.ValueKind == JsonValueKind.String)
                    detail = property.GetString() ?? detail;
            }
            catch (JsonException) { }
            throw new HttpRequestException(detail, exception, (HttpStatusCode)exception.StatusCode);
        }
    }
}