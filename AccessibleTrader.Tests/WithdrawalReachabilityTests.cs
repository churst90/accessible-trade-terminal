namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Withdrawals shipped as service and provider only — correct code that no user
    /// could reach, the same family as the quick-trade executor that never placed an
    /// order. These pin the two reachability links: the API Keys modal can SET the
    /// flag that makes <c>CanWithdrawAsync</c> true, and the flag actually flows
    /// into the saved profile.
    ///
    /// <para>
    /// Behavioural enforcement (trading lookups exclude the flag, activation refuses
    /// it, the confirmation phrase) is pinned in <see cref="ApiKeyServiceTests"/> and
    /// <see cref="WithdrawalServiceTests"/>. This file only guards against the UI
    /// wiring quietly disappearing again.
    /// </para>
    /// </summary>
    public class WithdrawalReachabilityTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Read(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        [Fact]
        public void The_api_keys_modal_offers_the_withdrawal_checkbox()
        {
            string modal = Read("AccessibleTrader.BlazorClient.Components", "ApiKeysModal.razor");

            Assert.Contains("_newAllowsWithdrawal", modal);
            Assert.Contains("type=\"checkbox\"", modal);
        }

        [Fact]
        public void The_checkbox_flows_into_the_saved_profile()
        {
            // A checkbox that binds a field nobody passes to SaveKeyAsync would look
            // done and set nothing — the bool-param bug shape, on the money flag.
            string modal = Read("AccessibleTrader.BlazorClient.Components", "ApiKeysModal.razor");

            Assert.Contains("AllowsWithdrawal: _newAllowsWithdrawal", modal);
        }

        [Fact]
        public void The_checkbox_explains_why_a_trading_key_must_not_carry_this()
        {
            // The explanation is part of the control: without the WHY, the obvious
            // user "fix" is to grant withdrawal permission to the trading key.
            string modal = Read("AccessibleTrader.BlazorClient.Components", "ApiKeysModal.razor");

            Assert.Contains("never for trading", modal);
            Assert.Contains("WITHOUT trading permission", modal);
        }

        [Fact]
        public void The_withdraw_dialog_exists_and_opens_on_its_event()
        {
            string modal = Read("AccessibleTrader.BlazorClient.Components", "WithdrawModal.razor");
            string layout = Read("AccessibleTrader.BlazorClient.Components", "Layout", "MainLayout.razor");

            Assert.Contains("Subscribe<OpenWithdrawEvent>", modal);
            Assert.Contains("<WithdrawModal />", layout);
        }

        [Fact]
        public void The_dialog_has_no_free_text_address_field()
        {
            // The strongest property of the design, checked at the UI layer too:
            // destinations are chosen from the venue's whitelist in a <select>, and
            // the only address input is readonly. An editable address field here
            // would quietly discard what the SDK enforces structurally.
            string modal = Read("AccessibleTrader.BlazorClient.Components", "WithdrawModal.razor");

            Assert.Contains("withdraw-destination", modal);
            int at = modal.IndexOf("withdraw-address-field", StringComparison.Ordinal);
            Assert.True(at > 0, "The destination-address confirmation field is gone.");
            string field = modal.Substring(at, 200);
            Assert.Contains("readonly", field);
        }

        [Fact]
        public void The_typed_confirmation_and_the_spoken_readback_are_wired()
        {
            string modal = Read("AccessibleTrader.BlazorClient.Components", "WithdrawModal.razor");

            // The phrase comes from the service constant — never a second string
            // that could drift from what the service checks.
            Assert.Contains("WithdrawalService.ConfirmationPhrase", modal);
            Assert.DoesNotContain("\"WITHDRAW\"", modal);
            // The readback sentence is the service's, spoken interrupting.
            Assert.Contains("WithdrawalService.Confirmation(", modal);
            Assert.Contains("Speak(_readback, interrupt: true)", modal);
        }

        [Fact]
        public void The_amount_field_says_it_is_required_and_only_says_it_is_invalid_once_typed_in()
        {
            // The money path's own half of the 2026-09-02 error-state work. Two rules, and
            // the second is the one that is easy to get wrong: an EMPTY amount must not
            // announce "invalid entry", because the field starts empty and the user would
            // meet the refusal before typing a character. Blank is what aria-required is
            // for; aria-invalid means a value was entered and rejected.
            //
            // A source scan, in this file's idiom, because nothing renders this dialog in a
            // test — the whole withdraw surface is guarded by reading it. Recorded as a gap
            // rather than left implied.
            string modal = Read("AccessibleTrader.BlazorClient.Components", "WithdrawModal.razor");

            int at = modal.IndexOf("id=\"withdraw-amount\"", StringComparison.Ordinal);
            Assert.True(at > 0, "The amount field is gone — update this test with what replaced it.");
            string field = modal.Substring(at, 600);
            Assert.Contains("aria-required=\"true\"", field);
            Assert.Contains("aria-invalid=", field);

            // The predicate itself: typed AND unusable, never merely empty.
            Assert.Contains("AmountInvalid => _amountText.Length > 0 && Amount <= 0", modal);
        }

        [Fact]
        public void Editing_anything_voids_the_quote_and_the_typed_word()
        {
            // What was read aloud must be exactly what is sent. Each of the three
            // inputs above the confirmation must route through the invalidator.
            string modal = Read("AccessibleTrader.BlazorClient.Components", "WithdrawModal.razor");

            foreach (var handler in new[] { "OnAssetInput", "OnDestinationChanged", "OnAmountInput" })
            {
                int at = modal.IndexOf($"private void {handler}", StringComparison.Ordinal);
                Assert.True(at > 0, $"{handler} is gone — update this test with what replaced it.");
                Assert.Contains("VoidQuote()", modal.Substring(at, 250));
            }
        }

        [Fact]
        public void A_withdrawal_profile_is_named_in_the_screen_reader_row_label()
        {
            // The list row's aria-label is the only thing a screen-reader user hears
            // when reviewing profiles; a withdrawal profile that sounds identical to
            // a trading profile is indistinguishable where it matters most.
            string modal = Read("AccessibleTrader.BlazorClient.Components", "ApiKeysModal.razor");

            Assert.Contains("withdrawal profile", modal);
        }
    }
}
