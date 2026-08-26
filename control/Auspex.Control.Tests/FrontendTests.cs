using System.Text.RegularExpressions;

namespace Auspex.Control.Tests;

/// <summary>
/// The two things about the front end that no other test can see, because
/// both of them still answer HTTP 200.
///
/// <para>
/// Both were real, and both came out of the 0.9.0 renaming rather than out of
/// a test: <c>appearance.js</c> still called <c>anwenden(read())</c> in its
/// last line, so the script threw at startup and theme, accent, density and
/// font size all stopped working. And the stylesheet the fonts come from was
/// still called <c>schriften.css</c> on disk while the page asked for
/// <c>fonts.css</c> — a 404 that shows only as a page in the fallback font.
/// </para>
///
/// <para>
/// Both are text-level checks. JavaScript has no compiler here and Blazor
/// resolves its asset names at run time; a check that reads the files as
/// strings is the only one available short of running a browser.
/// </para>
/// </summary>
public class FrontendTests
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

    /// <summary>
    /// Strips comments and string literals. Crude on purpose — it only has to
    /// stop prose and paths from looking like code, not to parse JavaScript.
    /// A comment saying "runs in Chrome (Manifest V3)" otherwise reads as a
    /// call to something named Chrome.
    /// </summary>
    private static string WithoutTextAndComments(string js)
    {
        js = Regex.Replace(js, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        js = Regex.Replace(js, @"//.*", " ");
        js = Regex.Replace(js, @"`[^`]*`", " `` ");
        js = Regex.Replace(js, "\"[^\"\\n]*\"", " \"\" ");
        js = Regex.Replace(js, @"'[^'\n]*'", " '' ");
        return js;
    }

    /// <summary>Words that are followed by "(" without being a call.</summary>
    private static readonly HashSet<string> Keywords =
    [
        "if", "for", "while", "switch", "catch", "return", "function", "typeof",
        "new", "delete", "await", "of", "in", "do", "else", "throw", "case",
        "instanceof", "void", "yield", "async", "var", "let", "const", "class",
        "import", "export", "try", "finally",
    ];

    /// <summary>
    /// What the browser brings. Not a complete list of the platform — only
    /// what these files actually reach for. A new global belongs added here
    /// with the same deliberateness as a new dependency.
    /// </summary>
    private static readonly HashSet<string> Globals =
    [
        "document", "window", "console", "JSON", "Math", "Date", "String",
        "Number", "Boolean", "Array", "Object", "Set", "Map", "URL", "fetch",
        "setTimeout", "clearTimeout", "setInterval", "parseInt", "parseFloat",
        "isNaN", "MutationObserver", "localStorage", "Promise", "RegExp",
        "Error", "encodeURIComponent", "decodeURIComponent", "chrome",
        "browser", "alert", "getComputedStyle", "structuredClone",
        "queueMicrotask", "require",
    ];

    public static TheoryData<string> ScriptFiles()
    {
        var root = Root();
        var data = new TheoryData<string>();
        foreach (var f in Directory
                     .GetFiles(Path.Combine(root, "control", "Auspex.Control", "wwwroot"), "*.js")
                     .Concat(Directory.GetFiles(Path.Combine(root, "extension", "shared"), "*.js"))
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            data.Add(f);
        }
        return data;
    }

    /// <summary>
    /// Every function a script calls has to be defined in it, imported into
    /// it, or a global it names. A call to something that does not exist is
    /// not a syntax error — <c>node --check</c> passes it, and the browser
    /// only says so when the line is reached.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScriptFiles))]
    public void No_script_calls_a_function_that_is_not_there(string path)
    {
        var js = WithoutTextAndComments(File.ReadAllText(path));

        var defined = new HashSet<string>(StringComparer.Ordinal);

        // function name(…)  ·  const name = …  ·  var name = …
        foreach (Match m in Regex.Matches(js,
                     @"function\s+([A-Za-z_$][\w$]*)|(?:const|let|var)\s+([A-Za-z_$][\w$]*)"))
        {
            defined.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        }

        // import { a, b } from …
        foreach (Match m in Regex.Matches(js, @"import\s*\{([^}]*)\}"))
        {
            foreach (var name in m.Groups[1].Value.Split(','))
            {
                defined.Add(name.Trim());
            }
        }

        // Parameters, so a function taking a callback may call it.
        foreach (Match m in Regex.Matches(js, @"\(([^()]*)\)\s*(?:=>|\{)"))
        {
            foreach (var p in m.Groups[1].Value.Split(','))
            {
                var name = p.Split('=')[0].Trim().TrimStart('.');
                if (Regex.IsMatch(name, @"^[A-Za-z_$][\w$]*$"))
                {
                    defined.Add(name);
                }
            }
        }

        // A call: a name followed by "(", not preceded by a dot — that would
        // be a method on something else, and what that something has is not
        // this file's business.
        var called = Regex.Matches(js, @"(?<![.\w$])([A-Za-z_$][\w$]*)\s*\(")
            .Select(m => m.Groups[1].Value)
            .Where(n => !Keywords.Contains(n) && !Globals.Contains(n))
            .ToHashSet(StringComparer.Ordinal);

        var missing = called.Where(n => !defined.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            $"{Path.GetFileName(path)} calls something that is not defined there: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// Colocated scripts sit next to their component, everything else in
    /// wwwroot.
    /// </summary>
    private static bool Exists(string project, string name)
    {
        var relative = name.Replace('/', Path.DirectorySeparatorChar);
        return File.Exists(Path.Combine(project, "wwwroot", relative))
            || File.Exists(Path.Combine(project, relative));
    }

    /// <summary>
    /// Every static file the markup asks for has to exist. Blazor resolves
    /// <c>@Assets["x"]</c> at run time and, when it finds nothing, hands out
    /// the name unchanged — so the page renders, the browser fetches, and a
    /// 404 goes into a console nobody has open.
    /// </summary>
    [Fact]
    public void Every_asset_the_markup_asks_for_exists()
    {
        var root = Root();
        var project = Path.Combine(root, "control", "Auspex.Control");
        var markup = string.Concat(Directory
            .GetFiles(Path.Combine(project, "Components"), "*.razor", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        var missing = new List<string>();

        foreach (Match m in Regex.Matches(markup, @"Assets\[""([^""]+)""\]"))
        {
            var name = m.Groups[1].Value;
            // Produced by the build, not by us.
            if (name.EndsWith(".styles.css", StringComparison.Ordinal)
                || name.StartsWith("_framework/", StringComparison.Ordinal))
            {
                continue;
            }
            if (!Exists(project, name))
            {
                missing.Add(name);
            }
        }

        foreach (Match m in Regex.Matches(markup,
                     @"(?:href|src)=""/?([a-zA-Z0-9._/-]+\.(?:css|js|png|ico|webmanifest))"""))
        {
            var name = m.Groups[1].Value;
            if (name.StartsWith("_framework/", StringComparison.Ordinal))
            {
                continue;
            }
            if (!Exists(project, name))
            {
                missing.Add(name);
            }
        }

        Assert.True(missing.Count == 0,
            "The markup asks for files that do not exist: "
            + string.Join(", ", missing.Distinct()));
    }

    /// <summary>
    /// Names the framework sets, not us. Each one needs a reason, or this
    /// list becomes the place a real mismatch goes to hide.
    /// </summary>
    private static readonly HashSet<string> SetByTheFramework =
    [
        "ReturnUrl", // cookie authentication, from options.LoginPath
    ];

    /// <summary>
    /// Every query parameter a page reads has to be one something sets.
    ///
    /// <para>
    /// The sign-in page bound <c>?error</c> while the endpoint redirected to
    /// <c>?fehler</c>. A wrong password therefore bounced back to the form
    /// with nothing said — no error, no hint, just the form again. Nothing
    /// threw, nothing logged, and the page answered 200.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_query_parameter_a_page_reads_is_one_something_sets()
    {
        var root = Root();
        var project = Path.Combine(root, "control", "Auspex.Control");
        var files = Directory
            .GetFiles(Path.Combine(project, "Components"), "*.razor", SearchOption.AllDirectories)
            .Append(Path.Combine(project, "Program.cs"))
            .ToList();
        var all = string.Concat(files.Select(File.ReadAllText));

        var read = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(all, @"SupplyParameterFromQuery\(Name\s*=\s*""([^""]+)"""))
        {
            read.Add(m.Groups[1].Value);
        }
        // Two shapes: the indexer straight on the call, and the usual
        //   var q = ParseQueryString(…); q["name"]
        // A file that parses a query string at all has its indexers counted.
        foreach (var file in files.Where(f => File.ReadAllText(f).Contains("ParseQueryString")))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\[""([a-z][a-zA-Z]*)""\]"))
            {
                read.Add(m.Groups[1].Value);
            }
        }

        Assert.NotEmpty(read);
        var never = read
            .Where(p => !SetByTheFramework.Contains(p))
            .Where(p => !Regex.IsMatch(all, @"[?&]" + Regex.Escape(p) + "="))
            .ToList();

        Assert.True(never.Count == 0,
            "Pages read query parameters that nothing sets: " + string.Join(", ", never));

        // And the other way round, which is the direction the real bug went:
        // a redirect carried ?fehler= while the page bound ?error=, so
        // something was set that nobody reads. One direction alone would have
        // stayed green — ?error=stale still existed a few lines further up.
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(all, @"[""$]/[a-z/]+\?([A-Za-z]+)="))
        {
            set.Add(m.Groups[1].Value);
        }
        foreach (Match m in Regex.Matches(all, @"&([A-Za-z]+)="))
        {
            set.Add(m.Groups[1].Value);
        }

        var unread = set
            .Where(p => !SetByTheFramework.Contains(p))
            .Where(p => !read.Contains(p))
            .ToList();

        Assert.True(unread.Count == 0,
            "Something sets query parameters that no page reads: " + string.Join(", ", unread));
    }

    /// <summary>
    /// The manifests point at files that have to exist in the bundle. A
    /// missing one shows only when the browser refuses to load the extension,
    /// and its message names the manifest rather than the file.
    /// </summary>
    [Theory]
    [InlineData("chrome")]
    [InlineData("firefox")]
    public void The_manifest_points_at_files_that_are_there(string browser)
    {
        var root = Root();
        var shared = Path.Combine(root, "extension", "shared");
        var manifest = Path.Combine(root, "extension", browser, "manifest.json");
        var text = File.ReadAllText(manifest);

        var missing = Regex.Matches(text, @"""([A-Za-z0-9_./-]+\.(?:js|html|css|png))""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Where(name => !File.Exists(Path.Combine(shared, name.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        Assert.True(missing.Count == 0,
            $"{browser}/manifest.json names files that are not in shared/: "
            + string.Join(", ", missing));
    }
}
