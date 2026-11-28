using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace VeloTime.Agent.Identity;

/// <summary>
/// Derives deterministic passing IDs (RFC 9562 UUIDv5) for <see cref="Model.Passing.Id"/>, which Timing receives as
/// <c>PassingObservedV1.SourcePassingId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Timing treats the same transponder, loop and instant under a different ID as a <c>DuplicateObservation</c>
/// conflict, so an agent must produce the same ID for the same physical passing on every resend and replay, across
/// restarts and agent versions. The ID is <c>UUIDv5(UUIDv5(<see cref="Namespace"/>, agentType), identity)</c>.
/// </para>
/// <para>What an agent puts into the identity:</para>
/// <list type="bullet">
/// <item>a stable decoder or source identity (decoder serial, timing server ID) plus the decoder's own passing
/// identity (decoder passing number, SDK passing ID, usually the value stored in
/// <see cref="Model.Passing.HardwarePassingId"/>) or the canonical passing fields;</item>
/// <item>never the hostname, IP address or endpoint, so moving the agent does not change IDs;</item>
/// <item>never the receive time or anything else from the agent's own clock or process.</item>
/// </list>
/// <para>
/// The namespace, the derivation and the encoding of each overload are fixed. Changing any of them changes every ID,
/// and every replay would then become a Timing conflict.
/// </para>
/// </remarks>
public static class PassingId
{
    /// <summary>The fixed VeloTime agent passing ID namespace. Never change it.</summary>
    public static readonly Guid Namespace = new("f76d74ca-8d93-49de-8f40-9a8d552769e2");

    private const int StackLimit = 256;
    private static readonly ConcurrentDictionary<string, Guid> AgentNamespaces = new(StringComparer.Ordinal);

    /// <summary>Creates the passing ID for raw identity bytes.</summary>
    /// <param name="agentType">A constant agent type such as <c>"P3"</c> or <c>"X2"</c>; compared exactly and never renamed.</param>
    /// <param name="identity">The stable identity of the passing; must not be empty.</param>
    public static Guid Create(string agentType, ReadOnlySpan<byte> identity)
    {
        if (identity.IsEmpty)
            throw new ArgumentException("The passing identity must not be empty.", nameof(identity));
        return UuidV5(AgentNamespace(agentType), identity);
    }

    /// <summary>Creates the passing ID for an identity string, encoded as UTF-8 without normalisation.</summary>
    /// <param name="agentType">A constant agent type such as <c>"P3"</c> or <c>"X2"</c>; compared exactly and never renamed.</param>
    /// <param name="identity">The stable identity of the passing; must not be empty.</param>
    public static Guid Create(string agentType, string identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Length == 0)
            throw new ArgumentException("The passing identity must not be empty.", nameof(identity));
        var length = Encoding.UTF8.GetByteCount(identity);
        byte[]? rented = null;
        Span<byte> buffer = length <= StackLimit
            ? stackalloc byte[length]
            : (rented = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);
        try
        {
            var written = Encoding.UTF8.GetBytes(identity, buffer);
            return Create(agentType, buffer[..written]);
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Creates the passing ID for several identity parts. Each part is encoded as its UTF-8 byte length (4 bytes,
    /// big-endian) followed by its UTF-8 bytes, so <c>("ab", "c")</c> and <c>("a", "bc")</c> give different IDs.
    /// </summary>
    /// <param name="agentType">A constant agent type such as <c>"P3"</c> or <c>"X2"</c>; compared exactly and never renamed.</param>
    /// <param name="parts">One or more non-null parts; empty strings are allowed.</param>
    public static Guid Create(string agentType, params ReadOnlySpan<string> parts)
    {
        if (parts.IsEmpty)
            throw new ArgumentException("At least one identity part is required.", nameof(parts));
        var length = 0;
        foreach (var part in parts)
        {
            if (part is null)
                throw new ArgumentException("Identity parts must not be null.", nameof(parts));
            length = checked(length + sizeof(int) + Encoding.UTF8.GetByteCount(part));
        }

        byte[]? rented = null;
        Span<byte> buffer = length <= StackLimit
            ? stackalloc byte[length]
            : (rented = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);
        try
        {
            var offset = 0;
            foreach (var part in parts)
            {
                var written = Encoding.UTF8.GetBytes(part, buffer[(offset + sizeof(int))..]);
                BinaryPrimitives.WriteInt32BigEndian(buffer[offset..], written);
                offset += sizeof(int) + written;
            }
            return Create(agentType, buffer[..offset]);
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static Guid AgentNamespace(string agentType)
    {
        if (string.IsNullOrWhiteSpace(agentType))
            throw new ArgumentException("The agent type must not be empty.", nameof(agentType));
        return AgentNamespaces.GetOrAdd(agentType, static type => UuidV5(Namespace, Encoding.UTF8.GetBytes(type)));
    }

    /// <summary>RFC 9562 section 5.5 UUIDv5: SHA-1 over the big-endian namespace bytes followed by the name.</summary>
    internal static Guid UuidV5(Guid @namespace, ReadOnlySpan<byte> name)
    {
        var length = 16 + name.Length;
        byte[]? rented = null;
        Span<byte> input = length <= StackLimit
            ? stackalloc byte[length]
            : (rented = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);
        try
        {
            if (!@namespace.TryWriteBytes(input, bigEndian: true, out _))
                throw new InvalidOperationException("Could not write the namespace UUID.");
            name.CopyTo(input[16..]);
            Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
            SHA1.HashData(input, hash);
            hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC 9562 variant
            return new Guid(hash[..16], bigEndian: true);
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
