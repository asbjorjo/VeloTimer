using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using VeloTimer.Shared.Hub;

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
                    client => client.BaseAddress = new Uri(builder.Configuration["VELOTIMER_API_URL"]))
                .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>(); ;

            builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>()
                                               .CreateClient("VeloTimerWeb.ServerAPI"));

            builder.Services.AddSingleton<HubConnection>(sp => {
                var navigationManager = sp.GetRequiredService<NavigationManager>();
                return new HubConnectionBuilder().WithUrl(navigationManager.ToAbsoluteUri(Strings.hubUrl))
                                                 .WithAutomaticReconnect()
                                                 .Build();
            });

            builder.Services.AddOidcAuthentication(options =>
            {
                builder.Configuration.Bind("Local", options.ProviderOptions);
            });
                        
            await builder.Build().RunAsync();
        }
    }
}
