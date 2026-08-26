using System.Text.Json.Serialization;

namespace Auspex.Sensor;

/// <summary>
/// The JSON shapes the sensor knows — generated at compile time rather than
/// discovered at runtime.
///
/// <para>
/// The reason is trimming. The sensor should sit ready as a single file to
/// download, and that means self-contained: the runtime is in there too.
/// Untrimmed that is around 70 MB, trimmed a fraction of it — but trimming
/// removes whatever is only reachable through reflection, and
/// reflection-based serialisation is exactly that. It does not survive:
/// at runtime an empty object would come out, with no error.
/// </para>
///
/// <para>
/// With the source generator the route to every field is known at compile
/// time. Trimming can then take nothing away that is needed — and if it did,
/// the compiler says so, not production.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ReportBatch))]
[JsonSerializable(typeof(ReportReply))]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SensorJson : JsonSerializerContext;
