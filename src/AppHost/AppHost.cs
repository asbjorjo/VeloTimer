using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var migrate = false;
var seed = false;
IResourceBuilder<ProjectResource>? bootstrap = null;
IResourceBuilder<ProjectResource> facilitiesmigrator;
IResourceBuilder<ProjectResource> statisticsmigrator;
IResourceBuilder<ProjectResource> timingmigrator;
IResourceBuilder<IResourceWithConnectionString> velotimedb;

var k8s = builder.AddKubernetesEnvironment("k8s");

var servicebusconnection = builder.AddConnectionString("MessageBus");
var statisticsprocsecret = builder.AddParameter("StatisticsProcessorSecret", secret: true);
var timingprocsecret = builder.AddParameter("TimingProcessorSecret", secret: true);
var usermigrationsecret = builder.AddParameter("UserMigrationSecret", secret: true);

var velotimerdb = builder.AddConnectionString("VeloTimerDb");
var velotimeridentitydb = builder.AddConnectionString("VeloTimerIdentityDb");
//var keycloakconnection = builder.AddConnectionString("keycloak", "https+http://keycloak/realms/velotime");

var keycloak = builder.AddKeycloak("keycloak", 8888)
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
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(keycloak)
    .WaitFor(velotimedb);
var statisticsapi = builder.AddProject<Projects.VeloTime_Module_Statistics_Api>("module-statistics-api")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(keycloak)
    .WaitFor(velotimedb);
var timingapi = builder.AddProject<Projects.VeloTime_Module_Timing_Api>("module-timing-api")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(keycloak)
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
    .WithReference(keycloak)
    .WithReference(facilityapi)
    .WithReference(statisticsapi)
    .WithReference(timingapi);

var statsproc = builder.AddProject<Projects.VeloTime_Module_Statistics_Processor>("module-statistics-processor")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(servicebusconnection)
    .WithReference(facilityapi)
    .WithReference(keycloak)
    .WithEnvironment("CLIENT_SECRET", statisticsprocsecret)
    .WaitFor(velotimedb)
    .WaitFor(facilityapi)
    .WithExplicitStart();
var timingproc = builder.AddProject<Projects.VeloTime_Module_Timing_Processor>("module-timing-processor")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(servicebusconnection)
    .WithReference(keycloak)
    .WithEnvironment("CLIENT_SECRET", timingprocsecret)
    .WaitFor(velotimedb)
    .WithExplicitStart();

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

    facilityapi.WaitForCompletion(facilitiesmigrator);
    statisticsapi.WaitForCompletion(statisticsmigrator);
    timingapi.WaitForCompletion(timingmigrator);
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
