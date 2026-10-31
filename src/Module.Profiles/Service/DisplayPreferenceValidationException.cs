namespace VeloTime.Module.Profiles.Service;

/// <summary>A stable Profiles validation failure that an API can map to a contract error code.</summary>
public sealed class DisplayPreferenceValidationException(string message, string errorCode, string? paramName = null)
    : ArgumentException(message, paramName)
{
    public string ErrorCode { get; } = errorCode;
}
