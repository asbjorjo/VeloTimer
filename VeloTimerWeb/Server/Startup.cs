using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VeloTimer.Shared.Hub;
using VeloTimerWeb.Server.Data;
using VeloTimerWeb.Server.Hubs;
using VeloTimerWeb.Server.Services;
using VeloTimerWeb.Server.Services.Mylaps;

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

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    Configuration.GetConnectionString("Azure")));

            services.AddDatabaseDeveloperPageExceptionFilter();

            services.AddControllersWithViews();
            services.AddRazorPages();
            services.AddServerSideBlazor();

            services.AddSignalR();

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

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBlazorHub();
                endpoints.MapHub<PassingHub>(Strings.hubUrl);
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}
