using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auspex.Sensor;

/// <summary>What the dashboard reports back.</summary>
public sealed record ReportReply(
    [property: JsonPropertyName("accepted")] int Applied,
    [property: JsonPropertyName("device")] string? Device);

/// <summary>
/// A batch, in the shape it goes over the wire.
///
/// <para>
/// The names here have to match the record on the other side exactly. Up to
/// 0.9.0 they did not: the sensor sent <c>verbindungen</c> and <c>prozess</c>
/// while the control plane had already been renamed to <c>Connections</c>
/// and <c>Process</c>. Nothing bound, and the endpoint fell over on the
/// first field it read. Hence the attributes, spelled out rather than left
/// to a naming policy — so a rename on one side shows up here.
/// </para>
/// </summary>
public sealed record ReportBatch(
    [property: JsonPropertyName("connections")] IReadOnlyList<ReportItem> Connections);

/// <summary>One relation in the shape the API expects.</summary>
public sealed record ReportItem(
    [property: JsonPropertyName("process")] string Process,
    [property: JsonPropertyName("destination")] string Destination,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("count")] long Count,
    [property: JsonPropertyName("first")] DateTimeOffset First,
    [property: JsonPropertyName("last")] DateTimeOffset Last,
    [property: JsonPropertyName("bytesOut")] long? BytesOut,
    [property: JsonPropertyName("bytesIn")] long? BytesIn);

/// <summary>
/// Sends the relations to the dashboard.
///
/// <para>
/// The sensor only reports and asks for nothing. It needs no information
/// about the network — it supplies some. What it does not send matters just
/// as much: no paths, no window titles, no command lines. The program name
/// answers the question "who is sending things here?"; the path would give
/// away user names and install locations without answering it any better.
/// </para>
/// </summary>
public sealed class Reporter(HttpClient http, Settings settings)
{
    /// <summary>
    /// Reports one batch. Returns the number of rows accepted, or
    /// <c>null</c> if it did not work.
    /// </summary>
    public async Task<int?> ReportAsync(IReadOnlyList<Relation> relations, CancellationToken ct)
    {
        if (relations.Count == 0)
        {
            return 0;
        }

        var batch = new ReportBatch([.. relations.Select(b => new ReportItem(
            b.Process, b.Destination, b.Port, b.Protocol, b.Count,
            b.First, b.Last, b.BytesOut, b.BytesIn))]);

        try
        {
            using var query = new HttpRequestMessage(
                HttpMethod.Post, settings.BaseUrl + "/api/ext/connections")
            {
                Content = JsonContent.Create(batch, SensorJson.Default.ReportBatch),
            };
            query.Headers.Add("Authorization", "Bearer " + settings.Token);

            using var reply = await http.SendAsync(query, ct);
            if (!reply.IsSuccessStatusCode)
            {
                Console.Error.WriteLine(
                    $"The dashboard answers with {(int)reply.StatusCode} — "
                    + await reply.Content.ReadAsStringAsync(ct));
                return null;
            }

            var result = await reply.Content.ReadFromJsonAsync(
                SensorJson.Default.ReportReply, ct);
            return result?.Applied ?? relations.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The dashboard is not there right now. No reason to stop - the
            // next round tries again. Whatever was in this batch is lost;
            // that is the price of holding nothing back on the machine being
            // watched.
            Console.Error.WriteLine($"Unreachable: {ex.Message}");
            return null;
        }
    }
}
