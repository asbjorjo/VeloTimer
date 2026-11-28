using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Agent;
using VeloTime.Agent.Dummy;

var builder = Host.CreateApplicationBuilder(args);

// Configure core agent services
builder.ConfigureAgent(agentType: "Dummy");


// Add services for specific system
builder.Services.AddHostedService<DummyWorker>();

// Applies the agent store migrations before starting.
await builder.RunAgent();