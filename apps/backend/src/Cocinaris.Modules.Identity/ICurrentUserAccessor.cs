namespace Cocinaris.Modules.Identity;

/// <summary>
/// Resolves the Cocinaris-owned user identity authorized for the current application request.
/// Module use cases depend on this inward-facing contract rather than on HTTP claims, OIDC SDK
/// types, or Entity Framework persistence services.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>Gets the authoritative internal user for the current authenticated request.</summary>
    /// <param name="cancellationToken">Token that cancels identity resolution.</param>
    /// <returns>The internal identity derived from the authenticated issuer and subject.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the request has no complete authenticated identity.</exception>
    Task<InternalUser> GetCurrentAsync(CancellationToken cancellationToken);
}
