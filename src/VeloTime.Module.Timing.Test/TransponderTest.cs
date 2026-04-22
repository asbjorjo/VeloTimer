using System.Globalization;
using VeloTime.Module.Timing.Model;

namespace VeloTime.Module.Timing.Test;

public class TestTransponder : Transponder
{
    public override TimingSystem System { get; } = TimingSystem.Unknown;
}

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

    [Fact]
    public void AddOwner()
    {
        Transponder transponder = new TestTransponder { Id = Guid.NewGuid(), Owners = [
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
}
