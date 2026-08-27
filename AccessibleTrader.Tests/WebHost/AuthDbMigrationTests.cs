using AccessibleTrader.WebHost.Account;
using Microsoft.EntityFrameworkCore;

namespace AccessibleTrader.Tests.WebHost
{
    /// <summary>
    /// <b><c>auth.db</c> survives a schema change.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>Program.cs</c> called <c>EnsureCreated()</c> and the repo contained <b>no EF
    /// migrations at all</b> — <c>find</c> for <c>*Migration*</c> returned one markdown file.
    /// <c>EnsureCreated</c> is create-or-nothing: it will not alter a database that already has
    /// tables.
    /// </para>
    ///
    /// <para>
    /// So the moment anyone added a property to <c>AppUser</c> — which already carries three
    /// custom columns, <c>CreatedUtc</c>, <c>LastSeenUtc</c> and <c>Tier</c>, so this has
    /// happened before and will again — or an Identity minor version added one, the next deploy
    /// would see tables, do nothing, and every query against <c>AspNetUsers</c> would throw
    /// <c>SqliteException: no such column</c>. <b>Sign-in breaks for every existing account,
    /// with no recovery short of hand-editing SQLite or deleting the accounts.</b>
    /// </para>
    ///
    /// <para>
    /// <c>SERVER_SETUP.md</c> documents that risk for <c>trader_local.db</c> only, and its
    /// advice there — "delete it on deploy and let it rebuild — it is a cache, and nothing in
    /// it is authoritative" — is exactly the advice that <b>cannot</b> be followed for
    /// <c>auth.db</c>.
    /// </para>
    ///
    /// <para>
    /// ── The part that needed care ──────────────────────────────────────────────
    /// Every existing deployment has an <c>auth.db</c> built by <c>EnsureCreated()</c>, with
    /// Identity's tables and <b>no <c>__EFMigrationsHistory</c></b>. A plain
    /// <c>Database.Migrate()</c> on one of those tries to apply the initial migration and dies
    /// on "table AspNetUsers already exists" — so the fix for the upgrade problem would itself
    /// have broken every upgrade. <see cref="AuthDbSchema"/> baselines those first.
    /// </para>
    /// </summary>
    public class AuthDbMigrationTests : IDisposable
    {
        private readonly string _dir = TestTemp.NewDir("att-authdb-");

        private AuthDbContext NewContext(string file) =>
            new(new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_dir, file)}")
                .Options);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void The_repository_actually_contains_a_migration()
        {
            // The finding was "there are none". Without at least one, everything below passes
            // by describing an empty set.
            using var db = NewContext("probe.db");
            Assert.NotEmpty(db.Database.GetMigrations());
        }

        [Fact]
        public void A_brand_new_database_is_created_and_recorded_as_migrated()
        {
            using var db = NewContext("fresh.db");

            AuthDbSchema.BringUpToDate(db);

            Assert.NotEmpty(db.Database.GetAppliedMigrations());
            Assert.True(db.Database.CanConnect());
            // The schema is real, not just recorded.
            Assert.Empty(db.Users);
        }

        [Fact]
        public void A_database_built_by_the_old_EnsureCreated_path_is_baselined_not_recreated()
        {
            // This is the upgrade every existing deployment will perform exactly once.
            using (var legacy = NewContext("legacy.db"))
            {
                legacy.Database.EnsureCreated();                       // the old startup path
                Assert.Empty(legacy.Database.GetAppliedMigrations());  // no history table
            }

            using var db = NewContext("legacy.db");

            var ex = Record.Exception(() => AuthDbSchema.BringUpToDate(db));

            Assert.Null(ex);
            Assert.NotEmpty(db.Database.GetAppliedMigrations());
        }

        [Fact]
        public void Baselining_does_not_destroy_the_accounts_already_in_the_database()
        {
            // The whole point. An upgrade that "worked" by dropping AspNetUsers would pass
            // every other assertion here.
            using (var legacy = NewContext("withuser.db"))
            {
                legacy.Database.EnsureCreated();
                legacy.Users.Add(new AppUser
                {
                    Id = "u1",
                    UserName = "someone@example.com",
                    NormalizedUserName = "SOMEONE@EXAMPLE.COM",
                    Email = "someone@example.com",
                    NormalizedEmail = "SOMEONE@EXAMPLE.COM",
                    SecurityStamp = "stamp",
                });
                legacy.SaveChanges();
            }

            using var db = NewContext("withuser.db");
            AuthDbSchema.BringUpToDate(db);

            var user = Assert.Single(db.Users);
            Assert.Equal("someone@example.com", user.Email);
        }

        [Fact]
        public void Running_it_twice_is_a_no_op()
        {
            // Startup runs on every boot, so it has to be idempotent.
            using (var db = NewContext("twice.db")) AuthDbSchema.BringUpToDate(db);

            using var again = NewContext("twice.db");
            var ex = Record.Exception(() => AuthDbSchema.BringUpToDate(again));

            Assert.Null(ex);
        }

        [Fact]
        public void A_database_already_on_migrations_is_not_baselined_again()
        {
            using (var db = NewContext("already.db")) AuthDbSchema.BringUpToDate(db);

            using var db2 = NewContext("already.db");
            Assert.False(AuthDbSchema.NeedsBaselining(db2));
        }

        [Fact]
        public void A_database_that_does_not_exist_yet_is_not_baselined()
        {
            // Baselining a database with no tables would record the schema as present when it
            // is not — the one outcome worse than the defect, because Migrate would then skip
            // creating it and every query would fail.
            using var db = NewContext("nothere.db");
            Assert.False(AuthDbSchema.NeedsBaselining(db));
        }
    }
}
