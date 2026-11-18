using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// Identity store for hosted accounts, kept in its own SQLite file (auth.db) so it
    /// migrates and backs up independently of the shared OHLCV cache DB (AppDbContext).
    /// Only registered when accounts are enabled.
    /// </summary>
    public class AuthDbContext : IdentityDbContext<AppUser>
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            var user = builder.Entity<AppUser>();
            // Case-insensitive uniqueness for display names. SQLite applies the column's
            // NOCASE collation to comparisons and to the unique index, so "Cody" and "cody"
            // collide. The filter leaves NULL names out of the index: any number of accounts
            // may have no display name.
            user.Property(u => u.DisplayName)
                .HasMaxLength(30)
                .UseCollation("NOCASE");
            user.HasIndex(u => u.DisplayName)
                .IsUnique()
                .HasFilter("\"DisplayName\" IS NOT NULL");
        }
    }
}
