using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SlimMessageBus;
using SlimMessageBus.Host;
using SlimMessageBus.Host.AzureServiceBus;
using SlimMessageBus.Host.Outbox;
using SlimMessageBus.Host.Outbox.PostgreSql;
using SlimMessageBus.Host.Outbox.PostgreSql.Configuration;
using SlimMessageBus.Host.Outbox.PostgreSql.DbContext;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using VeloTime.Module.Statistics.Interface.Messages;
using VeloTime.Module.Statistics.Model;
using VeloTime.Module.Statistics.Storage;
using Assert = Xunit.Assert;

namespace VeloTime.Workers.Test;

public sealed partial class StatisticsSmbOutboxProofTests
{
    [PostgreSqlStatisticsFact]
    public async Task MigratedProcessorSchemaAcceptsStockSmbAndCommitsMarkerWithItsInsert()
    {
        await Sql(connection, "CREATE TABLE statistics.smb_outbox (id integer PRIMARY KEY); INSERT INTO statistics.smb_outbox VALUES (7)");
        var services = Host.CreateApplicationBuilder().Services;
        services.AddDbContext<StatisticsDbContext>(o => o.UseNpgsql(connection).UseSnakeCaseNamingConvention());
        services.AddSlimMessageBus(mbb =>
        {
            mbb.AutoStartConsumersEnabled(false);
            mbb.WithProviderServiceBus(options =>
            {
                options.ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==";
                options.TopologyProvisioning.Enabled = false;
                options.SenderFactory = (_, _) => sender;
            });
            mbb.AddJsonSerializer();
            mbb.Produce<SampleComplete>(x => x.DefaultTopic("velotime-statistics"));
            mbb.UseOutbox(true, type => type == typeof(SampleComplete));
            mbb.AddOutboxUsingDbContext<StatisticsDbContext>(options =>
            {
                options.PostgreSqlSettings.DatabaseSchemaName = "statistics_processor";
                options.MaintainSequence = true;
                options.PollBatchSize = 1;
                options.MessageCleanup = new OutboxMessageCleanupSettings { Enabled = false };
            });
        });
        await using var container = services.BuildServiceProvider();
        var bus = container.GetRequiredService<IMasterMessageBus>();
        await bus.Start();
        try
        {
            var row = Receipt();
            await using (var scope = container.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
                await using var tx = await db.Database.BeginTransactionAsync();
                db.Add(row);
                await db.SaveChangesAsync();
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().Publish(Message(row));
                row.SmbEnqueuedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM statistics_processor.smb_outbox").SingleAsync());
                await tx.RollbackAsync();
            }
            Assert.Equal(0, await Scalar<int>("SELECT count(*)::int FROM statistics_processor.smb_outbox"));
            Assert.Equal(0, await Scalar<int>("SELECT count(*)::int FROM statistics.sample_complete_outbox"));

            var committed = Receipt();
            await using (var scope = container.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();
                await using var tx = await db.Database.BeginTransactionAsync();
                db.Add(committed);
                await db.SaveChangesAsync();
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().Publish(Message(committed));
                committed.SmbEnqueuedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            Assert.Equal(1, await Scalar<int>("SELECT count(*)::int FROM statistics_processor.smb_outbox"));
            Assert.Equal(1, await Scalar<int>("SELECT count(*)::int FROM statistics.sample_complete_outbox WHERE smb_enqueued_at_utc IS NOT NULL"));
            Assert.Equal(7, await Scalar<int>("SELECT id FROM statistics.smb_outbox"));
        }
        finally
        {
            await bus.Stop();
        }
    }
}
