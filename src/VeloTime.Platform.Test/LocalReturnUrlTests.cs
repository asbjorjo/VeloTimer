using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VeloTime.WebUI.Mud;

namespace VeloTime.Platform.Test;

public sealed class LocalReturnUrlTests
{
    [Theory]
    // Values the UI sends: RedirectToLogin uses the absolute page URL, the logout form a base-relative path.
    [InlineData("https://velotime.test/profile?tab=1", "/profile?tab=1")]
    [InlineData("https://velotime.test/profile#top", "/profile")]
    [InlineData("profile", "/profile")]
    [InlineData("/statistics", "/statistics")]
    [InlineData("a b", "/a b")] // Previously threw UriFormatException.
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    // Open-redirect and malformed attempts fall back to the root.
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("\\\\evil.example", "/")]
    [InlineData("https://velotime.test//evil.example", "/")]
    [InlineData("https://evil.example//evil.example/x", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("file:///etc/passwd", "/")]
    [InlineData("/ok\r\nLocation: https://evil.example", "/")]
    public void Normalize_KeepsOnlyLocalPaths(string? returnUrl, string expected) =>
        Assert.Equal(expected, LocalReturnUrl.Normalize(returnUrl, CreateContext()));

    [Theory]
    [InlineData(null, "/app")]
    [InlineData("//evil.example", "/app")]
    [InlineData("profile", "/app/profile")]
    [InlineData("https://velotime.test/app/profile", "/app/profile")]
    public void Normalize_UsesPathBase(string? returnUrl, string expected) =>
        Assert.Equal(expected, LocalReturnUrl.Normalize(returnUrl, CreateContext("/app")));

    [Fact]
    public void LoginProperties_RedirectProtocolRelativeUrlToRoot() =>
        Assert.Equal("/", LoginLogoutEndpointRouteBuilderExtensions
            .GetAuthProperties(CreateContext(), "//evil.example").RedirectUri);

    private static DefaultHttpContext CreateContext(string pathBase = "")
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("velotime.test");
        http.Request.PathBase = pathBase;
        return http;
    }
}
