using Cocinaris.Api.Profiles;
using Microsoft.AspNetCore.WebUtilities;

namespace Cocinaris.IntegrationTests;

/// <summary>Owner-bound profile ETag codec coverage for PLAN-0012 final remediation.</summary>
public sealed class ProfileHttpTokenServiceTests
{
    private readonly ProfileHttpTokenService _tokens = new();

    [Fact]
    public void TryReadVersionAcceptsTokenForExpectedOwner()
    {
        var owner = Guid.NewGuid();
        var concurrency = Guid.NewGuid();
        var token = _tokens.WriteVersion(owner, concurrency);

        Assert.True(_tokens.TryReadVersion(owner, token, out var parsed));
        Assert.Equal(concurrency, parsed);
    }

    [Fact]
    public void TryReadVersionRejectsTokenBelongingToAnotherOwner()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var token = _tokens.WriteVersion(owner, Guid.NewGuid());

        Assert.False(_tokens.TryReadVersion(other, token, out var parsed));
        Assert.Equal(Guid.Empty, parsed);
    }

    [Fact]
    public void TryReadVersionRejectsAdulteratedOwnerBytes()
    {
        var owner = Guid.NewGuid();
        var concurrency = Guid.NewGuid();
        var token = _tokens.WriteVersion(owner, concurrency);
        var payload = WebEncoders.Base64UrlDecode(token);
        Guid.NewGuid().TryWriteBytes(payload.AsSpan(0, 16));
        var adulterated = WebEncoders.Base64UrlEncode(payload);

        Assert.False(_tokens.TryReadVersion(owner, adulterated, out _));
    }

    [Fact]
    public void TryReadVersionParsesAdulteratedConcurrencyAsDifferentToken()
    {
        var owner = Guid.NewGuid();
        var originalConcurrency = Guid.NewGuid();
        var token = _tokens.WriteVersion(owner, originalConcurrency);
        var payload = WebEncoders.Base64UrlDecode(token);
        var adulteratedConcurrency = Guid.NewGuid();
        adulteratedConcurrency.TryWriteBytes(payload.AsSpan(16, 16));
        var adulterated = WebEncoders.Base64UrlEncode(payload);

        Assert.True(_tokens.TryReadVersion(owner, adulterated, out var parsed));
        Assert.Equal(adulteratedConcurrency, parsed);
        Assert.NotEqual(originalConcurrency, parsed);
    }

    [Fact]
    public void TryReadVersionRejectsTruncatedToken()
    {
        var owner = Guid.NewGuid();
        var token = _tokens.WriteVersion(owner, Guid.NewGuid());

        Assert.False(_tokens.TryReadVersion(owner, token[..^2], out _));
    }

    [Fact]
    public void TryReadVersionRejectsIncorrectPayloadLength()
    {
        var owner = Guid.NewGuid();
        var payload = new byte[16];
        owner.TryWriteBytes(payload);
        var token = WebEncoders.Base64UrlEncode(payload);

        Assert.False(_tokens.TryReadVersion(owner, token, out _));
    }

    [Fact]
    public void TryReadVersionRejectsInvalidBase64Url()
    {
        Assert.False(_tokens.TryReadVersion(Guid.NewGuid(), "%%%not-base64%%%", out _));
    }

    [Fact]
    public void TryReadVersionRejectsEmptyConcurrencyToken()
    {
        var owner = Guid.NewGuid();
        var token = _tokens.WriteVersion(owner, Guid.Empty);

        Assert.False(_tokens.TryReadVersion(owner, token, out var parsed));
        Assert.Equal(Guid.Empty, parsed);
    }
}
