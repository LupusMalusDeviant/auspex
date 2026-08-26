namespace Auspex.Control.Services.Localization;

// What the browser extension gets back as an answer.
//
// These sentences do not go to a Blazor page but over the API to a window
// 380 pixels wide. Which language applies there is said by the
// X-Auspex-Language header — see LanguageHeaderProvider.

public abstract partial class Strings
{
    public abstract string NotAValidName { get; }
    public abstract string NoProfileWithoutMac { get; }
    public abstract string NoProfileForDevice { get; }
    public abstract string AlreadyAllowedDeadlineSet(string domain, TimeSpan deadline);
    public abstract string AlreadyPermanentlyAllowed(string domain);
    public abstract string AllowedFor(string domain, TimeSpan deadline, string extra);
    public abstract string PermanentlyAllowed(string domain, string extra);
    public abstract string WasNotAllowed(string domain);
    public abstract string BlockedAgain(string domain);

    /// <summary>
    /// The addendum for when the query ran over a redirect that is blocked
    /// as well. The target name goes out beside it as a field of its own —
    /// the extension builds a second button from it and does <em>not</em>
    /// dig it back out of this sentence.
    /// </summary>
    public abstract string ForwardingBlocked(string destination);

    public abstract string DeviceNotRecognised { get; }
    public abstract string DeviceNotRecognisedShort { get; }

    /// <summary>
    /// The 401 the extension gets. It reaches the popup unchanged, so it has
    /// to say what to do — "unauthorized" would only be true, not useful.
    /// </summary>
    public abstract string TokenNoLongerValid { get; }

    /// <summary>A deadline in words — "15 minutes", "2 hours".</summary>
    public abstract string Deadline(TimeSpan duration);
}

public sealed partial class StringsDe
{
    public override string NotAValidName => "Kein gültiger Name.";
    public override string NoProfileWithoutMac =>
        "Für dieses Gerät gibt es kein Profil, und ohne bekannte MAC lässt sich "
        + "keines anlegen, das morgen noch greift.";
    public override string NoProfileForDevice => "Für dieses Gerät gibt es kein Profil.";
    public override string AlreadyAllowedDeadlineSet(string domain, TimeSpan deadline) =>
        $"{domain} war schon frei — Frist auf {Deadline(deadline)} gesetzt.";
    public override string AlreadyPermanentlyAllowed(string domain) =>
        $"{domain} ist bereits dauerhaft frei.";
    public override string AllowedFor(string domain, TimeSpan deadline, string extra) =>
        $"{domain} ist für {Deadline(deadline)} frei.{extra}";
    public override string PermanentlyAllowed(string domain, string extra) =>
        $"{domain} ist dauerhaft frei.{extra}";
    public override string WasNotAllowed(string domain) =>
        $"{domain} war nicht freigegeben.";
    public override string BlockedAgain(string domain) => $"{domain} ist wieder gesperrt.";

    public override string ForwardingBlocked(string destination) =>
        $" Achtung: die Anfrage lief zuletzt über eine Weiterleitung auf {destination}, "
        + "und die ist ebenfalls gesperrt — ohne sie lädt die Seite vermutlich "
        + "weiterhin nicht.";

    public override string DeviceNotRecognised =>
        "Der Resolver kennt dieses Gerät nicht. Läuft die Anfrage über einen Proxy, "
        + "oder ist Auspex nicht der DNS-Server dieses Geräts?";
    public override string DeviceNotRecognisedShort => "Gerät nicht erkannt.";
    public override string TokenNoLongerValid =>
        "Das Zeichen gilt nicht mehr. Im Dashboard unter Einstellungen ein neues "
        + "erzeugen und in der Erweiterung eintragen.";

    public override string Deadline(TimeSpan d) => d switch
    {
        { TotalMinutes: < 60 } => $"{(int)d.TotalMinutes} Minuten",
        { TotalHours: < 24 } => $"{(int)d.TotalHours} Stunden",
        _ => $"{(int)d.TotalDays} Tage",
    };
}

public sealed partial class StringsEn
{
    public override string NotAValidName => "Not a valid name.";
    public override string NoProfileWithoutMac =>
        "There is no profile for this device, and without a known MAC none can be "
        + "created that would still apply tomorrow.";
    public override string NoProfileForDevice => "There is no profile for this device.";
    public override string AlreadyAllowedDeadlineSet(string domain, TimeSpan deadline) =>
        $"{domain} was already allowed — the window is now {Deadline(deadline)}.";
    public override string AlreadyPermanentlyAllowed(string domain) =>
        $"{domain} is already allowed for good.";
    public override string AllowedFor(string domain, TimeSpan deadline, string extra) =>
        $"{domain} is allowed for {Deadline(deadline)}.{extra}";
    public override string PermanentlyAllowed(string domain, string extra) =>
        $"{domain} is allowed for good.{extra}";
    public override string WasNotAllowed(string domain) =>
        $"{domain} was not allowed.";
    public override string BlockedAgain(string domain) => $"{domain} is blocked again.";

    public override string ForwardingBlocked(string destination) =>
        $" Careful: the request last went through a redirect to {destination}, and that is "
        + "blocked as well — without it the page will probably still not load.";

    public override string DeviceNotRecognised =>
        "The resolver does not know this device. Is the request going through a "
        + "proxy, or is Auspex not this device's DNS server?";
    public override string DeviceNotRecognisedShort => "Device not recognised.";
    public override string TokenNoLongerValid =>
        "That token is no longer valid. Issue a new one in the dashboard under "
        + "Settings and enter it in the extension.";

    public override string Deadline(TimeSpan d) => d switch
    {
        { TotalMinutes: < 60 } => $"{(int)d.TotalMinutes} minutes",
        { TotalHours: < 24 } => $"{(int)d.TotalHours} hours",
        _ => $"{(int)d.TotalDays} days",
    };
}
