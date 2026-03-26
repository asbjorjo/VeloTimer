using VeloTime.Module.Timing;
using VeloTime.Module.Timing.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddModuleAuthentication();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.AddModuleTiming();

var app = builder.Build();

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
