using System.Diagnostics.Metrics;

namespace VeloTime.Workers.Test;

/// <summary>Collects observable gauge readings of one meter on demand.</summary>
internal sealed class GaugeReader : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly List<(string Name, double Value, Dictionary<string, string?> Tags)> readings = [];

    public GaugeReader(string meter)
    {
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == meter) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
        listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i, v, t));
        listener.Start();
    }

    public IReadOnlyList<(double Value, Dictionary<string, string?> Tags)> Read(string instrument)
    {
        lock (readings) readings.Clear();
        listener.RecordObservableInstruments();
        lock (readings) return readings.Where(r => r.Name == instrument).Select(r => (r.Value, r.Tags)).ToList();
    }

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, string?>();
        foreach (var tag in tags) copy[tag.Key] = tag.Value?.ToString();
        lock (readings) readings.Add((instrument.Name, value, copy));
    }

    public void Dispose() => listener.Dispose();
}
