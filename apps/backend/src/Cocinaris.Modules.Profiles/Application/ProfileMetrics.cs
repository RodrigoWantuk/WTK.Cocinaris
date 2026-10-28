using System.Diagnostics.Metrics;

namespace Cocinaris.Modules.Profiles.Application;

/// <summary>Bounded profile metrics with stable low-cardinality tags.</summary>
public sealed class ProfileMetrics
{
    private static readonly Meter Meter = new("Cocinaris.Profiles", "1.0.0");
    private readonly Counter<long> _mutations = Meter.CreateCounter<long>("profile_mutations_total");
    private readonly Counter<long> _reads = Meter.CreateCounter<long>("profile_reads_total");

    /// <summary>Records one profile mutation attempt.</summary>
    public void RecordMutation(string operation, string? errorCode = null)
    {
        var outcome = errorCode ?? "succeeded";
        _mutations.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>Records one profile read attempt. Must not be used for mutations.</summary>
    public void RecordRead(string operation, string? errorCode = null)
    {
        var outcome = errorCode ?? "succeeded";
        _reads.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("outcome", outcome));
    }
}
