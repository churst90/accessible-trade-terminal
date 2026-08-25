using System.Reflection;
using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The wallet probe's key-file reader. Hand-rolled parsers are where edge cases
    /// hide, and this one reads files a human types by hand under no schema at all —
    /// so it is tested against the shapes people actually produce.
    /// </summary>
    public class WalletProbeKeyFileTests
    {
        private static (string? Key, string Secret) Load(string contents)
        {
            string path = Path.Combine(Path.GetTempPath(), "att-keyfile-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, contents);
            try
            {
                var m = typeof(WalletProbeCommand).GetMethod("LoadKey",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(m);
                return ((string?, string))m!.Invoke(null, new object?[] { path, "Kraken" })!;
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [Fact]
        public void Reads_the_labelled_form()
        {
            var (key, secret) = Load("key: ABC123\nprivate key: c2VjcmV0\n");

            Assert.Equal("ABC123", key);
            Assert.Equal("c2VjcmV0", secret);
        }

        [Theory]
        [InlineData("secret")]
        [InlineData("api secret")]
        [InlineData("apisecret")]
        [InlineData("private key")]
        public void Accepts_the_several_names_people_give_the_secret(string label)
        {
            var (_, secret) = Load($"key: ABC123\n{label}: c2VjcmV0\n");

            Assert.Equal("c2VjcmV0", secret);
        }

        [Fact]
        public void Ignores_comments_and_blank_lines()
        {
            // Every other key file in patches/ carries a comment header.
            var (key, secret) = Load("# Kraken — testing only, will be cycled\n\nkey: ABC123\n\nsecret: c2VjcmV0\n");

            Assert.Equal("ABC123", key);
            Assert.Equal("c2VjcmV0", secret);
        }

        [Fact]
        public void A_base64_secret_containing_padding_and_slashes_survives()
        {
            // Kraken's private key is base64: +, / and = are all legal in it. Only a
            // colon would break the label split, and base64 has no colon.
            const string s = "abc+def/ghi==";
            var (_, secret) = Load($"key: K\nprivate key: {s}\n");

            Assert.Equal(s, secret);
        }

        [Fact]
        public void A_lone_bare_value_is_taken_as_the_key()
        {
            // How the file looked when it first arrived: the public key alone, no
            // labels. Reading it as the key is what let the probe say "no secret"
            // rather than failing with something unhelpful about the format.
            var (key, secret) = Load("kGtpyGaSp8V\n");

            Assert.Equal("kGtpyGaSp8V", key);
            Assert.Equal("", secret);
        }

        [Fact]
        public void A_missing_file_reports_no_key_rather_than_throwing()
        {
            var m = typeof(WalletProbeCommand).GetMethod("LoadKey",
                BindingFlags.NonPublic | BindingFlags.Static);
            var (key, _) = ((string?, string))m!.Invoke(null,
                new object?[] { "/nonexistent/nope.txt", "Kraken" })!;

            Assert.Null(key);
        }
    }
}
