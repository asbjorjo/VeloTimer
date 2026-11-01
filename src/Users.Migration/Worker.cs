using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using VeloTime.Module.Statistics.Client;
using VeloTime.Module.Timing.Client;
using VeloTime.Users.Migration.Data;
using VeloTime.Users.Migration.Keycloak;
using VeloTime.Users.Migration.Models.Identity;
using VeloTime.Users.Migration.Models.Riders;
using VeloTime.Users.Migration.Services;

namespace VeloTime.Users.Migration;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime,
    ILogger<Worker> logger) : BackgroundService
{
    public const string ActivitySourceName = "VeloTime.Users.Migration";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = ActivitySource.StartActivity("Applying Migrations", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var velotimerdb = scope.ServiceProvider.GetRequiredService<VeloTimerDbContext>();
            var identitydb = scope.ServiceProvider.GetRequiredService<VeloIdentityDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<VeloTimeUserManager<User>>();
            var keycloak = scope.ServiceProvider.GetRequiredService<IKeycloakClient>();
            var statistics = scope.ServiceProvider.GetRequiredService<IStatisticsClient>();
            var timing = scope.ServiceProvider.GetRequiredService<ITimingClient>();

            var riders = await velotimerdb.Set<Rider>()
                .Include(r => r.Transponders)
                .ThenInclude(t => t.Transponder)
                .ToListAsync(stoppingToken);

            var preflight = OwnershipPreflight.Create(riders.SelectMany(rider => rider.Transponders.Select(ownership =>
                new SourceOwnership(rider.Id, ownership.Id, ownership.Transponder.TimingSystem,
                    ownership.Transponder.SystemId, ownership.OwnedFrom, ownership.OwnedUntil))));
            var ownershipWriter = new OwnershipMigrationWriter(timing, logger, preflight);
            var profileWriter = new StatsProfileMigrationWriter(statistics, logger);
            var keycloakWriter = new KeycloakUserMigrationWriter(keycloak, logger);
            ownershipWriter.LogPreflight();

            foreach (var rider in riders)
            {
                stoppingToken.ThrowIfCancellationRequested();
                User? user;
                try
                {
                    user = await userManager.FindByIdAsync(rider.UserId.ToString());
                }
                catch (Exception exception) when (!MigrationFailure.IsFatalOrStopping(exception, stoppingToken))
                {
                    logger.LogError(exception, "Could not look up identity user {UserId} for rider {RiderId}; skipping rider.", rider.UserId, rider.Id);
                    const string reason = "the identity account lookup failed";
                    profileWriter.MissingOwner(rider.Id, reason);
                    ownershipWriter.MissingOwner(rider.Id, reason);
                    continue;
                }

                if (user == null)
                {
                    profileWriter.MissingOwner(rider.Id);
                    ownershipWriter.MissingOwner(rider.Id);
                    continue;
                }

                IList<Microsoft.AspNetCore.Identity.UserLoginInfo> logins;
                try
                {
                    logins = await userManager.GetLoginsAsync(user);
                }
                catch (Exception exception) when (!MigrationFailure.IsFatalOrStopping(exception, stoppingToken))
                {
                    logger.LogError(exception, "Could not look up external logins for identity user {UserId}, rider {RiderId}; skipping rider.", user.Id, rider.Id);
                    const string reason = "the identity account logins could not be read";
                    profileWriter.MissingOwner(rider.Id, reason);
                    ownershipWriter.MissingOwner(rider.Id, reason);
                    continue;
                }

                var userId = await keycloakWriter.EnsureUserAsync(rider.Id, user, logins,
                    rider.FirstName, rider.LastName, stoppingToken);
                if (userId is null)
                {
                    const string reason = "the Keycloak account could not be created or verified";
                    profileWriter.MissingOwner(rider.Id, reason);
                    ownershipWriter.MissingOwner(rider.Id, reason);
                    continue;
                }

                await profileWriter.EnsureAsync(rider.Id, userId.Value, rider.Name, rider.IsPublic, stoppingToken);
                await ownershipWriter.RegisterRiderAsync(rider.Id, userId.Value, stoppingToken);
            }
            stoppingToken.ThrowIfCancellationRequested();
            keycloakWriter.LogSummary();
            profileWriter.LogSummary();
            ownershipWriter.Complete(profileWriter.Issues.Concat(keycloakWriter.Issues));
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }
}
