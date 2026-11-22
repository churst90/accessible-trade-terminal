using System.Globalization;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// Indicator codes are matched by casing them first. Under a Turkish culture the current-culture
/// <c>ToLower</c> turns "CIPHER_B" into "cıpher_b" (dotless i) and <c>ToUpper</c> turns "cipher_a"
/// into "CİPHER_A", so the match missed and the indicator landed in the wrong pane and category.
/// </summary>
public sealed class PaneAssignmentCultureTests
{
    [Theory]
    [InlineData("CIPHER_B")]
    [InlineData("cipher_a")]
    [InlineData("RSI")]
    public void A_Turkish_culture_assigns_the_same_pane_and_category(string code)
    {
        var service = new PaneAssignmentService();
        var saved = CultureInfo.CurrentCulture;
        string invariantPane, invariantCategory;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantPane = service.GetPane(code);
            invariantCategory = service.GetCategory(code);
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal(invariantPane, service.GetPane(code));
            Assert.Equal(invariantCategory, service.GetCategory(code));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
}
