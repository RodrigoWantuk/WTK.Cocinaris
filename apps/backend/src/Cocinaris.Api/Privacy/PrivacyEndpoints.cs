using Cocinaris.Api.Services;
using Cocinaris.Modules.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Cocinaris.Api.Privacy;

/// <summary>Maps the privacy center (PLAN-0049): what the page needs to know, and the export download.</summary>
public static class PrivacyEndpoints
{
    /// <summary>Maps <c>GET /</c> (the page's settings), <c>GET /export</c> (the ZIP), and <c>POST /account-deletion</c>.</summary>
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
        group.MapPost("/account-deletion", async (AccountDeletionService deletion, AccountDeletionRequest body, HttpContext context, CancellationToken cancellationToken) =>
            {
                var (requestedAt, refusal) = await deletion.RequestAsync(body.Confirmation, cancellationToken);
                if (refusal == AccountDeletionRefusal.WrongConfirmation)
                {
                    return ApiProblem.Create(context, StatusCodes.Status400BadRequest, "validation_failed", "Type the confirmation word.", new Dictionary<string, string[]> { ["confirmation"] = ["Type the confirmation word."] });
                }

                if (refusal == AccountDeletionRefusal.ReauthenticationRequired)
                {
                    return ApiProblem.Create(context, StatusCodes.Status403Forbidden, "reauthentication_required", "Sign in again to delete the account.");
                }

                // The session ends now; the erasure follows within seconds.
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                return Results.Json(new AccountDeletionView(requestedAt!.Value), statusCode: StatusCodes.Status202Accepted);
            })
            .AddEndpointFilter(Cocinaris.Api.Planning.ShoppingPlanEndpoints.ValidateCsrfAsync)
            .RequireRateLimiting("mutation")
            .Produces<AccountDeletionView>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(403);
        return group;
    }
}

/// <summary>Body of an account deletion: the typed confirmation word (<c>EXCLUIR</c>, <c>DELETE</c>, or <c>ELIMINAR</c>).</summary>
/// <param name="Confirmation">What the person typed.</param>
public sealed record AccountDeletionRequest(string? Confirmation);

/// <summary>An accepted deletion; the account's data is erased within seconds.</summary>
/// <param name="RequestedAt">When it was requested.</param>
public sealed record AccountDeletionView(DateTimeOffset RequestedAt);

/// <summary>Settings of the privacy center, bound from the <c>Privacy</c> section.</summary>
public sealed class PrivacyOptions
{
    /// <summary>Gets or sets where people write about rights the page cannot serve; empty hides the line. Never a default address in code.</summary>
    public string? ContactEmail { get; set; }
}

/// <summary>What the privacy page shows besides fixed copy.</summary>
/// <param name="ContactEmail">The support address, or null when none is configured.</param>
public sealed record PrivacyCenterView(string? ContactEmail);
