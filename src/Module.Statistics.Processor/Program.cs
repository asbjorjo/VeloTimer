var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddModuleIdentity("velotime.statistics.processor");

builder.AddServices();

var host = builder.Build();
host.Run();
