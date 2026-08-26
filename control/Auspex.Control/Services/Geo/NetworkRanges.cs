using Microsoft.Data.Sqlite;

namespace Auspex.Control.Services.Geo;

/// <summary>What is known about a network.</summary>
public sealed record NetworkInfo(int Asn, string? Country, string? Operator);

/// <summary>
/// The mapping from address ranges to their operators.
///
/// <para>
/// Around 717,000 ranges, IPv4 and IPv6. They live in an SQLite file of
/// <em>their own</em>, not in the analysis database: this is lookup data that
/// can be downloaded again at any time. It would have no business in the
/// backup — it would quadruple it without its loss costing anything.
/// </para>
///
/// <para>
/// The lookup is a range query on an index: take the last range that begins
/// at or before the address and check whether it reaches far enough. That is
/// an index seek and not a scan, even at three quarters of a million rows.
/// </para>
///
/// <para>
/// The bounds are stored as 16-byte blobs in network order. SQLite compares
/// blobs byte by byte, which makes the ordering correct for both address
/// families in a single table. Compared as text, "9.9.9.9" would be greater
/// than "89.0.0.1", and the search would miss.
/// </para>
/// </summary>
public sealed class NetworkRanges(string path, ILogger<NetworkRanges> log) : INetworkRanges
{
    private readonly string _connection = new SqliteConnectionStringBuilder
    {
        DataSource = path,
    }.ToString();

    public string DbPath => path;

    /// <summary>
    /// Version of the schema.
    ///
    /// <para>
    /// This file is lookup data, not holdings — it can be fetched again in
    /// two minutes. So it needs no migration but the simplest rule there is:
    /// if the schema does not match, throw it away and import again.
    /// </para>
    ///
    /// <para>
    /// Without this check exactly that caught me out: <c>CREATE TABLE IF NOT
    /// EXISTS</c> left an existing table untouched, and every lookup then ran
    /// into "no such column: v6". The service caught the error and carried
    /// on — all that was visible was that nothing was being filled in any
    /// more.
    /// </para>
    /// </summary>
    private const int SchemaVersion = 2;

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connection);
        c.Open();
        return c;
    }

    /// <summary>
    /// Whether there is a file at all.
    ///
    /// <para>
    /// The reading paths have to ask this before they open anything. SQLite
    /// creates a missing file on opening — but only when the directory
    /// exists, and before the first import it does not. What comes back then
    /// is <c>SQLite Error 14: unable to open database file</c>, and the
    /// settings page, whose whole job is to say that this part is missing,
    /// came down over it with a 500.
    /// </para>
    ///
    /// <para>
    /// Deliberately not solved by creating the directory here: a read must
    /// not lay anything down. <see cref="Prepare"/> creates it, and Prepare
    /// runs on the writing path.
    /// </para>
    /// </summary>
    private bool Exists => File.Exists(path);

    /// <summary>Creates the schema if it is not there yet.</summary>
    public void Prepare()
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        using var c = Open();

        using (var read = c.CreateCommand())
        {
            read.CommandText = "PRAGMA user_version";
            var existing = Convert.ToInt32(read.ExecuteScalar() ?? 0);
            if (existing != SchemaVersion)
            {
                using var throwaway = c.CreateCommand();
                throwaway.CommandText = """
                    DROP TABLE IF EXISTS bereiche;
                    DROP TABLE IF EXISTS bereiche_neu;
                    DROP TABLE IF EXISTS stand;
                    """;
                throwaway.ExecuteNonQuery();

                if (existing != 0)
                {
                    log.LogInformation(
                        "The lookup data is at version {Old}, {New} is needed - " +
                        "it is being read in again", existing, SchemaVersion);
                }
            }
        }

        using var b = c.CreateCommand();
        b.CommandText = $"""
            PRAGMA user_version = {SchemaVersion};

            CREATE TABLE IF NOT EXISTS bereiche (
                von        BLOB    NOT NULL,
                bis        BLOB    NOT NULL,
                v6         INTEGER NOT NULL,
                asn        INTEGER NOT NULL,
                land       TEXT,
                betreiber  TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_bereiche_von ON bereiche(v6, von);

            CREATE TABLE IF NOT EXISTS stand (
                quelle  TEXT PRIMARY KEY,
                geholt  TEXT    NOT NULL,
                zeilen  INTEGER NOT NULL
            );
            """;
        b.ExecuteNonQuery();
    }

    /// <summary>
    /// When the ranges were last imported, and how many.
    ///
    /// <para>
    /// Before the first import the table does not exist at all — and that is
    /// the normal case, not the exception: the origin sources are opt-in, and
    /// whoever never switches them on never has anything here. "Nothing there
    /// yet" therefore has to be an answer and not an exception. It was one:
    /// the settings page, which wants to show this state, would have come
    /// down over it on a fresh installation.
    /// </para>
    /// </summary>
    public (DateTime? Fetched, long Rows) State()
    {
        if (!Exists)
        {
            return (null, 0);
        }

        using var c = Open();

        // Ask whether the table exists first, then query it. An EXISTS in the
        // same statement is not enough: SQLite resolves the names while
        // compiling and fails on FROM stand before the condition is ever
        // reached.
        using (var da = c.CreateCommand())
        {
            da.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'stand'";
            if (da.ExecuteScalar() is null)
            {
                return (null, 0);
            }
        }

        using var b = c.CreateCommand();
        b.CommandText = "SELECT geholt, zeilen FROM stand WHERE quelle = 'asn'";
        using var reader = b.ExecuteReader();
        if (!reader.Read())
        {
            return (null, 0);
        }
        return (DateTime.Parse(reader.GetString(0), null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetInt64(1));
    }

    /// <summary>
    /// Looks an address up.
    ///
    /// <para>
    /// <c>null</c> means "in no known range" — that happens and is not a
    /// fault. The source itself also carries ranges as "not routed"; those
    /// have number 0 and are left out here, because "AS 0, Not routed" is
    /// not information but its absence.
    /// </para>
    /// </summary>
    public NetworkInfo? Lookup(UInt128 address)
    {
        if (!Exists)
        {
            return null;
        }

        using var c = Open();
        return Lookup(c, address);
    }

    private static NetworkInfo? Lookup(SqliteConnection c, UInt128 address)
    {
        using var b = c.CreateCommand();
        // The family is stated explicitly in the condition.
        //
        // IPv4 is embedded as ::ffff:a.b.c.d and therefore lies numerically
        // INSIDE low IPv6 ranges. Without this condition an IPv6 range
        // beginning at :: and reaching far enough could claim an IPv4 address
        // - and attribute an operator to it that has nothing to do with it.
        // That it did not happen so far was only because the source carries
        // those ranges as "not routed"; that is luck, not design.
        b.CommandText = """
            SELECT asn, land, betreiber, bis
              FROM bereiche
             WHERE v6 = @v6 AND von <= @x
             ORDER BY von DESC
             LIMIT 1
            """;
        b.Parameters.AddWithValue("@x", Bytes(address));
        b.Parameters.AddWithValue("@v6", IsV6(address) ? 1 : 0);

        using var reader = b.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        // The last range beginning before the address need not contain it -
        // there are gaps between ranges.
        var until = (byte[])reader["bis"];
        if (Compare(until, Bytes(address)) < 0)
        {
            return null;
        }

        var asn = reader.GetInt32(0);
        if (asn == 0)
        {
            return null;
        }

        return new NetworkInfo(
            asn,
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    /// <summary>Several addresses on one connection — saves the open and close.</summary>
    public Dictionary<UInt128, NetworkInfo> Lookup(IEnumerable<UInt128> addresses)
    {
        if (!Exists)
        {
            return [];
        }

        var hits = new Dictionary<UInt128, NetworkInfo>();
        using var c = Open();
        foreach (var a in addresses)
        {
            if (Lookup(c, a) is { } info)
            {
                hits[a] = info;
            }
        }
        return hits;
    }

    /// <summary>
    /// Replaces the holdings with the rows from the source.
    ///
    /// <para>
    /// Replace and not append: the source is a complete snapshot, and a range
    /// no longer in it has been reassigned. Leaving it in place would mean
    /// giving outdated information rather than none.
    /// </para>
    ///
    /// <para>
    /// Into a staging table and then renamed, so the lookup does not sit on a
    /// half-filled table while the import runs.
    /// </para>
    /// </summary>
    public long Import(IEnumerable<(UInt128 From, UInt128 To, int Asn, string? Country, string? Operator)> lines)
    {
        using var c = Open();
        using var before = c.CreateCommand();
        before.CommandText = """
            DROP TABLE IF EXISTS bereiche_neu;
            CREATE TABLE bereiche_neu (
                von        BLOB    NOT NULL,
                bis        BLOB    NOT NULL,
                v6         INTEGER NOT NULL,
                asn        INTEGER NOT NULL,
                land       TEXT,
                betreiber  TEXT
            );
            """;
        before.ExecuteNonQuery();

        long counted = 0;
        using (var tx = c.BeginTransaction())
        {
            using var insert = c.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText =
                "INSERT INTO bereiche_neu (von, bis, v6, asn, land, betreiber) " +
                "VALUES (@von, @bis, @v6, @asn, @land, @betreiber)";

            var pFrom = insert.Parameters.Add("@von", SqliteType.Blob);
            var pTo = insert.Parameters.Add("@bis", SqliteType.Blob);
            var pV6 = insert.Parameters.Add("@v6", SqliteType.Integer);
            var pAsn = insert.Parameters.Add("@asn", SqliteType.Integer);
            var pCountry = insert.Parameters.Add("@land", SqliteType.Text);
            var pOperator = insert.Parameters.Add("@betreiber", SqliteType.Text);

            foreach (var z in lines)
            {
                pFrom.Value = Bytes(z.From);
                pTo.Value = Bytes(z.To);
                pV6.Value = IsV6(z.From) ? 1 : 0;
                pAsn.Value = z.Asn;
                pCountry.Value = (object?)z.Country ?? DBNull.Value;
                pOperator.Value = (object?)z.Operator ?? DBNull.Value;
                insert.ExecuteNonQuery();
                counted++;
            }

            tx.Commit();
        }

        using var swap = c.CreateCommand();
        swap.CommandText = """
            DROP TABLE IF EXISTS bereiche;
            ALTER TABLE bereiche_neu RENAME TO bereiche;
            CREATE INDEX ix_bereiche_von ON bereiche(v6, von);
            """;
        swap.ExecuteNonQuery();

        using var remember = c.CreateCommand();
        remember.CommandText =
            "INSERT INTO stand (quelle, geholt, zeilen) VALUES ('asn', @g, @z) " +
            "ON CONFLICT(quelle) DO UPDATE SET geholt = @g, zeilen = @z";
        remember.Parameters.AddWithValue("@g", DateTime.UtcNow.ToString("O"));
        remember.Parameters.AddWithValue("@z", counted);
        remember.ExecuteNonQuery();

        log.LogInformation("{Rows} network ranges read in", counted);
        return counted;
    }

    /// <summary>
    /// Whether the number is a genuine IPv6 address — that is, <em>not</em>
    /// an embedded IPv4 address in the range <c>::ffff:0:0</c> to
    /// <c>::ffff:ffff:ffff</c>.
    /// </summary>
    internal static bool IsV6(UInt128 value)
    {
        // ::ffff:0.0.0.0 = 0xFFFF_00000000, ::ffff:255.255.255.255 = 0xFFFF_FFFFFFFF
        const ulong unten = 0xFFFF_00000000UL;
        const ulong oben = 0xFFFF_FFFFFFFFUL;
        return value < unten || value > oben;
    }

    /// <summary>Die Adresse als 16 Bytes in Netzreihenfolge.</summary>
    internal static byte[] Bytes(UInt128 value)
    {
        var b = new byte[16];
        for (var i = 15; i >= 0; i--)
        {
            b[i] = (byte)(value & 0xFF);
            value >>= 8;
        }
        return b;
    }

    private static int Compare(byte[] a, byte[] b)
    {
        for (var i = 0; i < 16; i++)
        {
            if (a[i] != b[i])
            {
                return a[i] < b[i] ? -1 : 1;
            }
        }
        return 0;
    }
}
