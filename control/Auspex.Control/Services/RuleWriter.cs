using Microsoft.Extensions.Options;

namespace Auspex.Control.Services;

public class RuleFileOptions
{
    public const string SectionName = "Rules";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Two files both sides share: the control plane writes them, the
    /// resolver reads them as lists — one with <c>allow: true</c>, the other
    /// as an ordinary block list. No additional API route needed, the list
    /// mechanism already carries it.
    /// </summary>
    public string AllowPath { get; set; } = "var/allowlist.txt";

    public string BlockPath { get; set; } = "var/blocklist.txt";
}

/// <summary>Which of the two rule files is meant.</summary>
public enum RuleTarget
{
    Allow,
    Block,
}

/// <summary>
/// Result of a write attempt. Writing and reloading are two separate things:
/// the rule can be in the file while the resolver happens to be unreachable —
/// it then takes effect on the next reload. Squeezing both into one bool
/// would make the interface lie.
/// </summary>
public record RuleWriteResult(bool Written, bool Reloaded, string? Error = null);

/// <summary>
/// Writes our own rules into the shared files and has the resolver reload.
/// That turns "something is wrong here" into a click.
/// </summary>
public sealed class RuleWriter(
    IOptions<RuleFileOptions> options,
    IAuspexClient auspex,
    ILogger<RuleWriter> log)
    : IRuleWriter
{
    private readonly RuleFileOptions _opt = options.Value;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public bool Enabled => _opt.Enabled;

    public string PathFor(RuleTarget target)
        => target == RuleTarget.Block ? _opt.BlockPath : _opt.AllowPath;

    /// <summary>Appends a rule and reloads the rule set.</summary>
    public async Task<RuleWriteResult> AddAsync(
        string rule,
        string reason,
        RuleTarget target = RuleTarget.Allow,
        CancellationToken ct = default)
    {
        if (!_opt.Enabled || string.IsNullOrWhiteSpace(rule))
        {
            return new RuleWriteResult(false, false, Localization.Strings.Current.RuleWritingOff);
        }

        var path = PathFor(target);

        await Gate.WaitAsync(ct);
        try
        {
            var existing = await ReadAsync(target, ct);
            if (existing.Any(l => string.Equals(l.Trim(), rule.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                log.LogInformation("Rule {Rule} is already in {Path}", rule, path);
                // Reload all the same: perhaps the resolver does not know it yet.
                return new RuleWriteResult(true, await auspex.ReloadAsync(force: false, ct));
            }

            EnsureDirectory(path);

            // The comment still explains a year from now why the line is there.
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            await File.AppendAllTextAsync(path,
                $"# {stamp} — {reason}{Environment.NewLine}{rule}{Environment.NewLine}", ct);

            log.LogInformation("Rule {Rule} added ({Reason})", rule, reason);

            // force:false — local files are read fresh anyway, and the big lists
            // should not be downloaded again for this.
            return new RuleWriteResult(true, await auspex.ReloadAsync(force: false, ct));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError(ex, "The rule could not be written: {Path}", path);
            return new RuleWriteResult(false, false, ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ReadAsync(
        RuleTarget target = RuleTarget.Allow, CancellationToken ct = default)
    {
        var path = PathFor(target);
        if (!File.Exists(path)) return [];
        try
        {
            return await File.ReadAllLinesAsync(path, ct);
        }
        catch (IOException ex)
        {
            log.LogWarning(ex, "The rule file cannot be read: {Path}", path);
            return [];
        }
    }

    /// <summary>
    /// Creates both files if they are missing and triggers one reload. The
    /// resolver starts before the control plane and therefore does not find
    /// them when it first builds the rule set.
    /// </summary>
    public async Task EnsureExistsAsync(CancellationToken ct = default)
    {
        if (!_opt.Enabled) return;

        var created = false;
        foreach (var target in new[] { RuleTarget.Allow, RuleTarget.Block })
        {
            created |= await EnsureOneAsync(target, ct);
        }
        if (!created) return;

        // A best-effort attempt: if it does not work, the next reload catches
        // up — nothing functional hangs off empty files.
        if (!await auspex.ReloadAsync(force: false, ct))
        {
            log.LogInformation("Rule files created, the resolver was unreachable for the reload");
        }
    }

    private async Task<bool> EnsureOneAsync(RuleTarget target, CancellationToken ct)
    {
        var path = PathFor(target);
        if (File.Exists(path)) return false;

        try
        {
            EnsureDirectory(path);
            var kind = target == RuleTarget.Block ? "Blocks" : "Ausnahmen";
            var how = target == RuleTarget.Block ? "ordinary list" : "a list with allow: true";
            await File.WriteAllTextAsync(path,
                $"# Eigene {kind}, von Auspex.Control angelegt.{Environment.NewLine}"
                + $"# The resolver reads this file as {how}.{Environment.NewLine}", ct);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "The rule file could not be created: {Path}", path);
            return false;
        }
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
