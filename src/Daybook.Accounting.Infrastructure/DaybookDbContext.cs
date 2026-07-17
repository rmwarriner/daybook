using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// The EF Core context (spec §7.1). Public only so the eventual Api host
/// can register it for DI (<c>AddDbContext</c>); its <c>DbSet</c>s stay
/// internal since nothing outside Infrastructure should query them
/// directly — callers go through the port interfaces instead.
/// </summary>
public sealed class DaybookDbContext(DbContextOptions<DaybookDbContext> options) : DbContext(options)
{
    internal DbSet<BookEntity> Books => Set<BookEntity>();

    internal DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    }
}