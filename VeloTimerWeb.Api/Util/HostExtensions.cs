﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VeloTimerWeb.Api.Data;
using VeloTimerWeb.Api.Services;

namespace Microsoft.Extensions.Hosting
{
    public static class HostExtensions
    {
        public static IHost SeedData(this IHost host)
        {
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var context = services.GetService<VeloTimerDbContext>();
                var passingservice = services.GetService<IPassingService>();

                // now we have the DbContext. Run migrations
                context.Database.EnsureCreated();

                // now that the database is up to date. Let's seed
                new SolaArenaSeed(context, passingservice).SeedData();

#if DEBUG
                // if we are debugging, then let's run the test data seeder
                // alternatively, check against the environment to run this seeder
                //new TestDataSeeder(context).SeedData();
#endif
            }

            return host;
        }
    }
}
