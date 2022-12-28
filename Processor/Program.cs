using Azure.Identity;
using VeloTime.Processor.Services;
using VeloTime.Shared.Messaging;
using VeloTime.Storage.Data;
using VeloTimerWeb.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables("VELOTIME_");
    
if (!builder.Environment.IsDevelopment())
{
    var vault = Environment.GetEnvironmentVariable("VELOTIME_VAULT");
    if (vault != null)
    {
        var keyVaultEndpoint = new Uri(vault);
        builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, new DefaultAzureCredential());
    } else
    {
        throw new Exception("Vault not configured");
    }
};

builder.Services.ConfigureMessaging(builder.Configuration);
builder.Services.ConfigureStorage(builder.Configuration);
builder.Services.AddVeloTimeServices();
builder.Services.AddHostedService<PassingHandler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.Run();