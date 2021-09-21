using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VeloTimerWeb.Server.Data;
using VeloTimerWeb.Server.Hubs;
using VeloTimerWeb.Server.Models;
using VeloTimerWeb.Server.Services;
using VeloTimerWeb.Server.Services.Mylaps;
using VeloTimerWeb.Server.Util;

namespace VeloTimerWeb.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<PassingDatabaseSettings>(
                Configuration.GetSection(nameof(PassingDatabaseSettings)));
            services.AddSingleton<IPassingDatabaseSettings>(sp =>
                sp.GetRequiredService<IOptions<PassingDatabaseSettings>>().Value);
            services.AddSingleton<AmmcPassingService>();

            services.AddSingleton<IAuthorizationHandler, AllowAnonymous>();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    Configuration.GetConnectionString("DefaultConnection")));

            services.AddDatabaseDeveloperPageExceptionFilter();

            services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
                .AddEntityFrameworkStores<ApplicationDbContext>();

            services.AddIdentityServer()
                .AddApiAuthorization<ApplicationUser, ApplicationDbContext>();

            services.AddAuthentication()
                .AddIdentityServerJwt();

            services.AddControllersWithViews();
            services.AddRazorPages();
            services.AddServerSideBlazor();

            services.AddSignalR()
                    .AddAzureSignalR();

            services.AddHostedService<RefreshPassingsService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseMigrationsEndPoint();
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseIdentityServer();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBlazorHub().WithMetadata(new AllowAnonymousAttribute());
                endpoints.MapHub<PassingHub>(PassingHub.hubUrl).WithMetadata(new AllowAnonymousAttribute());
                endpoints.MapRazorPages().WithMetadata(new AllowAnonymousAttribute());
                endpoints.MapControllers().WithMetadata(new AllowAnonymousAttribute());
                endpoints.MapFallbackToFile("index.html").WithMetadata(new AllowAnonymousAttribute());
            });

            app.UseAzureSignalR(
                routes => routes.MapHub<PassingHub>(PassingHub.hubUrl));
        }
    }
}
