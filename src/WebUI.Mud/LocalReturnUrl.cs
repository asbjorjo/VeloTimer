namespace VeloTime.WebUI.Mud;

/// <summary>
/// Validates and normalizes user-supplied return URLs so redirects stay on this origin.
/// </summary>
internal static class LocalReturnUrl
{
    /// <summary>
    /// Returns true when <paramref name="value"/> is a rooted path that resolves to the request origin.
    /// Rejects protocol-relative (<c>//host</c>) and backslash forms, fragments, and control characters.
    /// </summary>
    internal static bool IsLocal(string? value, HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('/') || value.StartsWith("//") ||
            value.Contains('\\') || value.Contains('#') || value.Any(char.IsControl))
            return false;

        // A rooted path can parse as an absolute file URI. Validate its rooted
        // path/query shape above, then resolve against the origin instead.
        var origin = new Uri($"{http.Request.Scheme}://{http.Request.Host}");
        return Uri.TryCreate(origin, value, out var uri) &&
               uri.Scheme == origin.Scheme &&
               uri.Authority == origin.Authority &&
               string.IsNullOrEmpty(uri.UserInfo);
    }

    /// <summary>
    /// Turns a return URL from the UI into a local redirect target, or the application root when it is not local.
    /// Accepts an absolute http(s) URL (its path and query are kept), a rooted path, or a base-relative path.
    /// </summary>
    internal static string Normalize(string? value, HttpContext http)
    {
        var pathBase = http.Request.PathBase.Value ?? string.Empty;
        var fallback = string.IsNullOrEmpty(pathBase) ? "/" : pathBase;

        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string candidate;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            candidate = absolute.PathAndQuery;
        else if (value.StartsWith('/') || value.StartsWith('\\'))
            candidate = value;
        else if (value.Contains(':'))
            return fallback; // Other schemes, such as javascript: or file:.
        else
            candidate = $"{pathBase}/{value}";

        return IsLocal(candidate, http) ? candidate : fallback;
    }
}
