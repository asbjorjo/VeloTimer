using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using VeloTimer.Shared.Hub;
using VeloTimerWeb.Client.Services;

namespace VeloTimerWeb.Client
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Services.AddScoped<VeloTimerAuthorizationMessageHandler>();

            builder.Services.AddHttpClient(
                    "VeloTimerWeb.ServerAPI",
                    client => client.BaseAddress = new Uri(builder.Configuration["VELOTIMER_API_URL"]))
                .AddHttpMessageHandler<VeloTimerAuthorizationMessageHandler>();

            builder.Services.AddHttpClient<IApiClient, ApiClient>()
                .AddHttpMessageHandler<VeloTimerAuthorizationMessageHandler>();

            builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>()
                                               .CreateClient("VeloTimerWeb.ServerAPI"));

            builder.Services.AddSingleton<HubConnection>(sp =>
            {
                var navigationManager = sp.GetRequiredService<NavigationManager>();
                return new HubConnectionBuilder().WithUrl(new Uri(new Uri(builder.Configuration["VELOTIMER_API_URL"]), Strings.hubUrl))
                                                 .WithAutomaticReconnect()
                                                 .Build();
            });

            builder.Services.AddApiAuthorization(options =>
            {
                builder.Configuration.Bind("oidc", options.ProviderOptions);
                options.AuthenticationPaths.RemoteProfilePath = $"{builder.Configuration["VELOTIMER_API_URL"]}/Identity/Account/Manage";
                options.AuthenticationPaths.RemoteRegisterPath = $"{builder.Configuration["VELOTIMER_API_URL"]}/Identity/Account/Register";
            }).AddAccountClaimsPrincipalFactory<RolesClaimsPrincipalFactory>();

            await builder.Build().RunAsync();
        }
    }
}
