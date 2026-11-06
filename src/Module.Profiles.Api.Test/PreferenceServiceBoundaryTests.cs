using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VeloTime.Module.Profiles.Service;
using VeloTime.Module.Profiles.Storage;

namespace VeloTime.Module.Profiles.Api.Test;

public sealed class PreferenceServiceBoundaryTests
{
    [Theory]
    [MemberData(nameof(PreferenceEndpointTests.InvalidIdentities), MemberType = typeof(PreferenceEndpointTests))]
    public async Task InvalidIdentity_NeverOpensPersistence(string? issuer, string? subject)
    {
        var interceptor = new FailOpening();
        await using var context = Context(interceptor);
        var service = new LanguagePreferenceService(context);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetAsync(issuer!, subject!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.UpdateAsync(issuer!, subject!, "en"));
        Assert.Equal(0, interceptor.Attempts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EN")]
    [InlineData("nb-no")]
    [InlineData(" en")]
    [InlineData("en ")]
    [InlineData("nb")]
    [InlineData("no")]
    [InlineData("nn-NO")]
    [InlineData("en-US")]
    public async Task InvalidLanguage_NeverOpensPersistence(string? language)
    {
        var interceptor = new FailOpening();
        await using var context = Context(interceptor);
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new LanguagePreferenceService(context).UpdateAsync("issuer", "subject", language));
        Assert.Equal("language", exception.ParamName);
        Assert.Equal(0, interceptor.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistenceFailure_PropagatesWithoutFalseResult(bool write)
    {
        var interceptor = new FailOpening();
        await using var context = Context(interceptor);
        var service = new LanguagePreferenceService(context);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => write
            ? service.UpdateAsync("issuer", "subject", "en")
            : service.GetAsync("issuer", "subject"));
        Assert.Same(interceptor.Failure, exception);
        Assert.Equal(1, interceptor.Attempts);
    }

    private static ProfilesDbContext Context(FailOpening interceptor) => new(
        new DbContextOptionsBuilder<ProfilesDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused")
            .UseSnakeCaseNamingConvention().AddInterceptors(interceptor).Options);

    private sealed class FailOpening : DbConnectionInterceptor
    {
        public int Attempts { get; private set; }
        public InvalidOperationException Failure { get; } = new("Synthetic persistence failure");

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw Failure;
        }
    }
}
