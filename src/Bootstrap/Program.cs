using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Facilities.Storage;
using VeloTime.Module.Statistics.Storage;
using VeloTime.Module.Timing.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<Worker>();

builder.Services.AddDbContext<FacilityDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("FacilityDbConnection"));
    options.UseSnakeCaseNamingConvention();
});
builder.Services.AddDbContext<StatisticsDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("StatisticsDbConnection"));
    options.UseSnakeCaseNamingConvention();
});

builder.Services.AddDbContext<TimingDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("TimingDbConnection"));
    options.UseSnakeCaseNamingConvention();
});


var host = builder.Build();
host.Run();
