using System.Diagnostics;

namespace Cocinaris.Api.Services;

/// <summary>Creates the stable privacy-safe Problem Details envelope used by API and framework failures.</summary>
public static class ApiProblem
{
    /// <summary>Creates a Problem Details result with a safe trace identifier and optional field errors.</summary>
    /// <param name="context">Current HTTP context.</param>
    /// <param name="statusCode">HTTP failure status.</param>
    /// <param name="errorCode">Stable machine-readable error code.</param>
    /// <param name="detail">Non-sensitive diagnostic detail.</param>
    /// <param name="errors">Optional field-level errors.</param>
    /// <returns>An <c>application/problem+json</c> result.</returns>
    public static IResult Create(HttpContext context, int statusCode, string errorCode, string? detail = null, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier
        };
        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(detail: detail, statusCode: statusCode, extensions: extensions);
    }
}
