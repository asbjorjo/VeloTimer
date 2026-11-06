using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.WebUI.Mud.Client.Services;

namespace VeloTime.Platform.Test;

// Exercise the RELEASE PUBLISH manifest over real HTTP, not Development's
// source-file provider or a mocked static route. No Redis/OIDC/broker is needed:
// this focused host uses the same MapStaticAssets pipeline as the Auto host.
public sealed class NotificationsPublishedAssetTests(NotificationsPublishFixture publish)
    : IClassFixture<NotificationsPublishFixture>
{
    [Theory]
    [InlineData("")]
    [InlineData("/velotime")]
    public async Task ImportedModuleIsServedFromProductionPublish(string pathBase)
    {
        var manifest = Path.Combine(publish.Output, "VeloTime.WebUI.Mud.staticwebassets.endpoints.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifest));
        var routes = document.RootElement.GetProperty("Endpoints").EnumerateArray()
            .Select(endpoint => endpoint.GetProperty("Route").GetString()).ToArray();
        var moduleRoute = NotificationsBrowserService.ModulePath;
        Assert.StartsWith("./", moduleRoute);
        Assert.Contains(moduleRoute[2..], routes);
        Assert.DoesNotContain("_content/VeloTime.WebUI.Mud.Client/notifications.js", routes);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = publish.Output,
            WebRootPath = Path.Combine(publish.Output, "wwwroot")
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        Assert.True(app.Environment.IsProduction());
        if (pathBase.Length > 0) app.UsePathBase(pathBase);
        app.MapStaticAssets(manifest);
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            // Resolve the exact shared import specifier as the browser does
            // against <base href>, including a deployment path base.
            var documentBase = new Uri(address + pathBase + "/");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync(new Uri(documentBase, moduleRoute));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("javascript", response.Content.Headers.ContentType!.MediaType!);
            Assert.Equal(await File.ReadAllTextAsync(Path.Combine(publish.Output, "wwwroot", moduleRoute[2..])),
                await response.Content.ReadAsStringAsync());
            using var oldResponse = await client.GetAsync(
                new Uri(documentBase, "./_content/VeloTime.WebUI.Mud.Client/notifications.js"));
            Assert.Equal(HttpStatusCode.NotFound, oldResponse.StatusCode);
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await app.StopAsync(stop.Token);
        }
    }
}

public sealed class NotificationsPublishFixture : IAsyncLifetime
{
    public string Output { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "VeloTimer.sln")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        Output = Path.Combine(repository.FullName, ".validation", "notifications-assets-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repository.FullName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "publish", "src/WebUI.Mud/VeloTime.WebUI.Mud.csproj",
            "--configuration", "Release", "--no-restore", "--output", Output, "--verbosity", "quiet"
        }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not publish WebUI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Release WebUI publish exceeded five minutes.");
        }
        Assert.True(process.ExitCode == 0, $"Release publish failed:\n{await stdout}\n{await stderr}");
    }

    public Task DisposeAsync()
    {
        if (Output is not null && Directory.Exists(Output)) Directory.Delete(Output, recursive: true);
        return Task.CompletedTask;
    }
}
