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
            ownershipWriter.LogPreflight();

            foreach (var rider in riders)
            {
                stoppingToken.ThrowIfCancellationRequested();
                var user = await userManager.FindByIdAsync(rider.UserId.ToString());
                if (user == null)
                {
                    profileWriter.MissingOwner(rider.Id);
                    ownershipWriter.MissingOwner(rider.Id);
                    continue;
                }

                var logins = await userManager.GetLoginsAsync(user);

                UserRepresentation kcuser;


                var kcusers = await keycloak.UsersAll3Async(username: user.UserName,
                                                        realm: "velotime",
                                                        cancellationToken: stoppingToken,
                                                        briefRepresentation: false,
                                                        createdAfter: "",
                                                        createdBefore: "",
                                                        email: "",
                                                        emailVerified: null,
                                                        enabled: null,
                                                        exact: null,
                                                        first: null,
                                                        firstName: "",
                                                        idpAlias: "",
                                                        idpUserId: "",
                                                        lastName: "",
                                                        max: null,
                                                        q: "",
                                                        search: "");
                if (kcusers.Any())
                {
                    kcuser = kcusers.First();
                }
                else
                {
                    kcuser = new UserRepresentation
                    {
                        Username = user.UserName,
                        Enabled = true,
                        EmailVerified = true,
                        Email = user.Email,
                        FirstName = rider.FirstName,
                        LastName = rider.LastName
                    };
                }

                if (kcuser.FederatedIdentities == null) kcuser.FederatedIdentities = new List<FederatedIdentityRepresentation>();

                foreach (var login in logins)
                {
                    var provider = login.LoginProvider;
                    var key = login.ProviderKey;

                    FederatedIdentityRepresentation federatedIdentity = new()
                    {
                        IdentityProvider = provider.ToLower(),
                        UserId = key,
                    };

                    kcuser.FederatedIdentities.Add(federatedIdentity);
                }
   
                if (kcuser.Id == null)
                {
                    await keycloak.UsersPOSTAsync(kcuser, "velotime", stoppingToken);
                } else
                {
                    await keycloak.UsersPUTAsync(kcuser, "velotime", kcuser.Id, stoppingToken);
                }

                kcusers = await keycloak.UsersAll3Async(username: user.UserName,
                                                        realm: "velotime",
                                                        cancellationToken: stoppingToken,
                                                        briefRepresentation: false,
                                                        createdAfter: "",
                                                        createdBefore: "",
                                                        email: "",
                                                        emailVerified: null,
                                                        enabled: null,
                                                        exact: null,
                                                        first: null,
                                                        firstName: "",
                                                        idpAlias: "",
                                                        idpUserId: "",
                                                        lastName: "",
                                                        max: null,
                                                        q: "",
                                                        search: "");
                kcuser = kcusers.First();
                var userId = Guid.Parse(kcuser.Id);

                await profileWriter.EnsureAsync(rider.Id, userId, rider.Name, rider.IsPublic, stoppingToken);
                await ownershipWriter.RegisterRiderAsync(rider.Id, userId, stoppingToken);
            }
            stoppingToken.ThrowIfCancellationRequested();
            profileWriter.LogSummary();
            ownershipWriter.Complete(profileWriter.Issues);
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
