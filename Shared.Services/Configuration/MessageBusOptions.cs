using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace VeloTimer.Shared.Services.Configuration
{
    public class MessageBusOptions
    {
        public static readonly string Section = "MessageBus";

        public string ConnectionString { get; set; }
        public string QueueName { get; set; } = "velo-passings";
        public int MaxConcurrency { get; set; } = 5;
    }

    public static class MessageBusOptionsExtensions
    {
        public static MessageBusOptions ConfigureMessaging(this IServiceCollection services, IConfiguration config)
        {
            var mbconfig = config.GetSection(MessageBusOptions.Section);
            var settings = mbconfig.Get<MessageBusOptions>() ?? new MessageBusOptions();
            settings.ConnectionString = config.GetConnectionString("MessageBus");

            services.TryAddSingleton(settings);

            return settings;
        }
    }
}
