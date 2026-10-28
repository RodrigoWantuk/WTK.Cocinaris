using System.Diagnostics.Metrics;

namespace Cocinaris.Api.Observability;

/// <summary>
/// Records low-cardinality authentication and request-protection failures without accepting
/// identities, issuers, addresses, credentials, request content, or resource data.
/// </summary>
public sealed class SecurityMetrics
{
    private static readonly Meter Meter = new("Cocinaris.Security", "1.0.0");
    private static readonly Counter<long> AccessFailures = Meter.CreateCounter<long>(
        "cocinaris.security.access_failures",
        unit: "failures",
        description: "Authentication, authorization, CSRF, and rate-limit failures by stable category.");

    /// <summary>Records one access-control failure using a bounded stable category.</summary>
    /// <param name="category">One of authentication, authorization, csrf, or rate_limit.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a category outside the bounded set.</exception>
    public void RecordFailure(string category)
    {
        if (category is not ("authentication" or "authorization" or "csrf" or "rate_limit"))
        {
            throw new ArgumentOutOfRangeException(nameof(category), "The security metric category is not registered.");
        }

        AccessFailures.Add(1, new KeyValuePair<string, object?>("category", category));
    }
}
