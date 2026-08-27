using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// Brings <c>auth.db</c>'s schema up to date, including for databases created before
    /// migrations existed.
    ///
    /// <para>
    /// ── Why this is not a one-liner ────────────────────────────────────────────
    /// The accounts database was created with <c>EnsureCreated()</c> and the repo carried no
    /// migrations, so every existing deployment has an <c>auth.db</c> with the right tables and
    /// <b>no <c>__EFMigrationsHistory</c></b>. Calling <c>Database.Migrate()</c> on one of those
    /// would try to apply the initial migration and fail on "table AspNetUsers already exists".
    /// </para>
    ///
    /// <para>
    /// So: a database with no history table but with Identity's tables already present is
    /// <b>baselined</b> — the initial migration is recorded as applied without running it —
    /// and everything after that is an ordinary <c>Migrate()</c>. A database that does not
    /// exist yet is created by <c>Migrate()</c> in the normal way.
    /// </para>
    ///
    /// <para>
    /// This is the one place where getting it wrong locks every user out of their account, so
    /// it fails LOUDLY rather than continuing: a server that cannot prove its accounts schema
    /// is correct should refuse to serve rather than reject valid passwords.
    /// </para>
    /// </summary>
    public static class AuthDbSchema
    {
        /// <summary>The migration that reproduces what <c>EnsureCreated()</c> used to build.</summary>
        internal const string BaselineMigrationSuffix = "_InitialIdentitySchema";

        public static void BringUpToDate(AuthDbContext db, ILogger? log = null)
        {
            if (NeedsBaselining(db))
            {
                var baseline = db.Database.GetMigrations()
                    .FirstOrDefault(m => m.EndsWith(BaselineMigrationSuffix, StringComparison.Ordinal));

                if (baseline != null)
                {
                    log?.LogInformation(
                        "auth.db predates migrations; recording {Migration} as already applied.",
                        baseline);
                    MarkApplied(db, baseline);
                }
            }

            db.Database.Migrate();
        }

        /// <summary>
        /// True when the database has Identity's tables but no migrations history — i.e. it was
        /// built by the old <c>EnsureCreated()</c> path.
        /// </summary>
        internal static bool NeedsBaselining(AuthDbContext db)
        {
            if (!db.Database.CanConnect()) return false;               // brand new — Migrate creates it
            if (db.Database.GetAppliedMigrations().Any()) return false; // already on migrations

            return TableExists(db, "AspNetUsers");
        }

        private static bool TableExists(AuthDbContext db, string table)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
            var p = cmd.CreateParameter();
            p.ParameterName = "$name";
            p.Value = table;
            cmd.Parameters.Add(p);

            bool opened = false;
            if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            {
                cmd.Connection.Open();
                opened = true;
            }
            try { return Convert.ToInt64(cmd.ExecuteScalar()) > 0; }
            finally { if (opened) cmd.Connection.Close(); }
        }

        private static void MarkApplied(AuthDbContext db, string migrationId)
        {
            db.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" ("
                + "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, "
                + "\"ProductVersion\" TEXT NOT NULL);");

            db.Database.ExecuteSqlRaw(
                "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") "
                + "VALUES ({0}, {1});",
                migrationId,
                typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "10.0.0");
        }
    }
}
