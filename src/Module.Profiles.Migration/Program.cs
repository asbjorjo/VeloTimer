using VeloTime.Module.Common;
using VeloTime.Module.Profiles.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<MigrationWorker<ProfilesDbContext>>();
builder.AddModuleStorage<ProfilesDbContext>(connectionName: "velotimedb");

var host = builder.Build();
host.Run();
