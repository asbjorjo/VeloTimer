using System.Text.Json;
using VeloTime.Module.Facilities.Interface.Data;

namespace VeloTime.WebUI.Mud.Client.Services;

public sealed class AdministrationHttpClient(HttpClient client)
{
    public async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = "The request could not be completed.";
            try
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    message = detail.GetString() ?? message;
            }
            catch (JsonException) { }
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return response.StatusCode == System.Net.HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
    }

    public async Task SendTimingAssociationAsync(string path, SetCoursePointTimingAssociationRequest body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            try
            {
                var preview = await response.Content.ReadFromJsonAsync<TimingAssociationConflictPreview>(
                    cancellationToken: cancellationToken);
                if (preview is not null && !string.IsNullOrWhiteSpace(preview.ConfirmationToken))
                    throw new TimingAssociationConflictException(preview);
            }
            catch (JsonException)
            {
                // A 409 without a valid contract preview is an explicit transport failure.
            }
            throw new HttpRequestException("The timing-association conflict preview could not be read.",
                null, System.Net.HttpStatusCode.BadGateway);
        }
        if (!response.IsSuccessStatusCode)
            throw await CreateFailureAsync(response, cancellationToken);
    }

    private static async Task<HttpRequestException> CreateFailureAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var message = "The request could not be completed.";
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("detail", out var detail) &&
                detail.ValueKind == JsonValueKind.String)
                message = detail.GetString() ?? message;
        }
        catch (JsonException) { }
        return new HttpRequestException(message, null, response.StatusCode);
    }
}