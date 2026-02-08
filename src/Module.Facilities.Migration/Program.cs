using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Facilities.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<MigrationWorker<FacilityDbContext>>();

builder.Services.AddDbContext<FacilityDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("FacilityDbConnection"));
    options.UseSnakeCaseNamingConvention();
});

var host = builder.Build();
host.Run();
