using System.Reflection;
using System.Text.Json;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The build says what it can do, and it cannot leave anything out.</b>
///
/// <para>
/// Two recurring items from <c>patches/HOSTED-DEPLOY-NOTES.md</c>, with one cause. The public
/// feature page has gone false five times because a sentence about the app has no way to ask the
/// build; and a <c>HostMode</c> gate lands with no runtime signal, so the only evidence that the
/// hosted alert monitor had stopped was a journal grep finding NOTHING — an absence, the weakest
/// evidence there is.
/// </para>
///
/// <para>
/// <see cref="DemoPolicy"/> already is the manifest. These pin that reading it is complete: a
/// capability added to the policy is in the startup line and in the JSON without anyone
/// remembering to add it, because a hand-maintained list is a second place every flag has to be
/// added and this repo has found that defect on four separate occasions.
/// </para>
/// </summary>
public sealed class CapabilityManifestTests
{
    [Theory]
    [InlineData(HostMode.Full)]
    [InlineData(HostMode.Hosted)]
    [InlineData(HostMode.Demo)]
    public void TheManifestCarriesEveryBooleanThePolicyDeclares(HostMode mode)
    {
        var policy = new DemoPolicy(mode);
        var declared = typeof(DemoPolicy)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var manifest = CapabilityManifest.For(policy);

        Assert.Equal(declared.OrderBy(n => n), manifest.Keys.OrderBy(n => n));
        Assert.True(manifest.Count >= 15, $"only {manifest.Count} capabilities found — reflection is not reaching the policy");
    }

    /// <summary>
    /// Each flag reports what the policy actually answers. A manifest that is complete and wrong
    /// is worse than no manifest, because it would be believed.
    /// </summary>
    [Theory]
    [InlineData(HostMode.Full)]
    [InlineData(HostMode.Hosted)]
    [InlineData(HostMode.Demo)]
    public void EveryFlagReportsWhatThePolicyAnswers(HostMode mode)
    {
        var policy = new DemoPolicy(mode);
        foreach (var (name, value) in CapabilityManifest.For(policy))
        {
            var prop = typeof(DemoPolicy).GetProperty(name)!;
            Assert.Equal((bool)prop.GetValue(policy)!, value);
        }
    }

    /// <summary>
    /// The startup line names the mode and every flag, and says "off" out loud. A gate that turns
    /// something OFF is precisely the case the line exists for — the hosted head's background
    /// alerts — and a line that only listed what is enabled would leave that as an absence again.
    /// </summary>
    [Fact]
    public void TheStartupLine_SaysWhatIsOff_NotOnlyWhatIsOn()
    {
        string line = CapabilityManifest.StartupLine(new DemoPolicy(HostMode.Hosted));

        Assert.Contains("mode=Hosted", line);
        Assert.Contains("AllowBackgroundAlerts=off", line);
        Assert.Contains("AllowTrading=on", line);
        foreach (var name in CapabilityManifest.For(new DemoPolicy(HostMode.Hosted)).Keys)
            Assert.Contains(name + "=", line);
    }

    /// <summary>
    /// The three modes produce three different lines. Identical lines would mean the manifest is
    /// reading something that is not the policy.
    /// </summary>
    [Fact]
    public void TheThreeModesAreDistinguishable()
    {
        var lines = new[] { HostMode.Full, HostMode.Hosted, HostMode.Demo }
            .Select(m => CapabilityManifest.StartupLine(new DemoPolicy(m))).ToList();
        Assert.Equal(3, lines.Distinct().Count());
    }

    /// <summary>
    /// The JSON form is what a check outside the process reads — comparing a release's public
    /// claims against what the tag actually allows.
    /// </summary>
    [Fact]
    public void TheJsonManifest_CarriesTheVersionTheModeAndTheFlags()
    {
        using var doc = JsonDocument.Parse(CapabilityManifest.ToJson(new DemoPolicy(HostMode.Hosted), "2.9.0+abc1234"));
        var root = doc.RootElement;

        Assert.Equal("2.9.0+abc1234", root.GetProperty("version").GetString());
        Assert.Equal("Hosted", root.GetProperty("mode").GetString());
        Assert.False(root.GetProperty("capabilities").GetProperty("AllowBackgroundAlerts").GetBoolean());
        Assert.True(root.GetProperty("capabilities").GetProperty("AllowAlerts").GetBoolean());
    }

    /// <summary>
    /// The sentence the hosted site is about to get wrong, as an assertion. "Close the browser and
    /// your alerts keep running" is true on the desktop and false on the hosted head since
    /// 2026-09-11, and this is the fact a public claim has to be checked against.
    /// </summary>
    [Fact]
    public void AlertsWithTheBrowserClosed_AreADesktopCapabilityOnly()
    {
        Assert.True(CapabilityManifest.For(new DemoPolicy(HostMode.Full))["AllowBackgroundAlerts"]);
        Assert.False(CapabilityManifest.For(new DemoPolicy(HostMode.Hosted))["AllowBackgroundAlerts"]);
        Assert.False(CapabilityManifest.For(new DemoPolicy(HostMode.Demo))["AllowBackgroundAlerts"]);
    }
}
