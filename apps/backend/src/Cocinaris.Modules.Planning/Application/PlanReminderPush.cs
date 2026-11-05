using System.Buffers.Text;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Profiles.Application;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>One browser's push subscription (ADR-0013). The keys are personal data and are never logged.</summary>
/// <param name="OwnerUserId">Owner.</param>
/// <param name="Endpoint">Push service address, on a host <see cref="PushSubscriptionRules"/> accepts.</param>
/// <param name="P256dh">Browser's public key, base64url.</param>
/// <param name="Auth">Browser's authentication secret, base64url.</param>
public sealed record PushSubscriptionEntry(Guid OwnerUserId, string Endpoint, string P256dh, string Auth);

/// <summary>Whether push can be offered.</summary>
/// <param name="Available">True when the server has valid keys.</param>
/// <param name="PublicKey">Public key the browser subscribes with, when available.</param>
public sealed record PushStatusView(bool Available, string? PublicKey);

/// <summary>What the push service answered.</summary>
public enum PushSendOutcome
{
    /// <summary>The service accepted the message.</summary>
    Delivered,

    /// <summary>The subscription no longer exists (HTTP 404 or 410); delete it.</summary>
    Gone,

    /// <summary>Anything else; keep the subscription.</summary>
    Failed,
}

/// <summary>What one owner's turn did.</summary>
public enum PlanReminderPushOutcome
{
    /// <summary>Not 09:00 hour in the owner's time zone, or no valid time zone.</summary>
    NotDue,

    /// <summary>Nothing due today.</summary>
    NothingDue,

    /// <summary>Already handled today.</summary>
    AlreadySent,

    /// <summary>The notification went to the owner's subscriptions.</summary>
    Sent,
}

/// <summary>Sends one encrypted message. Implementations must not throw for a bad subscription, and must not log its address or keys.</summary>
public interface IWebPushSender
{
    /// <summary>Gets the public key, or null when push is not configured.</summary>
    string? PublicKey { get; }

    /// <summary>Sends the message once.</summary>
    /// <param name="subscription">Destination.</param>
    /// <param name="payload">JSON payload; must contain no user data.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<PushSendOutcome> SendAsync(PushSubscriptionEntry subscription, string payload, CancellationToken cancellationToken);
}

/// <summary>Persistence of push subscriptions and of the last day each owner was notified.</summary>
public interface IPushSubscriptionStore
{
    /// <summary>Creates the subscription or moves it to this owner, then keeps the owner's newest <paramref name="maxPerOwner"/>.</summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="now">Current time.</param>
    /// <param name="maxPerOwner">Most subscriptions an owner keeps.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task UpsertAsync(PushSubscriptionEntry subscription, DateTimeOffset now, int maxPerOwner, CancellationToken cancellationToken);

    /// <summary>Removes the owner's subscription; another owner's row stays untouched.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="endpoint">Address.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task DeleteAsync(Guid ownerUserId, string endpoint, CancellationToken cancellationToken);

    /// <summary>Lists owners with at least one subscription, in a stable order.</summary>
    /// <param name="limit">Most owners returned.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<Guid>> ListOwnersAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Lists the owner's subscriptions.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<PushSubscriptionEntry>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Records that a message was accepted.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="endpoint">Address.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task MarkDeliveredAsync(Guid ownerUserId, string endpoint, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Takes the owner's turn for the day. Atomic: of any concurrent callers, one gets true.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="day">The owner's local day.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>True when no earlier call took this day or a later one.</returns>
    Task<bool> TryClaimDayAsync(Guid ownerUserId, DateOnly day, CancellationToken cancellationToken);
}

/// <summary>Validates what a browser sends. The endpoint is a URL the server will call, so it is treated as hostile.</summary>
public static class PushSubscriptionRules
{
    /// <summary>Longest endpoint accepted.</summary>
    public const int MaxEndpointLength = 2048;

    /// <summary>Most subscriptions an owner keeps.</summary>
    public const int MaxPerOwner = 10;

    private const int P256dhBytes = 65;
    private const int AuthBytes = 16;
    private const byte UncompressedPoint = 0x04;

    private static readonly string[] ExactHosts = ["fcm.googleapis.com", "web.push.apple.com"];
    private static readonly string[] HostSuffixes = [".push.services.mozilla.com", ".notify.windows.com", ".push.apple.com"];

    /// <summary>True for HTTPS on the default port, without user information, on a known push service.</summary>
    /// <param name="endpoint">Address.</param>
    public static bool IsAllowedEndpoint(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint) || endpoint.Length > MaxEndpointLength || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
        {
            return false;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        return ExactHosts.Contains(host, StringComparer.Ordinal) || HostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal));
    }

    /// <summary>True for an uncompressed P-256 point in base64url.</summary>
    /// <param name="value">Key.</param>
    public static bool IsValidP256dh(string? value) => Decode(value) is { Length: P256dhBytes } bytes && bytes[0] == UncompressedPoint;

    /// <summary>True for a 16-byte secret in base64url.</summary>
    /// <param name="value">Secret.</param>
    public static bool IsValidAuth(string? value) => Decode(value) is { Length: AuthBytes };

    private static byte[]? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128)
        {
            return null;
        }

        try
        {
            return Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>When a notification is due. Pure.</summary>
public static class PlanReminderPushSchedule
{
    /// <summary>Local hour the notification belongs to.</summary>
    public const int SendHour = 9;

    private static readonly string[] EligibleKinds = ["buy", "thaw", "cook_by"];

    /// <summary>The owner's local day when it is the sending hour there; otherwise null, including for a missing or unknown zone.</summary>
    /// <param name="utcNow">Current time.</param>
    /// <param name="timeZoneId">Time zone from the profile.</param>
    public static DateOnly? DueDay(DateTimeOffset utcNow, string? timeZoneId)
    {
        if (string.IsNullOrEmpty(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        return local.Hour == SendHour ? DateOnly.FromDateTime(local.DateTime) : null;
    }

    /// <summary>True when a reminder of an eligible kind is due today or earlier.</summary>
    /// <param name="reminders">The owner's reminders.</param>
    /// <param name="today">The owner's local day.</param>
    public static bool HasDueReminder(IEnumerable<PreparationReminderView> reminders, DateOnly today) =>
        reminders.Any(item => item.DueDate <= today && EligibleKinds.Contains(item.Kind, StringComparer.Ordinal));

    /// <summary>
    /// Time until the next hour starts. The job aligns to the hour so a slow run cannot drift past a
    /// zone's whole sending hour; half-hour zones reach it at :30, which is still inside that hour.
    /// </summary>
    /// <param name="utcNow">Current time.</param>
    public static TimeSpan DelayToNextHour(DateTimeOffset utcNow) =>
        TimeSpan.FromHours(1) - TimeSpan.FromTicks(utcNow.Ticks % TimeSpan.TicksPerHour);

    /// <summary>The message for the worker's script. Only a closed code and a language, never text or user data (ADR-0013).</summary>
    /// <param name="language">Profile language, if any.</param>
    public static string Payload(string? language) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "plan_reminder",
            ["language"] = language is "pt-BR" or "en" or "es" ? language : "pt-BR",
        });
}

/// <summary>A person's push choice, for the signed-in owner only.</summary>
public sealed class PushSubscriptionService(ICurrentUserAccessor currentUser, IPushSubscriptionStore store, IWebPushSender sender, TimeProvider timeProvider)
{
    /// <summary>Whether push can be offered.</summary>
    public PushStatusView GetStatus() => new(sender.PublicKey is not null, sender.PublicKey);

    /// <summary>Saves this browser's subscription. Repeating it changes nothing.</summary>
    /// <param name="endpoint">Push service address.</param>
    /// <param name="p256dh">Browser's public key.</param>
    /// <param name="auth">Browser's secret.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<bool>> SubscribeAsync(string? endpoint, string? p256dh, string? auth, CancellationToken cancellationToken)
    {
        if (sender.PublicKey is null)
        {
            return RecipeApplicationResult<bool>.Failure("push_unavailable", "Notifications are not available.", null);
        }

        if (!PushSubscriptionRules.IsAllowedEndpoint(endpoint))
        {
            return Invalid("endpoint", "This browser's push service is not supported.");
        }

        if (!PushSubscriptionRules.IsValidP256dh(p256dh) || !PushSubscriptionRules.IsValidAuth(auth))
        {
            return Invalid("keys", "The subscription keys are not valid.");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        await store.UpsertAsync(new PushSubscriptionEntry(user.Id, endpoint!, p256dh!, auth!), timeProvider.GetUtcNow(), PushSubscriptionRules.MaxPerOwner, cancellationToken);
        return RecipeApplicationResult<bool>.Succeeded(true);
    }

    /// <summary>Forgets this browser's subscription. An address the owner does not hold does nothing and reveals nothing.</summary>
    /// <param name="endpoint">Push service address.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<bool>> UnsubscribeAsync(string? endpoint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(endpoint) || endpoint.Length > PushSubscriptionRules.MaxEndpointLength)
        {
            return Invalid("endpoint", "The address is not valid.");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        await store.DeleteAsync(user.Id, endpoint, cancellationToken);
        return RecipeApplicationResult<bool>.Succeeded(true);
    }

    private static RecipeApplicationResult<bool> Invalid(string field, string detail) =>
        RecipeApplicationResult<bool>.Failure("validation_failed", detail, new Dictionary<string, string[]> { [field] = [detail] });
}

/// <summary>
/// Decides and sends one owner's notification (ADR-0013). There is no signed-in user here: everything
/// uses the explicit owner id. The day is claimed before the first send, so a crash can lose a
/// notification but a restart can never repeat it.
/// </summary>
public sealed class PlanReminderPushService(
    IGetProfileSessionProjectionUseCase profiles,
    MenuPlanningApplicationService planning,
    IPushSubscriptionStore store,
    IWebPushSender sender,
    TimeProvider timeProvider)
{
    private const int DaysChecked = 2;
    private static readonly Meter Meter = new("Cocinaris.PlanReminderPush", "1.0.0");
    private static readonly Counter<long> Sends = Meter.CreateCounter<long>("cocinaris.push.sent");

    /// <summary>Handles one owner.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<PlanReminderPushOutcome> ProcessOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetAsync(ownerUserId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (PlanReminderPushSchedule.DueDay(now, profile.TimeZone) is not { } today)
        {
            return PlanReminderPushOutcome.NotDue;
        }

        var reminders = await planning.GetPreparationForOwnerAsync(ownerUserId, today, DaysChecked, cancellationToken);
        if (!PlanReminderPushSchedule.HasDueReminder(reminders.Reminders, today))
        {
            return PlanReminderPushOutcome.NothingDue;
        }

        if (!await store.TryClaimDayAsync(ownerUserId, today, cancellationToken))
        {
            return PlanReminderPushOutcome.AlreadySent;
        }

        var payload = PlanReminderPushSchedule.Payload(profile.Language);
        foreach (var subscription in await store.ListAsync(ownerUserId, cancellationToken))
        {
            var outcome = await sender.SendAsync(subscription, payload, cancellationToken);
            Sends.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));
            if (outcome == PushSendOutcome.Delivered)
            {
                await store.MarkDeliveredAsync(ownerUserId, subscription.Endpoint, timeProvider.GetUtcNow(), cancellationToken);
            }
            else if (outcome == PushSendOutcome.Gone)
            {
                await store.DeleteAsync(ownerUserId, subscription.Endpoint, cancellationToken);
            }
        }

        return PlanReminderPushOutcome.Sent;
    }
}
