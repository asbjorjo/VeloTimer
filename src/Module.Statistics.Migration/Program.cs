using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<MigrationWorker<StatisticsDbContext>>();

builder.AddModuleStorage<StatisticsDbContext>(connectionName: "velotimedb");

var host = builder.Build();
host.Run();
