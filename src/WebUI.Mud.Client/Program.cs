using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;
using VeloTime.WebUI.Mud.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();
builder.Services.AddMudLocalization();
builder.Services.AddLocalization();
builder.Services.AddTransient<MudLocalizer, LocalizedMudLocalizer>();
builder.Services.AddScoped<UnsavedChangesTracker>();

builder.Services.AddAuthorizationCore(AdministrationPolicies.Configure);
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
builder.Services.AddScoped<IStatisticsAdministrationService, StatisticsAdministrationHttpService>();
builder.Services.AddScoped<ITransponderManagementService, TransponderManagementHttpService>();
builder.Services.AddScoped<ITransponderOwnerService, TransponderOwnerHttpService>();
builder.Services.AddScoped<ITransponderStatusService, TransponderStatusHttpService>();
builder.Services.AddScoped<IInfrastructureAdministrationService, InfrastructureAdministrationHttpService>();
builder.Services.AddScoped<IProfileService, ProfileHttpService>();
builder.Services.AddScoped<IAuthDebugService, AuthDebugHttpService>();

await builder.Build().RunAsync();
