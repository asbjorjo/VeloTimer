using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddModuleIdentity("velotime.timing.processor");

builder.AddServices();

var app = builder.Build();

app.Run();
