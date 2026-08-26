using System.IO.Compression;

namespace Auspex.Control.Services.Extension;

/// <summary>
/// Packs the browser extension into an archive on request.
///
/// Until now you had to get to the project directory and run <c>build.sh</c>
/// there — on a machine that has the repository. Whoever operates the
/// dashboard from a different device could not reach the extension at all.
///
/// It is packed from the sources, not from a bundled archive. A checked-in
/// zip would be a build artefact in the repository and would drift apart
/// from the source until somebody noticed. What comes out here is by
/// construction as current as the application itself.
/// </summary>
public sealed class ExtensionPackage(ILogger<ExtensionPackage> log)
{
    /// <summary>The two versions <c>build.sh</c> produces as well.</summary>
    public static readonly string[] Browser = ["chrome", "firefox"];

    private string Root => Path.Combine(AppContext.BaseDirectory, "extension");

    /// <summary>
    /// Whether packing is possible at all. If the sources are missing —
    /// because the image was built without them, say — the interface should
    /// say so and not offer a button that leads nowhere.
    /// </summary>
    public bool Available =>
        Directory.Exists(Path.Combine(Root, "shared"))
        && Browser.All(b => File.Exists(Path.Combine(Root, b, "manifest.json")));

    /// <summary>
    /// Version from the manifest, for the file name. A file called
    /// <c>auspex-chrome.zip</c> in the downloads folder says nothing after
    /// three months about which version is inside it.
    /// </summary>
    public string? Version()
    {
        try
        {
            var path = Path.Combine(Root, "chrome", "manifest.json");
            using var s = File.OpenRead(path);
            using var doc = System.Text.Json.JsonDocument.Parse(s);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "The extension version cannot be read");
            return null;
        }
    }

    /// <summary>
    /// Packs one version. The same assembly as <c>build.sh</c>: everything
    /// from <c>shared/</c> plus the respective browser's manifest.
    /// </summary>
    /// <returns>The archive, or null when the sources are missing.</returns>
    public byte[]? Pack(string browser)
    {
        if (!Browser.Contains(browser, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var shared = Path.Combine(Root, "shared");
        var manifest = Path.Combine(Root, browser.ToLowerInvariant(), "manifest.json");
        if (!Directory.Exists(shared) || !File.Exists(manifest))
        {
            log.LogWarning("The extension sources are missing under {Path}", Root);
            return null;
        }

        using var store = new MemoryStream();
        // Deliberately into memory and not onto disk: the whole thing is a
        // good thirty kilobytes, and somebody would have to clear a temporary
        // file away again.
        using (var archive = new ZipArchive(store, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Recursive: shared/ now contains icons/, and a flat
            // EnumerateFiles would have silently left those out - the
            // extension would have landed in the browser without an icon.
            foreach (var file in Directory.EnumerateFiles(shared, "*", SearchOption.AllDirectories))
            {
                // Paths in the archive always with a forward slash, even when
                // Windows supplies them with a backslash. Through the
                // platform's separator rather than a literal character: that
                // way no backslash sits in the source to be mangled by the
                // next round of edits.
                var relative = Path.GetRelativePath(shared, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                Add(archive, file, relative);
            }

            // Last, so a manifest of the same name from shared/ - should one
            // ever land there - does not displace the browser-specific one.
            Add(archive, manifest, "manifest.json");
        }

        return store.ToArray();
    }

    private static void Add(ZipArchive archive, string source, string name)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var destination = entry.Open();
        using var read = File.OpenRead(source);
        read.CopyTo(destination);
    }
}
