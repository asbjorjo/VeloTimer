using System.Text.Json;
using VeloTime.Agent.Interface.Messages.Events;
using VeloTime.Agent.Model;

namespace VeloTime.Workers.Test;

public sealed class PassingWarningContractTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void PassingWarnings_SurviveMappingAndSerialization(bool battery, bool strength, bool hits)
    {
        var passing = new Passing { Id = "test", Time = DateTime.UtcNow, TransponderType = "X2",
            TransponderId = "106187718", LoopId = "finish", LowBattery = battery, LowStrength = strength, LowHits = hits };
        var message = JsonSerializer.Deserialize<PassingEvent>(JsonSerializer.Serialize(passing.ToMessage()))!;
        Assert.Equal(battery, message.LowBattery);
        Assert.Equal(strength, message.LowStrength);
        Assert.Equal(hits, message.LowHits);
        var (time, type, id, loop) = message;
        Assert.Equal(passing.Time, time);
        Assert.Equal(passing.TransponderType, type);
        Assert.Equal(passing.TransponderId, id);
        Assert.Equal(passing.LoopId, loop);
    }

    [Fact]
    public void LegacyPassing_DeserializesWithoutWarnings()
    {
        var message = JsonSerializer.Deserialize<PassingEvent>(
            """{"Time":"2026-10-30T12:00:00Z","TransponderType":"X2","TransponderId":"106187718","LoopId":"finish"}""")!;
        Assert.False(message.LowBattery || message.LowStrength || message.LowHits);
    }
}
