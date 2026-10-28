using System.Security.Claims;
using Cocinaris.Modules.Identity;

namespace Cocinaris.Api.Services;

/// <summary>
/// Extracts the authenticated OIDC issuer and subject from the current HTTP principal. Internal
/// Cocinaris identity provisioning is owned by <see cref="CurrentUserResolver"/> in the
/// Identity module rather than by this transport adapter.
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : IOidcSubjectAccessor
{
    /// <summary>Gets the current request's complete OIDC issuer-and-subject pair.</summary>
    /// <param name="cancellationToken">Token that cancels the database operation.</param>
    /// <returns>The external identity pair used by the Identity application service.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the authenticated principal lacks a stable issuer or subject.</exception>
    public Task<OidcSubject> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        var subjectClaim = principal.FindFirst("sub") ?? principal.FindFirst(ClaimTypes.NameIdentifier);
        var subject = subjectClaim?.Value;
        var issuer = principal.FindFirstValue("iss") ?? principal.Claims.Select(claim => claim.Issuer).FirstOrDefault(claimIssuer => !string.IsNullOrWhiteSpace(claimIssuer) && claimIssuer != ClaimsIdentity.DefaultIssuer);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(issuer))
        {
            throw new UnauthorizedAccessException("The authenticated identity is incomplete.");
        }

        return Task.FromResult(new OidcSubject(issuer, subject));
    }
}
