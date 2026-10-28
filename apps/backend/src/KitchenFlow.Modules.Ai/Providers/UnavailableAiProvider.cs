using KitchenFlow.Modules.Ai.Abstractions;

namespace KitchenFlow.Modules.Ai.Providers;

/// <summary>
/// Production-safe provider used when the live AI capability is disabled or unconfigured. Every
/// invocation fails as <see cref="AiProviderFailureKind.Unavailable"/> so the cook-now generate
/// path surfaces a truthful degraded state without substituting mock recipes.
/// </summary>
public sealed class UnavailableAiProvider : IAiProvider
{
    /// <inheritdoc />
    public string Name => "unavailable";

    /// <inheritdoc />
    public Task<AiProviderInvocationResult> InvokeAsync(AiProviderInvocationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(AiProviderInvocationResult.Failure(AiProviderFailureKind.Unavailable));
}
