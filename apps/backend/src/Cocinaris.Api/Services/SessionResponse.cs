namespace Cocinaris.Api.Services;

/// <summary>
/// Returns the Cocinaris internal user identifier, request-specific CSRF token, and safe profile session projection
/// for a backend-managed browser session. OIDC access and refresh tokens are intentionally never present in this response.
/// </summary>
public sealed record SessionResponse(
    Guid UserId,
    string CsrfToken,
    IReadOnlyList<string> SupportedLocales,
    string? DisplayName,
    string? Language,
    string? TimeZone,
    string? MeasurementSystem,
    bool ProfileExists,
    int ProfilePercentComplete,
    string AdultDeclarationState);
