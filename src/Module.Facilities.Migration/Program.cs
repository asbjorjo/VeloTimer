using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<MigrationWorker<FacilityDbContext>>();

builder.AddModuleStorage<FacilityDbContext>(connectionName: "velotimedb");

var host = builder.Build();
host.Run();
