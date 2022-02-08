using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using VeloTime.Shared.Messaging;
using VeloTime.Storage.Data;
using VeloTimer.Shared.Hub;
using VeloTimerWeb.Api.Configuration;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Hubs;
using VeloTimerWeb.Api.Services;

namespace VeloTimerWeb.Api
{
    public class Startup
    {
        readonly string AllowedOrigins = "_allowedOrigins";

        public Startup(IConfiguration configuration, IHostEnvironment env)
        {
            Configuration = configuration;
            Environment = env;
        }

        public IConfiguration Configuration { get; }
        public IHostEnvironment Environment { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddApplicationInsightsTelemetry();

            services.ConfigureMessaging(Configuration);
            services.ConfigureStorage(Configuration);

            services.AddIdentityServices(Configuration, Environment);
            services.AddVeloTimeServices();

            services.AddDatabaseDeveloperPageExceptionFilter();

            
            services.AddCors(options =>
            {
                options.AddPolicy(name: AllowedOrigins,
                                  builder =>
                                  {
                                      builder.WithOrigins("https://veloti.me", "https://www.veloti.me");
                                      builder.AllowAnyMethod();
                                      builder.AllowAnyHeader();
                                      builder.AllowCredentials();
                                  });
            });

            services.AddSignalR();
            services.AddRazorPages();
            services.AddControllers()
                .AddNewtonsoftJson(o =>
                {
                    o.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
                });

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "VeloTimerWeb.Api", Version = "v1" });
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseMigrationsEndPoint();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "VeloTimerWeb.Api v1"));
                app.UseWebAssemblyDebugging();
            }

            if (!env.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseCors(AllowedOrigins);

            app.UseIdentityServices();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapHub<PassingHub>(Strings.hubUrl);
                endpoints.MapControllers();
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}
