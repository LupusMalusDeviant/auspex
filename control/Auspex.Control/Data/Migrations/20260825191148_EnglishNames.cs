using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auspex.Control.Data.Migrations
{
    /// <summary>
    /// Tables and columns in English — renamed, not recreated.
    ///
    /// <para>
    /// <strong>This file is written by hand.</strong> The draft that
    /// <c>dotnet ef migrations add</c> produced wanted to drop <c>Ziele</c>,
    /// <c>Aufloesungen</c> and <c>Verbindungen</c> and create them again
    /// under new names — EF does not see a rename as a rename but as a
    /// vanished table and a new one. It warned about it itself ("may result
    /// in the loss of data"), and what it meant was 3313 destinations, 5541
    /// resolutions and 192 connections.
    /// </para>
    ///
    /// <para>
    /// SQLite can do both, since 3.25: <c>ALTER TABLE … RENAME TO</c> takes
    /// the indexes with it, <c>RENAME COLUMN</c> carries along the indexes
    /// that sit on the column. Only the index <em>names</em> stay put, which
    /// is why they are set again below — an index is derived and costs
    /// nothing at these row counts.
    /// </para>
    ///
    /// <para>
    /// Order: the table first, then its columns. The other way round,
    /// <c>RenameColumn</c> pointed at a table that no longer exists under
    /// that name.
    /// </para>
    /// </summary>
    public partial class EnglishNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder b)
        {
            // ── Findings ──────────────────────────────────────────────────
            b.RenameColumn(name: "Werte", table: "Findings", newName: "Values");

            // ── Ziele → Destinations ──────────────────────────────────────
            b.RenameTable(name: "Ziele", newName: "Destinations");
            b.RenameColumn(name: "GeprueftUtc", table: "Destinations", newName: "CheckedUtc");
            b.RenameColumn(name: "Land", table: "Destinations", newName: "Country");
            b.RenameColumn(name: "Stadt", table: "Destinations", newName: "City");
            b.RenameColumn(name: "Betreiber", table: "Destinations", newName: "Operator");
            b.RenameColumn(name: "StadtUnsicher", table: "Destinations", newName: "CityUncertain");
            b.RenameColumn(name: "StadtGeprueftUtc", table: "Destinations", newName: "CityCheckedUtc");
            b.RenameColumn(name: "Privat", table: "Destinations", newName: "IsPrivate");
            b.RenameColumn(name: "ErstUtc", table: "Destinations", newName: "FirstUtc");
            b.RenameColumn(name: "ZuletztUtc", table: "Destinations", newName: "LastUtc");

            b.DropIndex(name: "IX_Ziele_Asn", table: "Destinations");
            b.DropIndex(name: "IX_Ziele_Ip", table: "Destinations");
            b.DropIndex(name: "IX_Ziele_Privat_GeprueftUtc", table: "Destinations");
            b.CreateIndex(name: "IX_Destinations_Asn", table: "Destinations", column: "Asn");
            b.CreateIndex(name: "IX_Destinations_Ip", table: "Destinations", column: "Ip", unique: true);
            b.CreateIndex(name: "IX_Destinations_IsPrivate_CheckedUtc", table: "Destinations",
                columns: ["IsPrivate", "CheckedUtc"]);

            // ── Aufloesungen → Resolutions ────────────────────────────────
            b.RenameTable(name: "Aufloesungen", newName: "Resolutions");
            b.RenameColumn(name: "ErstUtc", table: "Resolutions", newName: "FirstUtc");
            b.RenameColumn(name: "ZuletztUtc", table: "Resolutions", newName: "LastUtc");
            b.RenameColumn(name: "Anzahl", table: "Resolutions", newName: "Count");

            b.DropIndex(name: "IX_Aufloesungen_Ip", table: "Resolutions");
            b.DropIndex(name: "IX_Aufloesungen_ZuletztUtc", table: "Resolutions");
            b.DropIndex(name: "IX_Aufloesungen_Domain_ZuletztUtc", table: "Resolutions");
            b.DropIndex(name: "IX_Aufloesungen_Name_Ip", table: "Resolutions");
            b.CreateIndex(name: "IX_Resolutions_Ip", table: "Resolutions", column: "Ip");
            b.CreateIndex(name: "IX_Resolutions_LastUtc", table: "Resolutions", column: "LastUtc");
            b.CreateIndex(name: "IX_Resolutions_Domain_LastUtc", table: "Resolutions",
                columns: ["Domain", "LastUtc"]);
            b.CreateIndex(name: "IX_Resolutions_Name_Ip", table: "Resolutions",
                columns: ["Name", "Ip"], unique: true);

            // ── Verbindungen → Connections ────────────────────────────────
            b.RenameTable(name: "Verbindungen", newName: "Connections");
            b.RenameColumn(name: "Geraet", table: "Connections", newName: "Device");
            b.RenameColumn(name: "Prozess", table: "Connections", newName: "Process");
            b.RenameColumn(name: "Ziel", table: "Connections", newName: "Destination");
            b.RenameColumn(name: "Protokoll", table: "Connections", newName: "Protocol");
            b.RenameColumn(name: "ErstUtc", table: "Connections", newName: "FirstUtc");
            b.RenameColumn(name: "ZuletztUtc", table: "Connections", newName: "LastUtc");
            b.RenameColumn(name: "Anzahl", table: "Connections", newName: "Count");
            b.RenameColumn(name: "BytesRaus", table: "Connections", newName: "BytesOut");
            b.RenameColumn(name: "BytesRein", table: "Connections", newName: "BytesIn");

            b.DropIndex(name: "IX_Verbindungen_Ziel", table: "Connections");
            b.DropIndex(name: "IX_Verbindungen_ZuletztUtc", table: "Connections");
            b.DropIndex(name: "IX_Verbindungen_Geraet_ZuletztUtc", table: "Connections");
            b.DropIndex(name: "IX_Verbindungen_Client_Prozess_Ziel_Port_Protokoll", table: "Connections");
            b.CreateIndex(name: "IX_Connections_Destination", table: "Connections", column: "Destination");
            b.CreateIndex(name: "IX_Connections_LastUtc", table: "Connections", column: "LastUtc");
            b.CreateIndex(name: "IX_Connections_Device_LastUtc", table: "Connections",
                columns: ["Device", "LastUtc"]);
            b.CreateIndex(name: "IX_Connections_Client_Process_Destination_Port_Protocol",
                table: "Connections",
                columns: ["Client", "Process", "Destination", "Port", "Protocol"], unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder b)
        {
            b.DropIndex(name: "IX_Connections_Destination", table: "Connections");
            b.DropIndex(name: "IX_Connections_LastUtc", table: "Connections");
            b.DropIndex(name: "IX_Connections_Device_LastUtc", table: "Connections");
            b.DropIndex(name: "IX_Connections_Client_Process_Destination_Port_Protocol", table: "Connections");
            b.RenameColumn(name: "BytesIn", table: "Connections", newName: "BytesRein");
            b.RenameColumn(name: "BytesOut", table: "Connections", newName: "BytesRaus");
            b.RenameColumn(name: "Count", table: "Connections", newName: "Anzahl");
            b.RenameColumn(name: "LastUtc", table: "Connections", newName: "ZuletztUtc");
            b.RenameColumn(name: "FirstUtc", table: "Connections", newName: "ErstUtc");
            b.RenameColumn(name: "Protocol", table: "Connections", newName: "Protokoll");
            b.RenameColumn(name: "Destination", table: "Connections", newName: "Ziel");
            b.RenameColumn(name: "Process", table: "Connections", newName: "Prozess");
            b.RenameColumn(name: "Device", table: "Connections", newName: "Geraet");
            b.RenameTable(name: "Connections", newName: "Verbindungen");
            b.CreateIndex(name: "IX_Verbindungen_Ziel", table: "Verbindungen", column: "Ziel");
            b.CreateIndex(name: "IX_Verbindungen_ZuletztUtc", table: "Verbindungen", column: "ZuletztUtc");
            b.CreateIndex(name: "IX_Verbindungen_Geraet_ZuletztUtc", table: "Verbindungen",
                columns: ["Geraet", "ZuletztUtc"]);
            b.CreateIndex(name: "IX_Verbindungen_Client_Prozess_Ziel_Port_Protokoll", table: "Verbindungen",
                columns: ["Client", "Prozess", "Ziel", "Port", "Protokoll"], unique: true);

            b.DropIndex(name: "IX_Resolutions_Ip", table: "Resolutions");
            b.DropIndex(name: "IX_Resolutions_LastUtc", table: "Resolutions");
            b.DropIndex(name: "IX_Resolutions_Domain_LastUtc", table: "Resolutions");
            b.DropIndex(name: "IX_Resolutions_Name_Ip", table: "Resolutions");
            b.RenameColumn(name: "Count", table: "Resolutions", newName: "Anzahl");
            b.RenameColumn(name: "LastUtc", table: "Resolutions", newName: "ZuletztUtc");
            b.RenameColumn(name: "FirstUtc", table: "Resolutions", newName: "ErstUtc");
            b.RenameTable(name: "Resolutions", newName: "Aufloesungen");
            b.CreateIndex(name: "IX_Aufloesungen_Ip", table: "Aufloesungen", column: "Ip");
            b.CreateIndex(name: "IX_Aufloesungen_ZuletztUtc", table: "Aufloesungen", column: "ZuletztUtc");
            b.CreateIndex(name: "IX_Aufloesungen_Domain_ZuletztUtc", table: "Aufloesungen",
                columns: ["Domain", "ZuletztUtc"]);
            b.CreateIndex(name: "IX_Aufloesungen_Name_Ip", table: "Aufloesungen",
                columns: ["Name", "Ip"], unique: true);

            b.DropIndex(name: "IX_Destinations_Asn", table: "Destinations");
            b.DropIndex(name: "IX_Destinations_Ip", table: "Destinations");
            b.DropIndex(name: "IX_Destinations_IsPrivate_CheckedUtc", table: "Destinations");
            b.RenameColumn(name: "LastUtc", table: "Destinations", newName: "ZuletztUtc");
            b.RenameColumn(name: "FirstUtc", table: "Destinations", newName: "ErstUtc");
            b.RenameColumn(name: "IsPrivate", table: "Destinations", newName: "Privat");
            b.RenameColumn(name: "CityCheckedUtc", table: "Destinations", newName: "StadtGeprueftUtc");
            b.RenameColumn(name: "CityUncertain", table: "Destinations", newName: "StadtUnsicher");
            b.RenameColumn(name: "Operator", table: "Destinations", newName: "Betreiber");
            b.RenameColumn(name: "City", table: "Destinations", newName: "Stadt");
            b.RenameColumn(name: "Country", table: "Destinations", newName: "Land");
            b.RenameColumn(name: "CheckedUtc", table: "Destinations", newName: "GeprueftUtc");
            b.RenameTable(name: "Destinations", newName: "Ziele");
            b.CreateIndex(name: "IX_Ziele_Asn", table: "Ziele", column: "Asn");
            b.CreateIndex(name: "IX_Ziele_Ip", table: "Ziele", column: "Ip", unique: true);
            b.CreateIndex(name: "IX_Ziele_Privat_GeprueftUtc", table: "Ziele",
                columns: ["Privat", "GeprueftUtc"]);

            b.RenameColumn(name: "Values", table: "Findings", newName: "Werte");
        }
    }
}
