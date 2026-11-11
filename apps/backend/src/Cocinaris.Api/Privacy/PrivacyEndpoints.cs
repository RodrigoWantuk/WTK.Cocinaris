using Cocinaris.Modules.Identity;
using Microsoft.Extensions.Options;

namespace Cocinaris.Api.Privacy;

/// <summary>Maps the privacy center (PLAN-0049): what the page needs to know, and the export download.</summary>
public static class PrivacyEndpoints
{
    /// <summary>Maps <c>GET /</c> (the page's settings) and <c>GET /export</c> (the ZIP).</summary>
    public static RouteGroupBuilder MapPrivacyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (IOptions<PrivacyOptions> options) =>
                Results.Json(new PrivacyCenterView(string.IsNullOrWhiteSpace(options.Value.ContactEmail) ? null : options.Value.ContactEmail.Trim())))
            .Produces<PrivacyCenterView>();
        // A GET, so no CSRF token: it changes nothing, and the session cookie is SameSite.
        group.MapGet("/export", async (ICurrentUserAccessor currentUser, PrivacyExportService export, string? language, HttpContext context, CancellationToken cancellationToken) =>
            {
                // Resolved before the first byte, so a missing identity is a 401 and not a broken file.
                var user = await currentUser.GetCurrentAsync(cancellationToken);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Stream(body => export.WriteAsync(user.Id, language, body, cancellationToken), "application/zip", export.FileName());
            })
            .RequireRateLimiting("privacy-export")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(429);
        return group;
    }
}

/// <summary>Settings of the privacy center, bound from the <c>Privacy</c> section.</summary>
public sealed class PrivacyOptions
{
    /// <summary>Gets or sets where people write about rights the page cannot serve; empty hides the line. Never a default address in code.</summary>
    public string? ContactEmail { get; set; }
}

/// <summary>What the privacy page shows besides fixed copy.</summary>
/// <param name="ContactEmail">The support address, or null when none is configured.</param>
public sealed record PrivacyCenterView(string? ContactEmail);
