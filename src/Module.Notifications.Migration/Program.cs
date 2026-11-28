using VeloTime.Module.Common;
using VeloTime.Module.Notifications.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<MigrationWorker<NotificationsDbContext>>();
builder.AddModuleStorage<NotificationsDbContext>(connectionName: "velotimedb");

var host = builder.Build();
host.Run();
