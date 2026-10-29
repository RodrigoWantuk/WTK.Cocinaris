using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Profiles.Application;

namespace Cocinaris.Api.Inventory;

/// <summary>
/// Request-scoped "today" for attention evaluation, in the time zone saved in the user's profile
/// (UTC when none is saved or the zone is unknown). Advisory day boundaries must follow the user's
/// local calendar: in Brazil, a UTC date would roll over at 21:00.
/// </summary>
public sealed class InventoryCalendar(ICurrentUserAccessor currentUser, IGetProfileSessionProjectionUseCase profile, TimeProvider timeProvider)
{
    private (DateOnly Today, TimeZoneInfo Zone)? _resolved;

    /// <summary>Gets the current calendar date in the user's time zone; call <see cref="ResolveAsync"/> first.</summary>
    public DateOnly Today => _resolved?.Today ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>Gets the user's time zone; call <see cref="ResolveAsync"/> first.</summary>
    public TimeZoneInfo Zone => _resolved?.Zone ?? TimeZoneInfo.Utc;

    /// <summary>Loads the user's time zone once per request.</summary>
    public async Task ResolveAsync(CancellationToken cancellationToken)
    {
        if (_resolved is not null)
        {
            return;
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var projection = await profile.GetAsync(user.Id, cancellationToken);
        var zone = projection.TimeZone is { Length: > 0 } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        _resolved = (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime), zone);
    }
}
