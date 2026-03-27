using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

//builder.Services.AddHttpClient<IFacitiliesClient, HttpFacilitiesClient>(
//            client => client.BaseAddress = new($"{builder.HostEnvironment.BaseAddress}/api/facility/"));
//builder.Services.AddHttpClient<IStatisticsClient, HttpStatisticsClient>(
//            client => client.BaseAddress = new($"{builder.HostEnvironment.BaseAddress}/api/statistics/"));
//builder.Services.AddHttpClient<ITimingClient, TimingHttpClient>(
//            client => client.BaseAddress = new($"{builder.HostEnvironment.BaseAddress}/api/timing/"));

await builder.Build().RunAsync();
