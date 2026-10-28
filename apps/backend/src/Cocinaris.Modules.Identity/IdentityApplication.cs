namespace Cocinaris.Modules.Identity;

/// <summary>
/// Stable issuer-and-subject pair obtained by an outer authentication adapter. The pair is the
/// only external identity input accepted by the Cocinaris identity application boundary.
/// </summary>
/// <param name="Issuer">Normalized OIDC issuer URI asserted by the authenticated principal.</param>
/// <param name="Subject">OIDC subject stable within <paramref name="Issuer"/>.</param>
public sealed record OidcSubject(string Issuer, string Subject);

/// <summary>
/// Provides the authenticated OIDC subject from an outer transport adapter without exposing HTTP,
/// claims, or provider SDK types to module application services.
/// </summary>
public interface IOidcSubjectAccessor
{
    /// <summary>Gets the complete authenticated OIDC subject for the current request.</summary>
    /// <param name="cancellationToken">Token that cancels retrieval.</param>
    /// <returns>The issuer-and-subject pair used to resolve Cocinaris identity.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the authenticated principal is incomplete.</exception>
    Task<OidcSubject> GetCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Persistence boundary for resolving an OIDC issuer-and-subject pair to an internal Cocinaris
/// user. Implementations must be race safe because concurrent first requests are valid.
/// </summary>
public interface IInternalUserStore
{
    /// <summary>Finds or atomically creates the internal user for an authenticated subject.</summary>
    /// <param name="subject">Authoritative external identity pair.</param>
    /// <param name="occurredAt">UTC instant used only when a mapping must be created.</param>
    /// <param name="cancellationToken">Token that cancels the persistence operation.</param>
    /// <returns>The Cocinaris-owned internal identity.</returns>
    Task<InternalUser> FindOrCreateAsync(OidcSubject subject, DateTimeOffset occurredAt, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the current authenticated OIDC identity through the module's explicit persistence
/// boundary. It owns no HTTP claims parsing and no Entity Framework access.
/// </summary>
public sealed class CurrentUserResolver(
    IOidcSubjectAccessor subjectAccessor,
    IInternalUserStore userStore,
    TimeProvider timeProvider) : ICurrentUserAccessor
{
    /// <inheritdoc />
    public async Task<InternalUser> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var subject = await subjectAccessor.GetCurrentAsync(cancellationToken);
        return await userStore.FindOrCreateAsync(subject, timeProvider.GetUtcNow(), cancellationToken);
    }
}
