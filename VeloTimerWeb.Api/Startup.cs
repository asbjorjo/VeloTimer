using Azure.Identity;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.ApiAuthorization.IdentityServer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography.X509Certificates;
using VeloTimer.PassingLoader.Services.Messaging;
using VeloTimer.Shared.Hub;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Hubs;
using VeloTimerWeb.Api.Models;
using VeloTimerWeb.Api.Models.Identity;
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
            var azureId = new DefaultAzureCredential();

            services.AddApplicationInsightsTelemetry();

            services.ConfigureMessaging(Configuration);

            services.AddDbContext<VeloTimerDbContext>(options =>
            {
                options.UseNpgsql(
                    Configuration
                        .GetConnectionString("PgSql"), sqloptions =>
                        {
                            sqloptions.CommandTimeout((int)TimeSpan.FromMinutes(5).TotalSeconds);
                        });
            });
            services.AddDbContext<VeloIdentityDbContext>(options =>
            {
                options.UseSqlServer(Configuration.GetConnectionString("Azure"));
            });

            var dpBuilder = services.AddDataProtection();
            dpBuilder.PersistKeysToDbContext<VeloIdentityDbContext>();

            if (Environment.IsDevelopment())
            {
                dpBuilder.SetApplicationName("veloti.me-development");
                dpBuilder.SetDefaultKeyLifetime(TimeSpan.FromDays(7));
            } else
            {
                dpBuilder.SetApplicationName("veloti.me");
                dpBuilder.ProtectKeysWithAzureKeyVault(new Uri(new Uri(Configuration["VAULT"]), "keys/dataprotection"), azureId);
            }

            services.AddDefaultIdentity<User>(options =>
            {
            })
                .AddUserManager<VeloTimeUserManager<User>>()
                .AddRoles<Role>()
                .AddEntityFrameworkStores<VeloIdentityDbContext>();

            if (Environment.IsProduction())
            {
                services.Configure<JwtBearerOptions>(
                    IdentityServerJwtConstants.IdentityServerJwtBearerScheme,
                    options =>
                    {
                        options.Authority = Configuration["VELOTIMER_API_URL"];
                        options.TokenValidationParameters.ValidIssuers = new[]
                        {
                            "https://veloti.me",
                            "https://www.veloti.me",
                            "https://velotime.azurewebsites.net",
                            "https://velotime-github-ci.azurewebsites.net",
                            "https://velotime-noe.azurewebsites.net",
                            "https://velotime-noe-github-ci.azurewebsites.net"
                        };
                    });
            }
            else if (Environment.IsStaging())
            {
                services.Configure<JwtBearerOptions>(
                    IdentityServerJwtConstants.IdentityServerJwtBearerScheme,
                    options =>
                    {
                        options.Authority = Configuration["VELOTIMER_API_URL"];
                        options.TokenValidationParameters.ValidIssuers = new[]
                        {
                            "https://velotime-dev.azurewebsites.net"
                        };
                    });
            }


            var identitybuilder = services.AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;

                // see https://identityserver4.readthedocs.io/en/latest/topics/resources.html
                options.EmitStaticAudienceClaim = true;

                options.LicenseKey = Configuration["LicenseKeys:IdentityServer"];

                //if (Environment.IsProduction())
                //    options.IssuerUri = "https://veloti.me";
            })
                .AddModifiedApiAuthorization<User, VeloIdentityDbContext>(options =>
                {
                    options.Clients.Add(new Duende.IdentityServer.Models.Client
                    {
                        ClientId = "WebApi.Loader",
                        AllowedGrantTypes = { GrantType.ClientCredentials },
                        ClientSecrets = { new Duende.IdentityServer.Models.Secret("secret".Sha256()) },
                        AllowedScopes = { "VeloTimer.Api", "VeloTimer.ApiAPI" }
                    });
                });

            if (Environment.IsDevelopment())
            {
                Console.WriteLine("Using development key.");
                identitybuilder.AddDeveloperSigningCredential();
            }
            else
            {
                string key;

                if (Environment.IsStaging())
                {
                    key = Configuration["tokensiging"];
                }
                else
                {
                    key = Configuration["tokensigning"];
                }

                var pfxBytes = Convert.FromBase64String(key);

                //// Create the certificate.
                var cert = new X509Certificate2(pfxBytes);

                identitybuilder
                    .AddSigningCredential(cert);
            }

            services.AddTransient<IProfileService, ProfileService>();
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Remove("role");

            services.AddAuthentication()
                .AddIdentityServerJwt()

                .AddStrava(options =>
                {
                    options.ClientId = Configuration["Authentication:Strava:ClientId"];
                    options.ClientSecret = Configuration["Authentication:Strava:ClientSecret"];
                })
                .AddFacebook(options =>
                {
                    options.AppId = Configuration["Authentication:Facebook:AppId"];
                    options.AppSecret = Configuration["Authentication:Facebook:AppSecret"];
                })
                .AddGoogle(options =>
                {
                    options.ClientId = Configuration["Authentication:Google:ClientId"];
                    options.ClientSecret = Configuration["Authentication:Google:ClientSecret"];
                });

            services.AddAuthorization();

            services.AddDatabaseDeveloperPageExceptionFilter();

            services.AddAutoMapper(typeof(VeloTimeProfile));

            services.AddScoped<IPassingService, PassingService>();
            services.AddScoped<IRiderService, RiderService>();
            services.AddScoped<IStatisticsService, StatisticsService>();
            services.AddScoped<ITrackService, TrackService>();
            services.AddScoped<ITransponderService, TransponderService>();

            services.AddHostedService<CreatePassingHandler>();

            services.AddCors(options =>
            {
                options.AddPolicy(name: AllowedOrigins,
                                  builder =>
                                  {
                                      builder.WithOrigins("https://veloti.me", "https://www.veloti.me", "https://velotime.azurewebsites.net", "https://velotime-github-ci.azurewebsites.net");
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

            app.UseIdentityServer();
            app.UseAuthentication();
            app.UseAuthorization();

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
