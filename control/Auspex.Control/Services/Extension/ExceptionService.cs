using Auspex.Control.Data;
using Microsoft.EntityFrameworkCore;

using Auspex.Control.Services.Localization;

namespace Auspex.Control.Services.Extension;

/// <summary>
/// Sets exceptions for a single device and clears the timed ones away again.
///
/// The rule itself lives in the resolver's device profile — that is the only
/// truth it goes by. The database only remembers when one of them should
/// disappear again. Two sources for the same statement would be an
/// invitation to let them drift apart.
/// </summary>
public class ExceptionService(
    IAuspexClient auspex,
    AnalyticsDbContext db,
    ILogger<ExceptionService> log)
{
    /// <summary>How long a timed exception lasts by default.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Allows a name for a device.
    ///
    /// An empty <paramref name="duration"/> means permanent. Creates the
    /// device profile if there is none yet — bound to the MAC, not to the
    /// address: under IPv6 an address binding would be worthless from
    /// tomorrow.
    /// </summary>
    public async Task<ExceptionResult> ErlaubeAsync(
        string device, string mac, string domain, TimeSpan? duration, string source,
        CancellationToken ct = default)
    {
        domain = Normalisiere(domain);
        if (domain.Length == 0)
        {
            return new ExceptionResult(false, Strings.Current.NotAValidName);
        }

        var rule = $"@@||{domain}^";

        var profiles = await auspex.GetClientsAsync(ct);
        var profile = profiles.FirstOrDefault(p =>
            p.Name.Equals(device, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            if (string.IsNullOrWhiteSpace(mac))
            {
                return new ExceptionResult(false, Strings.Current.NoProfileWithoutMac);
            }

            profile = new ManagedClient { Name = device, Macs = [mac] };
            log.LogInformation("Creating profile {Device}, bound to {Mac}", device, mac);
        }
        else if (!string.IsNullOrWhiteSpace(mac)
                 && !profile.Macs.Any(m => m.Equals(mac, StringComparison.OrdinalIgnoreCase)))
        {
            // A profile that hung off addresses only gets the MAC added - or it
            // stops applying at the next address change.
            profile.Macs.Add(mac);
            log.LogInformation("Profile {Device} additionally bound to {Mac}", device, mac);
        }

        if (profile.AllowRules.Any(r => r.Equals(rule, StringComparison.OrdinalIgnoreCase)))
        {
            // Already there: with a time limit, at least extend the deadline
            // rather than adding a second identical line.
            //
            // Clear the cache all the same. Whoever clicks a second time
            // almost always does so because nothing seemed to happen the
            // first time - and if precisely this route skipped the clearing,
            // nothing would happen again.
            await auspex.ForgetNameAsync(domain, ct);

            if (duration is { } d)
            {
                await SetDeadlineAsync(device, rule, domain, d, source, ct);
                return new ExceptionResult(true, Strings.Current.AlreadyAllowedDeadlineSet(domain, d));
            }
            await ClearDeadlineAsync(device, rule, ct);
            return new ExceptionResult(true, Strings.Current.AlreadyPermanentlyAllowed(domain));
        }

        profile.AllowRules.Add(rule);
        var error = await auspex.PutClientAsync(profile, ct);
        if (error is not null)
        {
            return new ExceptionResult(false, error);
        }

        // Without this the exception only takes effect once the negative TTL
        // expires - and to whoever just clicked, that looks like it did not
        // work.
        await auspex.ForgetNameAsync(domain, ct);

        // The name is allowed - which does not yet mean it resolves. If it
        // points by CNAME at something that is also on a list, the cloaking
        // check catches it anyway. That is exactly what happened with
        // analytics.tiktok.com: allowed, and the page still would not load.
        // Whoever reads "is allowed" then concludes the tool is broken.
        // The target name and the sentence about it are now two things.
        // Before there was only the sentence, and NextName() dug the name
        // back out of it - by the substring "Weiterleitung auf ". That worked
        // as long as there was one language; in English the phrase is
        // "redirect to", and the second button would have vanished silently.
        var destination = await ForwardingBlockedAsync(device, domain, ct);
        var extra = destination is null ? "" : Strings.Current.ForwardingBlocked(destination);

        if (duration is { } deadline)
        {
            await SetDeadlineAsync(device, rule, domain, deadline, source, ct);
            log.LogInformation("{Domain} allowed for {Device}, {Duration}",
                domain, device, Strings.Current.Deadline(deadline));
            return new ExceptionResult(true,
                Strings.Current.AllowedFor(domain, deadline, extra), destination);
        }

        await ClearDeadlineAsync(device, rule, ct);
        log.LogInformation("{Domain} allowed for {Device} for good", domain, device);
        return new ExceptionResult(true,
            Strings.Current.PermanentlyAllowed(domain, extra), destination);
    }

    /// <summary>Withdraws an exception immediately.</summary>
    public async Task<ExceptionResult> WiderrufeAsync(
        string device, string domain, CancellationToken ct = default)
    {
        domain = Normalisiere(domain);
        var rule = $"@@||{domain}^";

        var profiles = await auspex.GetClientsAsync(ct);
        var profile = profiles.FirstOrDefault(p =>
            p.Name.Equals(device, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return new ExceptionResult(false, Strings.Current.NoProfileForDevice);
        }

        var gone = profile.AllowRules.RemoveAll(r => r.Equals(rule, StringComparison.OrdinalIgnoreCase));
        if (gone == 0)
        {
            return new ExceptionResult(false, Strings.Current.WasNotAllowed(domain));
        }

        var error = await auspex.PutClientAsync(profile, ct);
        if (error is not null)
        {
            return new ExceptionResult(false, error);
        }

        await ClearDeadlineAsync(device, rule, ct);
        // The other way round too: otherwise the answer would still be valid
        // even though the exception is gone.
        await auspex.ForgetNameAsync(domain, ct);
        return new ExceptionResult(true, Strings.Current.BlockedAgain(domain));
    }

    /// <summary>A device's timed exceptions that are still running.</summary>
    public async Task<IReadOnlyList<TemporaryAllow>> LaufendeAsync(
        string device, CancellationToken ct = default) =>
        await db.TemporaryAllows
            .Where(t => t.Device == device && t.UntilUtc > DateTime.UtcNow)
            .OrderBy(t => t.UntilUtc)
            .ToListAsync(ct);

    /// <summary>
    /// Removes every expired exception from the profiles.
    ///
    /// Collected per device, so a profile with five expired rules is written
    /// once and not five times — every write makes the resolver rebuild its
    /// rule set.
    /// </summary>
    public async Task<int> CleanUpAsync(CancellationToken ct = default)
    {
        var due = await db.TemporaryAllows
            .Where(t => t.UntilUtc <= DateTime.UtcNow)
            .ToListAsync(ct);
        if (due.Count == 0)
        {
            return 0;
        }

        var profiles = await auspex.GetClientsAsync(ct);
        var removed = 0;

        foreach (var group in due.GroupBy(t => t.Device))
        {
            var profile = profiles.FirstOrDefault(p =>
                p.Name.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                // The profile is gone - so the rule is gone too. The entry can
                // still go.
                continue;
            }

            var before = profile.AllowRules.Count;
            foreach (var t in group)
            {
                profile.AllowRules.RemoveAll(r => r.Equals(t.Rule, StringComparison.OrdinalIgnoreCase));
            }

            if (profile.AllowRules.Count != before)
            {
                var error = await auspex.PutClientAsync(profile, ct);
                if (error is not null)
                {
                    // Try again on the next pass: the entry stays, so the rule does
                    // not sit orphaned in the profile.
                    log.LogWarning("Expired exceptions for {Device} were not removed: {Error}",
                        group.Key, error);
                    continue;
                }
                foreach (var t in group)
                {
                    await auspex.ForgetNameAsync(t.Domain, ct);
                }
                removed += before - profile.AllowRules.Count;
                log.LogInformation("{Count} expired exceptions for {Device} removed",
                    before - profile.AllowRules.Count, group.Key);
            }

            db.TemporaryAllows.RemoveRange(group);
        }

        await db.SaveChangesAsync(ct);
        return removed;
    }

    /// <summary>
    /// Looks for whether the same query was last blocked via a redirect — and
    /// names the target.
    ///
    /// A name can be allowed and still fail to resolve: if it points by CNAME
    /// at something on a list, the cloaking check bites. Without this hint
    /// the user reads "is allowed", the page still does not load, and the
    /// tool looks broken.
    /// </summary>
    private async Task<string?> ForwardingBlockedAsync(
        string device, string domain, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddMinutes(-30);

        // Look specifically for a block via the redirect, not for the most
        // recent block at all: the most recent one is almost always the
        // direct rule the user has just allowed. That is precisely the one
        // not to report here.
        var rule = await db.Queries
            .Where(q => q.ClientName == device && q.TimeUtc >= since
                        && q.Action == "blocked" && q.Source == "cname"
                        && q.Name == domain && q.Rule != null)
            .OrderByDescending(q => q.TimeUtc)
            .Select(q => q.Rule)
            .FirstOrDefaultAsync(ct);

        var destination = NameFromRule(rule);
        return destination.Length == 0 || destination == domain ? null : destination;
    }

    /// <summary>Pulls the name out of a rule like <c>||name^</c>.</summary>
    public static string NameFromRule(string? rule)
    {
        var r = (rule ?? "").Trim();
        if (r.StartsWith("@@", StringComparison.Ordinal))
        {
            r = r[2..];
        }
        r = r.TrimStart('|').TrimEnd('^');
        return Normalisiere(r);
    }

    private async Task SetDeadlineAsync(
        string device, string rule, string domain, TimeSpan duration, string source, CancellationToken ct)
    {
        var existing = await db.TemporaryAllows
            .FirstOrDefaultAsync(t => t.Device == device && t.Rule == rule, ct);

        if (existing is null)
        {
            db.TemporaryAllows.Add(new TemporaryAllow
            {
                Device = device,
                Rule = rule,
                Domain = domain,
                CreatedUtc = DateTime.UtcNow,
                UntilUtc = DateTime.UtcNow + duration,
                Source = source,
            });
        }
        else
        {
            existing.UntilUtc = DateTime.UtcNow + duration;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ClearDeadlineAsync(string device, string rule, CancellationToken ct)
    {
        var existing = await db.TemporaryAllows
            .Where(t => t.Device == device && t.Rule == rule)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            db.TemporaryAllows.RemoveRange(existing);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Turns a name into the name. From the extension sometimes a whole URL
    /// arrives, sometimes a name with a trailing dot.
    /// </summary>
    public static string Normalisiere(string raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant().TrimEnd('.');
        if (s.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(s, UriKind.Absolute, out var u))
        {
            s = u.Host;
        }
        if (s.Contains('/', StringComparison.Ordinal))
        {
            s = s[..s.IndexOf('/', StringComparison.Ordinal)];
        }
        if (s.Contains(':', StringComparison.Ordinal))
        {
            s = s[..s.IndexOf(':', StringComparison.Ordinal)];
        }

        // Whatever is left has to look like a name. Otherwise a rule ends up
        // in the profile that never applies.
        return s.Length is > 0 and <= 253
            && s.Contains('.', StringComparison.Ordinal)
            && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            ? s
            : "";
    }

}

/// <param name="Next">
/// A name that would additionally have to be allowed — the redirect it is
/// otherwise still stuck on. So the interface can offer a second button
/// rather than making the user type it out.
/// </param>
public record ExceptionResult(bool Ok, string ReportItem, string? Forwarded = null);
