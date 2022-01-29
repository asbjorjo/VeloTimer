using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace VeloTimerWeb.Api.Data
{
    public static class Startup
    {
        public static IServiceCollection ConfigureData(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<VeloTimerDbContext>(options =>
            {
                options.UseNpgsql(
                    configuration
                        .GetConnectionString("PgSql"), sqloptions =>
                        {
                            sqloptions.CommandTimeout((int)TimeSpan.FromMinutes(5).TotalSeconds);
                        });
            });
            
            return services;
        }
    }
}
