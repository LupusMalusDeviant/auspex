namespace Auspex.Control.Services.Localization;

// The shell: header, navigation, appearance panel, error banner.
//
// Everything that appears on every page. The tabs name WHAT YOU DO THERE —
// and that survives translation: "Beobachten/Eingreifen/Anlage" are
// activities, not data names, and activities can be carried across without
// the grouping losing its point.

public abstract partial class Strings
{
    // ── Kopf ──────────────────────────────────────────────────────────────
    public abstract string BrandSubline { get; }
    public abstract string SignOut { get; }

    // ── Tabs and entries ──────────────────────────────────────────────────
    public abstract string TabWatch { get; }
    public abstract string TabIntervene { get; }
    public abstract string TabSystem { get; }

    public abstract string NavOverview { get; }
    public abstract string NavQueryLog { get; }
    public abstract string NavAnalysis { get; }
    public abstract string NavFindings { get; }
    public abstract string NavDevices { get; }
    public abstract string NavLists { get; }
    public abstract string NavLearnMode { get; }
    public abstract string NavRouter { get; }
    public abstract string NavBackup { get; }
    public abstract string NavSettings { get; }

    /// <summary>Addendum in the tooltip of the tab that is currently open.</summary>
    public abstract string TabCurrent { get; }

    /// <summary>Addendum in the tooltip of the tabs you can expand.</summary>
    public abstract string TabExpand { get; }

    // ── Darstellung ───────────────────────────────────────────────────────
    public abstract string AppearanceTitle { get; }

    public abstract string AxisTheme { get; }
    public abstract string ThemeAuto { get; }
    public abstract string ThemeLight { get; }
    public abstract string ThemeDark { get; }

    public abstract string AxisAccent { get; }

    public abstract string AxisDensity { get; }
    public abstract string DensityTight { get; }
    public abstract string DensityNormal { get; }
    public abstract string DensityWide { get; }

    public abstract string AxisFontSize { get; }
    public abstract string FontSmall { get; }
    public abstract string FontNormal { get; }
    public abstract string FontLarge { get; }

    public abstract string AxisLanguage { get; }

    /// <summary>
    /// The names of the eight accent tones. Colour names are the one thing
    /// here that cannot be carried across literally: "Petrol" is not
    /// "petrol" in English but "teal", and "Oxblut" is "oxblood".
    /// </summary>
    public abstract string AccentName(string key);

    // ── Fehlerbanner ──────────────────────────────────────────────────────
    public abstract string UnexpectedError { get; }
    public abstract string Reload { get; }
}

public sealed partial class StringsDe
{
    public override string BrandSubline => "Control";
    public override string SignOut => "abmelden";

    public override string TabWatch => "Beobachten";
    public override string TabIntervene => "Eingreifen";
    public override string TabSystem => "Anlage";

    public override string NavOverview => "Übersicht";
    public override string NavQueryLog => "Query-Log";
    public override string NavAnalysis => "Auswertung";
    public override string NavFindings => "Auffälligkeiten";
    public override string NavDevices => "Geräte";
    public override string NavLists => "Listen";
    public override string NavLearnMode => "Lernmodus";
    public override string NavRouter => "Router";
    public override string NavBackup => "Sicherung";
    public override string NavSettings => "Einstellungen";

    public override string TabCurrent => "(aktueller Bereich)";
    public override string TabExpand => "aufklappen";

    public override string AppearanceTitle => "Darstellung";

    public override string AxisTheme => "Fassung";
    public override string ThemeAuto => "Auto";
    public override string ThemeLight => "Hell";
    public override string ThemeDark => "Dunkel";

    public override string AxisAccent => "Akzent";

    public override string AxisDensity => "Dichte";
    public override string DensityTight => "Eng";
    public override string DensityNormal => "Normal";
    public override string DensityWide => "Weit";

    public override string AxisFontSize => "Schrift";
    public override string FontSmall => "Klein";
    public override string FontNormal => "Normal";
    public override string FontLarge => "Gross";

    public override string AxisLanguage => "Sprache";

    public override string AccentName(string key) => key switch
    {
        "oxblut" => "Oxblut",
        "rost" => "Rost",
        "messing" => "Messing",
        "moos" => "Moos",
        "petrol" => "Petrol",
        "stahl" => "Stahl",
        "indigo" => "Indigo",
        "pflaume" => "Pflaume",
        _ => key,
    };

    public override string UnexpectedError => "Ein unerwarteter Fehler ist aufgetreten.";
    public override string Reload => "Neu laden";
}

public sealed partial class StringsEn
{
    public override string BrandSubline => "Control";
    public override string SignOut => "sign out";

    // "Watch / Step in / Plant" — the same idea as in German: the tab names
    // the activity. "Plant" here is the installation in the sense of works
    // or plant, not the vegetable; alongside router, backup and settings
    // that is unambiguous.
    public override string TabWatch => "Watch";
    public override string TabIntervene => "Step in";
    public override string TabSystem => "Plant";

    public override string NavOverview => "Overview";
    public override string NavQueryLog => "Query log";
    public override string NavAnalysis => "Analysis";
    public override string NavFindings => "Anomalies";
    public override string NavDevices => "Devices";
    public override string NavLists => "Lists";
    public override string NavLearnMode => "Learning mode";
    public override string NavRouter => "Router";
    public override string NavBackup => "Backup";
    public override string NavSettings => "Settings";

    public override string TabCurrent => "(current section)";
    public override string TabExpand => "expand";

    public override string AppearanceTitle => "Appearance";

    public override string AxisTheme => "Theme";
    public override string ThemeAuto => "Auto";
    public override string ThemeLight => "Light";
    public override string ThemeDark => "Dark";

    public override string AxisAccent => "Accent";

    public override string AxisDensity => "Density";
    public override string DensityTight => "Tight";
    public override string DensityNormal => "Normal";
    public override string DensityWide => "Loose";

    public override string AxisFontSize => "Text";
    public override string FontSmall => "Small";
    public override string FontNormal => "Normal";
    public override string FontLarge => "Large";

    public override string AxisLanguage => "Language";

    public override string AccentName(string key) => key switch
    {
        "oxblut" => "Oxblood",
        "rost" => "Rust",
        "messing" => "Brass",
        "moos" => "Moss",
        // Not "petrol": in English that is the fuel. The colour tone is called
        // "teal".
        "petrol" => "Teal",
        "stahl" => "Steel",
        "indigo" => "Indigo",
        "pflaume" => "Plum",
        _ => key,
    };

    public override string UnexpectedError => "Something went wrong.";
    public override string Reload => "Reload";
}
