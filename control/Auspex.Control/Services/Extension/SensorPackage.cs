using System.IO.Compression;
using System.Text;

namespace Auspex.Control.Services.Extension;

/// <summary>
/// Packs the sensor into an archive to download and run.
///
/// <para>
/// Unlike the extension the sensor is not a bundle of source files but an
/// executable. It cannot be produced at runtime — the runtime image has no
/// SDK. It is therefore built while the image is built and merely sits ready
/// here.
/// </para>
///
/// <para>
/// What is produced here <em>at runtime</em> is the <c>sensor.json</c>: it
/// carries the address of this dashboard, exactly as the browser just called
/// it. Whoever fetches the package does not have to type it out — and does
/// not mistype the one detail without which nothing works.
/// </para>
///
/// <para>
/// <strong>The token deliberately does not travel in the archive.</strong>
/// It would be more convenient and would turn a file in the downloads folder
/// into a key to the dashboard — one that stays lying there and travels
/// along when somebody passes the folder on. The setup script asks for it.
/// </para>
/// </summary>
public sealed class SensorPackage(ILogger<SensorPackage> log)
{
    private string Root => Path.Combine(AppContext.BaseDirectory, "sensor");
    private string Program => Path.Combine(Root, "auspex-sensor.exe");

    /// <summary>
    /// Whether there is anything to deliver at all. If the executable is
    /// missing — because the image was built without it, say — the interface
    /// should say so and not offer a button that leads nowhere.
    /// </summary>
    public bool Available => File.Exists(Program);

    /// <summary>How large the executable is, for display.</summary>
    public long Size => Available ? new FileInfo(Program).Length : 0;

    /// <summary>
    /// Packs the archive.
    /// </summary>
    /// <param name="baseUrl">
    /// The address this dashboard is reachable at — taken from the request,
    /// not from the configuration: whoever calls the dashboard by a name
    /// should get the name and not an address that may not even be right
    /// from their machine.
    /// </param>
    public byte[]? Pack(string basis)
    {
        if (!Available)
        {
            return null;
        }

        try
        {
            using var store = new MemoryStream();
            using (var archive = new ZipArchive(store, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.EnumerateFiles(Root))
                {
                    archive.CreateEntryFromFile(file, Path.GetFileName(file));
                }

                // The address, without the token. Whoever sees the file sees where
                // reports go - and nothing they could report with.
                var entry = archive.CreateEntry("sensor.json");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write($$"""
                    {
                      "basis": "{{basis}}",
                      "zeichen": ""
                    }
                    """);
            }

            return store.ToArray();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "The sensor package could not be built");
            return null;
        }
    }

    /// <summary>
    /// The address the calling browser reached this dashboard at.
    ///
    /// <para>
    /// From the request and not from the configuration: the dashboard does
    /// not know which name it is reachable under — it can sit behind a proxy,
    /// run under several names or be known only on the home network. The
    /// browser knows, because it has just done it.
    /// </para>
    /// </summary>
    public static string BaseFrom(HttpRequest query) =>
        $"{query.Scheme}://{query.Host}".TrimEnd('/');
}
