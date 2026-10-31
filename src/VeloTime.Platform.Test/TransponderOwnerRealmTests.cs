using System.Text.Json;

namespace VeloTime.Platform.Test;

public sealed class TransponderOwnerRealmTests
{
    [Theory]
    [InlineData("timing-admin", true)]
    [InlineData("velotime-admin", true)]
    [InlineData("facilities-admin", true)]
    [InlineData("statistics-admin", true)]
    [InlineData("velotime-user", false)]
    public async Task OwnerDirectory_RealmRolesGrantOnlyReadPermissions(string roleName, bool canReadUsers)
    {
        using var realm = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "velotime-realm.json")));
        var role = realm.RootElement.GetProperty("roles").GetProperty("realm").EnumerateArray().Single(role => role.GetProperty("name").GetString() == roleName);
        if (!canReadUsers)
        {
            Assert.False(role.TryGetProperty("composites", out _));
            return;
        }
        Assert.True(role.GetProperty("composite").GetBoolean());
        var permissions = role.GetProperty("composites").GetProperty("client").GetProperty("realm-management").EnumerateArray()
            .Select(permission => permission.GetString() ?? throw new InvalidOperationException("A realm permission must be a string.")).Order().ToArray();
        Assert.Equal(["query-users", "view-users"], permissions);
    }

    [Fact]
    public async Task OwnerDirectory_DelegatedTokensIncludeClientRolesAndResolvedAudienceWithoutNewSecrets()
    {
        using var realm = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "velotime-realm.json")));
        var web = realm.RootElement.GetProperty("clients").EnumerateArray().Single(client => client.GetProperty("clientId").GetString() == "velotime.webui");
        Assert.True(web.GetProperty("publicClient").GetBoolean());
        Assert.True(web.GetProperty("fullScopeAllowed").GetBoolean());
        Assert.False(web.TryGetProperty("secret", out _));
        var scope = realm.RootElement.GetProperty("clientScopes").EnumerateArray().Single(scope => scope.GetProperty("name").GetString() == "velotime");
        Assert.Contains("velotime", realm.RootElement.GetProperty("defaultDefaultClientScopes").EnumerateArray().Select(scope => scope.GetString()));
        var mappers = scope.GetProperty("protocolMappers").EnumerateArray().ToArray();
        Assert.Contains(mappers, mapper => mapper.GetProperty("protocolMapper").GetString() == "oidc-usermodel-client-role-mapper" &&
            mapper.GetProperty("config").GetProperty("access.token.claim").GetString() == "true");
        Assert.Contains(mappers, mapper => mapper.GetProperty("protocolMapper").GetString() == "oidc-audience-resolve-mapper" &&
            mapper.GetProperty("config").GetProperty("access.token.claim").GetString() == "true");
    }
}
