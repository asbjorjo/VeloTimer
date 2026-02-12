using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<Worker>();

builder.AddModuleStorage<FacilityDbContext>(connectionName: "velotimedb");
builder.AddModuleStorage<StatisticsDbContext>(connectionName: "velotimedb");
builder.AddModuleStorage<TimingDbContext>(connectionName: "velotimedb");

var host = builder.Build();
host.Run();
