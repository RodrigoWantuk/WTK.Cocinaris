using System.Diagnostics;
using OpenTelemetry;

namespace Cocinaris.Api.Observability;

/// <summary>
/// Removes sensitive tags from completed telemetry activities before any configured exporter can
/// observe them. Authentication material, request bodies, product names, and private notes are
/// deliberately excluded from operational telemetry.
/// </summary>
public sealed class SensitiveTelemetryRedactionProcessor : BaseProcessor<Activity>
{
    private static readonly string[] SensitiveFragments = ["authorization", "cookie", "token", "body", "product", "note"];

    /// <inheritdoc />
    public override void OnEnd(Activity data) => Redact(data);

    /// <summary>Removes sensitive tags from an activity in place.</summary>
    /// <param name="activity">The completed activity to redact before export.</param>
    public static void Redact(Activity activity)
    {
        foreach (var tag in activity.TagObjects.ToArray())
        {
            if (SensitiveFragments.Any(fragment => tag.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                activity.SetTag(tag.Key, null);
            }
        }
    }
}
