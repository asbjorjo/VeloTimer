using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace VeloTimerWeb.Client
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Services.AddHttpClient(
                "VeloTimerWeb.ServerAPI", 
                client => client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("VELOTIMER_API_URL")));

            // Supply HttpClient instances that include access tokens when making requests to the server project
            builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("VeloTimerWeb.ServerAPI"));

            await builder.Build().RunAsync();
        }
    }
}
