using System;
using System.IO;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The address field's accessibility contract, pinned as markup.
    ///
    /// <para>
    /// This is the screen where being wrong costs money directly, and most of what
    /// makes it safe is invisible to a behavioural test: whether the field is
    /// reachable by keyboard, whether it is read-only rather than disabled, whether
    /// a memo is given the same treatment as the address. Those are properties of
    /// the rendered element, so they are checked as such.
    /// </para>
    /// </summary>
    public class WalletModalMarkupTests
    {
        private static string Modal()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string path = Path.Combine(dir!.FullName, "AccessibleTrader.BlazorClient.Components", "WalletModal.razor");
            Assert.True(File.Exists(path), $"WalletModal.razor not found at {path}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void The_address_is_a_readonly_field_not_a_disabled_one()
        {
            // A disabled input is skipped by keyboard navigation entirely, which
            // would put the address out of reach of the screen reader's review
            // cursor — the one channel that can spell it out reliably.
            string s = Modal();

            Assert.Contains("id=\"wallet-address-field\"", s);
            Assert.Contains("readonly", s);
            Assert.DoesNotContain("id=\"wallet-address-field\" type=\"text\" disabled", s);
        }

        [Fact]
        public void The_address_field_has_a_copy_button()
        {
            Assert.Contains("Copy the deposit address to the clipboard", Modal());
        }

        [Fact]
        public void The_address_is_never_truncated_or_chunked_in_the_markup()
        {
            // Chunked presentation was explicitly rejected: addresses get written
            // down and compared by hand, so the field must hold the whole string.
            string s = Modal();

            Assert.Contains("value=\"@_checked.Address.Address\"", s);
            Assert.DoesNotContain("Substring", s);
            Assert.DoesNotContain("text-overflow: ellipsis", s);
        }

        [Fact]
        public void A_memo_gets_its_own_field_and_copy_button()
        {
            // Omitting a destination tag loses the funds as completely as a wrong
            // address, so it is not a footnote next to the address.
            string s = Modal();

            Assert.Contains("id=\"wallet-memo-field\"", s);
            Assert.Contains("Copy the memo to the clipboard", s);
            Assert.Contains("lost</strong> without the", s);
        }

        [Fact]
        public void There_is_a_character_by_character_read_that_names_capitals()
        {
            // For anyone without a review cursor or braille display. Speech drops
            // case, and b and B are different addresses.
            string s = Modal();

            Assert.Contains("ReadCharacters", s);
            Assert.Contains("capital", s);
        }

        [Fact]
        public void The_network_is_chosen_before_an_address_is_fetched_and_has_no_default()
        {
            // Picking a network for the user is picking which chain their money goes
            // to. The Get-address button stays disabled until they choose.
            string s = Modal();

            Assert.Contains("— choose a network —", s);
            Assert.Contains("disabled=\"@(string.IsNullOrEmpty(_network))\"", s);
        }

        [Fact]
        public void Nothing_is_cached_between_opens()
        {
            // Address substitution needs a stored copy to substitute. Several venues
            // also rotate addresses per request, so a cached one is wrong as well as
            // unsafe.
            string s = Modal();

            Assert.Contains("_checked = null;", s);
            Assert.DoesNotContain("localStorage", s);
            Assert.DoesNotContain("SaveSetting", s);
        }

        [Fact]
        public void A_failed_copy_says_so_rather_than_appearing_to_work()
        {
            // A silent copy failure is the worst outcome available here: the user
            // pastes whatever was on the clipboard beforehand.
            Assert.Contains("could NOT be copied", Modal());
        }
    }
}
