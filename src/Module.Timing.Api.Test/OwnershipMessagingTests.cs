using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using VeloTime.Module.Timing.Api;

namespace VeloTime.Module.Timing.Api.Test;

public sealed class OwnershipMessagingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingConnectionFailsExplicitly(string? connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MessageBus"] = connectionString });
        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddOwnershipMessaging());
        Assert.Contains("ConnectionStrings:MessageBus is required", exception.Message);
    }
}
