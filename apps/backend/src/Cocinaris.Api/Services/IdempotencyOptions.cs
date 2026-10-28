namespace Cocinaris.Api.Services;

/// <summary>
/// Retention policy for completed PostgreSQL-backed idempotency records. Retention constrains
/// future cleanup work; this first slice deliberately does not register a cleanup worker.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>Gets or sets the period for which completed command keys remain replayable.</summary>
    public TimeSpan Retention { get; set; }

    /// <summary>Validates the bounded replay-retention policy without exposing configuration values.</summary>
    public bool IsValid() => Retention >= TimeSpan.FromDays(1) && Retention <= TimeSpan.FromDays(90);
}
