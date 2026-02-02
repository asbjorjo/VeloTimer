using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Test;

public class TransponderTest
{
    [Fact]
    public void Code()
    {
        string Code = "HT-24422";
        long Expected = 106187718;

        var transponder = MylapsX2Transponder.CodeToId(Code);

        Assert.Equal(Expected, transponder);
    }

    [Fact]
    public void LongId()
    {
        long Id = 106187718;
        string Expected = "HT-24422";

        var transponder = MylapsX2Transponder.IdToCode(Id);

        Assert.Equal(Expected, transponder);
    }

    [Fact]
    public void InvalidStringId()
    {
        string Id = "HT-24422";

        var exception = Assert.Throws<ArgumentException>(() => MylapsX2Transponder.IdToCode(Id));
    }

    [Fact]
    public void InvalidId()
    {
        long Id = 123;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => MylapsX2Transponder.IdToCode(Id));
    }

    [Fact]
    public void InvalidCode()
    {
        string Code = "AA-12345";

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => MylapsX2Transponder.CodeToId(Code));
    }
}
