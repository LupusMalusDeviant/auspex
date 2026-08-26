namespace Auspex.Control.Services.Localization;

// Sign-in, error pages, connection loss.
//
// The pages you only see when something is wrong — and which therefore
// nobody had touched: "Not Found", "An error occurred while processing your
// request." and the reconnect overlay were still in Blazor's shipped state,
// that is, in English, in the middle of a German interface. I only noticed
// while translating; whoever builds bilingually reads every line once
// deliberately.

public abstract partial class Strings
{
    // ── Anmeldung ─────────────────────────────────────────────────────────
    public abstract string TitleSignIn { get; }
    public abstract string FieldUser { get; }
    public abstract string FieldPassword { get; }
    public abstract string SignIn { get; }
    public abstract string SignInStale { get; }
    public abstract string SignInWrong { get; }
    public abstract string PasswordGenerated { get; }

    // ── Not found ─────────────────────────────────────────────────────────
    public abstract string NotFoundTitle { get; }
    public abstract string NotFoundText { get; }

    // ── Fehlerseite ───────────────────────────────────────────────────────
    public abstract string ErrorTitle { get; }
    public abstract string ErrorHeading { get; }
    public abstract string ErrorId { get; }
    public abstract string ErrorWhatToDo { get; }

    // ── Verbindungsabriss ─────────────────────────────────────────────────
    public abstract string Reconnecting { get; }
    /// <remarks>
    /// Contains the place where Blazor inserts the seconds — hence two
    /// halves instead of one sentence.
    /// </remarks>
    public abstract string ReconnectBefore { get; }
    public abstract string ReconnectAfter { get; }
    public abstract string ReconnectFailed { get; }
    public abstract string TryAgain { get; }
    public abstract string SessionPaused { get; }
    public abstract string SessionResumeFailed { get; }
    public abstract string Resume { get; }

    // ── Auswertungsregister ───────────────────────────────────────────────
    public abstract string TabHistory { get; }
    public abstract string TabRuleImpact { get; }
}

public sealed partial class StringsDe
{
    public override string TitleSignIn => "Anmeldung";
    public override string FieldUser => "Benutzer";
    public override string FieldPassword => "Kennwort";
    public override string SignIn => "Anmelden";
    public override string SignInStale =>
        "Diese Seite war zu lange offen. Lade sie neu und melde dich dann an — "
        + "dein Kennwort war vermutlich richtig.";
    public override string SignInWrong => "Benutzer oder Kennwort stimmen nicht.";
    public override string PasswordGenerated =>
        "Es ist kein Kennwort konfiguriert. Für diesen Start gilt ein zufällig "
        + "erzeugtes — es steht im Log der Control-Plane. Dauerhaft gehört "
        + "Auth:PasswordHash in die Konfiguration.";

    public override string NotFoundTitle => "Nicht gefunden";
    public override string NotFoundText => "Diese Seite gibt es hier nicht.";

    public override string ErrorTitle => "Fehler";
    public override string ErrorHeading =>
        "Beim Bearbeiten der Anfrage ist etwas schiefgegangen.";
    public override string ErrorId => "Kennung der Anfrage";
    public override string ErrorWhatToDo =>
        "Was genau, steht im Log der Control-Plane — dort unter dieser Kennung. "
        + "Die ausführliche Fehlerseite bleibt bewusst aus: sie zeigt Innenleben, "
        + "das niemanden außerhalb etwas angeht.";

    public override string Reconnecting => "Verbindung wird wiederhergestellt …";
    public override string ReconnectBefore => "Nicht erreicht — neuer Versuch in ";
    public override string ReconnectAfter => " Sekunden.";
    public override string ReconnectFailed =>
        "Verbindung nicht wiederhergestellt. Erneut versuchen oder die Seite neu laden.";
    public override string TryAgain => "Erneut versuchen";
    public override string SessionPaused => "Der Server hat die Sitzung angehalten.";
    public override string SessionResumeFailed =>
        "Die Sitzung ließ sich nicht fortsetzen. Erneut versuchen oder die Seite neu laden.";
    public override string Resume => "Fortsetzen";

    public override string TabHistory => "Verlauf";
    public override string TabRuleImpact => "Regelwirkung";
}

public sealed partial class StringsEn
{
    public override string TitleSignIn => "Sign in";
    public override string FieldUser => "User";
    public override string FieldPassword => "Password";
    public override string SignIn => "Sign in";
    public override string SignInStale =>
        "This page sat open too long. Reload it and sign in again — "
        + "your password was probably fine.";
    public override string SignInWrong => "That user and password do not match.";
    public override string PasswordGenerated =>
        "No password is configured. A random one applies for this run — "
        + "it is in the control-plane log. For good, put Auth:PasswordHash "
        + "into the configuration.";

    public override string NotFoundTitle => "Not found";
    public override string NotFoundText => "There is no such page here.";

    public override string ErrorTitle => "Error";
    public override string ErrorHeading =>
        "Something went wrong while handling that request.";
    public override string ErrorId => "Request ID";
    public override string ErrorWhatToDo =>
        "What exactly is in the control-plane log, under this ID. The detailed "
        + "error page stays off on purpose: it shows internals that are nobody "
        + "else's business.";

    public override string Reconnecting => "Reconnecting …";
    public override string ReconnectBefore => "No luck — trying again in ";
    public override string ReconnectAfter => " seconds.";
    public override string ReconnectFailed =>
        "Could not reconnect. Try again, or reload the page.";
    public override string TryAgain => "Try again";
    public override string SessionPaused => "The server paused this session.";
    public override string SessionResumeFailed =>
        "The session would not resume. Try again, or reload the page.";
    public override string Resume => "Resume";

    public override string TabHistory => "History";
    public override string TabRuleImpact => "Rule impact";
}
