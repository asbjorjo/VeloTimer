using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Agent;
using VeloTime.Agent.Dummy;

var builder = Host.CreateApplicationBuilder(args);

// Configure core agent services
builder.ConfigureAgent();


// Add services for specific system
builder.Services.AddHostedService<DummyWorker>();

var app = builder.Build();

await app.RunAsync();