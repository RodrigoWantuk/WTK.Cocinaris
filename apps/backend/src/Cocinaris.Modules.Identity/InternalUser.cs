namespace Cocinaris.Modules.Identity;

/// <summary>Internal Cocinaris user derived from an OIDC issuer and subject pair.</summary>
public sealed class InternalUser
{
    /// <summary>Creates an immutable internal identity mapping.</summary>
    /// <param name="id">Cocinaris-owned user identifier.</param>
    /// <param name="issuer">Normalized OIDC issuer URI.</param>
    /// <param name="subject">OIDC subject stable within the issuer.</param>
    /// <param name="createdAt">UTC mapping creation instant.</param>
    public InternalUser(Guid id, string issuer, string subject, DateTimeOffset createdAt)
    {
        Id = id;
        Issuer = issuer;
        Subject = subject;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the Cocinaris-owned user identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the authoritative OIDC issuer.</summary>
    public string Issuer { get; private set; }

    /// <summary>Gets the authoritative OIDC subject.</summary>
    public string Subject { get; private set; }

    /// <summary>Gets the UTC mapping creation instant.</summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
