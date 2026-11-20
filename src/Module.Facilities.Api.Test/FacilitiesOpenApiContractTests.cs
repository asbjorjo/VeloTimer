using System.Text.Json;

namespace VeloTime.Module.Facilities.Api.Test;

/// <summary>
/// Guards response schemas that generated NSwag clients depend on: an operation without a
/// 200 schema generates a non-generic Task and silently drops the typed result.
/// </summary>
public sealed class FacilitiesOpenApiContractTests(FacilitiesApiFactory factory) : IClassFixture<FacilitiesApiFactory>
{
    [Theory]
    [InlineData("/api/facilities/coursepoint/{id}/timing-link", "put", "CoursePointDTO", new[] { "400", "404", "409" })]
    [InlineData("/api/facilities/timingpoint/{start}/distance/{end}", "get", "CoursePointDistance", new[] { "404", "422" })]
    public async Task Operation_DeclaresTypedSuccessSchemaAndErrorStatuses(
        string path, string method, string schema, string[] errorStatuses)
    {
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var responses = document.RootElement.GetProperty("paths").GetProperty(path)
            .GetProperty(method).GetProperty("responses");
        Assert.Equal($"#/components/schemas/{schema}", responses.GetProperty("200")
            .GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString());
        Assert.All(errorStatuses, status => Assert.True(responses.TryGetProperty(status, out _), status));
    }
}
