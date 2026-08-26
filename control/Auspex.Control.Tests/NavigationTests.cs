using System.Text.RegularExpressions;

namespace Auspex.Control.Tests;

/// <summary>
/// The navigation and the pages are two sides of a contract with no compiler
/// between them: <c>MainLayout</c> writes route strings by hand, and a page
/// declares its own with <c>@page</c>. Nothing connects the two, so a typo or
/// a forgotten entry produces a menu item that leads nowhere — or a page
/// nobody can reach.
///
/// This project has been bitten by exactly that shape of gap before: a login
/// redirect with a renamed query parameter, a stylesheet renamed on one side
/// only. Both were found by hand, and both now have a test. This is the same
/// medicine for the menu.
/// </summary>
public class NavigationTests
{
    private static string Project()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, "Auspex.Control", "Components")))
        {
            here = here.Parent;
        }
        Assert.NotNull(here);
        return Path.Combine(here!.FullName, "Auspex.Control");
    }

    /// <summary>Every route a page declares, without the leading slash.</summary>
    private static HashSet<string> DeclaredRoutes()
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(
                     Path.Combine(Project(), "Components"), "*.razor", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"@page\s+""/([^""]*)"""))
            {
                routes.Add(m.Groups[1].Value);
            }
        }
        return routes;
    }

    [Fact]
    public void Every_menu_entry_leads_to_a_page_that_exists()
    {
        var layout = File.ReadAllText(
            Path.Combine(Project(), "Components", "Layout", "MainLayout.razor"));
        var routes = DeclaredRoutes();

        var dangling = new List<string>();
        foreach (Match m in Regex.Matches(layout, @"new Entry\(""([^""]+)"""))
        {
            var target = m.Groups[1].Value;
            // Sub-paths like "router/wlan" are declared by their own page;
            // anything the menu names has to be found among the routes.
            if (!routes.Contains(target))
            {
                dangling.Add(target);
            }
        }

        Assert.True(dangling.Count == 0,
            "menu entries with no page behind them: " + string.Join(", ", dangling));
    }

    /// <summary>
    /// And the guard that makes the one above worth anything: if the reading
    /// of either side silently found nothing, it would pass on an empty set.
    /// </summary>
    [Fact]
    public void Both_sides_were_actually_read()
    {
        var layout = File.ReadAllText(
            Path.Combine(Project(), "Components", "Layout", "MainLayout.razor"));

        Assert.True(Regex.Matches(layout, @"new Entry\(""([^""]+)""").Count >= 8,
            "hardly any menu entries found — the pattern probably no longer matches");
        Assert.True(DeclaredRoutes().Count >= 15,
            "hardly any page routes found — the pattern probably no longer matches");
    }

    /// <summary>
    /// The three pages built on 2026-08-26 are reachable from the menu. Named
    /// explicitly, because "the test is green" and "I can get there" turned
    /// out to be different statements: from outside, the login redirect
    /// answers every address alike, so a missing route cannot be told from a
    /// present one.
    /// </summary>
    [Theory]
    [InlineData("programs")]
    [InlineData("dossier")]
    [InlineData("findings")]
    public void The_page_is_reachable(string route)
    {
        Assert.Contains(route, DeclaredRoutes());

        var layout = File.ReadAllText(
            Path.Combine(Project(), "Components", "Layout", "MainLayout.razor"));
        Assert.Contains($"new Entry(\"{route}\"", layout, StringComparison.Ordinal);
    }
}
