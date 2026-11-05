using System.Buffers.Text;
using Cocinaris.Modules.Planning.Application;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0062 Slice 7: which addresses the server may call, and when a notification is due.</summary>
public sealed class PlanReminderPushTests
{
    private static PreparationReminderView Reminder(string kind, DateOnly due) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Ensopado", due.AddDays(1), "dinner", kind, "Cebola", "unit", 1m, due, null, null);

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc")]
    [InlineData("https://web.push.apple.com/abc")]
    [InlineData("https://FCM.googleapis.com/fcm/send/abc")]
    public void KnownPushServicesAreAccepted(string endpoint) => Assert.True(PushSubscriptionRules.IsAllowedEndpoint(endpoint));

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative")]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc")]
    [InlineData("https://user:secret@fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://fcm.googleapis.com.evil.example/abc")]
    [InlineData("https://evilfcm.googleapis.com/abc")]
    [InlineData("https://evilnotify.windows.com/abc")]
    [InlineData("https://127.0.0.1/abc")]
    [InlineData("https://localhost/abc")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/abc")]
    [InlineData("file:///etc/passwd")]
    public void EveryOtherAddressIsRefused(string endpoint) => Assert.False(PushSubscriptionRules.IsAllowedEndpoint(endpoint));

    [Fact]
    public void AnEndpointLongerThanTheLimitIsRefused() =>
        Assert.False(PushSubscriptionRules.IsAllowedEndpoint("https://fcm.googleapis.com/" + new string('a', PushSubscriptionRules.MaxEndpointLength)));

    [Fact]
    public void KeysMustHaveTheShapeTheStandardRequires()
    {
        var point = new byte[65];
        point[0] = 0x04;
        Assert.True(PushSubscriptionRules.IsValidP256dh(Base64Url.EncodeToString(point)));
        Assert.False(PushSubscriptionRules.IsValidP256dh(Base64Url.EncodeToString(new byte[65])));
        Assert.False(PushSubscriptionRules.IsValidP256dh(Base64Url.EncodeToString(new byte[64])));
        Assert.False(PushSubscriptionRules.IsValidP256dh("not base64!"));
        Assert.False(PushSubscriptionRules.IsValidP256dh(null));
        Assert.True(PushSubscriptionRules.IsValidAuth(Base64Url.EncodeToString(new byte[16])));
        Assert.False(PushSubscriptionRules.IsValidAuth(Base64Url.EncodeToString(new byte[15])));
        Assert.False(PushSubscriptionRules.IsValidAuth(string.Empty));
    }

    [Theory]
    [InlineData("2026-11-10T12:00:00Z", "America/Sao_Paulo", "2026-11-10")]
    [InlineData("2026-11-10T12:59:59Z", "America/Sao_Paulo", "2026-11-10")]
    [InlineData("2026-11-10T13:00:00Z", "America/Sao_Paulo", null)]
    [InlineData("2026-11-10T11:59:59Z", "America/Sao_Paulo", null)]
    // Half-hour zone: the top of the hour in UTC is 09:30 there.
    [InlineData("2026-11-10T04:00:00Z", "Asia/Kolkata", "2026-11-10")]
    // The local day differs from the UTC day.
    [InlineData("2026-11-09T20:00:00Z", "Pacific/Auckland", "2026-11-10")]
    [InlineData("2026-11-10T12:00:00Z", null, null)]
    [InlineData("2026-11-10T12:00:00Z", "", null)]
    [InlineData("2026-11-10T12:00:00Z", "Not/AZone", null)]
    public void TheSendingHourFollowsTheProfileTimeZone(string now, string? zone, string? expected) =>
        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), PlanReminderPushSchedule.DueDay(DateTimeOffset.Parse(now), zone));

    [Fact]
    public void OnlyEligibleKindsDueByTodayCount()
    {
        var today = new DateOnly(2026, 11, 10);
        Assert.True(PlanReminderPushSchedule.HasDueReminder([Reminder("buy", today)], today));
        Assert.True(PlanReminderPushSchedule.HasDueReminder([Reminder("thaw", today)], today));
        Assert.True(PlanReminderPushSchedule.HasDueReminder([Reminder("cook_by", today.AddDays(-1))], today));
        Assert.False(PlanReminderPushSchedule.HasDueReminder([Reminder("buy", today.AddDays(1))], today));
        Assert.False(PlanReminderPushSchedule.HasDueReminder([Reminder("use_by", today), Reminder("ready_portions", today)], today));
        Assert.False(PlanReminderPushSchedule.HasDueReminder([], today));
    }

    [Theory]
    [InlineData("2026-11-10T12:00:00Z", 3600)]
    [InlineData("2026-11-10T12:59:30Z", 30)]
    [InlineData("2026-11-10T12:15:00Z", 2700)]
    public void TheJobWaitsForTheTopOfTheNextHour(string now, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), PlanReminderPushSchedule.DelayToNextHour(DateTimeOffset.Parse(now)));

    [Theory]
    [InlineData("es", "es")]
    [InlineData("en", "en")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData(null, "pt-BR")]
    [InlineData("<script>", "pt-BR")]
    public void ThePayloadHoldsOnlyACodeAndAKnownLanguage(string? language, string expected) =>
        Assert.Equal($$"""{"type":"plan_reminder","language":"{{expected}}"}""", PlanReminderPushSchedule.Payload(language));
}
