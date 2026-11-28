using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using VeloTime.Module.Timing.Model;
using VeloTime.Module.Timing.Storage;

namespace VeloTime.Module.Timing.Service;

/// <summary>A loop status sample as reported by an agent, before Timing resolves its identities.</summary>
public sealed record LoopStatusObservation(
    string AgentId,
    string LoopId,
    DateTime Time,
    double Noise,
    int? LoopTriggers = null,
    double? DecoderTemperature = null,
    double? InputVoltage = null,
    bool? TimeSynchronised = null,
    int? SatellitesInUse = null,
    string? DecoderId = null,
    bool? NoiseSharesStrengthScale = null);

/// <summary>
/// Stores loop status samples and keeps their 5-minute rollup. The raw insert deduplicates on (timing point, time);
/// only a newly inserted sample is added to its rollup bucket, in the same transaction, so redelivery, full replays
/// and out-of-order arrival all give the same rows.
/// </summary>
public class LoopStatusService(InstallationService installationService, TimingDbContext storage, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <returns>True when the sample was new; false for a redelivery.</returns>
    public async Task<bool> RecordAsync(LoopStatusObservation observation, CancellationToken cancellationToken = default)
    {
        using var activity = Instrumentation.Source.StartActivity("RecordLoopStatus");
        activity?.SetTag("AgentId", observation.AgentId);
        activity?.SetTag("LoopId", observation.LoopId);

        // Identity resolution creates configuration rows idempotently and outside the sample transaction.
        var installation =
            await installationService.GetInstallationForAgent(observation.AgentId, cancellationToken) ??
            await installationService.CreateInstallationForAgent(observation.AgentId, cancellationToken: cancellationToken);
        var timingPoint =
            await installationService.GetTimingPoint(installation, observation.LoopId, cancellationToken) ??
            await installationService.CreateTimingPoint(installation, observation.LoopId, cancellationToken);

        var sample = new LoopStatusSample
        {
            Id = Guid.CreateVersion7(),
            InstallationId = installation.Id,
            TimingPointId = timingPoint.Id,
            Time = PassingTime.Normalize(observation.Time),
            ReceivedAt = _time.GetUtcNow().UtcDateTime,
            Noise = observation.Noise,
            LoopTriggers = observation.LoopTriggers,
            DecoderTemperature = observation.DecoderTemperature,
            InputVoltage = observation.InputVoltage,
            TimeSynchronised = observation.TimeSynchronised,
            SatellitesInUse = observation.SatellitesInUse,
            DecoderId = observation.DecoderId,
            NoiseSharesStrengthScale = observation.NoiseSharesStrengthScale
        };

        var ownsTransaction = storage.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = ownsTransaction ? await storage.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var inserted = await InsertSampleAsync(sample, cancellationToken) == 1;
            if (inserted) await AddToRollupAsync(sample, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            activity?.SetTag("Outcome", inserted ? "recorded" : "duplicate");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return inserted;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }

        // installation.LastSeen is updated by the caller.
    }

    private Task<int> InsertSampleAsync(LoopStatusSample sample, CancellationToken cancellationToken)
    {
        var sql = new UpsertSql(storage, typeof(LoopStatusSample));
        var values = new (string Property, object? Value)[]
        {
            (nameof(LoopStatusSample.Id), sample.Id),
            (nameof(LoopStatusSample.InstallationId), sample.InstallationId),
            (nameof(LoopStatusSample.TimingPointId), sample.TimingPointId),
            (nameof(LoopStatusSample.Time), sample.Time),
            (nameof(LoopStatusSample.ReceivedAt), sample.ReceivedAt),
            (nameof(LoopStatusSample.Noise), sample.Noise),
            (nameof(LoopStatusSample.LoopTriggers), sample.LoopTriggers),
            (nameof(LoopStatusSample.DecoderTemperature), sample.DecoderTemperature),
            (nameof(LoopStatusSample.InputVoltage), sample.InputVoltage),
            (nameof(LoopStatusSample.TimeSynchronised), sample.TimeSynchronised),
            (nameof(LoopStatusSample.SatellitesInUse), sample.SatellitesInUse),
            (nameof(LoopStatusSample.DecoderId), sample.DecoderId),
            (nameof(LoopStatusSample.NoiseSharesStrengthScale), sample.NoiseSharesStrengthScale),
        };
        var (columns, parameters) = sql.Values(values);
        var text = $"INSERT INTO {sql.Table} ({columns}) VALUES ({string.Join(", ", parameters.Select(p => p.ParameterName))}) " +
                   $"ON CONFLICT ({sql.Column(nameof(LoopStatusSample.TimingPointId))}, {sql.Column(nameof(LoopStatusSample.Time))}) DO NOTHING";
        return storage.Database.ExecuteSqlRawAsync(text, parameters, cancellationToken);
    }

    private Task<int> AddToRollupAsync(LoopStatusSample sample, CancellationToken cancellationToken)
    {
        var sql = new UpsertSql(storage, typeof(LoopStatusRollup));
        var values = new (string Property, object? Value)[]
        {
            (nameof(LoopStatusRollup.TimingPointId), sample.TimingPointId),
            (nameof(LoopStatusRollup.BucketStart), LoopStatusRollup.BucketOf(sample.Time)),
            (nameof(LoopStatusRollup.InstallationId), sample.InstallationId),
            (nameof(LoopStatusRollup.Samples), 1),
            (nameof(LoopStatusRollup.NoiseSum), sample.Noise),
            (nameof(LoopStatusRollup.NoiseMin), sample.Noise),
            (nameof(LoopStatusRollup.NoiseMax), sample.Noise),
            (nameof(LoopStatusRollup.TemperatureSamples), sample.DecoderTemperature is null ? 0 : 1),
            (nameof(LoopStatusRollup.TemperatureSum), sample.DecoderTemperature ?? 0),
            (nameof(LoopStatusRollup.TemperatureMin), sample.DecoderTemperature),
            (nameof(LoopStatusRollup.TemperatureMax), sample.DecoderTemperature),
            (nameof(LoopStatusRollup.VoltageSamples), sample.InputVoltage is null ? 0 : 1),
            (nameof(LoopStatusRollup.VoltageSum), sample.InputVoltage ?? 0),
            (nameof(LoopStatusRollup.VoltageMin), sample.InputVoltage),
            (nameof(LoopStatusRollup.VoltageMax), sample.InputVoltage),
            (nameof(LoopStatusRollup.TriggerSamples), sample.LoopTriggers is null ? 0 : 1),
            (nameof(LoopStatusRollup.TriggersSum), (long)(sample.LoopTriggers ?? 0)),
            (nameof(LoopStatusRollup.SyncSamples), sample.TimeSynchronised is null ? 0 : 1),
            (nameof(LoopStatusRollup.UnsynchronisedSamples), sample.TimeSynchronised == false ? 1 : 0),
            (nameof(LoopStatusRollup.SharedScaleSamples), sample.NoiseSharesStrengthScale == true ? 1 : 0),
        };
        var (columns, parameters) = sql.Values(values);
        // PostgreSQL's least/greatest ignore nulls; SQLite's multi-argument min/max return null, hence the coalesce.
        var npgsql = storage.Database.IsNpgsql();
        string Sum(string property) => $"{sql.Column(property)} = r.{sql.Column(property)} + excluded.{sql.Column(property)}";
        string Extreme(string property, bool min)
        {
            var c = sql.Column(property);
            var function = npgsql ? (min ? "least" : "greatest") : (min ? "min" : "max");
            return $"{c} = coalesce({function}(r.{c}, excluded.{c}), r.{c}, excluded.{c})";
        }
        var updates = new[]
        {
            Sum(nameof(LoopStatusRollup.Samples)),
            Sum(nameof(LoopStatusRollup.NoiseSum)),
            Extreme(nameof(LoopStatusRollup.NoiseMin), true),
            Extreme(nameof(LoopStatusRollup.NoiseMax), false),
            Sum(nameof(LoopStatusRollup.TemperatureSamples)),
            Sum(nameof(LoopStatusRollup.TemperatureSum)),
            Extreme(nameof(LoopStatusRollup.TemperatureMin), true),
            Extreme(nameof(LoopStatusRollup.TemperatureMax), false),
            Sum(nameof(LoopStatusRollup.VoltageSamples)),
            Sum(nameof(LoopStatusRollup.VoltageSum)),
            Extreme(nameof(LoopStatusRollup.VoltageMin), true),
            Extreme(nameof(LoopStatusRollup.VoltageMax), false),
            Sum(nameof(LoopStatusRollup.TriggerSamples)),
            Sum(nameof(LoopStatusRollup.TriggersSum)),
            Sum(nameof(LoopStatusRollup.SyncSamples)),
            Sum(nameof(LoopStatusRollup.UnsynchronisedSamples)),
            Sum(nameof(LoopStatusRollup.SharedScaleSamples)),
        };
        var text = $"INSERT INTO {sql.Table} AS r ({columns}) VALUES ({string.Join(", ", parameters.Select(p => p.ParameterName))}) " +
                   $"ON CONFLICT ({sql.Column(nameof(LoopStatusRollup.TimingPointId))}, {sql.Column(nameof(LoopStatusRollup.BucketStart))}) " +
                   $"DO UPDATE SET {string.Join(", ", updates)}";
        return storage.Database.ExecuteSqlRawAsync(text, parameters, cancellationToken);
    }

    /// <summary>Builds provider-correct identifiers and parameters from the EF model (snake_case in PostgreSQL).</summary>
    private sealed class UpsertSql
    {
        private readonly IEntityType entity;
        private readonly StoreObjectIdentifier table;
        private readonly ISqlGenerationHelper helper;
        private readonly DbCommand factory;

        public UpsertSql(TimingDbContext storage, Type type)
        {
            entity = storage.Model.FindEntityType(type) ?? throw new InvalidOperationException($"{type.Name} is not mapped.");
            var name = entity.GetTableName() ?? throw new InvalidOperationException($"{type.Name} is not mapped to a table.");
            table = StoreObjectIdentifier.Table(name, entity.GetSchema());
            helper = storage.GetService<ISqlGenerationHelper>();
            factory = storage.Database.GetDbConnection().CreateCommand();
            Table = helper.DelimitIdentifier(name, entity.GetSchema());
        }

        public string Table { get; }

        public string Column(string property) =>
            helper.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);

        public (string Columns, DbParameter[] Parameters) Values(IReadOnlyList<(string Property, object? Value)> values)
        {
            var parameters = new DbParameter[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                var property = entity.FindProperty(values[i].Property)!;
                // Provider mappings keep the representation EF uses (e.g. SQLite stores Guid and DateTime as TEXT).
                parameters[i] = property.GetRelationalTypeMapping().CreateParameter(factory, $"@p{i}", values[i].Value, property.IsNullable);
            }
            return (string.Join(", ", values.Select(v => Column(v.Property))), parameters);
        }
    }
}
