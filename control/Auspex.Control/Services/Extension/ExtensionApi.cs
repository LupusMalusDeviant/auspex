using System.Net;
using Auspex.Control.Data;
using Microsoft.EntityFrameworkCore;

using Auspex.Control.Services.Localization;

namespace Auspex.Control.Services.Extension;

/// <summary>
/// The browser extension's API.
///
/// Deliberately separate from the rest: it has its own sign-in, its own cut
/// and is explicitly allowed only what a device should do for itself. No
/// other device can be changed through it — which one is meant is said not
/// by the caller but by its sender address.
/// </summary>
public static class ExtensionApi
{
    public static void MapExtensionApi(this WebApplication app)
    {
        // AllowAnonymous is not a hole here but a redirection to a different
        // guard: the fallback rule demands the dashboard's session cookie,
        // and that is exactly what an extension cannot send. Every endpoint
        // beneath checks the token instead - and does so first of all.
        var group = app.MapGroup("/api/ext")
            .AllowAnonymous()
            .DisableAntiforgery();

        // The sensor on the machine reports here. The same group, because the
        // same rule applies: sign-in is by token, and which device is meant
        // is said by the sender address.
        group.MapSensorApi();

        // Who am I?
        //
        // The extension does not need to know its own device and should not
        // be able to pick it either: it follows from the address the request
        // comes from. So nobody can set an exception for the neighbours' TV
        // through the extension.
        // The appearance, so the extension's window looks like the dashboard.
        // Deliberately here and not under /api/appearance: there the sign-in
        // by session cookie applies, and the extension only has its token.
        group.MapGet("/appearance", (HttpContext http, IExtensionTokenStore token,
                                       AppearanceStore d) =>
            SignedIn(http, token) ? Results.Ok(d.Current) : Rejected());

        group.MapGet("/me", async (
            HttpContext http, IExtensionTokenStore token, IAuspexClient auspex,
            ExceptionService exceptions, CancellationToken ct) =>
        {
            if (!SignedIn(http, token))
            {
                return Rejected();
            }

            var who = await WhoAsync(http, auspex, ct);
            if (who is null)
            {
                return Results.Ok(new
                {
                    known = false,
                    hint = Strings.Current.DeviceNotRecognised,
                });
            }

            var running = who.Name is { Length: > 0 }
                ? await exceptions.LaufendeAsync(who.Name, ct)
                : [];

            return Results.Ok(new
            {
                known = true,
                device = who.Name,
                mac = who.Mac,
                profile = who.Profile,
                address = who.Ip,
                exceptions = running.Select(t => new
                {
                    t.Domain,
                    expires = t.UntilUtc,
                    remainingSeconds = (int)Math.Max(0, (t.UntilUtc - DateTime.UtcNow).TotalSeconds),
                }),
            });
        });

        // Allow. Without a minute count, permanently.
        group.MapPost("/allow", async (
            HttpContext http, IExtensionTokenStore token, IAuspexClient auspex,
            ExceptionService exceptions, TemporaryAllowRequest query, CancellationToken ct) =>
        {
            if (!SignedIn(http, token))
            {
                return Rejected();
            }

            var who = await WhoAsync(http, auspex, ct);
            if (who?.Name is not { Length: > 0 })
            {
                return Results.BadRequest(new { ok = false, error = Strings.Current.DeviceNotRecognisedShort });
            }

            TimeSpan? duration = query.Minutes is > 0
                ? TimeSpan.FromMinutes(Math.Min(query.Minutes.Value, 7 * 24 * 60))
                : null;

            var e = await exceptions.ErlaubeAsync(
                who.Name, who.Mac ?? "", query.Domain, duration, "extension", ct);

            return e.Ok
                ? Results.Ok(new { ok = true, report = e.ReportItem, forwarded = e.Forwarded })
                : Results.BadRequest(new { ok = false, error = e.ReportItem });
        });

        // Withdraw.
        group.MapPost("/revoke", async (
            HttpContext http, IExtensionTokenStore token, IAuspexClient auspex,
            ExceptionService exceptions, TemporaryAllowRequest query, CancellationToken ct) =>
        {
            if (!SignedIn(http, token))
            {
                return Rejected();
            }

            var who = await WhoAsync(http, auspex, ct);
            if (who?.Name is not { Length: > 0 })
            {
                return Results.BadRequest(new { ok = false, error = Strings.Current.DeviceNotRecognisedShort });
            }

            var e = await exceptions.WiderrufeAsync(who.Name, query.Domain, ct);
            return e.Ok
                ? Results.Ok(new { ok = true, report = e.ReportItem })
                : Results.BadRequest(new { ok = false, error = e.ReportItem });
        });

        // What was last blocked for this device?
        //
        // Through the browser the extension only sees what the browser asked
        // for. What an app tried in the background is only here.
        group.MapGet("/blocked", async (
            HttpContext http, IExtensionTokenStore token, IAuspexClient auspex,
            AnalyticsDbContext db, int? minutes, CancellationToken ct) =>
        {
            if (!SignedIn(http, token))
            {
                return Rejected();
            }

            var who = await WhoAsync(http, auspex, ct);
            if (who?.Name is not { Length: > 0 })
            {
                return Results.BadRequest(new { ok = false, error = Strings.Current.DeviceNotRecognisedShort });
            }

            var since = DateTime.UtcNow.AddMinutes(-Math.Clamp(minutes ?? 30, 1, 24 * 60));
            var hits = await db.Queries
                .Where(q => q.TimeUtc >= since && q.Action == "blocked" && q.ClientName == who.Name)
                .GroupBy(q => q.Name)
                .Select(g => new { name = g.Key, count = g.Count(), last = g.Max(x => x.TimeUtc) })
                .OrderByDescending(x => x.last)
                .Take(50)
                .ToListAsync(ct);

            return Results.Ok(new { ok = true, device = who.Name, hits });
        });
    }

    private static bool SignedIn(HttpContext http, IExtensionTokenStore token)
    {
        var header = http.Request.Headers.Authorization.ToString();
        var value = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : http.Request.Headers["X-Auspex-Token"].ToString();

        return token.Checks(value);
    }

    private static IResult Rejected() =>
        Results.Json(new { error = Strings.Current.TokenNoLongerValid }, statusCode: 401);

    /// <summary>
    /// Asks the resolver which device is behind the sender address.
    ///
    /// Deliberately there and not here: the resolver keeps the neighbour
    /// table and the device list anyway. Building the same mapping a second
    /// time would mean maintaining two truths.
    /// </summary>
    private static async Task<WhoEntry?> WhoAsync(
        HttpContext http, IAuspexClient auspex, CancellationToken ct)
    {
        var address = http.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var who = await auspex.WhoAsync(address.ToString(), ct);
        return who is { Known: true } ? who : null;
    }
}

public record TemporaryAllowRequest(string Domain, int? Minutes);
