using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PassingLoader.Services.Storage;

namespace VeloTimer.PassingLoader.Services.Storage
{
    public static class Startup
    {
        public static IServiceCollection ConfigureStorage(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<StorageContext>(options =>
            {
                options.UseNpgsql(configuration.GetConnectionString("PgSql"));
            });

            return services;
        }
    }
}
