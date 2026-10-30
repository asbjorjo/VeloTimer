using System.Net;
using System.Text.Json;
using VeloTime.Module.Timing.Client;
using VeloTime.WebUI.Mud.Client.Services;
using Contracts = VeloTime.Module.Timing.Interface.Data;

namespace VeloTime.WebUI.Mud.Services;

public sealed class TransponderManagementService(ITimingClient client, ITransponderOwnerService owners) : ITransponderManagementService
{
    public Task<Contracts.TransponderPage> GetTranspondersAsync(bool administration, int page = 0, int pageSize = 10,
        string search = "", bool includeHistory = true, CancellationToken cancellationToken = default) =>
        Execute(async () =>
        {
            ValidatePage(page, pageSize);
            cancellationToken.ThrowIfCancellationRequested();
            List<Guid>? matchingIds = null;
            if (!string.IsNullOrEmpty(search))
            {
                matchingIds = [];
                var ownProfile = administration ? null : await owners.GetOwnProfileAsync(cancellationToken);
                // Stream compact search metadata to preserve matches across the full displayed name string.
                for (var candidatePage = 0; ; candidatePage++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidates = administration
                        ? await client.LookupTranspondersAsync(candidatePage, 100, "", true, cancellationToken)
                        : await client.LookupMyTranspondersAsync(candidatePage, 100, "", true, cancellationToken);
                    var names = new Dictionary<Guid, string>();
                    if (administration)
                        foreach (var batch in candidates.Items
                            .Where(item => !$"{item.Label} {item.SystemId} ".Contains(search, StringComparison.OrdinalIgnoreCase))
                            .SelectMany(item => item.OwnerIds).Distinct().Chunk(100))
                            foreach (var owner in await owners.ResolveAsync(batch, cancellationToken))
                                names[owner.UserId] = owner.DisplayName;
                    foreach (var candidate in candidates.Items)
                    {
                        var ownerNames = candidate.OwnerIds.Select(id => administration
                            ? names.GetValueOrDefault(id, "Owner names unavailable") : ownProfile!.DisplayName);
                        if ($"{candidate.Label} {candidate.SystemId} ".Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            $"{candidate.Label} {candidate.SystemId} {string.Join(' ', ownerNames)}".Contains(search, StringComparison.OrdinalIgnoreCase))
                            matchingIds.Add(candidate.Id);
                    }
                    if (candidates.Items.Count == 0 || (long)(candidatePage + 1) * 100 >= candidates.TotalCount) break;
                }
            }
            var request = new TransponderPageRequest
            {
                Page = page, PageSize = pageSize, Search = search, IncludeHistory = includeHistory,
                MatchingTransponderIds = matchingIds
            };
            var result = administration ? await client.QueryTranspondersAsync(request, cancellationToken)
                : await client.QueryMyTranspondersAsync(request, cancellationToken);
            return new Contracts.TransponderPage(result.Items.Select(row => new Contracts.TransponderRow(
                new(row.Transponder.Id, row.Transponder.System, row.Transponder.SystemId, row.Transponder.Label, [], row.Transponder.LastSeen, row.Transponder.CanViewStatus),
                row.Ownership is { } owner ? new(owner.Id, owner.TransponderId, owner.OwnerId, owner.OwnedFrom, owner.OwnedTo) : null)).ToList(), result.TotalCount);
        });

    public Task<Contracts.TransponderLookupPage> LookupTranspondersAsync(int page, string search, CancellationToken cancellationToken = default) =>
        Execute(async () =>
        {
            ValidatePage(page, 10);
            var result = await client.LookupTranspondersAsync(page, 10, search, false, cancellationToken);
            return new Contracts.TransponderLookupPage(result.Items.Select(item =>
                new Contracts.TransponderLookupItem(item.Id, item.Label, item.SystemId, [])).ToList(), result.TotalCount);
        });

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 0 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new HttpRequestException("Page must be nonnegative and page size must be between 1 and 100, without offset overflow.", null, HttpStatusCode.BadRequest);
    }

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
        if (id.HasValue) await AdministrationClientCall.ExecuteNoContent(() => client.UpdateTransponderOwnershipAsync(id.Value, body, cancellationToken));
        else await AdministrationClientCall.ExecuteNoContent(() => client.CreateTransponderOwnershipAsync(body, cancellationToken));
        return true;
    });

    public Task EndOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        if (administration) await AdministrationClientCall.ExecuteNoContent(() => client.EndTransponderOwnershipAsync(id, cancellationToken));
        else await AdministrationClientCall.ExecuteNoContent(() => client.EndMyTransponderOwnershipAsync(id, cancellationToken));
        return true;
    });

    public Task DeleteOwnershipAsync(Guid id, bool administration, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        if (administration) await AdministrationClientCall.ExecuteNoContent(() => client.DeleteTransponderOwnershipAsync(id, cancellationToken));
        else await AdministrationClientCall.ExecuteNoContent(() => client.DeleteMyTransponderOwnershipAsync(id, cancellationToken));
        return true;
    });

    public Task SaveTransponderAsync(Guid id, string label, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await client.UpdateTransponderAsync(id, new SaveTransponderRequest { Label = label }, cancellationToken);
        return true;
    });

    public Task DeleteTransponderAsync(Guid id, CancellationToken cancellationToken = default) => Execute(async () =>
    {
        await AdministrationClientCall.ExecuteNoContent(() => client.DeleteTransponderAsync(id, cancellationToken));
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