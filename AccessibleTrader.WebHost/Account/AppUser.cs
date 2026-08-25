using Microsoft.AspNetCore.Identity;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// The hosted-multi-user account. Extends ASP.NET Core Identity's user with the
    /// account fields the product needs. Persisted in <see cref="AuthDbContext"/>
    /// (auth.db). Only used when accounts are enabled (Accounts:Enabled = true) — the
    /// local/demo WebHost modes and the MAUI head never touch this.
    /// </summary>
    public class AppUser : IdentityUser
    {
        /// <summary>When the account was created (UTC).</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Last sign-in (UTC), for housekeeping / inactive-account cleanup.</summary>
        public DateTime? LastSeenUtc { get; set; }

        /// <summary>
        /// Feature tier: "free" | "supporter" | … . Gates premium built-in indicators
        /// and history depth per docs/HOSTED_ACCOUNTS_STRATEGY.md. Defaults to free.
        /// </summary>
        public string Tier { get; set; } = "free";
    }
}
