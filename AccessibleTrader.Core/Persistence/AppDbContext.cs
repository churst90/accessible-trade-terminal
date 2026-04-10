using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
                var dbDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AccessibleTrader");
                if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
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
