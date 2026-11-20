using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VeloTime.Module.Notifications.Interface;

namespace VeloTime.Module.Notifications.Api.Test;

public sealed class NotificationsApiContractTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task InvalidBearerCannotDowngradeToAnonymousPublicActive()
    {
        using var factory = new NotificationsApiFactory(database);
        using var client = factory.CreateContractClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid.signed.token");

        using var response = await client.GetAsync(NotificationRoutes.Active);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(NotificationErrorCodes.AuthenticationRequired,
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApiHasBearerSecurityButNoSessionAssertionHeader()
    {
        using var factory = new NotificationsApiFactory(database) { EnvironmentName = "Development" };
        using var client = factory.CreateContractClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var headerParameters = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(item => item.Value.TryGetProperty("parameters", out _))
            .SelectMany(item => item.Value.GetProperty("parameters").EnumerateArray())
            .Where(parameter => parameter.TryGetProperty("in", out var location) &&
                location.GetString() == "header")
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        Assert.DoesNotContain(headerParameters, name =>
            name?.Contains("session", StringComparison.OrdinalIgnoreCase) == true ||
            name?.Contains("assertion", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains("Bearer", document.RootElement.GetProperty("components").GetProperty("securitySchemes").EnumerateObject()
            .Select(scheme => scheme.Name));
    }
}
