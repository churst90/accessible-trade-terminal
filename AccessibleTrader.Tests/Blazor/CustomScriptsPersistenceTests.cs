using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Bunit;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The Custom Scripts dialog's Save button did not save.
///
/// <para><c>_scripts</c> was a plain <c>List&lt;CustomScript&gt;</c> field and the component
/// referenced no store of any kind; <c>ICustomScriptService</c>, the interface that declares
/// <c>SaveScripts()</c>, has no implementation and no DI registration anywhere in the tree. The
/// button compiled, said nothing, and the work died with the process (surveyed 2026-09-04,
/// read-verified; these tests are the demonstration).</para>
///
/// <para>It is the mirror image of the defect Cody reported in the same message — a Settings
/// dialog whose "Close" button silently SAVED. Both are the same rule broken from opposite
/// sides: a control does what its label says, and nothing else does it behind your back.</para>
/// </summary>
public class CustomScriptsPersistenceTests
{
    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.CustomScriptsModal>
        Open(BlazorTestHarness h) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.CustomScriptsModal>(
            bus => bus.Publish(new OpenCustomScriptsEvent()));

    [Fact]
    public void CreatingAScript_WritesItToSettings()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h);

        cut.InvokeAsync(() => cut.Find("button[aria-label='New script']").Click()).GetAwaiter().GetResult();

        // The name of the key matters less than that SOMETHING was written and then flushed:
        // a SetSetting with no SaveSettings behind it is the same lost work one layer down.
        h.SettingsManager.Received().SetSetting("scripts.custom", Arg.Any<JToken>());
        h.SettingsManager.Received().SaveSettings();
    }

    [Fact]
    public void ScriptsWrittenLastSession_AreListedOnOpen()
    {
        // The other half. A dialog that writes and never reads is indistinguishable from one
        // that does neither, from the only seat that matters — the user's.
        using var h = new BlazorTestHarness();
        h.SettingsManager.GetSetting("scripts.custom", Arg.Any<JToken?>()).Returns(
            JToken.FromObject(new List<CustomScript>
            {
                new("id-1", "Volume spike", "// code", false),
            }));

        var cut = Open(h);

        Assert.Contains("Volume spike", cut.Markup);
    }

    [Fact]
    public void AnUnreadableScriptsSetting_OpensEmptyRatherThanThrowing()
    {
        // A settings file hand-edited into something that will not deserialise must not take the
        // dialog with it: an empty list is recoverable, an exception on open is not.
        using var h = new BlazorTestHarness();
        h.SettingsManager.GetSetting("scripts.custom", Arg.Any<JToken?>())
                         .Returns(JToken.FromObject("not a list"));

        var cut = Open(h);

        Assert.NotEmpty(cut.FindAll("[role='dialog']"));
    }
}
