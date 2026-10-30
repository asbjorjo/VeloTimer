using Microsoft.AspNetCore.Authentication.JwtBearer;
using VeloTime.Module.Profiles;
using VeloTime.Module.Profiles.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddModuleAuthentication();
builder.Services.Configure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options => options.MapInboundClaims = false);
builder.Services.AddOpenApi();

builder.AddModuleProfiles();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
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
