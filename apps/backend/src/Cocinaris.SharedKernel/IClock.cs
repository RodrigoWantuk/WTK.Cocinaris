namespace Cocinaris.SharedKernel;

/// <summary>Provides the current UTC instant for deterministic domain and application behavior.</summary>
public interface IClock
{
    /// <summary>Gets the current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Adapts <see cref="TimeProvider"/> to the Cocinaris clock abstraction.</summary>
/// <param name="timeProvider">Time source used to obtain the current UTC instant.</param>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    /// <summary>Gets the current UTC instant from the configured provider.</summary>
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
