using System.Diagnostics;
using Auspex.Sensor;

// The Auspex sensor.
//
// It answers the one question a DNS filter fundamentally cannot: WHICH
// PROGRAM is talking to which destination. Auspex sees "this machine asked
// for graph.microsoft.com" - which of the seventy running programs that was
// is written nowhere.
//
// What it does NOT see is stated right here, so nobody has to find out the
// hard way:
//
//   * No content. It reads the operating system's connection table, not the
//     traffic. GET, POST, headers and body sit in the encrypted part.
//   * No UDP, and therefore no QUIC. Windows keeps no remote end for UDP -
//     there is no table in the kernel saying where a datagram went. Whatever
//     Chrome and Edge move over HTTP/3 does not appear here.
//   * Bytes only with administrator rights, and even then as a lower bound.
//
// It only reports and asks for nothing.

var settings = Settings.Read();

if (!ConnectionTable.Available)
{
    Console.Error.WriteLine(
        "Dieser Sensor liest die Verbindungstabelle von Windows. Auf diesem "
        + "system it does not exist.");
    return 2;
}

// Just look, report nothing. Whoever sets the sensor up wants to know
// first whether it sees anything at all - and to find that out without
// sending data yet.
if (args.Contains("--show"))
{
    var names = ProgramNames();
    var seen = ConnectionTable.Read();

    Console.WriteLine($"{seen.Count} established TCP connections{Environment.NewLine}");
    foreach (var group in seen
        .GroupBy(v => names.GetValueOrDefault(v.Pid, $"PID {v.Pid}"))
        .OrderByDescending(g => g.Count()))
    {
        Console.WriteLine($"  {group.Key}  ({group.Count()})");
        foreach (var v in group.OrderBy(v => v.Remote.ToString()).Take(6))
        {
            Console.WriteLine($"      -> {v.Remote}:{v.Port}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("No UDP and therefore no QUIC - Windows keeps no remote end for it.");
    return 0;
}

if (!settings.Complete)
{
    Console.Error.WriteLine("""
        The address and the token are missing.

        Both are in the dashboard under "Settings" - the same token as for the
        browser extension. Either in a sensor.json next to this executable:

            {
              "base": "http://192.168.1.61:5390",
              "token": "..."
            }

        or as the environment variables AUSPEX_BASE and AUSPEX_TOKEN.
        """);
    return 2;
}

var bytesReason = "switched off";
var bytes = settings.Bytes ? ByteCounter.TryCreate(out bytesReason) : null;

// The log, because as a scheduled task nobody sees the console.
var journal = Journal.NextToProgram();

journal.Say($"The Auspex sensor is reporting to {settings.BaseUrl}");
journal.Say(bytes is null
    ? $"Bytes are not counted: {bytesReason}."
    : "Bytes are counted - as a lower bound, see ByteCounter.");
Console.WriteLine("No UDP, no QUIC, no content. Stop with Ctrl+C.");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
var reporter = new Reporter(http, settings);
var ledger = new Ledger(TimeProvider.System);

using var end = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    end.Cancel();
};

var query = TimeSpan.FromSeconds(Math.Max(1, settings.PollSeconds));
var report = TimeSpan.FromSeconds(Math.Max(5, settings.ReportSeconds));
var nextReport = DateTimeOffset.UtcNow + report;
var alreadyReported = false;

try
{
    while (!end.IsCancellationRequested)
    {
        var open = ConnectionTable.Read();
        ledger.Record(open, ProgramNames(), bytes?.Read(open));

        if (settings.Verbose)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {open.Count} open connections");
        }

        if (DateTimeOffset.UtcNow >= nextReport)
        {
            nextReport = DateTimeOffset.UtcNow + report;
            var batch = ledger.Collect();
            if (batch.Count > 0)
            {
                var accepted = await reporter.ReportAsync(batch, end.Token);
                Console.WriteLine(accepted is null
                    ? $"{DateTime.Now:HH:mm:ss}  {batch.Count} relations not delivered"
                    : $"{DateTime.Now:HH:mm:ss}  {accepted} Beziehungen gemeldet");

                // Into the log only what answers a question: is anything
                // arriving? The console gets every report, the file does not.
                if (accepted is null)
                {
                    journal.Write($"{batch.Count} relations not delivered");
                }
                else if (!alreadyReported)
                {
                    alreadyReported = true;
                    journal.Write($"First report arrived: {accepted} relations.");
                }
            }
        }

        await Task.Delay(query, end.Token);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C is not an error.
}

// Whatever is still pending goes out before the sensor stops.
var rest = ledger.Collect();
if (rest.Count > 0)
{
    using var shortName = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await reporter.ReportAsync(rest, shortName.Token);
}

Console.WriteLine("Beendet.");
return 0;

/// <summary>
/// Process id to program name, once per poll.
///
/// <para>
/// In one go rather than per connection: process ids get reused, and looking
/// them up one at a time and late eventually attributes one program's
/// connections to its predecessor.
/// </para>
/// </summary>
static Dictionary<int, string> ProgramNames()
{
    var names = new Dictionary<int, string>();
    foreach (var p in Process.GetProcesses())
    {
        try
        {
            names[p.Id] = p.ProcessName;
        }
        catch
        {
            // Ended while we were asking. It happens.
        }
        finally
        {
            p.Dispose();
        }
    }
    return names;
}
