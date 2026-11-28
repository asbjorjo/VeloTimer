using System.Text;
using VeloTime.Agent.Identity;

using Assert = Xunit.Assert;

namespace VeloTime.Workers.Test;

/// <summary>
/// Pins agent passing IDs. Every expected UUID below is a release contract: if one changes, every deployed agent's
/// replay turns into Timing DuplicateObservation conflicts. Values were computed independently with Python's uuid
/// module.
/// </summary>
public class PassingIdTests
{
    [Fact]
    public void UuidV5MatchesRfc9562Example()
    {
        var dns = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        Assert.Equal(new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"),
            PassingId.UuidV5(dns, Encoding.UTF8.GetBytes("www.example.com")));
    }

    [Fact]
    public void NamespaceIsFixed() =>
        Assert.Equal(new Guid("f76d74ca-8d93-49de-8f40-9a8d552769e2"), PassingId.Namespace);

    [Fact]
    public void AgentTypeNamespaceIsUuidV5OfLibraryNamespace() =>
        Assert.Equal(new Guid("0679ea3d-3c5f-5b81-8673-bc8e54b19f14"),
            PassingId.UuidV5(PassingId.Namespace, Encoding.UTF8.GetBytes("P3")));

    public static TheoryData<Guid, Func<Guid>> GoldenVectors => new()
    {
        { new Guid("c3d5873c-66c6-5c87-958e-62264725b210"), () => PassingId.Create("P3", [1, 2, 3, 4]) },
        { new Guid("3af8d4a4-b3d9-5609-8ccf-cefca6eccd86"), () => PassingId.Create("X2", "server-1/123456") },
        { new Guid("dee592d5-b2ad-511f-a8a5-77fd868d1eaa"), () => PassingId.Create("Dummy", "Bjørnstad") },
        { new Guid("09ab9384-4e28-5afc-bcbe-0b719e3f215d"), () => PassingId.Create("X2", "server-1", "123456") },
        { new Guid("c1940338-a7df-5a5a-8cdc-fcdab9a0d027"), () => PassingId.Create("P3", "decoder-42", "4711", "") },
        { new Guid("134423a2-7cca-50fb-9e38-000d572f72cc"), () => PassingId.Create("X2", new string('x', 1000)) },
    };

    [Theory]
    [MemberData(nameof(GoldenVectors))]
    public void GoldenVectorsAreStable(Guid expected, Func<Guid> create)
    {
        var id = create();
        Assert.Equal(expected, id);
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(5, id.Version);
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(id.TryWriteBytes(bytes, bigEndian: true, out _));
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }

    [Fact]
    public void StringOverloadEncodesUtf8()
    {
        const string identity = "decoder-42/Æøå";
        Assert.Equal(PassingId.Create("P3", Encoding.UTF8.GetBytes(identity)), PassingId.Create("P3", identity));
    }

    [Fact]
    public void SameInputIsDeterministicAndAgentTypesAreSeparated()
    {
        Assert.Equal(PassingId.Create("X2", "server-1", "1"), PassingId.Create("X2", "server-1", "1"));
        Assert.NotEqual(PassingId.Create("X2", "server-1", "1"), PassingId.Create("P3", "server-1", "1"));
        Assert.NotEqual(PassingId.Create("X2", "1"), PassingId.Create("x2", "1"));
    }

    [Fact]
    public void PartsDoNotCollideAcrossSplitPoints()
    {
        Assert.Equal(new Guid("c39f1ef9-bed7-5029-b99d-b26e45f77e42"), PassingId.Create("X2", "ab", "c"));
        Assert.Equal(new Guid("e3bf1dd6-d3e6-5ee3-a791-8517501a701d"), PassingId.Create("X2", "a", "bc"));
        Assert.NotEqual(PassingId.Create("X2", "abc"), PassingId.Create("X2", "abc", ""));
    }

    [Fact]
    public void InvalidInputThrows()
    {
        Assert.Throws<ArgumentException>(() => PassingId.Create("", "1"));
        Assert.Throws<ArgumentException>(() => PassingId.Create(" ", "1"));
        Assert.Throws<ArgumentException>(() => PassingId.Create(null!, "1"));
        Assert.Throws<ArgumentException>(() => PassingId.Create("X2", ""));
        Assert.Throws<ArgumentException>(() => PassingId.Create("X2", ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => PassingId.Create("X2", ReadOnlySpan<string>.Empty));
        Assert.Throws<ArgumentException>(() => PassingId.Create("X2", "a", null!));
    }
}
