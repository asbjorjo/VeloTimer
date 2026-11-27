using System.Globalization;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Test;

public class TestTransponder : Transponder
{
    public override TransponderType Type { get; } = TransponderType.Unknown;
}

public class TransponderTest
{
    [Fact]
    public void BaseType()
    {
        long Id = 106187718;
        string Code = "HT-24422";

        Transponder transponder = new MylapsProChipTransponder { Id = Guid.NewGuid(), SystemId = Id.ToString() };
        Assert.Equal(Id.ToString(), transponder.SystemId);
        Assert.Equal(Code, transponder.Label);
    }

    [Fact]
    public void Code()
    {
        string Code = "HT-24422";
        long Expected = 106187718;

        var transponder = MylapsProChipTransponder.CodeToId(Code);

        Assert.Equal(Expected, transponder);
    }

    [Fact]
    public void LongId()
    {
        long Id = 106187718;
        string Expected = "HT-24422";

        var transponder = MylapsProChipTransponder.IdToCode(Id);

        Assert.Equal(Expected, transponder);
    }

    [Fact]
    public void InvalidStringId()
    {
        string Id = "HT-24422";

        var exception = Assert.Throws<ArgumentException>(() => MylapsProChipTransponder.IdToCode(Id));
    }

    [Fact]
    public void InvalidId()
    {
        long Id = 123;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => MylapsProChipTransponder.IdToCode(Id));
    }

    [Fact]
    public void InvalidCode()
    {
        string Code = "AA-12345";

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => MylapsProChipTransponder.CodeToId(Code));
    }

    [Fact]
    public void AddOwner()
    {
        Transponder transponder = new TestTransponder { Id = Guid.NewGuid(), SystemId = string.Empty, Owners = [
            new()
        {
            OwnerId = new("CE23797E-7BA8-4E50-AB76-3402CE9FE9D9"),
            OwnedFrom = DateTime.ParseExact("2000-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            OwnedTo = DateTime.ParseExact("2000-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)
        },
            new()
        {
            OwnerId = new("CE23797E-7BA8-4E50-AB76-3402CE9FE9D8"),
            OwnedFrom = DateTime.ParseExact("2002-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            OwnedTo = null
        }] };

        transponder.AddOwner(
            Guid.NewGuid(),
            DateTime.ParseExact("2001-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.ParseExact("2001-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture));

        transponder.AddOwner(
            Guid.NewGuid(),
            DateTime.ParseExact("2000-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.ParseExact("2000-03-01", "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AddOwnerFail()
    {
        Transponder transponder = new TestTransponder
        {
            Id = Guid.NewGuid(),
            SystemId = string.Empty,
            Owners = [
            new()
        {
            OwnerId = new("CE23797E-7BA8-4E50-AB76-3402CE9FE9D9"),
            OwnedFrom = DateTime.ParseExact("2000-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            OwnedTo = DateTime.ParseExact("2000-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)
        },
            new()
        {
            OwnerId = new("CE23797E-7BA8-4E50-AB76-3402CE9FE9D8"),
            OwnedFrom = DateTime.ParseExact("2002-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            OwnedTo = null
        }]
        };

        var exception = Assert.Throws<InvalidOperationException>(() => transponder.AddOwner(
            Guid.NewGuid(),
            DateTime.ParseExact("1999-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.ParseExact("2003-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture))
        );
        exception = Assert.Throws<InvalidOperationException>(() => transponder.AddOwner(
            Guid.NewGuid(),
            DateTime.ParseExact("2000-01-02", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.ParseExact("2003-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture))
        );
        exception = Assert.Throws<InvalidOperationException>(() => transponder.AddOwner(
            Guid.NewGuid(),
            DateTime.ParseExact("2003-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.ParseExact("2003-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture))
        );
    }

    [Fact]
    public void AddOwnerInvalid()
    {
        Transponder transponder = new TestTransponder
        {
            Id = Guid.NewGuid(),
            SystemId = string.Empty,
            Owners = [
            new()
        {
            OwnerId = new("CE23797E-7BA8-4E50-AB76-3402CE9FE9D9"),
            OwnedFrom = DateTime.ParseExact("2000-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            OwnedTo = DateTime.ParseExact("2000-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)
        },
            new()
        {
            OwnerId = new("CE23797E-7BA8-4E50-AB76-3402CE9FE9D8"),
            OwnedFrom = DateTime.ParseExact("2002-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            OwnedTo = null
        }]
        };

        var exception = Assert.Throws<ArgumentException>(() => transponder.AddOwner(
            Guid.Empty,
            DateTime.ParseExact("1999-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.ParseExact("2003-02-01", "yyyy-MM-dd", CultureInfo.InvariantCulture))
        );
        exception = Assert.Throws<ArgumentException>(() => transponder.AddOwner(
            Guid.NewGuid(),
            DateTime.ParseExact("2000-02-02", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.ParseExact("2000-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture))
        );
        exception = Assert.Throws<ArgumentException>(() => transponder.AddOwner(
            Guid.Empty,
            DateTime.ParseExact("2000-02-02", "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.ParseExact("2000-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture))
        );
    }

    [Theory]
    [InlineData("106187718", true)]
    [InlineData("100663297", true)]
    [InlineData("100663296", false)]
    [InlineData("123456", false)]
    [InlineData("HT-24422", false)]
    [InlineData("+106187718", false)]
    [InlineData(" 106187718", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Factory_ValidatesProChipIds(string? systemId, bool valid)
    {
        Assert.Equal(valid, TransponderFactory.TryCreate(TransponderType.MylapsProChip, systemId, out var transponder, out var error));
        if (valid)
        {
            Assert.IsType<MylapsProChipTransponder>(transponder);
            Assert.Equal(systemId, transponder!.SystemId);
            Assert.Null(error);
        }
        else
        {
            Assert.Null(transponder);
            Assert.NotNull(error);
        }
    }

    [Fact]
    public void Factory_RejectsUnknownType() =>
        Assert.False(TransponderFactory.TryCreate(TransponderType.Unknown, "106187718", out _, out _));

    [Theory]
    [InlineData("ProChip", true)]
    [InlineData("PROCHIP", true)]
    [InlineData("X2", false)]
    [InlineData("MylapsX2", false)]
    [InlineData("MylapsProChip", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Factory_ParsesOnlyTheContractTypeValue(string? value, bool supported)
    {
        Assert.Equal(supported, TransponderFactory.TryParseType(value, out var type));
        Assert.Equal(supported ? TransponderType.MylapsProChip : TransponderType.Unknown, type);
    }

    [Theory]
    [InlineData("HT-24422", "106187718")]
    [InlineData("ht-24422", "106187718")]
    [InlineData("HT24422", "106187718")]
    public void Factory_ParsesPrintedProChipCodes(string code, string expected)
    {
        Assert.True(TransponderFactory.TryParseCode(TransponderType.MylapsProChip, code, out var systemId));
        Assert.Equal(expected, systemId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HT-2442")]
    [InlineData("AB-24422")]
    [InlineData("106187718")]
    public void Factory_RejectsInvalidPrintedCodes(string code) =>
        Assert.False(TransponderFactory.TryParseCode(TransponderType.MylapsProChip, code, out _));

    [Fact]
    public void ProChip_DisplaysThePrintedCode() =>
        Assert.Equal("HT-24422", new MylapsProChipTransponder { SystemId = "106187718" }.DisplayCode);
}
