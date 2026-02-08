using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Statistics.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<MigrationWorker<StatisticsDbContext>>();

builder.Services.AddDbContext<StatisticsDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("StatisticsDbConnection"));
    options.UseSnakeCaseNamingConvention();
});

var host = builder.Build();
host.Run();
