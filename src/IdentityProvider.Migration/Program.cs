var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.ConfigureServices();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
