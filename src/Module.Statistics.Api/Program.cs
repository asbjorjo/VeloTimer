using VeloTime.Module.Statistics;
using VeloTime.Module.Statistics.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddModuleAuthentication();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
});

builder.AddModuleStatistics();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapStatisticsEndpoints();

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

app.UseModuleStatistics();

app.Run();
