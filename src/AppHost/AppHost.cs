var builder = DistributedApplication.CreateBuilder(args);

var migrate = false;
var seed = false;

var k8s = builder.AddKubernetesEnvironment("k8s");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithDbGate()
    .WithOtlpExporter();

//var identitydb = builder.AddConnectionString("IdentityDbConnection");
//var timingdb = builder.AddConnectionString("TimingDbConnection");
//var facilitydb = builder.AddConnectionString("FacilityDbConnection");
//var statisticsdb = builder.AddConnectionString("StatisticsDbConnection");

var servicebusconnection = builder.AddConnectionString("MessageBus");

var keycloakdb = postgres.AddDatabase("keycloakdb");
//var identitydb = postgres.AddDatabase("identity");
var velotimedb = postgres.AddDatabase("velotimedb");

var cache = builder.AddRedis("cache")
    .WithRedisInsight();
//var cache = builder.AddValkey("cache");

var keycloak = builder.AddKeycloak("keycloak", 8888)
    .WaitFor(keycloakdb)
    .WithOtlpExporter()
    .WithPostgres(keycloakdb)
    .WithRealmImport("../../config/keycloak/realms");

//var identitymigrator = builder.AddProject<Projects.VeloTime_IdentityProvider_Migration>("identityprovider-migration")
//    .WithReference(identitydb);
//var identityprovider = builder.AddProject<Projects.VeloTime_IdentityProvider>("identity-provider")
//    .WithReference(identitydb)
//    .WaitForCompletion(identitymigrator);

var facilityapi = builder.AddProject<Projects.VeloTime_Module_Facilities_Api>("module-facilities-api")
    .WithReplicas(2)
    .WithReference(cache)
    .WithReference(velotimedb)
    .WaitFor(velotimedb);
var statisticsapi = builder.AddProject<Projects.VeloTime_Module_Statistics_Api>("module-statistics-api")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WaitFor(velotimedb);
var timingapi = builder.AddProject<Projects.VeloTime_Module_Timing_Api>("module-timing-api")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WaitFor(velotimedb);

var frontend = builder.AddProject<Projects.VeloTime_WebUI_Mud>("frontend")
    .WithExternalHttpEndpoints()
    .WithReference(cache)
    .WithReference(keycloak)
    .WithReference(facilityapi)
    .WithReference(statisticsapi)
    .WithReference(timingapi);

//builder.AddProject<Projects.VeloTime_Module_Facilities_Processor>("module-facilities-processor")
//    .WithReference(facilitydb)
//    .WithReference(servicebusconnection)
//    .WaitForCompletion(facilitiesmigrator);
var statsproc = builder.AddProject<Projects.VeloTime_Module_Statistics_Processor>("module-statistics-processor")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(servicebusconnection)
    .WithReference(facilityapi)
    .WaitFor(velotimedb)
    .WaitFor(facilityapi)
    .WithExplicitStart();
var timingproc = builder.AddProject<Projects.VeloTime_Module_Timing_Processor>("module-timing-processor")
    .WithReference(cache)
    .WithReference(velotimedb)
    .WithReference(servicebusconnection)
    .WaitFor(velotimedb)
    .WithExplicitStart();

IResourceBuilder<ProjectResource>? bootstrap = null;
IResourceBuilder<ProjectResource> facilitiesmigrator;
IResourceBuilder<ProjectResource> statisticsmigrator;
IResourceBuilder<ProjectResource> timingmigrator;

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
