namespace Cocinaris.SharedKernel;

/// <summary>Describes a stable application failure without HTTP transport details.</summary>
/// <param name="Code">Machine-readable error code.</param>
/// <param name="Message">Non-localized diagnostic message for the adapter.</param>
public sealed record Error(string Code, string Message)
{
    /// <summary>Creates a validation failure.</summary>
    /// <param name="message">Validation diagnostic.</param>
    /// <returns>A validation error.</returns>
    public static Error Validation(string message) => new("validation_failed", message);

    /// <summary>Creates a domain-rule failure.</summary>
    /// <param name="message">Domain diagnostic.</param>
    /// <returns>A domain error.</returns>
    public static Error Domain(string message) => new("domain_rule_violated", message);

    /// <summary>Creates a resource-not-found failure.</summary>
    /// <returns>A not-found error.</returns>
    public static Error NotFound() => new("resource_not_found", "The requested resource was not found.");
}

/// <summary>Represents either a successful value or an application failure.</summary>
/// <typeparam name="T">Successful value type.</typeparam>
public sealed class Result<T>
{
    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>Gets the successful value, or <see langword="null"/> when the result failed.</summary>
    public T? Value { get; }

    /// <summary>Gets the failure, or <see langword="null"/> when the result succeeded.</summary>
    public Error? Error { get; }

    /// <summary>Gets whether the result contains a successful value.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">Successful value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Success(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">Application failure.</param>
    /// <returns>A failed result.</returns>
    public static Result<T> Failure(Error error) => new(default, error);
}
