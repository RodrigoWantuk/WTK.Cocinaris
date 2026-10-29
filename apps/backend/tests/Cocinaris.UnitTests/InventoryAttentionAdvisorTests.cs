using Cocinaris.Modules.Inventory.Application;

namespace Cocinaris.UnitTests;

public sealed class InventoryAttentionAdvisorTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static InventoryAttentionFacts Facts(DateOnly? printed = null, string storage = "Pantry", string? package = null, string state = "AsAcquired", DateOnly? prepared = null, DateTimeOffset? frozenAt = null, DateTimeOffset? thawStartedAt = null, DateTimeOffset? thawedAt = null) =>
        new(printed, prepared, storage, package, state, frozenAt, thawStartedAt, thawedAt);

    [Theory]
    [InlineData(-8, InventoryAttentionState.LikelyDiscard, "date_passed_long_ago")]
    [InlineData(-1, InventoryAttentionState.CheckCondition, "date_passed")]
    [InlineData(0, InventoryAttentionState.UsePriority, "date_today_or_tomorrow")]
    [InlineData(1, InventoryAttentionState.UsePriority, "date_today_or_tomorrow")]
    [InlineData(4, InventoryAttentionState.UseSoon, "date_soon")]
    [InlineData(5, InventoryAttentionState.Normal, null)]
    public void PrintedDatesAreClassifiedByDaysLeft(int days, InventoryAttentionState expected, string? reason)
    {
        var result = InventoryAttentionAdvisor.Evaluate(Facts(printed: Today.AddDays(days)), Today, Utc);
        Assert.Equal(expected, result.State);
        Assert.Equal(Today.AddDays(days), result.EstimatedUseBy);
        Assert.Equal(InventoryAttentionEvidence.PrintedDate, result.Evidence);
        Assert.Equal("printed_date", result.Reasons[0]);
        if (reason is not null)
        {
            Assert.Contains(reason, result.Reasons);
        }
    }

    [Fact]
    public void AnUndatedSealedLotNeedsNoAttention()
    {
        var result = InventoryAttentionAdvisor.Evaluate(Facts(), Today, Utc);
        Assert.Equal(InventoryAttentionState.Normal, result.State);
        Assert.Equal(InventoryAttentionEvidence.Unknown, result.Evidence);
    }

    [Fact]
    public void OpeningMakesADistantPrintedDateAndAMissingDateUncertain()
    {
        Assert.Equal(InventoryAttentionState.EstimateUncertain, InventoryAttentionAdvisor.Evaluate(Facts(printed: Today.AddDays(30), package: "Opened"), Today, Utc).State);
        Assert.Equal(["opened_without_date"], InventoryAttentionAdvisor.Evaluate(Facts(package: "Opened"), Today, Utc).Reasons);
        // A near date stays the more urgent signal.
        Assert.Equal(InventoryAttentionState.UsePriority, InventoryAttentionAdvisor.Evaluate(Facts(printed: Today, package: "Opened"), Today, Utc).State);
    }

    [Fact]
    public void FreezingStopsThePrintedDateAndOnlyLongStorageAsksForACheck()
    {
        var recent = InventoryAttentionAdvisor.Evaluate(Facts(printed: Today.AddDays(-30), storage: "Freezer", state: "Frozen", frozenAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)), Today, Utc);
        Assert.Equal(InventoryAttentionState.Normal, recent.State);
        var old = InventoryAttentionAdvisor.Evaluate(Facts(storage: "Freezer", state: "Frozen", frozenAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), Today, Utc);
        Assert.Equal(InventoryAttentionState.CheckCondition, old.State);
        Assert.Equal(["frozen_long_time"], old.Reasons);
    }

    [Fact]
    public void ThawedFoodInTheRefrigeratorFollowsTheCuratedTwoDayRule()
    {
        var started = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
        var result = InventoryAttentionAdvisor.Evaluate(Facts(storage: "Refrigerator", state: "Thawing", thawStartedAt: started), Today, Utc);
        Assert.Equal(InventoryAttentionState.UsePriority, result.State);
        Assert.Equal(new DateOnly(2026, 10, 30), result.EstimatedUseBy);
        Assert.Equal(InventoryAttentionEvidence.CuratedRule, result.Evidence);

        // An earlier printed date stays the stronger evidence.
        var printed = InventoryAttentionAdvisor.Evaluate(Facts(printed: Today.AddDays(-1), storage: "Refrigerator", state: "Thawed", thawedAt: started), Today, Utc);
        Assert.Equal(InventoryAttentionEvidence.PrintedDate, printed.Evidence);
        Assert.Equal(InventoryAttentionState.CheckCondition, printed.State);
    }

    [Fact]
    public void ThawingOutsideTheRefrigeratorAsksForACheck()
    {
        var result = InventoryAttentionAdvisor.Evaluate(Facts(storage: "Pantry", state: "Thawing", thawStartedAt: DateTimeOffset.UnixEpoch), Today, Utc);
        Assert.Equal(InventoryAttentionState.CheckCondition, result.State);
        Assert.Equal(["thawing_outside_refrigerator"], result.Reasons);
    }

    [Fact]
    public void APreparedShelfLifeDateIsUsedBeforeThePrintedDate()
    {
        var result = InventoryAttentionAdvisor.Evaluate(Facts(printed: Today.AddDays(20), prepared: Today.AddDays(1), storage: "Refrigerator", state: "Prepared"), Today, Utc);
        Assert.Equal(InventoryAttentionEvidence.PreparedRecord, result.Evidence);
        Assert.Equal(InventoryAttentionState.UsePriority, result.State);
    }

    [Fact]
    public void TransitionInstantsUseTheUsersCalendarDate()
    {
        // 23:30 in São Paulo on Oct 27 is Oct 28 in UTC; the local date decides the rule.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var started = new DateTimeOffset(2026, 10, 28, 2, 30, 0, TimeSpan.Zero);
        var result = InventoryAttentionAdvisor.Evaluate(Facts(storage: "Refrigerator", state: "Thawing", thawStartedAt: started), Today, zone);
        Assert.Equal(new DateOnly(2026, 10, 29), result.EstimatedUseBy);
    }
}
