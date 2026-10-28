using Cocinaris.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of internal identity resolution. The unique issuer-and-subject index
/// is the final authority when concurrent authenticated requests create a mapping simultaneously.
/// </summary>
public sealed class PostgreSqlInternalUserStore(ApplicationDbContext database) : IInternalUserStore
{
    /// <inheritdoc />
    public async Task<InternalUser> FindOrCreateAsync(OidcSubject subject, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var existing = await database.Users.SingleOrDefaultAsync(
            user => user.Issuer == subject.Issuer && user.Subject == subject.Subject,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new InternalUser(Guid.NewGuid(), subject.Issuer, subject.Subject, occurredAt);
        database.Users.Add(created);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_users_Issuer_Subject"
        })
        {
            database.ChangeTracker.Clear();
            return await database.Users.SingleAsync(
                user => user.Issuer == subject.Issuer && user.Subject == subject.Subject,
                cancellationToken);
        }
        catch
        {
            database.ChangeTracker.Clear();
            throw;
        }
    }
}
