using System.Reflection;
using VeloTime.Module.Timing;
using VeloTime.Module.Timing.Api;
using VeloTime.Module.Timing.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddModuleAuthentication();
VeloTime.Module.Common.AdministrationReferences.AddServices(builder.Services);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.AddModuleTiming();
// Schema generation must not start outbox dispatch or connect to a message broker.
if (Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
    builder.AddOwnershipMessaging();
builder.Services.AddScoped<VeloTime.Module.Timing.Service.TransponderOwnershipService>();
builder.Services.AddScoped<VeloTime.Module.Timing.Service.TimingAdministrationService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapTimingEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "VeloTime API V1");
    });
}

app.UseHttpsRedirection();

app.UseModuleTiming();

app.Run();

public partial class Program;
