using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using VeloTime.Module.Profiles;
using VeloTime.Module.Profiles.Api.Endpoints;
using VeloTime.Module.Profiles.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddModuleAuthentication();
builder.Services.Configure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options => options.MapInboundClaims = false);
builder.Services.AddHealthChecks()
    .AddCheck<ProfilesDatabaseHealthCheck>("profiles-storage", tags: ["ready"]);
builder.Services.AddOpenApi();

builder.AddModuleProfiles();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
if (!app.Environment.IsDevelopment())
{
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/alive", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live")
    });
}
app.MapProfilePreferenceEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "VeloTime Profiles API V1");
    });
}

app.UseHttpsRedirection();

app.Run();

public partial class Program;
