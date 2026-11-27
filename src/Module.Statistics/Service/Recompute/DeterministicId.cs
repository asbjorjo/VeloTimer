using System.Security.Cryptography;
using System.Text;

namespace VeloTime.Module.Statistics.Service.Recompute;

/// <summary>
/// Name-based (RFC 9562 version 5) identities for derived rows (plan §3.4). A recompute of the same
/// sources yields the same IDs, so the diff step can match rows and duplicates are impossible by
/// construction. Keys are versioned; changing a key's shape must change its prefix.
/// </summary>
public static class DeterministicId
{
    /// <summary>Fixed namespace for VeloTime Statistics derived rows.</summary>
    private static readonly Guid Namespace = new("8f0d6a51-4b1e-4c33-9e0a-6a0f1b7a2c5e");

    public static Guid Sample(Guid startPassingId, Guid endPassingId) =>
        Create($"sample:v1:{startPassingId:D}:{endPassingId:D}");

    public static Guid SimpleEntry(Guid configId, Guid startPassingId, Guid endPassingId) =>
        Create($"simple-entry:v1:{configId:D}:{startPassingId:D}:{endPassingId:D}");

    public static Guid MultiEntry(Guid multiConfigId, Guid firstContributorId, Guid lastContributorId) =>
        Create($"multi-entry:v1:{multiConfigId:D}:{firstContributorId:D}:{lastContributorId:D}");

    public static Guid Create(string name)
    {
        Span<byte> input = stackalloc byte[16 + Encoding.UTF8.GetByteCount(name)];
        Namespace.TryWriteBytes(input[..16], bigEndian: true, out _);
        Encoding.UTF8.GetBytes(name, input[16..]);
        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(input, hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC variant
        return new Guid(bytes, bigEndian: true);
    }
}
