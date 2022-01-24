using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AmmcLoad.Configuration
{
    public class PassingDatabaseSettings : IPassingDatabaseSettings
    {
        public string PassingCollection { get; set; }
        public string PassingDatabase { get; set; }
        public string ConnectionString { get; set; }
    }

    public interface IPassingDatabaseSettings
    {
        string PassingCollection { get; set; }
        string PassingDatabase { get; set; }
        string ConnectionString { get; set; }
    }

    public static class PassingDatabaseSettingsExtensions
    {
        public static IPassingDatabaseSettings ConfigurePassingDatabase(this IServiceCollection services, IConfiguration config)
        {
            var passingconfig = config.GetSection(nameof(PassingDatabaseSettings));
            var settings = passingconfig.Get<PassingDatabaseSettings>() ?? new PassingDatabaseSettings();
            settings.ConnectionString = config.GetConnectionString("PassingDatabase");

            services.TryAddSingleton(settings);

            return settings;
        }
    }
}
