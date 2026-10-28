namespace Cocinaris.Api.Services;

/// <summary>Typed authoritative PostgreSQL connection configuration.</summary>
public sealed class DatabaseOptions
{
    /// <summary>Gets or sets the PostgreSQL connection string supplied by server-side configuration.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Validates that a database endpoint exists and production does not use fixture credentials.</summary>
    public bool IsValid(bool isDevelopment) => RuntimeOptionValidation.HasSafeRequiredValue(ConnectionString, isDevelopment);
}

/// <summary>Typed OpenID Connect client configuration used only by the backend.</summary>
public sealed class OidcOptions
{
    /// <summary>Gets or sets the OIDC issuer authority.</summary>
    public string? Authority { get; set; }

    /// <summary>Gets or sets the confidential backend client identifier.</summary>
    public string? ClientId { get; set; }

    /// <summary>Gets or sets the confidential backend client secret.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Validates environment-aware OIDC requirements without exposing configured values.</summary>
    public bool IsValid(bool isDevelopment) =>
        Uri.TryCreate(Authority, UriKind.Absolute, out var authority) &&
        (isDevelopment || authority.Scheme == Uri.UriSchemeHttps) &&
        RuntimeOptionValidation.HasSafeRequiredValue(ClientId, isDevelopment) &&
        (isDevelopment || RuntimeOptionValidation.HasSafeRequiredValue(ClientSecret, false));
}

/// <summary>Typed persistent ASP.NET Core Data Protection key-ring configuration.</summary>
public sealed class DataProtectionOptions
{
    /// <summary>Gets or sets the protected shared key-ring directory.</summary>
    public string? KeyRingPath { get; set; }

    /// <summary>Validates that non-development hosts use an absolute persistent key-ring path.</summary>
    public bool IsValid(bool isDevelopment) =>
        isDevelopment || RuntimeOptionValidation.HasSafeRequiredValue(KeyRingPath, false) && Path.IsPathFullyQualified(KeyRingPath!);
}

/// <summary>Typed secure backend-managed browser session configuration.</summary>
public sealed class SessionOptions
{
    /// <summary>Gets or sets the secure host-only cookie name.</summary>
    public string CookieName { get; set; } = "__Host-cocinaris-session";

    /// <summary>Gets or sets the bounded idle timeout for the backend session ticket.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Validates the host-only cookie prefix and bounded session duration.</summary>
    public bool IsValid() =>
        CookieName.StartsWith("__Host-", StringComparison.Ordinal) &&
        IdleTimeout >= TimeSpan.FromMinutes(5) &&
        IdleTimeout <= TimeSpan.FromDays(1);
}

internal static class RuntimeOptionValidation
{
    private static readonly string[] Placeholders = ["development-only-change-me", "change-me", "placeholder"];

    internal static bool HasSafeRequiredValue(string? value, bool allowDevelopmentFixture)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return allowDevelopmentFixture || !Placeholders.Any(placeholder => value.Contains(placeholder, StringComparison.OrdinalIgnoreCase));
    }
}
