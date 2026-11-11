using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Cocinaris.Modules.Identity;
using Cocinaris.SharedKernel.Privacy;

namespace Cocinaris.Api.Privacy;

/// <summary>Why a deletion request was refused, as a closed code.</summary>
public enum AccountDeletionRefusal
{
    /// <summary>The typed word is not the confirmation word.</summary>
    WrongConfirmation,

    /// <summary>The session's sign-in is older than <see cref="SessionGuard.RecentSignIn"/> (decision E1).</summary>
    ReauthenticationRequired,
}

/// <summary>
/// Asks for an account deletion (PLAN-0049 Slice 2, decision D1: at once, no way back). The request is
/// recorded durably, the worker is woken, and every session of the account ends; the erasure itself
/// runs in <see cref="AccountDeletionProcessor"/>.
/// </summary>
public sealed class AccountDeletionService(
    ICurrentUserAccessor currentUser,
    IAccountDeletionStore store,
    AccountDeletionMarks marks,
    AccountDeletionSignal signal,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor)
{
    /// <summary>The words that confirm, one per language; compared without case or surrounding spaces.</summary>
    public static readonly IReadOnlySet<string> ConfirmationWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EXCLUIR", "DELETE", "ELIMINAR" };

    /// <summary>Records the deletion of the current account, or returns the one already pending.</summary>
    /// <param name="confirmation">What the person typed.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>When the deletion was requested, or why it was refused.</returns>
    public async Task<(DateTimeOffset? RequestedAt, AccountDeletionRefusal? Refusal)> RequestAsync(string? confirmation, CancellationToken cancellationToken)
    {
        if (confirmation is null || !ConfirmationWords.Contains(confirmation.Trim()))
        {
            return (null, AccountDeletionRefusal.WrongConfirmation);
        }

        var principal = httpContextAccessor.HttpContext?.User ?? throw new UnauthorizedAccessException("No request.");
        var now = timeProvider.GetUtcNow();
        if (SessionGuard.SignedInAt(principal) is not { } signedInAt || now - signedInAt > SessionGuard.RecentSignIn)
        {
            return (null, AccountDeletionRefusal.ReauthenticationRequired);
        }

        var login = SessionGuard.LoginOf(principal) ?? throw new UnauthorizedAccessException("The authenticated identity is incomplete.");
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (_, requestedAt) = await store.RequestAsync(SessionGuard.HashOf(login.Issuer, login.Subject), user.Id, now, cancellationToken);
        marks.Invalidate();
        signal.Notify();
        return (requestedAt, null);
    }
}

/// <summary>Wakes the deletion worker so a request is erased within seconds, not at the next poll.</summary>
public sealed class AccountDeletionSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>Asks the worker to run; a run already asked for covers this one.</summary>
    public void Notify() => channel.Writer.TryWrite(true);

    /// <summary>Waits for a notification or the timeout, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            await channel.Reader.WaitToReadAsync(timer.Token);
            channel.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout passed with no request; the worker polls anyway.
        }
    }
}

/// <summary>
/// Erases due deletions, one transaction each. A failure is retried a minute later with its exception
/// type recorded; nothing of the account's data reaches logs or metrics.
/// </summary>
public sealed class AccountDeletionProcessor(IAccountDeletionStore store, AccountDeletionMarks marks, TimeProvider timeProvider, ILogger<AccountDeletionProcessor> logger)
{
    /// <summary>Wait after a failed attempt.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private static readonly Meter Meter = new("Cocinaris.Privacy.Deletion");
    private static readonly Counter<long> Deletions = Meter.CreateCounter<long>("cocinaris.privacy.deletion", "{deletion}", "Account erasures attempted, by outcome: completed or failed.");

    /// <summary>Erases every due deletion; returns how many completed.</summary>
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var completed = 0;
        foreach (var id in await store.ListDueAsync(timeProvider.GetUtcNow(), 10, cancellationToken))
        {
            try
            {
                if (await store.EraseAsync(id, timeProvider.GetUtcNow(), cancellationToken) is not null)
                {
                    completed++;
                    Deletions.Add(1, new KeyValuePair<string, object?>("outcome", "completed"));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Deletions.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
                logger.LogWarning("Account deletion {DeletionId} failed: {ExceptionType}", id, exception.GetType().Name);
                await store.RecordFailureAsync(id, exception.GetType().Name, timeProvider.GetUtcNow() + RetryAfter, cancellationToken);
            }
        }

        if (completed > 0)
        {
            // A completed deletion lets the login start a new account at once.
            marks.Invalidate();
        }

        return completed;
    }
}

/// <summary>Runs <see cref="AccountDeletionProcessor"/> when a request arrives and every 30 seconds, also after a restart.</summary>
public sealed class AccountDeletionWorker(IServiceScopeFactory scopes, AccountDeletionSignal signal, ILogger<AccountDeletionWorker> logger) : BackgroundService
{
    /// <summary>Longest wait between runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AccountDeletionProcessor>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Account deletion run failed: {ExceptionType}", exception.GetType().Name);
            }

            try
            {
                await signal.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
