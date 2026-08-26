namespace Auspex.Control.Services;

public enum RuleKind
{
    /// <summary>Hits the name itself and nothing else.</summary>
    Exact,
    /// <summary>Hits the name and every subdomain.</summary>
    Suffix,
    /// <summary>Hits subdomains only, not the name itself.</summary>
    SubOnly,
}

/// <remarks>
/// Deliberately with no display text. <c>KindLabel</c> and <c>ActionLabel</c>
/// once stood here - German captions on a record that mirrors the data
/// plane's rule semantics. That went fine while there was one language; with
/// two the record would have had a language, which it cannot be. What a rule
/// kind is called is now said by Strings.RuleKindLabel.
/// </remarks>
public record ParsedRule(string Pattern, RuleKind Kind, bool IsAllow, string Raw);

/// <summary>
/// Mirrors the data plane's rule semantics, so the impact analysis
/// understands the same thing the resolver does. The formats and their
/// meaning are deliberately identical — a rule read differently here from
/// there would be worse than no analysis at all.
/// </summary>
public static class RuleParser
{
    public static ParsedRule? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var line = raw.Trim();
        if (line[0] is '#' or '!' or ';' or '[') return null;

        var isAllow = false;
        if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            isAllow = true;
            line = line[2..];
        }

        // AdBlock-Syntax
        if (line.StartsWith("||", StringComparison.Ordinal))
        {
            var body = line[2..];
            if (body.Contains('$')) return null; // modifiers are not expressible in DNS
            body = body.TrimEnd('^', '|');
            return Plausible(body) ? new ParsedRule(Normalize(body), RuleKind.Suffix, isAllow, raw) : null;
        }

        if (line.IndexOfAny(['$', '#', '@', '/', '^', '|']) >= 0) return null;

        // Wildcard
        if (line.StartsWith("*.", StringComparison.Ordinal))
        {
            var body = line[2..];
            return Plausible(body) ? new ParsedRule(Normalize(body), RuleKind.SubOnly, isAllow, raw) : null;
        }

        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length switch
        {
            // Hosts-Format gilt exakt, nackte Domains samt allem darunter.
            >= 2 when System.Net.IPAddress.TryParse(fields[0], out _) && Plausible(fields[1])
                => new ParsedRule(Normalize(fields[1]), RuleKind.Exact, isAllow, raw),
            1 when Plausible(fields[0])
                => new ParsedRule(Normalize(fields[0]), RuleKind.Suffix, isAllow, raw),
            _ => null,
        };
    }

    private static string Normalize(string s) => s.Trim().TrimEnd('.').ToLowerInvariant();

    private static bool Plausible(string s)
    {
        s = Normalize(s);
        if (s.Length == 0 || s.Length > 253) return false;
        if (System.Net.IPAddress.TryParse(s, out _)) return false;
        if (!s.Contains('.')) return false;

        foreach (var label in s.Split('.'))
        {
            if (label.Length == 0 || label.Length > 63) return false;
            foreach (var c in label)
            {
                var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
                if (!ok) return false;
            }
        }
        return true;
    }
}
