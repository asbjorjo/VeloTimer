using Microsoft.Extensions.Hosting;
using VeloTime.Module.Notifications;
using VeloTime.Module.Notifications.Processor;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddModuleNotifications();
builder.AddNotificationsProcessor();

var app = builder.Build();
app.Run();
