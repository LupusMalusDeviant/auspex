namespace Auspex.Sensor;

/// <summary>
/// Writes what the sensor says somewhere a person can read it too.
///
/// <para>
/// As a scheduled task the sensor runs under SYSTEM with no window. Its
/// console does not exist then — and with it the line saying why the byte
/// counters stay empty. That is no small thing: the cause of an empty
/// counter was once written out properly on exactly that line, in a place
/// nobody could reach. A service without a log is a service you can only
/// guess at.
/// </para>
///
/// <para>
/// The file is <em>overwritten</em> on every start. It should show the
/// current state, not the history — and without a cleanup routine something
/// otherwise grows quietly that nobody asked for. If it gets too large
/// while running, it starts over.
/// </para>
///
/// <para>
/// The token is not in it, and the address only as an address. Passing this
/// log on does not pass on access.
/// </para>
/// </summary>
public sealed class Journal
{
    private const long MaxBytes = 200 * 1024;

    private readonly string? _file;

    public Journal(string? file)
    {
        _file = file;

        if (_file is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(_file, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No log is a loss, but not a reason to stop the sensor from
            // running.
            _file = null;
        }
    }

    /// <summary>Next to the executable, because the settings live there too.</summary>
    public static Journal NextToProgram() =>
        new(Path.Combine(AppContext.BaseDirectory, "auspex-sensor.log"));

    /// <summary>To the console and, if there is one, to the file.</summary>
    public void Say(string line)
    {
        Console.WriteLine(line);
        Write(line);
    }

    /// <summary>To the file only — for everything the console already has.</summary>
    public void Write(string line)
    {
        if (_file is null)
        {
            return;
        }

        try
        {
            if (new FileInfo(_file) is { Exists: true, Length: > MaxBytes })
            {
                File.WriteAllText(_file, $"— gekürzt, das Protokoll war voll —{Environment.NewLine}");
            }

            File.AppendAllText(_file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // See above.
        }
    }
}
