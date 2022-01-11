using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Net.Http;
using VeloTimer.AmmcLoad.Data;
using VeloTimer.AmmcLoad.Models;
using VeloTimer.AmmcLoad.Services;
using VeloTimer.Shared.Configuration;

namespace VeloTimer.AmmcLoad
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddApplicationInsightsTelemetry();

            services.ConfigurePassingDatabase(Configuration);
            services.ConfigureMessaging(Configuration);

            services.AddAutoMapper(typeof(AmmcProfile));
            services.AddSingleton<AmmcPassingService>();
            services.AddSingleton<IMessagingService, MessagingService>();

            services.AddScoped<IApiService, ApiService>();

            services.AddTransient<VeloHttpClientHandler>();
            services.AddHttpClient(
                "VeloTimerWeb.ServerAPI",
                client => client.BaseAddress = new Uri(new Uri(Configuration["VELOTIMER_API_URL"]), "api/")).ConfigurePrimaryHttpMessageHandler<VeloHttpClientHandler>();
            services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("VeloTimerWeb.ServerAPI"));
            services.AddHostedService<RefreshPassingsService>();

            services.AddRazorPages();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
            });
        }
    }

    public class VeloHttpClientHandler : HttpClientHandler
    {
        public VeloHttpClientHandler()
        {
            MaxConnectionsPerServer = 20;
        }
    }
}
