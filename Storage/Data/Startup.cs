using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VeloTime.Storage.Models;

namespace VeloTime.Storage.Data
{
    public static class Startup
    {
        public static IServiceCollection ConfigureStorage(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<VeloTimerDbContext>(options =>
            {
                options.UseNpgsql(
                    configuration
                        .GetConnectionString("PgSql"), sqloptions =>
                        {
                            sqloptions.CommandTimeout((int)TimeSpan.FromMinutes(5).TotalSeconds);
                            sqloptions.MigrationsAssembly("VeloTime.Storage");
                        });
            });

            services.AddAutoMapper(typeof(VeloTimeProfile));

            return services;
        }
    }
}
