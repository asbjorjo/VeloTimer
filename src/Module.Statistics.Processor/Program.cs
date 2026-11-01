var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddServices();

var host = builder.Build();
host.Run();
