using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.StrategyLab
{
    /// <summary>
    /// <c>StrategyLab wallet-probe</c> — the LIVE half of the capability work, for
    /// the wallet path. Connects with a real read-only key and reports what the
    /// account actually returns.
    ///
    /// <para>
    /// The static audit answers "does our code do what our code claims". It cannot
    /// answer "does this venue accept USDC on Solana" or "is THIS account allowed
    /// to read deposit addresses", because eligibility is decided per customer.
    /// Only a real call knows, which is what this is for.
    /// </para>
    ///
    /// <para>
    /// **Read-only by construction.** It calls deposit-address and deposit-history
    /// endpoints and nothing else — no order path exists in this file. Addresses
    /// are masked in the output by default: they are public by design, but they
    /// tie to an identity, and a probe's log is a bad place for one. Pass
    /// <c>--show-address</c> when you actually need to compare it.
    /// </para>
    /// </summary>
    public static class WalletProbeCommand
    {
        public static async Task<int> RunAsync(string[] args)
        {
            string provider = GetFlag(args, "--provider") ?? "Kraken";
            string asset    = GetFlag(args, "--asset")    ?? "BTC";
            string? keyFile = GetFlag(args, "--keyfile");
            bool showAddress = args.Contains("--show-address");

            var (key, secret) = LoadKey(keyFile, provider);
            if (key is null)
            {
                Console.Error.WriteLine(
                    $"No credentials found. Put them in patches/{provider.ToLowerInvariant()} api key.txt as\n"
                  + "  key: <public key>\n  private key: <secret>\n"
                  + "or pass --keyfile <path>.");
                return 2;
            }

            IWalletProvider wallet = provider.ToLowerInvariant() switch
            {
                "kraken" => Configure(new AccessibleTrader.Plugins.Kraken.KrakenProvider(), key, secret),
                _ => throw new NotSupportedException(
                    $"{provider} has no IWalletProvider implementation yet. Implemented: Kraken."),
            };

            Console.WriteLine($"Wallet probe — {provider}, asset {asset}");
            Console.WriteLine(new string('─', 60));

            // Prove the CREDENTIAL before probing the wallet, so a funding-side
            // refusal is never mistaken for a broken key or broken signing. These
            // are different problems with different fixes — one is ours, the other
            // is a verification step at the venue — and the whole point of this
            // work is that they must not read the same.
            if (wallet is IMarketDataProvider mdp)
            {
                var (valid, message) = await mdp.ValidateApiKeyAsync();
                Console.WriteLine($"credential : {(valid ? "OK" : "REJECTED")} — {message}");
                Console.WriteLine();
                if (!valid) return 1;
            }

            // 1. Networks. This is also the permission check: a key without Funding
            //    rights fails here, and the message says so rather than returning an
            //    empty list that reads as "this asset cannot be deposited".
            IReadOnlyList<string> networks;
            try
            {
                networks = await wallet.GetDepositNetworksAsync(asset);
                Console.WriteLine($"Deposit networks ({networks.Count}):");
                foreach (string n in networks) Console.WriteLine($"  • {n}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"REFUSED: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAILED: {ex.Message}");
                return 1;
            }

            if (networks.Count == 0)
            {
                Console.WriteLine($"\n{provider} does not accept {asset} deposits. That is an answer, not a fault.");
                return 0;
            }

            // 2. An address on each network, validated exactly as the UI would.
            Console.WriteLine();
            foreach (string network in networks)
            {
                try
                {
                    var addr = await wallet.GetDepositAddressAsync(asset, network);
                    var check = CryptoAddressValidator.Validate(addr.Address, addr.Network);

                    Console.WriteLine($"[{network}]");
                    Console.WriteLine($"  address    : {(showAddress ? addr.Address : Mask(addr.Address))}");
                    Console.WriteLine($"  length     : {addr.Address.Length}");
                    Console.WriteLine($"  validation : {check.Result} — {check.Detail}");
                    Console.WriteLine($"  memo       : {(addr.Memo is null ? "(none returned)" : $"{addr.MemoLabel ?? "memo"} = {addr.Memo}")}");
                    if (!check.IsDisplayable)
                        Console.WriteLine("  ** THE UI WOULD REFUSE TO SHOW THIS **");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{network}] failed: {ex.Message}");
                }
                Console.WriteLine();
            }

            // 3. History — the "did my deposit arrive?" loop.
            try
            {
                var deposits = await wallet.GetDepositsAsync(asset, 5);
                Console.WriteLine($"Recent {asset} deposits: {deposits.Count}");
                foreach (var d in deposits.Take(5))
                    Console.WriteLine($"  {d.SeenAtUtc:u}  {d.Amount} {d.Asset} via {d.Network} — {d.Status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Deposit history unavailable: {ex.Message}");
            }

            return 0;
        }

        /// <summary>First and last six characters only — enough to compare against
        /// something written down, without putting the whole thing in a log.</summary>
        private static string Mask(string a) =>
            a.Length <= 16 ? a : $"{a[..6]}…{a[^6..]}";

        private static IWalletProvider Configure(
            AccessibleTrader.Plugins.Kraken.KrakenProvider p, string key, string secret)
        {
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = key, ["ApiSecret"] = secret });
            return p;
        }

        /// <summary>
        /// Reads `key:` and `private key:` (or `secret:`) lines, ignoring comments
        /// and blanks. Tolerant of layout because these files are hand-written.
        /// </summary>
        private static (string? Key, string Secret) LoadKey(string? path, string provider)
        {
            path ??= Path.Combine(FindRepoRoot(), "patches", $"{provider.ToLowerInvariant()} api key.txt");
            if (!File.Exists(path)) return (null, "");

            string? key = null, secret = null;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                int colon = line.IndexOf(':');
                if (colon <= 0) { key ??= line; continue; }   // bare value, first one wins

                string label = line[..colon].Trim().ToLowerInvariant();
                string value = line[(colon + 1)..].Trim();
                if (label is "key" or "api key" or "apikey") key = value;
                else if (label is "private key" or "secret" or "api secret" or "apisecret") secret = value;
            }
            return (key, secret ?? "");
        }

        private static string? GetFlag(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
