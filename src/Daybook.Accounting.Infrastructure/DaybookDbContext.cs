using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// The EF Core context (spec §7.1). Public only so the eventual Api host
/// can register it for DI (<c>AddDbContext</c>); its <c>DbSet</c>s stay
/// internal since nothing outside Infrastructure should query them
/// directly — callers go through the port interfaces instead.
/// </summary>
/// <remarks>
/// Extends <see cref="IdentityUserContext{TUser,TKey}"/> (users/claims/
/// logins/tokens, spec §8) rather than plain <c>DbContext</c> or the
/// role-bearing <c>IdentityDbContext</c> — see <see cref="ApplicationUser"/>
/// for why roles are deliberately excluded.
/// </remarks>
public sealed class DaybookDbContext(DbContextOptions<DaybookDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    internal DbSet<BookEntity> Books => Set<BookEntity>();

    internal DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    internal DbSet<JournalEntryEntity> JournalEntries => Set<JournalEntryEntity>();

    internal DbSet<JournalLineEntity> JournalLines => Set<JournalLineEntity>();

    internal DbSet<AuditLogEntryEntity> AuditLogEntries => Set<AuditLogEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Identity's AspNetUsers/AspNetUserClaims/... defaults aren't
        // SQL-Server-specific - just a library naming convention. Renamed
        // for consistency with this project's plain PascalCase tables.
        modelBuilder.Entity<ApplicationUser>(user => user.ToTable("Users"));
        modelBuilder.Entity<IdentityUserClaim<Guid>>(claim => claim.ToTable("UserClaims"));
        modelBuilder.Entity<IdentityUserLogin<Guid>>(login => login.ToTable("UserLogins"));
        modelBuilder.Entity<IdentityUserToken<Guid>>(token => token.ToTable("UserTokens"));

        modelBuilder.Entity<BookEntity>(book =>
        {
            book.ToTable("Books");
            book.HasKey(x => x.Id);
            book.Property(x => x.Name).IsRequired();
            book.Property(x => x.Basis).HasConversion<string>().IsRequired();
            book.Property(x => x.BaseCurrency).IsRequired().HasMaxLength(3);
            book.Property(x => x.Status).HasConversion<string>().IsRequired();
        });

        modelBuilder.Entity<AccountEntity>(account =>
        {
            account.ToTable("Accounts");
            account.HasKey(x => x.Id);
            account.Property(x => x.Name).IsRequired();
            account.Property(x => x.Type).HasConversion<string>().IsRequired();
            account.HasIndex(x => new { x.BookId, x.Code }).IsUnique();
            account.HasOne<BookEntity>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            account.HasOne<AccountEntity>().WithMany().HasForeignKey(x => x.ParentAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalEntryEntity>(entry =>
        {
            entry.ToTable("JournalEntries");
            entry.HasKey(x => x.Id);
            entry.Property(x => x.Description).IsRequired();
            entry.Property(x => x.Status).HasConversion<string>().IsRequired();
            entry.Property(x => x.SchemaVersion).IsRequired();
            entry.HasIndex(x => new { x.BookId, x.SequenceNumber }).IsUnique();
            entry.HasOne<BookEntity>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            entry.HasOne<JournalEntryEntity>().WithMany().HasForeignKey(x => x.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalLineEntity>(line =>
        {
            line.ToTable("JournalLines");
            line.HasKey(x => x.Id);
            line.Property(x => x.Side).HasConversion<string>().IsRequired();
            line.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            line.HasOne<JournalEntryEntity>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Cascade);
            line.HasOne<AccountEntity>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLogEntryEntity>(entry =>
        {
            entry.ToTable("AuditLogEntries");
            entry.HasKey(x => x.Id);
            entry.Property(x => x.BeforeStatus).HasConversion<string>().IsRequired();
            entry.Property(x => x.AfterStatus).HasConversion<string>().IsRequired();
            entry.HasOne<JournalEntryEntity>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}