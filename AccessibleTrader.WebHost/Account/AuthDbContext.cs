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
    }
}
