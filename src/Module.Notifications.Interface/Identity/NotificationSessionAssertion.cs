using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VeloTime.Module.Notifications.Interface.Identity;

/// <summary>Server-to-server only. Never return assertions or login IDs to browsers or logs.</summary>
public static class NotificationSessionAssertion
{
    public const string HeaderName = "VeloTime-Notification-Session";
    public const string ConfigurationSection = "Notifications:SessionAssertion";
    public const string TicketLoginIdProperty = ".notifications.application_login";
    public const string Protocol = "velotime.notifications.login.v1";
    public const int DefaultLifetimeSeconds = 120;
    public const int MaximumLifetimeSeconds = 300;
    public const int MaximumClockSkewSeconds = 10;

    public static string NewApplicationLoginId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Sign(RSA privateKey, string keyId, string assertionIssuer, string audience,
        string validatedIssuer, string validatedSubject, string ticketLoginId, string accessToken,
        DateTimeOffset now, DateTimeOffset ticketExpiresAtUtc, int lifetimeSeconds = DefaultLifetimeSeconds)
    {
        if (privateKey.KeySize < 2048 || !ValidKeyId(keyId) ||
            new[] { assertionIssuer, audience, validatedIssuer, validatedSubject, ticketLoginId, accessToken }
                .Any(string.IsNullOrWhiteSpace) ||
            lifetimeSeconds is < 1 or > MaximumLifetimeSeconds || ticketExpiresAtUtc <= now)
            throw new ArgumentException("Invalid login assertion signing inputs.");
        var expires = now.AddSeconds(lifetimeSeconds);
        if (ticketExpiresAtUtc < expires) expires = ticketExpiresAtUtc;
        var payload = new Payload(Protocol, assertionIssuer, audience, validatedIssuer, validatedSubject,
            ticketLoginId, TokenHash(accessToken), now.ToUnixTimeSeconds(), expires.ToUnixTimeSeconds());
        var unsigned = keyId + "." + Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = privateKey.SignData(Encoding.UTF8.GetBytes(Protocol + "." + unsigned),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return unsigned + "." + Encode(signature);
    }

    public static string Verify(string assertion, IReadOnlyDictionary<string, string> publicKeys,
        string assertionIssuer, string audience, string validatedIssuer, string validatedSubject,
        string accessToken, DateTimeOffset now, int maximumLifetimeSeconds = DefaultLifetimeSeconds,
        int clockSkewSeconds = 5)
    {
        if (assertion.Length > 16384 || maximumLifetimeSeconds is < 1 or > MaximumLifetimeSeconds ||
            clockSkewSeconds is < 0 or > MaximumClockSkewSeconds)
            throw new CryptographicException("Invalid assertion.");
        var parts = assertion.Split('.');
        if (parts.Length != 3 || !ValidKeyId(parts[0]) || !publicKeys.TryGetValue(parts[0], out var pem))
            throw new CryptographicException("Untrusted assertion key.");
        using var key = RSA.Create();
        key.ImportFromPem(pem);
        if (key.KeySize < 2048 || !key.VerifyData(Encoding.UTF8.GetBytes(Protocol + "." + parts[0] + "." + parts[1]),
                Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("Invalid assertion signature.");
        var p = JsonSerializer.Deserialize<Payload>(Decode(parts[1]));
        var seconds = now.ToUnixTimeSeconds();
        if (p is null || p.Protocol != Protocol || p.AssertionIssuer != assertionIssuer || p.Audience != audience ||
            p.IdentityIssuer != validatedIssuer || p.Subject != validatedSubject ||
            string.IsNullOrWhiteSpace(p.LoginId) || p.LoginId.Length > 256 ||
            p.AccessTokenHash != TokenHash(accessToken) || p.Expires <= seconds ||
            p.Issued > seconds + clockSkewSeconds || p.Expires <= p.Issued ||
            p.Expires - p.Issued > maximumLifetimeSeconds || p.Issued < seconds - maximumLifetimeSeconds - clockSkewSeconds)
            throw new CryptographicException("Expired or mismatched assertion.");
        return p.LoginId;
    }

    private static bool ValidKeyId(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) =>
        Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));

    private sealed record Payload(string Protocol, string AssertionIssuer, string Audience, string IdentityIssuer,
        string Subject, string LoginId, string AccessTokenHash, long Issued, long Expires);
}
