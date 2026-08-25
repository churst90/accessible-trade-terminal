using Microsoft.EntityFrameworkCore;

namespace AccessibleTrader.Core.Persistence
{
    public class OhlcvEntity
    {
        public string Market { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public long Timestamp { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double Volume { get; set; }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<OhlcvEntity> OhlcvData { get; set; }

        public AppDbContext() { }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // PlatformPaths, not GetFolderPath: the latter returns an empty string on Unix
                // when the target does not exist, which would put trader_local.db in whatever
                // directory the process happens to be running from.
                var dbDir = AccessibleTrader.Core.Services.PlatformPaths.AppDataRoot();
                var dbPath = Path.Combine(dbDir, "trader_local.db");
                optionsBuilder.UseSqlite($"Data Source={dbPath}");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OhlcvEntity>()
                .HasKey(e => new { e.Market, e.Provider, e.Symbol, e.Timeframe, e.Timestamp });
            
            modelBuilder.Entity<OhlcvEntity>()
                .HasIndex(e => new { e.Market, e.Provider, e.Symbol, e.Timeframe, e.Timestamp });
        }
    }
}
