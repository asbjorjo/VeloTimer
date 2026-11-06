using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

if (builder.ExecutionContext.IsRunMode)
{
    var audience = builder.Configuration["OIDC_AUDIENCE"];
    if (audience is null && builder.Environment.IsDevelopment())
    {
        audience = "https://development.veloti.me";
        builder.Configuration["OIDC_AUDIENCE"] = audience;
    }

    if (string.IsNullOrWhiteSpace(audience)
        || audience != audience.Trim()
        || !Uri.TryCreate(audience, UriKind.Absolute, out var audienceUri)
        || (audienceUri.Scheme != Uri.UriSchemeHttps && audienceUri.Scheme != Uri.UriSchemeHttp)
        || !string.IsNullOrEmpty(audienceUri.UserInfo)
        || !string.IsNullOrEmpty(audienceUri.Query)
        || !string.IsNullOrEmpty(audienceUri.Fragment))
    {
        throw new InvalidOperationException("OIDC_AUDIENCE must be configured as an absolute HTTP(S) application base URL without credentials, query, or fragment.");
    }
}

var oidcAudience = builder.AddParameterFromConfiguration("oidc-audience", "OIDC_AUDIENCE")
    .WithDescription("Public application base URL used as the access-token audience.");

var migrate = true;
var seed = true;
IResourceBuilder<ProjectResource>? bootstrap = null;
IResourceBuilder<ProjectResource> facilitiesmigrator;
IResourceBuilder<ProjectResource> statisticsmigrator;
IResourceBuilder<ProjectResource> timingmigrator;
IResourceBuilder<ProjectResource> profilesmigrator;
IResourceBuilder<ProjectResource> notificationsmigrator;
IResourceBuilder<IResourceWithConnectionString> velotimedb;

var k8s = builder.AddKubernetesEnvironment("k8s");

var servicebusconnection = builder.AddConnectionString("MessageBus");
var statisticsprocsecret = builder.AddParameter("StatisticsProcessorSecret", secret: true);
var timingprocsecret = builder.AddParameter("TimingProcessorSecret", secret: true);
var usermigrationsecret = builder.AddParameter("UserMigrationSecret", secret: true);

string? notificationAssertionIssuer = null;
string? notificationAssertionAudience = null;
string? notificationAssertionKeyId = null;
string? notificationAssertionPrivateKey = null;
string? notificationAssertionPublicKey = null;
IResourceBuilder<ParameterResource>? notificationAssertionPrivateKeyParameter = null;
if (builder.ExecutionContext.IsRunMode)
{
    var localDevelopment = builder.Environment.IsDevelopment();
    notificationAssertionIssuer = builder.Configuration["Notifications:SessionAssertion:Issuer"]
        ?? (localDevelopment ? "VeloTime.WebUI.Mud" : null);
    notificationAssertionAudience = builder.Configuration["Notifications:SessionAssertion:Audience"]
        ?? (localDevelopment ? "VeloTime.Module.Notifications.Api" : null);
    notificationAssertionKeyId = builder.Configuration["Notifications:SessionAssertion:KeyId"]
        ?? (localDevelopment ? "aspire-local" : null);
    notificationAssertionPrivateKey = builder.Configuration["Notifications:SessionAssertion:PrivateKeyPem"];
    if (notificationAssertionKeyId is not null)
        notificationAssertionPublicKey = builder.Configuration[$"Notifications:SessionAssertion:PublicKeys:{notificationAssertionKeyId}"];
    if (localDevelopment && notificationAssertionPrivateKey is null && notificationAssertionPublicKey is null)
    {
        using var localNotificationSigner = System.Security.Cryptography.RSA.Create(3072);
        notificationAssertionPrivateKey = localNotificationSigner.ExportPkcs8PrivateKeyPem();
        notificationAssertionPublicKey = localNotificationSigner.ExportSubjectPublicKeyInfoPem();
    }

    if (string.IsNullOrWhiteSpace(notificationAssertionIssuer)
        || string.IsNullOrWhiteSpace(notificationAssertionAudience)
        || string.IsNullOrWhiteSpace(notificationAssertionKeyId)
        || string.IsNullOrWhiteSpace(notificationAssertionPrivateKey)
        || string.IsNullOrWhiteSpace(notificationAssertionPublicKey))
    {
        throw new InvalidOperationException(
            "Notifications SessionAssertion requires Issuer, Audience, KeyId, PrivateKeyPem, and PublicKeys:<KeyId>; local Development may generate an ephemeral signing key.");
    }
    if (notificationAssertionKeyId.Length is < 1 or > 64
        || !notificationAssertionKeyId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        throw new InvalidOperationException("Notifications SessionAssertion KeyId must contain 1-64 ASCII letters, digits, hyphens, or underscores.");

    try
    {
        using var privateKey = System.Security.Cryptography.RSA.Create();
        using var publicKey = System.Security.Cryptography.RSA.Create();
        privateKey.ImportFromPem(notificationAssertionPrivateKey);
        publicKey.ImportFromPem(notificationAssertionPublicKey);
        if (privateKey.KeySize < 2048 || publicKey.KeySize < 2048
            || !privateKey.ExportSubjectPublicKeyInfo().SequenceEqual(publicKey.ExportSubjectPublicKeyInfo()))
            throw new InvalidOperationException("Notifications SessionAssertion private and public keys must be a matching RSA pair of at least 2048 bits.");
    }
    catch (System.Security.Cryptography.CryptographicException)
    {
        throw new InvalidOperationException("Notifications SessionAssertion must use a valid matching RSA private and public key.");
    }

    builder.Configuration["Notifications:SessionAssertion:PrivateKeyPem"] = notificationAssertionPrivateKey;
    notificationAssertionPrivateKeyParameter = builder.AddParameterFromConfiguration(
        "notifications-session-assertion-private-key",
        "Notifications:SessionAssertion:PrivateKeyPem",
        secret: true);
}

var velotimerdb = builder.AddConnectionString("VeloTimerDb");
var velotimeridentitydb = builder.AddConnectionString("VeloTimerIdentityDb");
//var keycloakconnection = builder.AddConnectionString("keycloak", "https+http://keycloak/realms/velotime");

var keycloakadminuser = builder.AddParameter("KeycloakAdminUser");
var keycloakadminpassword = builder.AddParameter("KeycloakAdminPassword", secret: true);

var keycloak = builder.AddKeycloak("keycloak", 8888, keycloakadminuser, keycloakadminpassword)
    .WithEnvironment("OIDC_AUDIENCE", oidcAudience)
    .WithEnvironment("STATISTICS_PROCESSOR_SECRET", statisticsprocsecret)
    .WithEnvironment("TIMING_PROCESSOR_SECRET", timingprocsecret)
    .WithEnvironment("USER_MIGRATION_SECRET", usermigrationsecret)
    .WithOtlpExporter();

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume(name: "velotime_postgres_data")
    .WithOtlpExporter();

var keycloakdb = postgres.AddDatabase("keycloakdb");
velotimedb = postgres.AddDatabase("velotimedb");

var cache = builder
    .AddRedis("cache")
    .WithDataVolume(name: "velotime_redis_data")
    .WithPersistence();
keycloak
    .WithEnvironment("QUARKUS_HTTP_HTTP2", "false")
    .WithPostgres(keycloakdb);

if (builder.Environment.IsDevelopment())
{
    keycloak.WithRealmImport("../../config/keycloak/realms");
    cache.WithRedisInsight();
    postgres.WithDbGate();
}

var facilityapi = builder.AddProject<Projects.VeloTime_Module_Facilities_Api>("module-facilities-api")
    .WithEnvironment("OIDC_AUDIENCE", oidcAudience)
    .WithReference(servicebusconnection)
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(keycloak)
    .WaitFor(velotimedb);
var statisticsapi = builder.AddProject<Projects.VeloTime_Module_Statistics_Api>("module-statistics-api")
    .WithEnvironment("OIDC_AUDIENCE", oidcAudience)
    .WithReference(servicebusconnection)
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(keycloak)
    .WaitFor(velotimedb);
var timingapi = builder.AddProject<Projects.VeloTime_Module_Timing_Api>("module-timing-api")
    .WithEnvironment("OIDC_AUDIENCE", oidcAudience)
    .WithReference(servicebusconnection)
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(keycloak)
    .WaitFor(velotimedb);
var profilesapi = builder.AddProject<Projects.VeloTime_Module_Profiles_Api>("module-profiles-api")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("OIDC_AUDIENCE", oidcAudience)
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(keycloak)
    .WaitFor(velotimedb);
var notificationsapi = builder.AddProject<Projects.VeloTime_Module_Notifications_Api>("module-notifications-api")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("OIDC_AUDIENCE", oidcAudience)
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(keycloak)
    .WaitFor(velotimedb);
if (notificationAssertionIssuer is not null)
{
    notificationsapi
        .WithEnvironment("Notifications__SessionAssertion__Issuer", notificationAssertionIssuer)
        .WithEnvironment("Notifications__SessionAssertion__Audience", notificationAssertionAudience!)
        .WithEnvironment("Notifications__SessionAssertion__PublicKeys__" + notificationAssertionKeyId, notificationAssertionPublicKey!)
        .WithEnvironment("Notifications__SessionAssertion__MaximumLifetimeSeconds", "120")
        .WithEnvironment("Notifications__SessionAssertion__ClockSkewSeconds", "5");
}

var notificationsproc = builder.AddProject<Projects.VeloTime_Module_Notifications_Processor>("module-notifications-processor")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(servicebusconnection)
    .WaitFor(cache)
    .WaitFor(velotimedb);

statisticsapi.WithReference(facilityapi);
facilityapi.WithReference(timingapi).WithReference(statisticsapi);
timingapi.WithReference(facilityapi).WithReference(statisticsapi);

var users = builder.AddProject<Projects.VeloTime_Users_Migration>("users-migration")
    .WithReference(velotimerdb)
    .WithReference(velotimeridentitydb)
    .WithReference(velotimedb)
    .WithReference(keycloak)
    .WithReference(statisticsapi)
    .WithReference(timingapi)
    .WithEnvironment("CLIENT_SECRET", usermigrationsecret)
    .WaitFor(velotimedb)
    .WaitFor(keycloak)
    .WaitFor(statisticsapi)
    .WaitFor(timingapi);

var frontend = builder.AddProject<Projects.VeloTime_WebUI_Mud>("frontend")
    .WithExternalHttpEndpoints()
    .WithReference(cache)
    .WithReference(servicebusconnection)
    .WithReference(keycloak)
    .WithReference(facilityapi)
    .WithReference(statisticsapi)
    .WithReference(profilesapi)
    .WithReference(notificationsapi)
    .WithReference(timingapi);
if (notificationAssertionIssuer is not null)
{
    frontend
        .WithEnvironment("Notifications__SessionAssertion__Issuer", notificationAssertionIssuer)
        .WithEnvironment("Notifications__SessionAssertion__Audience", notificationAssertionAudience!)
        .WithEnvironment("Notifications__SessionAssertion__KeyId", notificationAssertionKeyId!)
        .WithEnvironment("Notifications__SessionAssertion__PrivateKeyPem", notificationAssertionPrivateKeyParameter!)
        .WithEnvironment("Notifications__SessionAssertion__LifetimeSeconds", "120");
}

var statsproc = builder.AddProject<Projects.VeloTime_Module_Statistics_Processor>("module-statistics-processor")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(servicebusconnection)
    .WithReference(facilityapi)
    .WithReference(timingapi)
    .WithReference(keycloak)
    .WithEnvironment("CLIENT_SECRET", statisticsprocsecret)
    .WaitFor(velotimedb)
    .WaitFor(facilityapi)
    .WaitFor(timingapi)
    .WithExplicitStart();
var timingproc = builder.AddProject<Projects.VeloTime_Module_Timing_Processor>("module-timing-processor")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(servicebusconnection)
    .WithReference(keycloak)
    .WithEnvironment("CLIENT_SECRET", timingprocsecret)
    .WaitFor(velotimedb)
    .WithExplicitStart();

// The topology job needs a Manage-rights connection string distinct from the runtime MessageBus
// connection. Without it, local development relies on topology that already exists.
if (!string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:MessageBusManagement"]))
{
    var servicebusmanagement = builder.AddConnectionString("MessageBusManagement");
    var messagebustopology = builder.AddProject<Projects.VeloTime_MessageBus_Topology>("messagebus-topology")
        .WithReference(servicebusmanagement);
    frontend.WaitForCompletion(messagebustopology);
    facilityapi.WaitForCompletion(messagebustopology);
    statisticsapi.WaitForCompletion(messagebustopology);
    timingapi.WaitForCompletion(messagebustopology);
    notificationsproc.WaitForCompletion(messagebustopology);
    statsproc.WaitForCompletion(messagebustopology);
    timingproc.WaitForCompletion(messagebustopology);
}

if (seed)
{
    bootstrap = builder.AddProject<Projects.VeloTime_Bootstrap>("bootstrap")
        .WithReference(velotimedb);
    facilityapi.WaitForCompletion(bootstrap);
    statisticsapi.WaitForCompletion(bootstrap);
    timingapi.WaitForCompletion(bootstrap);
    statsproc.WaitForCompletion(bootstrap);
    timingproc.WaitForCompletion(bootstrap);
}

if (migrate)
{
    facilitiesmigrator = builder.AddProject<Projects.VeloTime_Module_Facilities_Migration>("module-facilities-migration")
        .WaitFor(velotimedb)
        .WithReference(velotimedb);
    statisticsmigrator = builder.AddProject<Projects.VeloTime_Module_Statistics_Migration>("module-statistics-migration")
        .WaitFor(velotimedb)
        .WithReference(velotimedb);
    timingmigrator = builder.AddProject<Projects.VeloTime_Module_Timing_Migration>("module-timing-migration")
        .WaitFor(velotimedb)
        .WithReference(velotimedb);
    profilesmigrator = builder.AddProject<Projects.VeloTime_Module_Profiles_Migration>("module-profiles-migration")
        .WaitFor(velotimedb)
        .WithReference(velotimedb);
    notificationsmigrator = builder.AddProject<Projects.VeloTime_Module_Notifications_Migration>("module-notifications-migration")
        .WaitFor(velotimedb)
        .WithReference(velotimedb);

    facilityapi.WaitForCompletion(facilitiesmigrator);
    statisticsapi.WaitForCompletion(statisticsmigrator);
    timingapi.WaitForCompletion(timingmigrator);
    profilesapi.WaitForCompletion(profilesmigrator);
    notificationsapi.WaitForCompletion(notificationsmigrator);
    notificationsproc.WaitForCompletion(notificationsmigrator);
    statsproc.WaitForCompletion(statisticsmigrator);
    timingproc.WaitForCompletion(timingmigrator);

    if (bootstrap != null)
    {
        bootstrap.WaitForCompletion(facilitiesmigrator);
        bootstrap.WaitForCompletion(statisticsmigrator);
        bootstrap.WaitForCompletion(timingmigrator);
    }
}

builder.Build().Run();
