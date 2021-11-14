using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using VeloTimer.AmmcLoad.Data;
using VeloTimer.AmmcLoad.Services;
using VeloTimer.Shared.Hub;

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

            services.Configure<PassingDatabaseSettings>(
                                         Configuration.GetSection(nameof(PassingDatabaseSettings)));
            services.AddSingleton<IPassingDatabaseSettings>(sp =>
                        sp.GetRequiredService<IOptions<PassingDatabaseSettings>>().Value);
            services.AddSingleton<AmmcPassingService>();
            services.AddTransient<VeloHttpClientHandler>();
            services.AddHttpClient(
                "VeloTimerWeb.ServerAPI", 
                client => client.BaseAddress = new Uri(Configuration["VELOTIMER_API_URL"])).ConfigurePrimaryHttpMessageHandler<VeloHttpClientHandler>();
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
