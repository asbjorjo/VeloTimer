using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddScoped(_ =>
{
	var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
	return new HttpClient(new BffAntiforgeryHandler(baseAddress)
	{
		InnerHandler = new HttpClientHandler()
	}) { BaseAddress = baseAddress };
});
builder.Services.AddScoped<IFacilityService, FacilityHttpService>();
builder.Services.AddScoped<ITimingService, TimingHttpService>();
builder.Services.AddScoped<IStatisticsService, StatisticsHttpService>();
builder.Services.AddScoped<IProfileService, ProfileHttpService>();

await builder.Build().RunAsync();
