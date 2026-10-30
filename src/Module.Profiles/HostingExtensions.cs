using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTime.Module.Profiles.Service;
using VeloTime.Module.Profiles.Storage;

namespace VeloTime.Module.Profiles;

public static class StartupExtensions
{
    public static IHostApplicationBuilder AddModuleProfiles(this IHostApplicationBuilder builder)
    {
        builder.AddModuleStorage<ProfilesDbContext>(connectionName: "velotimedb");
        builder.Services.AddScoped<ILanguagePreferenceService, LanguagePreferenceService>();
        return builder;
    }
}
