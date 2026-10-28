namespace Cocinaris.Api.Services;

/// <summary>
/// Validates the non-secret runtime configuration required by the authenticated inventory slice.
/// Readiness intentionally validates local configuration only; it does not make an unbounded OIDC
/// metadata network call that could turn an identity-provider outage into a misleading probe.
/// </summary>
public sealed class RuntimeConfigurationReadiness
{
    /// <summary>Creates readiness validation for the active host environment.</summary>
    public RuntimeConfigurationReadiness(bool isDevelopment, DatabaseOptions database, OidcOptions oidc, DataProtectionOptions dataProtection, SessionOptions session, IdempotencyOptions idempotency)
    {
        IsDevelopment = isDevelopment;
        IsReady = database.IsValid(isDevelopment) && oidc.IsValid(isDevelopment) && dataProtection.IsValid(isDevelopment) && session.IsValid() && idempotency.IsValid();
        FailureReason = IsReady ? null : "Required database, OIDC, data-protection, session, or idempotency configuration is absent or unsafe for this environment.";
    }

    /// <summary>Gets whether this host is running with the explicitly relaxed development policy.</summary>
    public bool IsDevelopment { get; }

    /// <summary>Gets whether all configuration required for this environment is valid.</summary>
    public bool IsReady { get; }

    /// <summary>Gets a safe configuration failure category without exposing values or secrets.</summary>
    public string? FailureReason { get; }

    /// <summary>Throws a safe startup exception when non-development configuration is incomplete.</summary>
    /// <exception cref="InvalidOperationException">Thrown when production-like configuration is invalid.</exception>
    public void ThrowIfInvalidForNonDevelopment()
    {
        if (!IsDevelopment && !IsReady)
        {
            throw new InvalidOperationException(FailureReason);
        }
    }
}
