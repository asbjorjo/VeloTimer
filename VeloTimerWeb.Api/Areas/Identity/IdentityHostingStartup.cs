using Microsoft.AspNetCore.Hosting;

[assembly: HostingStartup(typeof(VeloTimerWeb.Api.Areas.Identity.IdentityHostingStartup))]
namespace VeloTimerWeb.Api.Areas.Identity
{
    public class IdentityHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) => {
            });
        }
    }
}