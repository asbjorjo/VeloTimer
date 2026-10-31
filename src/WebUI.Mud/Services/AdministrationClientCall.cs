using System.Net;
using System.Text.Json;

namespace VeloTime.WebUI.Mud.Services;

internal static class AdministrationClientCall
{
    internal static async Task ExecuteNoContent(Func<Task> action)
    {
        try { await action(); }
        // Generated clients can reject successful statuses not declared in the upstream OpenAPI contract.
        catch (Exception exception) when (exception switch
        {
            Module.Facilities.Client.ApiException api => api.StatusCode is >= 200 and <= 299,
            Module.Timing.Client.ApiException api => api.StatusCode is >= 200 and <= 299,
            Module.Statistics.Client.ApiException api => api.StatusCode is >= 200 and <= 299,
            Keycloak.ApiException api => api.StatusCode is >= 200 and <= 299,
            _ => false
        })
        {
        }
    }

    internal static async Task<TResult> Execute<TResult>(Func<Task<TResult>> action)
    {
        try { return await action(); }
        catch (Exception exception) when (exception is Module.Facilities.Client.ApiException or Module.Timing.Client.ApiException or Module.Statistics.Client.ApiException)
        {
            var (status, response) = exception switch
            {
                Module.Facilities.Client.ApiException api => (api.StatusCode, api.Response),
                Module.Timing.Client.ApiException api => (api.StatusCode, api.Response),
                Module.Statistics.Client.ApiException api => (api.StatusCode, api.Response),
                _ => (502, "")
            };
            if (status is not (400 or 401 or 403 or 404 or 409 or 422 or 503)) throw;
            var message = "The request could not be completed.";
            try
            {
                using var document = JsonDocument.Parse(response);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                    message = string.Join(" ", document.RootElement.EnumerateArray().Where(element => element.ValueKind == JsonValueKind.String).Select(element => element.GetString()));
                else if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    message = detail.GetString() ?? message;
            }
            catch (JsonException) { }
            throw new HttpRequestException(message, exception, (HttpStatusCode)status);
        }
    }
}