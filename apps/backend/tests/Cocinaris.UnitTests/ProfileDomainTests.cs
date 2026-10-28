using Cocinaris.Modules.Profiles.Domain;

namespace Cocinaris.UnitTests;

public sealed class ProfileDomainTests
{
    [Fact]
    public void StableCodeRejectsWhitespace()
    {
        Assert.False(StableCode.TryCreate("peanut allergy", out _));
        Assert.True(StableCode.TryCreate("peanut_allergy", out var code));
        Assert.Equal("peanut_allergy", code!.Value);
    }

    [Fact]
    public void IanaTimeZoneRejectsUnknownZone()
    {
        Assert.False(IanaTimeZoneId.TryCreate("Not/AZone", out _));
        Assert.True(IanaTimeZoneId.TryCreate("America/Sao_Paulo", out var zone));
        Assert.Equal("America/Sao_Paulo", zone!.Value);
    }

    [Fact]
    public void CompletenessDoesNotRequireFullProfile()
    {
        var profile = UserProfile.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());
        var summary = ProfileCompletenessCalculator.Compute(profile, 0, 0, 0, 0);
        Assert.Equal(0, summary.PercentComplete);
        Assert.Equal(AdultDeclarationState.NotDeclared, summary.AdultDeclarationState);
    }

    [Fact]
    public void PreferenceEntryCanBeExplicitlyRemoved()
    {
        StableCode.TryCreate("shellfish", out var code);
        var entry = PreferenceEntry.CreateConfirmed(Guid.NewGuid(), PreferenceCategory.Allergy, code!, null, 0, DateTimeOffset.UtcNow);
        entry.Remove(DateTimeOffset.UtcNow);
        Assert.Equal(ProfileFieldPresence.Removed, entry.Presence);
    }
}
