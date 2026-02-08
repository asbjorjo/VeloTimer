var builder = DistributedApplication.CreateBuilder(args);

var k8s = builder.AddKubernetesEnvironment("k8s");

var identitydb = builder.AddConnectionString("IdentityDbConnection");
var timingdb = builder.AddConnectionString("TimingDbConnection");
var facilitydb = builder.AddConnectionString("FacilityDbConnection");
var statisticsdb = builder.AddConnectionString("StatisticsDbConnection");
var servicebusconnection = builder.AddConnectionString("MessageBus");

var cache = builder.AddRedis("cache")
    .WithRedisCommander()
    .WithRedisInsight();

//var identitymigrator = builder.AddProject<Projects.VeloTime_IdentityProvider_Migration>("identityprovider-migration")
//    .WithReference(identitydb);
//var identityprovider = builder.AddProject<Projects.VeloTime_IdentityProvider>("identity-provider")
//    .WithReference(identitydb)
//    .WaitForCompletion(identitymigrator);

var facilitiesmigrator = builder.AddProject<Projects.VeloTime_Module_Facilities_Migration>("module-facilities-migration")
    .WithReference(facilitydb);
var statisticsmigrator = builder.AddProject<Projects.VeloTime_Module_Statistics_Migration>("module-statistics-migration")
    .WithReference(statisticsdb);
var timingmigrator = builder.AddProject<Projects.VeloTime_Module_Timing_Migration>("module-timing-migration")
    .WithReference(timingdb);

var bootstrap = builder.AddProject<Projects.VeloTime_Bootstrap>("bootstrap")
    .WithReference(timingdb)
    .WithReference(facilitydb)
    .WithReference(statisticsdb)
    .WaitForCompletion(facilitiesmigrator)
    .WaitForCompletion(statisticsmigrator)
    .WaitForCompletion(timingmigrator);

var facilityapi = builder.AddProject<Projects.VeloTime_Module_Facilities_Api>("module-facilities-api")
    .WithReference(cache)
    .WithReference(facilitydb)
    .WaitForCompletion(facilitiesmigrator)
    .WaitForCompletion(bootstrap);
var statisticsapi = builder.AddProject<Projects.VeloTime_Module_Statistics_Api>("module-statistics-api")
    .WithReference(cache)
    .WithReference(statisticsdb)
    .WaitForCompletion(statisticsmigrator)
    .WaitForCompletion(bootstrap);
var timingapi = builder.AddProject<Projects.VeloTime_Module_Timing_Api>("module-timing-api")
    .WithReference(cache)
    .WithReference(timingdb)
    .WaitForCompletion(timingmigrator)
    .WaitForCompletion(bootstrap);

//var frontend = builder.AddProject<Projects.VeloTime_WebUI_Mud>("frontend")
//    .WithExternalHttpEndpoints()
//    .WithReference(cache)
//    .WithReference(identityprovider)
//    .WithReference(facilityapi)
//    .WithReference(statisticsapi)
//    .WithReference(timingapi);

//builder.AddProject<Projects.VeloTime_Module_Facilities_Processor>("module-facilities-processor")
//    .WithReference(facilitydb)
//    .WithReference(servicebusconnection)
//    .WaitForCompletion(facilitiesmigrator);
builder.AddProject<Projects.VeloTime_Module_Statistics_Processor>("module-statistics-processor")
    .WithReference(cache)
    .WithReference(statisticsdb)
    .WithReference(servicebusconnection)
    .WithReference(facilityapi)
    .WaitForCompletion(statisticsmigrator)
    .WaitForCompletion(bootstrap)
    .WithExplicitStart();
builder.AddProject<Projects.VeloTime_Module_Timing_Processor>("module-timing-processor")
    .WithReference(cache)
    .WithReference(timingdb)
    .WithReference(servicebusconnection)
    .WaitForCompletion(timingmigrator)
    .WaitForCompletion(bootstrap)
    .WithExplicitStart();

builder.Build().Run();
