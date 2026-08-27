using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// Design-time factory for <see cref="AuthDbContext"/>, used only by
    /// <c>dotnet ef migrations add</c>.
    ///
    /// <para>Without it, the EF tooling tries to build the whole application host to find a
    /// context, which fails here — <c>Program.cs</c> takes command-line switches the tooling
    /// does not supply, and the accounts DbContext is registered conditionally on
    /// <c>accountsEnabled</c>. Pointing the tooling at a throwaway SQLite connection string is
    /// enough: a migration is generated from the MODEL, not from any real database, so the
    /// path below is never opened.</para>
    ///
    /// <para>Runtime is unaffected. The real registration still comes from
    /// <c>AccountsServiceExtensions</c> with the configured path.</para>
    /// </summary>
    public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
    {
        public AuthDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlite("Data Source=design-time-only.db")
                .Options;
            return new AuthDbContext(options);
        }
    }
}
