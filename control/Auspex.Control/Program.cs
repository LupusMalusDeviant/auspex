using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Auspex.Control.Components;
using Auspex.Control.Data;
using Auspex.Control.Services;
using Auspex.Control.Services.Extension;
using Auspex.Control.Services.Geo;
using Auspex.Control.Services.Router;
using Auspex.Control.Services.Localization;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

// A helper invocation for producing a password hash, so nobody has to write
// plaintext into the configuration.
if (args is ["--hash-password", var plainText])
{
    Console.WriteLine(Auspex.Control.Services.PasswordAuth.Hash(plainText));
    return;
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var authSection = builder.Configuration.GetSection(AuthOptions.SectionName);
builder.Services.Configure<AuthOptions>(authSection);
var auth = authSection.Get<AuthOptions>() ?? new AuthOptions();

// Without persistent keys they live in memory: every restart invalidates
// sign-in cookies and antiforgery tokens. The browser then sends a form with
// a token nobody can decrypt any more - and the answer is HTTP 400 with no
// explanation at all.
var keyPath = builder.Configuration["Auth:KeyPath"] ?? "var/keys";
Directory.CreateDirectory(keyPath);
if (!Path.IsPathRooted(keyPath))
{
    // A relative path is right for a local run and wrong in a container:
    // there it lands in the working directory and is gone with the next
    // recreate. Everybody is signed out and the extension token can no
    // longer be decrypted, with nothing anywhere saying why.
    //
    // So it is said out loud. The damage only shows up later and then looks
    // like something else entirely.
    Console.Error.WriteLine(
        $"[WARN] Auth:KeyPath is relative ({keyPath}). In a container the key ring "
        + "does not survive a recreate - set Auth__KeyPath to an absolute path on "
        + "a mounted volume.");
}
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .SetApplicationName("auspex-control");

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<PasswordAuth>();
builder.Services.AddCascadingAuthenticationState();

if (auth.Enabled)
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/logout";
            options.AccessDeniedPath = "/login";
            options.ExpireTimeSpan = auth.SessionLifetime;
            options.SlidingExpiration = true;
            options.Cookie.Name = "auspex.control";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // SameAsRequest rather than Always: the dashboard often runs in a
            // home network without TLS, and a cookie that then never gets set
            // would be a lockout rather than a protection.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

    builder.Services.AddAuthorization(options =>
    {
        // Everything is protected except what is explicitly released. The other
        // way round, every new page would be accidentally open.
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });
}
else
{
    // Sign-in switched off means: something in front of it already
    // authenticated — forward auth through Authentik, say. It does NOT mean
    // that the endpoints marked RequireAuthorization stop existing.
    //
    // Without these two lines they threw. AuthorizationMiddleware wants to
    // challenge on a failed policy, finds no IAuthenticationService and comes
    // back with a 500 — for the backup download, the extension token, both
    // packages and every router call. The pages were fine, because the
    // fallback policy is the only thing that goes away with auth.Enabled.
    //
    // So: register both, and let every policy pass. Whoever switches the
    // sign-in off has said they are handling it elsewhere.
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();
    });
}

// The Go data plane is the only data source.
builder.Services.AddHttpClient<IAuspexClient, AuspexClient>(client =>
{
    var baseUrl = builder.Configuration["Auspex:BaseUrl"] ?? "http://127.0.0.1:5380";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
// Also under the class name, for as long as not every caller is on the
// interface. Two registrations here explicitly do NOT mean two HttpClients:
// the second one fetches the first.
builder.Services.AddScoped(sp => (AuspexClient)sp.GetRequiredService<IAuspexClient>());
// IAuspexClient carries IClientProfiles, but the container does not infer
// that: a service asking for the narrow interface would otherwise fail to be
// constructed at all. Found in the running system, not by a test — the unit
// tests build the service directly with a double and never touch the
// container.
builder.Services.AddScoped<IClientProfiles>(sp => sp.GetRequiredService<IAuspexClient>());

var analyticsSection = builder.Configuration.GetSection(AnalyticsOptions.SectionName);
builder.Services.Configure<AnalyticsOptions>(analyticsSection);
var analytics = analyticsSection.Get<AnalyticsOptions>() ?? new AnalyticsOptions();

builder.Services.AddDbContext<AnalyticsDbContext>(options => options.UseSqlite(analytics.ConnectionString));
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<ImpactService>();
builder.Services.AddScoped<RollupService>();
builder.Services.AddScoped<LongTermService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<RestoreService>();
builder.Services.Configure<NotificationOptions>(
    builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddScoped<FindingNotifier>();
builder.Services.Configure<RuleFileOptions>(
    builder.Configuration.GetSection(RuleFileOptions.SectionName));

// The router connection. The client is always registered - it knows itself
// whether an account is stored, and the interface asks it. That way there is
// no second place carrying the same condition again.
builder.Services.Configure<RouterOptions>(
    builder.Configuration.GetSection(RouterOptions.SectionName));
builder.Services.AddSingleton<RouterSettingsStore>();
builder.Services.AddSingleton<IRouterSettingsStore>(sp => sp.GetRequiredService<RouterSettingsStore>());
builder.Services.AddSingleton<Tr064Client>();
builder.Services.AddScoped<RouterAdmin>();
builder.Services.AddScoped<IRouterAdmin>(sp => sp.GetRequiredService<RouterAdmin>());
// The second channel for what TR-064 does not provide - above all the local
// DNS server the box hands out over DHCP.
builder.Services.AddScoped<FritzWebClient>();
// Reads the catalogue in advance, so the first visit to the router section
// does not have to wait for close to forty description files.
builder.Services.AddHostedService<RouterWarmupService>();
// Writes the router's device list out for the resolver: it needs it to
// attribute temporary IPv6 addresses to a device by MAC.
builder.Services.AddHostedService<DeviceNameExportService>();
// Watches what changes on the router without anybody here setting it in
// motion - above all port mappings via UPnP. Needs the analysis, because
// that is where the findings land; without it there would be no place for
// them to become visible.
if (analytics.Enabled)
{
    builder.Services.AddSingleton<RouterWatchService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<RouterWatchService>());
}

// Browser extension: its own sign-in, its own cut. It is explicitly allowed
// only what a device should do for itself.
builder.Services.AddSingleton<ExtensionTokenStore>();
builder.Services.AddSingleton<IExtensionTokenStore>(sp => sp.GetRequiredService<ExtensionTokenStore>());
builder.Services.AddSingleton<ExtensionPackage>();
builder.Services.AddSingleton<SensorPackage>();
builder.Services.AddSingleton<AppearanceStore>();
builder.Services.AddSingleton<IAppearanceStore>(sp => sp.GetRequiredService<AppearanceStore>());
builder.Services.AddScoped<ExceptionService>();
builder.Services.AddHostedService<ExceptionCleanupService>();
builder.Services.AddScoped<RuleWriter>();
builder.Services.AddScoped<IRuleWriter>(sp => sp.GetRequiredService<RuleWriter>());

// Always registered, so the interface can inject it and trigger a run by
// hand. Only as a background service does it depend on the setting -
// otherwise the findings page would be a server error rather than an empty
// list when analysis is switched off.
builder.Services.AddSingleton<DetectionService>();

if (analytics.Enabled)
{
    builder.Services.AddHostedService<IngestService>();
    builder.Services.AddHostedService<CacheWarmingService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DetectionService>());
}

// Origin of the destinations: who owns the address, in which country and
// city it sits. Looked up exclusively in LOCAL files - a geo API would learn
// with every lookup where this household sends things, and that is exactly
// what Auspex is built against.
builder.Services.Configure<GeoOptions>(
    builder.Configuration.GetSection(GeoOptions.SectionName));
var geo = builder.Configuration.GetSection(GeoOptions.SectionName).Get<GeoOptions>() ?? new GeoOptions();

// The interfaces point at the same instance as the class - otherwise a
// caller taking the class would have a different state in front of it than
// one taking the interface.
builder.Services.AddSingleton(sp => new NetworkRanges(
    Path.Combine(geo.Path, "netzbereiche.db"),
    sp.GetRequiredService<ILogger<NetworkRanges>>()));
builder.Services.AddSingleton<INetworkRanges>(sp => sp.GetRequiredService<NetworkRanges>());
builder.Services.AddSingleton<CityLookup>();
builder.Services.AddScoped<DossierService>();
builder.Services.AddScoped<ProgramService>();

// The quarantine list lives beside the other small state, not in the
// database: a restart is exactly when a forgotten quarantine would become a
// device that is off the network with nothing left to say why.
builder.Services.AddSingleton(_ => new QuarantineStore(
    builder.Configuration["Quarantine:Path"] ?? "var/quarantine.json"));
builder.Services.AddScoped<QuarantineService>();
builder.Services.AddHostedService<QuarantineExpiryService>();
builder.Services.AddScoped<Prerequisites>();
builder.Services.AddHttpClient<GeoSources>(c =>
{
    // A 90 MB file over a home line is allowed to take its time.
    c.Timeout = TimeSpan.FromMinutes(10);
});

if (geo.Enabled)
{
    builder.Services.AddHostedService<GeoService>();
}

// Language and culture.
//
// This used to be a fixed line on de-DE. It was necessary because .NET
// formats invariantly with no culture set and writes "0.1 ms" and "1,000
// queries" - in a German sentence the first reads like a typo and the second
// like a different number.
//
// The same reasoning now applies in both directions: an English interface
// writing "1.234,5" is just as wrong. The culture therefore hangs off the
// request, not the process. What it brings with it is the part of the
// translation you do not do by hand - decimal separators, month names,
// weekday order.
//
// German comes first and is the fallback: the original is German, and
// whoever chooses nothing should get it unchanged.
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.SetDefaultCulture(Strings.Kulturen[0]);
    o.AddSupportedCultures(Strings.Kulturen);
    o.AddSupportedUICultures(Strings.Kulturen);

    // Only what somebody explicitly set: our own cookie for the interface,
    // our own header for the browser extension. The browser's
    // Accept-Language header is deliberately NOT in there - Auspex runs in a
    // home network, and a browser that happens to be set to English should
    // not switch the interface over without anybody wanting it.
    o.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new LanguageHeaderProvider(),
    ];
});

// The text itself hangs off the request's culture. Scoped and not singleton:
// otherwise two browsers with different languages would see the same text,
// and whoever got there first would win.
builder.Services.AddScoped<Strings>(_ =>
    Strings.For(System.Globalization.CultureInfo.CurrentUICulture));

// Refuse to start when something registered cannot be built, instead of
// discovering it later as a line in the log.
//
// The occasion was a service that took a narrow interface the container did
// not know: every unit test passed, the container failed once a minute in the
// background, and the feature silently did nothing. A start that fails loudly
// is the cheaper failure by a wide margin.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

var app = builder.Build();

// The database is always created, even when the background services are off:
// the pages query it anyway, and a missing table would be a server error
// rather than an empty analysis.
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

    // SQLite does not create directories by itself.
    var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(analytics.ConnectionString).DataSource;
    var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }
    await db.Database.MigrateAsync();

    await scope.ServiceProvider.GetRequiredService<RuleWriter>().EnsureExistsAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// No HTTPS redirection in the container: TLS is terminated by the reverse
// proxy in front. A redirection from here would not know the right port
// anyway - the application has been logging that as a warning on every start.
//
// Instead, evaluate the forwarded headers so the application knows the
// client arrived over https: otherwise redirects after sign-in point back at
// http.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost,
};
foreach (var network in builder.Configuration.GetSection("Auth:TrustedProxies").Get<string[]>() ?? [])
{
    if (System.Net.IPAddress.TryParse(network, out var ip))
    {
        forwarded.KnownProxies.Add(ip);
    }
}
if (forwarded.KnownProxies.Count == 0)
{
    // With nothing specified, believe nobody: otherwise any client could
    // claim an arbitrary origin and an arbitrary scheme.
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
}
app.UseForwardedHeaders(forwarded);

// Before anything else that produces text - including before sign-in, so the
// sign-in page already appears in the chosen language. Whoever switched to
// English and then gets signed out should not land on a German page.
app.UseRequestLocalization();

app.UseAuthentication();
app.UseAuthorization();

if (!auth.Enabled)
{
    app.Logger.LogWarning(
        "Sign-in is switched off. The dashboard can change filter lists - " +
        "run it that way only if something in front of it authenticates.");
}

app.UseAntiforgery();

// Downloading over an endpoint of its own: a file cannot be delivered over
// an open Blazor connection.
// Router actions without an interface. Two reasons: configuring the network
// should be scriptable and not only clickable - and a call that can be
// triggered from outside is a call you can test. The guard is the same as
// everywhere: be signed in, and read-only blocks anything changing inside
// the client itself.
// Creating a token without the interface too - the same guard as the
// dashboard, that is, the session cookie. Useful for setting up through a
// script, and for being able to test the route without clicking.
app.MapPost("/api/extension/token", async (ExtensionTokenStore z, CancellationToken ct) =>
{
    var fresh = await z.NewAsync(ct);
    return Results.Ok(new { token = fresh, hint = "Not shown again." });
}).RequireAuthorization().DisableAntiforgery();

app.MapDelete("/api/extension/token", (ExtensionTokenStore z) =>
{
    z.Delete();
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// The extension as an archive. Until now you had to get to the project
// directory and run build.sh there - whoever operates the dashboard from a
// different machine could not reach it at all.
app.MapGet("/api/extension/package/{browser}", (string browser, ExtensionPackage package) =>
{
    var data = package.Pack(browser);
    if (data is null)
    {
        return Results.NotFound(new { error = "Unknown browser, or the sources are missing from the image." });
    }

    var version = package.Version() is { Length: > 0 } v ? "-" + v : "";
    return Results.File(data, "application/zip",
        $"auspex-{browser.ToLowerInvariant()}{version}.zip");
}).RequireAuthorization();

// The appearance lives server-side, so it applies on every machine and the
// browser extension can read it - localStorage is bound to the origin, and
// an extension has a different one.
app.MapGet("/api/appearance", (AppearanceStore d) => Results.Ok(d.Current))
   .RequireAuthorization();

app.MapPut("/api/appearance", (Appearance wish, AppearanceStore d) =>
        Results.Ok(d.Set(wish)))
   .RequireAuthorization();

// The language switch.
//
// Deliberately a page request with a redirect and not a click inside the
// running Blazor circuit: the language is decided while the request is being
// built, not afterwards. A switch without a reload would leave half the page
// in the old language - exactly the picture I want to avoid.
//
// Without requiring sign-in, so it can be switched on the sign-in page too.
// The cookie carries a display language and nothing else.
app.MapGet("/language/{code}", (string code, string? back, HttpContext http,
        AppearanceStore appearance) =>
{
    var culture = Strings.CultureToCode(code);
    if (culture is null)
    {
        // Unknown code: do not fail, just do nothing. A typo in the address
        // should not strand anybody on an error page.
        return Results.LocalRedirect("/");
    }

    http.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions
        {
            // A year: choosing a language is not a session matter. Not HttpOnly,
            // because the browser extension should be able to read it too.
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

    // And record it server-side. Not for the interface - that reads the
    // cookie - but for the browser extension: it cannot reach the cookie and
    // asks /api/ext/appearance instead.
    appearance.SetLanguage(code.ToLowerInvariant());

    // Back to where you were - but only if that is a path on this
    // installation. The check lives in ReturnPath so a test can reach it; why
    // it is needed is written there.
    return Results.LocalRedirect(ReturnPath.Safe(back));
}).AllowAnonymous();

// The sensor for Windows. As with the extension's package: with sign-in,
// because while it carries no secrets it is also nobody's business who is
// not signed in to the dashboard.
app.MapGet("/api/sensor/package", (SensorPackage package, HttpRequest query) =>
{
    var data = package.Pack(SensorPackage.BaseFrom(query));
    return data is null
        ? Results.NotFound(new { error = "The sensor is not in this image." })
        : Results.File(data, "application/zip", "auspex-sensor.zip");
}).RequireAuthorization();

app.MapExtensionApi();

app.MapGet("/api/router/catalog", async (RouterAdmin router, CancellationToken ct) =>
{
    if (!router.Configured)
    {
        return Results.NotFound(new { error = "No router account has been stored." });
    }

    var k = await router.GetCatalogAsync(ct);
    return Results.Ok(new
    {
        modell = k.Model,
        name = k.FriendlyName,
        readOnly = router.ReadOnly,
        complete = k.IsComplete,
        fehlend = k.Incomplete,
        services = k.Services.Where(d => d.Actions.Count > 0).Select(d => new
        {
            name = d.Name,
            controlUrl = d.ControlUrl,
            actions = d.Actions.Select(a => new
            {
                name = a.Name,
                liest = a.IsReadOnly,
                dangerous = a.IsDangerous,
                input = a.In.Select(x => x.Name),
                output = a.Out.Select(x => x.Name),
            }),
        }),
    });
}).RequireAuthorization();

// The home network's IPv4 settings, read through the box's web interface.
// As an API, so the state before and after a change can be compared field by
// field - with a form of over a hundred fields, "looks fine" is not a check.
app.MapGet("/api/router/ipv4", async (FritzWebClient web, CancellationToken ct) =>
{
    var snapshot = await web.GetIpv4Async(ct);
    return snapshot is null
        ? Results.BadRequest(new { error = "The IPv4 settings cannot be read." })
        : Results.Ok(new
        {
            lokalerDns = snapshot.LocalDns,
            boxAddress = snapshot.BoxAddress,
            subnetMask = snapshot.SubnetMask,
            dhcpOn = snapshot.DhcpOn,
            dhcpFrom = snapshot.DhcpFrom,
            dhcpTo = snapshot.DhcpTo,
            leaseDays = snapshot.LeaseDays,
            pointsAtTheBox = snapshot.PointsAtTheBox,
            fields = snapshot.AllFields,
        });
}).RequireAuthorization();

app.MapPost("/api/router/ipv4/dns", async (
    FritzWebClient web, LocalDnsChange query, CancellationToken ct) =>
{
    var (ok, report) = await web.SetLokalerDnsAsync(query.Address, ct);
    return ok
        ? Results.Ok(new { ok = true, report })
        : Results.BadRequest(new { ok = false, error = report });
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/api/router/call", async (
    RouterAdmin router, RouterCall query, CancellationToken ct) =>
{
    if (!router.Configured)
    {
        return Results.NotFound(new { error = "No router account has been stored." });
    }

    var r = await router.InvokeAsync(
        query.Service, query.ControlUrl, query.Action,
        query.Values ?? new Dictionary<string, string?>(), ct);

    return r.Ok
        ? Results.Ok(new { ok = true, values = r.Values })
        : Results.BadRequest(new { ok = false, error = r.Error });
}).RequireAuthorization().DisableAntiforgery();

// The archive goes into a temporary file first, and only then out.
//
// Writing straight into the response body looked leaner and threw:
// ZipArchive writes its central directory on Dispose, and it writes it
// synchronously. Kestrel refuses synchronous writes, so the download ended in
// "Synchronous operations are disallowed" - a 500 with a stack trace that
// names ZipArchive and not the backup. The tests did not catch it because
// they write into a MemoryStream, where synchronous is allowed.
//
// A file and not a MemoryStream: a backup with years of history is not
// something to hold in memory twice. DeleteOnClose means it goes away when
// the response is finished, including when the download breaks off.
app.MapGet("/api/backup", async (BackupService backup, CancellationToken ct) =>
{
    var temp = Path.Combine(Path.GetTempPath(), $"auspex-backup-{Guid.NewGuid():N}.zip");
    await using (var file = File.Create(temp))
    {
        await backup.WriteAsync(file, ct);
    }

    var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read,
        bufferSize: 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
    return Results.File(stream, "application/zip", backup.DefaultFileName);
}).RequireAuthorization();

// Health check: tests whether the database answers - "the process is alive"
// would also be passed by a control plane stuck on a locked file that can no
// longer analyse anything.
app.MapGet("/healthz", async (AnalyticsDbContext database, CancellationToken ct) =>
{
    try
    {
        await database.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        return Results.Ok("ok");
    }
    catch (Exception ex)
    {
        return Results.Problem($"The database is not answering: {ex.Message}", statusCode: 503);
    }
}).AllowAnonymous();

// Signing in on a separate, plain endpoint: here it is certain the response
// has not begun when the cookie is set.
app.MapPost("/signin", async (
    HttpContext http,
    PasswordAuth auth,
    IAntiforgery antiforgery,
    [FromForm] string? username,
    [FromForm] string? password,
    [FromForm] string? returnUrl) =>
{
    // Antiforgery is checked by hand here rather than letting it reject the
    // form.
    //
    // The reason: if the check fails - because the page sat open for a long
    // time, or because the key ring changed, say after a rename or a
    // restored backup - then the request never gets here at all. The browser
    // gets the sign-in page back, with not a word about it. Whoever typed
    // the right password just watches it disappear again and assumes they
    // mistyped. That is exactly what happened on 23 August 2026, and it cost
    // an hour of looking in the wrong place.
    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException)
    {
        app.Logger.LogWarning(
            "Sign-in rejected: the antiforgery token is invalid (origin {Origin}). "
            + "Usually a stale page or a rotated key ring.",
            http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt");
        return Results.Redirect("/login?error=stale");
    }

    if (!auth.Verify(username, password))
    {
        // Failed attempts used to be invisible: neither a typo nor somebody
        // knocking from outside left a trace. The user name and the origin
        // belong in the log, the password never. The length is on debug only
        // - it gives away little but helps immediately with the question of
        // whether what somebody typed arrived at all.
        app.Logger.LogWarning(
            "Sign-in failed: user \"{User}\" from {Origin}",
            username ?? "(empty)", http.Connection.RemoteIpAddress?.ToString() ?? "(unknown)");
        app.Logger.LogDebug(
            "Failed attempt: the password sent was {Length} characters long",
            password?.Length ?? 0);
        return Results.Redirect("/login?error=1");
    }

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, username ?? "admin")],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    // Local targets only: an open redirect would be a phishing tool with this
    // application's trust behind it.
    var destination = returnUrl is not null && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
        ? returnUrl
        : "/";
    return Results.Redirect(destination);
}).AllowAnonymous().DisableAntiforgery();

app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).AllowAnonymous().DisableAntiforgery();

// Without AllowAnonymous the fallback rule applies here too: CSS and
// blazor.web.js would be redirected to the sign-in page, and the browser
// would get HTML instead of a stylesheet. The sign-in page itself would then
// look unstyled and Blazor would not run - which is exactly how it showed.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// The time zone belongs in the log, because otherwise it can be invisibly
// wrong. The container stood on UTC, the interface dutifully computed
// ToLocalTime() - and showed everything two hours early in summer, without
// anything anywhere looking broken. If TZ drops out (missing tzdata, a typo
// in the name) you land right back there. Then at least it is written here.
// Touch the store once here, so a stored zone applies before the first page
// is built - and not only when somebody happens to open the settings.
using (var start = app.Services.CreateScope())
{
    start.ServiceProvider.GetRequiredService<AppearanceStore>();
}

var displayZone = Auspex.Control.Services.Localization.DisplayTime.Zone;
if (displayZone.Id is "UTC" or "Etc/UTC")
{
    app.Logger.LogWarning(
        "The display is computing in UTC. Is TZ set and tzdata in the image - or "
        + "has a zone been chosen under Settings? Otherwise every time in the "
        + "dashboard is out by the offset.");
}
else
{
    app.Logger.LogInformation(
        "The display computes in {Zone} (currently {Offset}).",
        displayZone.Id,
        displayZone.GetUtcOffset(DateTime.UtcNow));
}

app.Run();

/// <summary>One invocation of a router action over the HTTP API.</summary>
public record RouterCall(
    string Service,
    string ControlUrl,
    string Action,
    Dictionary<string, string?>? Values);

/// <summary>A change to the local DNS server.</summary>
public record LocalDnsChange(string Address);
