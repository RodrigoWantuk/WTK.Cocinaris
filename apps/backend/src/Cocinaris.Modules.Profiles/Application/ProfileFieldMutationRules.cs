namespace Cocinaris.Modules.Profiles.Application;

/// <summary>Validates progressive field mutation actions and durability values.</summary>
public static class ProfileFieldMutationRules
{
    /// <summary>Supported durable field actions.</summary>
    public static readonly IReadOnlySet<string> ValidActions = new HashSet<string>(StringComparer.Ordinal) { "confirm", "remove", "absent" };

    /// <summary>Supported durability values for profile mutations.</summary>
    public static readonly IReadOnlySet<string> ValidDurabilities = new HashSet<string>(StringComparer.Ordinal) { "durable" };

    /// <summary>Validates a field mutation action.</summary>
    /// <param name="action">The requested action.</param>
    /// <param name="allowAbsent">Whether absent is allowed for replace semantics.</param>
    /// <param name="error">The validation error when invalid.</param>
    /// <returns><see langword="true"/> when the action is valid.</returns>
    public static bool IsValidAction(string? action, bool allowAbsent, out string error)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            error = "action must be confirm, remove, or absent.";
            return false;
        }

        if (string.Equals(action, "absent", StringComparison.Ordinal) && !allowAbsent)
        {
            error = "action 'absent' is only allowed in PUT requests.";
            return false;
        }

        if (!ValidActions.Contains(action))
        {
            error = "action must be confirm, remove, or absent.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Validates a field mutation durability value.</summary>
    /// <param name="durability">The requested durability.</param>
    /// <param name="error">The validation error when invalid.</param>
    /// <returns><see langword="true"/> when the durability is valid.</returns>
    public static bool IsValidDurability(string? durability, out string error)
    {
        if (string.IsNullOrWhiteSpace(durability))
        {
            error = "durability must be durable.";
            return false;
        }

        if (string.Equals(durability, "temporary", StringComparison.Ordinal))
        {
            error = "temporary durability is not supported on this endpoint.";
            return false;
        }

        if (!ValidDurabilities.Contains(durability))
        {
            error = "durability must be durable.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
