using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeloTime.Module.Profiles.Model;
using VeloTime.Module.Profiles.Service;

namespace VeloTime.Module.Profiles.Api.Test;

[Trait("Category", "PostgreSQL")]
public sealed class PostgresPreferenceTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [PostgresFact]
    public async Task Migration_ReapplicationHasNoPendingChanges_AndRetainsSavedData()
    {
        var subject = Guid.NewGuid().ToString();
        await using (var first = database.CreateContext())
        {
            Assert.NotEmpty(await first.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await first.Database.GetPendingMigrationsAsync());
            Assert.False(first.Database.HasPendingModelChanges());
            await new LanguagePreferenceService(first).UpdateAsync("migration", subject, "en");
        }
        await using var restarted = database.CreateContext();
        await restarted.Database.MigrateAsync();
        await restarted.Database.MigrateAsync();
        Assert.Empty(await restarted.Database.GetPendingMigrationsAsync());
        Assert.Equal("en", (await new LanguagePreferenceService(restarted).GetAsync("migration", subject)).Language);
    }

    [PostgresFact]
    public async Task MissingReads_ReturnNull_AndNeverCreateRows()
    {
        var issuer = "missing-" + Guid.NewGuid();
        await using var context = database.CreateContext();
        var service = new LanguagePreferenceService(context);
        Assert.Null((await service.GetAsync(issuer, "subject")).Language);
        Assert.Null((await service.GetAsync(issuer, "subject")).Language);
        Assert.False(await context.LanguagePreferences.AnyAsync(p => p.Issuer == issuer));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgresFact]
    public async Task CompositeIdentity_IsExactCaseSensitiveAndIsolated_AcrossFreshContexts()
    {
        var prefix = "https://issuer.example/" + Guid.NewGuid();
        (string Issuer, string Subject, string Language)[] identities =
        [
            (prefix, "User", "en"),
            (prefix, "user", "nb-NO"),
            (prefix.ToUpperInvariant(), "User", "nb-NO"),
            (prefix + "/", "User", "en"),
            (prefix, " User ", "nb-NO"),
            (" " + prefix + " ", "User", "nb-NO")
        ];
        foreach (var identity in identities)
        {
            await using var context = database.CreateContext();
            await new LanguagePreferenceService(context).UpdateAsync(identity.Issuer, identity.Subject, identity.Language);
        }
        foreach (var identity in identities)
        {
            await using var context = database.CreateContext();
            Assert.Equal(identity.Language,
                (await new LanguagePreferenceService(context).GetAsync(identity.Issuer, identity.Subject)).Language);
        }
        await using var verification = database.CreateContext();
        var issuers = identities.Select(i => i.Issuer).Distinct().ToArray();
        Assert.Equal(identities.Length, await verification.LanguagePreferences.CountAsync(p => issuers.Contains(p.Issuer)));
        Assert.Null((await new LanguagePreferenceService(verification).GetAsync(prefix, "USER")).Language);
    }

    [PostgresFact]
    public async Task CanonicalTransitions_AreDurable_AndReadsInSameContextSeeLatestWrite()
    {
        var subject = Guid.NewGuid().ToString();
        await using (var context = database.CreateContext())
        {
            var service = new LanguagePreferenceService(context);
            foreach (var language in new[] { "en", "nb-NO", "en", "en" })
            {
                Assert.Equal(language, (await service.UpdateAsync("transitions", subject, language)).Language);
                Assert.Equal(language, (await service.GetAsync("transitions", subject)).Language);
            }
        }
        // A new connection/context has no shared change tracker, cache, or open transaction.
        await using var restarted = database.CreateContext();
        Assert.Equal("en", (await new LanguagePreferenceService(restarted).GetAsync("transitions", subject)).Language);
        Assert.Equal(1, await restarted.LanguagePreferences.CountAsync(p => p.Issuer == "transitions" && p.Subject == subject));
    }

    [PostgresTheory]
    [InlineData("")]
    [InlineData("EN")]
    [InlineData("nb-no")]
    [InlineData("NB-NO")]
    [InlineData("en ")]
    [InlineData(" en")]
    [InlineData("en-US")]
    [InlineData("nb")]
    [InlineData("no")]
    [InlineData("nn-NO")]
    public async Task DatabaseCheckConstraint_RejectsNoncanonicalDirectWrites(string language)
    {
        var subject = Guid.NewGuid().ToString();
        await using var context = database.CreateContext();
        // Bypass the service to prove enforcement is in PostgreSQL, not just application validation.
        context.LanguagePreferences.Add(new LanguagePreference { Issuer = "constraint", Subject = subject, Language = language });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
        await using var verify = database.CreateContext();
        Assert.Null((await new LanguagePreferenceService(verify).GetAsync("constraint", subject)).Language);
    }

    [PostgresFact]
    public async Task DatabaseNotNullConstraint_RejectsNullLanguage()
    {
        await using var context = database.CreateContext();
        var subject = Guid.NewGuid().ToString();
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO profiles.language_preference (issuer, subject, language)
                VALUES ({"null-constraint"}, {subject}, NULL)
                """));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
    }

    [PostgresFact]
    public async Task InvalidUpdate_DoesNotChangeExistingPreference()
    {
        var subject = Guid.NewGuid().ToString();
        await using var context = database.CreateContext();
        var service = new LanguagePreferenceService(context);
        await service.UpdateAsync("invalid-update", subject, "nb-NO");
        foreach (var language in new string?[] { null, "", "EN", " en", "nb", "no", "nn-NO" })
            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync("invalid-update", subject, language));
        Assert.Equal("nb-NO", (await service.GetAsync("invalid-update", subject)).Language);
        Assert.Equal(1, await context.LanguagePreferences.CountAsync(p => p.Issuer == "invalid-update" && p.Subject == subject));
    }

    [PostgresFact]
    public async Task ConcurrentFirstSaves_UseSeparateConnections_ProduceOneRowAndNoUniqueErrors()
    {
        // Several independent identities/repetitions exercise the first-insert race, not just updates.
        for (var round = 0; round < 4; round++)
        {
            var subject = Guid.NewGuid().ToString();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0;
            var saves = Enumerable.Range(0, 16).Select(async index =>
            {
                await using var context = database.CreateContext();
                await context.Database.OpenConnectionAsync();
                if (Interlocked.Increment(ref ready) == 16) start.TrySetResult();
                await start.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var language = index % 2 == 0 ? "en" : "nb-NO";
                var saved = await new LanguagePreferenceService(context).UpdateAsync("concurrent", subject, language);
                Assert.Equal(language, saved.Language);
            }).ToArray();
            await Task.WhenAll(saves).WaitAsync(TimeSpan.FromSeconds(30));
            await using var verify = database.CreateContext();
            var row = Assert.Single(await verify.LanguagePreferences
                .Where(p => p.Issuer == "concurrent" && p.Subject == subject).ToListAsync());
            Assert.Contains(row.Language, new[] { "en", "nb-NO" });
        }
    }

    [PostgresFact]
    public async Task IdentityAndLanguageAreParameterized_AndRealisticIdentityLengthsPersist()
    {
        var issuer = "https://identity.example/realms/" + new string('a', 200) + "/'; --";
        var subject = Guid.NewGuid() + "'; DROP TABLE profiles.language_preference; --";
        await using var context = database.CreateContext();
        var service = new LanguagePreferenceService(context);
        await service.UpdateAsync(issuer, subject, "nb-NO");
        Assert.Equal("nb-NO", (await service.GetAsync(issuer, subject)).Language);
        Assert.True(await context.LanguagePreferences.AnyAsync(p => p.Issuer == issuer && p.Subject == subject));
    }

    [PostgresFact]
    public async Task PreCanceledReadAndWrite_RemainCancellation_AndDoNotWrite()
    {
        var subject = Guid.NewGuid().ToString();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var context = database.CreateContext();
        var service = new LanguagePreferenceService(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync("cancel", subject, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateAsync("cancel", subject, "en", cancellation.Token));
        await using var verify = database.CreateContext();
        Assert.Null((await new LanguagePreferenceService(verify).GetAsync("cancel", subject)).Language);
    }

    [PostgresFact]
    public async Task InFlightBlockedWrite_CanBeCanceled_WithoutChangingSavedValue()
    {
        var subject = Guid.NewGuid().ToString();
        await using var lockContext = database.CreateContext();
        await new LanguagePreferenceService(lockContext).UpdateAsync("blocked", subject, "en");
        await using var transaction = await lockContext.Database.BeginTransactionAsync();
        await lockContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE profiles.language_preference SET language = language
            WHERE issuer = {"blocked"} AND subject = {subject}
            """);
        await using var writer = database.CreateContext();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LanguagePreferenceService(writer).UpdateAsync("blocked", subject, "nb-NO", cancellation.Token));
        await transaction.RollbackAsync();
        await using var verify = database.CreateContext();
        Assert.Equal("en", (await new LanguagePreferenceService(verify).GetAsync("blocked", subject)).Language);
    }
}
