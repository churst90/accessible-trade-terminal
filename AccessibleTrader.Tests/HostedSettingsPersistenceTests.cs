using AccessibleTrader.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// "Are F12 preferences saved when I log into /terminal, use the terminal, and then log out?"
///
/// <para>
/// Three things have to hold and each is a separate failure: the settings document must be
/// written at all (the anonymous demo deliberately never writes one), it must be written under
/// the signed-in account's own directory rather than a shared one, and it must still be there
/// for a fresh <see cref="SettingsManager"/> — which is what the next login gets, since the
/// service is per-circuit.
/// </para>
/// </summary>
public sealed class HostedSettingsPersistenceTests
{
    private static SettingsManager Make(IPlatformPathService paths, HostMode mode) =>
        new(paths, NullLogger<SettingsManager>.Instance, new DemoPolicy(mode));

    [Fact]
    public void HostedSettingsSurviveLogoutAndLogIn()
    {
        var paths = new TempWorkspacePaths();   // one account's directory

        // Session one: change something in the F12 dialog and let it save.
        var first = Make(paths, HostMode.Hosted);
        first.SetSetting("audio.masterVolume", JToken.FromObject(0.42));
        first.SaveSettings();

        // Session two: a brand-new instance against the same account directory, which is what a
        // later login resolves to.
        var second = Make(paths, HostMode.Hosted);

        Assert.Equal(0.42, second.GetSetting("audio.masterVolume")!.ToObject<double>(), 3);
        Assert.True(File.Exists(Path.Combine(paths.AppDataDirectory, "settings.json")));
    }

    [Fact]
    public void TwoHostedAccountsDoNotShareSettings()
    {
        var alice = new TempWorkspacePaths();
        var bob = new TempWorkspacePaths();

        var a = Make(alice, HostMode.Hosted);
        a.SetSetting("audio.masterVolume", JToken.FromObject(0.1));
        a.SaveSettings();

        var b = Make(bob, HostMode.Hosted);
        b.SetSetting("audio.masterVolume", JToken.FromObject(0.9));
        b.SaveSettings();

        Assert.Equal(0.1, Make(alice, HostMode.Hosted).GetSetting("audio.masterVolume")!.ToObject<double>(), 3);
        Assert.Equal(0.9, Make(bob, HostMode.Hosted).GetSetting("audio.masterVolume")!.ToObject<double>(), 3);
    }

    /// <summary>
    /// The anonymous demo still must not write: visitors share one process, and one person's
    /// preferences becoming everyone's is the reason that gate exists.
    /// </summary>
    [Fact]
    public void TheAnonymousDemoStillPersistsNothing()
    {
        var paths = new TempWorkspacePaths();

        var demo = Make(paths, HostMode.Demo);
        demo.SetSetting("audio.masterVolume", JToken.FromObject(0.42));
        demo.SaveSettings();

        Assert.False(File.Exists(Path.Combine(paths.AppDataDirectory, "settings.json")));
    }
}
