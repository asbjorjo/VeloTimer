using VeloTime.Module.Facilities;
using VeloTime.Module.Facilities.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddModuleAuthentication();

builder.Services.AddHttpForwarderWithServiceDiscovery();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.Expire(TimeSpan.FromSeconds(30));
    });
});

builder.AddModuleFacilities();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapFacilitiesEndpoints();

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

app.UseModuleFacilities();

app.UseOutputCache();

app.Run();
