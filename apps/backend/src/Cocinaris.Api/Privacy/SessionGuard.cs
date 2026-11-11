using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cocinaris.Api.Services;
using Cocinaris.SharedKernel.Privacy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Cocinaris.Api.Privacy;

/// <summary>
/// Session rules of PLAN-0049 (decisions E2 and E3): a session lives at most 30 days from its sign-in,
/// and a session signed in before its account's deletion ends at once. A sign-in during a deletion is
/// refused until the deletion completes; after that, signing in starts an empty account (decision D2).
/// </summary>
public static class SessionGuard
{
    /// <summary>Claim holding the sign-in time, in Unix milliseconds, stamped when the session is created.</summary>
    public const string SignedInAtClaim = "cocinaris:signed_in_at";

    /// <summary>The longest a session lives from its sign-in.</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);

    /// <summary>How long a tombstone matters: one day longer than the longest session.</summary>
    public static readonly TimeSpan TombstoneLifetime = AbsoluteLifetime + TimeSpan.FromDays(1);

    /// <summary>How recent a sign-in must be to delete the account (decision E1).</summary>
    public static readonly TimeSpan RecentSignIn = TimeSpan.FromMinutes(15);

    /// <summary>The login's issuer and subject, as the identity module reads them; null without both.</summary>
    public static (string Issuer, string Subject)? LoginOf(ClaimsPrincipal principal)
    {
        var subject = (principal.FindFirst("sub") ?? principal.FindFirst(ClaimTypes.NameIdentifier))?.Value;
        var issuer = principal.FindFirstValue("iss") ?? principal.Claims.Select(claim => claim.Issuer).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value != ClaimsIdentity.DefaultIssuer);
        return string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(issuer) ? null : (issuer, subject);
    }

    /// <summary>SHA-256, in lowercase hex, of issuer and subject: the only form of the login a tombstone keeps.</summary>
    public static string HashOf(string issuer, string subject) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(issuer + "\n" + subject)));

    /// <summary>The sign-in time of a session, or null when the session has none.</summary>
    public static DateTimeOffset? SignedInAt(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(SignedInAtClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    /// <summary>A copy of the principal with the sign-in time added to its first identity.</summary>
    public static ClaimsPrincipal WithSignedInAt(ClaimsPrincipal principal, DateTimeOffset signedInAt)
    {
        var copy = principal.Clone();
        if (copy.Identity is ClaimsIdentity identity)
        {
            foreach (var old in identity.FindAll(SignedInAtClaim).ToList())
            {
                identity.RemoveClaim(old);
            }

            identity.AddClaim(new Claim(SignedInAtClaim, signedInAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        }

        return copy;
    }

    /// <summary>
    /// Cookie validation: a session from before this rule has no sign-in time, so it gets the cookie's
    /// issue time. That time is always before any tombstone, because tombstones did not exist then, so
    /// an old session of a deleted account still ends.
    /// </summary>
    public static Task StampMissingSignInAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is { } principal && SignedInAt(principal) is null)
        {
            context.ReplacePrincipal(WithSignedInAt(principal, context.Properties.IssuedUtc ?? DateTimeOffset.UnixEpoch));
            context.ShouldRenew = true;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Ends the request with 401 when the session is past its lifetime, older than its account's
    /// deletion, or signed in while that deletion runs; a cookie session is also removed.
    /// </summary>
    public static async Task EnforceAsync(HttpContext context, Func<Task> next)
    {
        // Only the API holds a person's data; health checks and static files must not depend on the database here.
        if (!context.Request.Path.StartsWithSegments("/api") || context.User.Identity?.IsAuthenticated != true || LoginOf(context.User) is not { } login)
        {
            await next();
            return;
        }

        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var signedInAt = SignedInAt(context.User);
        string? ended = null;
        if (signedInAt is { } started && now - started > AbsoluteLifetime)
        {
            ended = "session_expired";
        }
        else
        {
            var marks = await context.RequestServices.GetRequiredService<AccountDeletionMarks>().GetAsync(context.RequestServices, now, context.RequestAborted);
            if (marks.TryGetValue(HashOf(login.Issuer, login.Subject), out var mark))
            {
                if (signedInAt is null || signedInAt < mark.RequestedAt)
                {
                    ended = "session_ended";
                }
                else if (!mark.Completed)
                {
                    ended = "account_deletion_in_progress";
                }
            }
        }

        if (ended is null)
        {
            await next();
            return;
        }

        // The cookie goes whatever the identity's type: one made by the OIDC sign-in is not typed as a
        // cookie identity, and a cookie left behind would refuse every request, even signing in again.
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            // Signing in again replaces the ended session instead of failing on it.
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            await next();
            return;
        }

        await ApiProblem.Create(context, StatusCodes.Status401Unauthorized, ended, "The session has ended.").ExecuteAsync(context);
    }
}

/// <summary>
/// Recent deletions by login hash, read at most every 15 seconds: the check runs on every request, and
/// deletions are rare. A new request clears it, so its own sessions end at once.
/// </summary>
public sealed class AccountDeletionMarks
{
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyDictionary<string, AccountDeletionMark> marks = new Dictionary<string, AccountDeletionMark>();
    private DateTimeOffset readAt = DateTimeOffset.MinValue;

    /// <summary>The deletions that can still end a session, by login hash; the latest per login wins.</summary>
    public async Task<IReadOnlyDictionary<string, AccountDeletionMark>> GetAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now - readAt < Freshness)
        {
            return marks;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (now - readAt >= Freshness)
            {
                var list = await services.GetRequiredService<IAccountDeletionStore>().ListMarksAsync(now - SessionGuard.TombstoneLifetime, cancellationToken);
                marks = list.GroupBy(mark => mark.SubjectHash, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.MaxBy(mark => mark.RequestedAt)!, StringComparer.Ordinal);
                readAt = now;
            }

            return marks;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Forgets what was read, so the next request reads again.</summary>
    public void Invalidate() => readAt = DateTimeOffset.MinValue;
}
