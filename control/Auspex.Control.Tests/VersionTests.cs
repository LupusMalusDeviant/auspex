using System.Text.RegularExpressions;

namespace Auspex.Control.Tests;

/// <summary>
/// The version stands in four places and has to be the same everywhere.
///
/// <para>
/// Resolver, browser extension, control plane and sensor each carry it
/// separately - that cannot be avoided, because four toolchains read it out
/// of four different files. What can be avoided is their drifting apart: the
/// VERSION file in the root directory is the source, and this test is the
/// assurance.
/// </para>
///
/// <para>
/// Without it the overview page eventually shows a version that exists
/// nowhere - and to the question "which state is actually running there?"
/// that is the wrong answer.
/// </para>
/// </summary>
public class VersionTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VERSION")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    public static TheoryData<string, string> Places()
    {
        var root = Root();
        return new TheoryData<string, string>
        {
            { Path.Combine(root, "auspex", "cmd", "auspex", "main.go"), @"var version = ""([^""]+)""" },
            { Path.Combine(root, "extension", "chrome", "manifest.json"), @"""version"":\s*""([^""]+)""" },
            { Path.Combine(root, "extension", "firefox", "manifest.json"), @"""version"":\s*""([^""]+)""" },
            { Path.Combine(root, "control", "Auspex.Control", "Auspex.Control.csproj"), @"<Version>([^<]+)</Version>" },
            { Path.Combine(root, "sensor", "Auspex.Sensor", "Auspex.Sensor.csproj"), @"<Version>([^<]+)</Version>" },
        };
    }

    [Theory]
    [MemberData(nameof(Places))]
    public void The_same_version_everywhere(string path, string pattern)
    {
        var expected = File.ReadAllText(Path.Combine(Root(), "VERSION")).Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+$", expected);

        Assert.True(File.Exists(path), $"does not exist: {path}");
        var m = Regex.Match(File.ReadAllText(path), pattern);
        Assert.True(m.Success, $"no version found in {path}");
        Assert.Equal(expected, m.Groups[1].Value);
    }
}
