using Microsoft.EntityFrameworkCore;
using VeloTime.Module.Common;
using VeloTime.Module.Timing.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<MigrationWorker<TimingDbContext>>();

builder.Services.AddDbContext<TimingDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("TimingDbConnection"));
    options.UseSnakeCaseNamingConvention();
});

var host = builder.Build();
host.Run();
