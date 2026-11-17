using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests;

/// <summary>
/// The browser harness's Playwright is named in two places that must agree: the
/// <c>Microsoft.Playwright</c> package, whose driver asks for one Chromium build, and the
/// <c>npx playwright@</c> line in tests.yml that installs that build. A package moved without the
/// line installs a Chromium the driver does not look for, and the browser job fails at its first
/// launch with nothing pointing at the version. The hint the harness prints when Chromium is
/// missing names the same version, and so does SERVER_SETUP.md.
/// </summary>
public sealed class PlaywrightVersionPinTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    [Fact]
    public void ThePackageAndEveryInstallLineNameTheSameVersion()
    {
        var package = Regex.Match(Read("AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj"),
            @"<PackageReference Include=""Microsoft\.Playwright"" Version=""([0-9.]+)""");
        Assert.True(package.Success, "No Microsoft.Playwright PackageReference in the browser harness");
        string version = package.Groups[1].Value;

        foreach (var file in new[] { ".github/workflows/tests.yml", "AccessibleTrader.BrowserTests/BrowserAvailability.cs", "docs/SERVER_SETUP.md" })
        {
            var installs = Regex.Matches(Read(file), @"playwright@([0-9.]+)").Select(m => m.Groups[1].Value).ToList();
            Assert.True(installs.Count > 0, $"{file} no longer names a playwright@ version");
            Assert.All(installs, v => Assert.True(v == version,
                $"{file} installs playwright@{v}, and the package is Microsoft.Playwright {version}"));
        }
    }
}
