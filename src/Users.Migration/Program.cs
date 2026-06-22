using Duende.AccessTokenManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using VeloTime.Users.Migration.Data;
using VeloTime.Users.Migration.Models.Identity;
using VeloTime.Users.Migration.Services;
using VeloTime.Users.Migration.Keycloak;
using VeloTime.Users.Migration;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

//builder.AddModuleStorage<TimingDbContext>(connectionName: "velotimedb");

builder.AddModuleIdentity("velotime.users.migration");

builder.Services
    .AddHttpClient<IKeycloakClient, KeycloakClient>((provider, client) =>
    {
        client.BaseAddress = new Uri("https+http://keycloak/");
    })
    .AddDefaultAccessTokenResiliency()
    .AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse("velotime.users.migration"));

builder.Services.AddDbContext<VeloTimerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("velotimerdb")));

builder.Services.AddDbContext<VeloIdentityDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("velotimeridentitydb"));
});

builder.Services.AddIdentity<User, Role>(options =>
{
})
    .AddUserManager<VeloTimeUserManager<User>>()
    .AddRoles<Role>()
    .AddEntityFrameworkStores<VeloIdentityDbContext>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
