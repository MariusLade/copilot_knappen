using System.Text;

namespace CopilotKnappen.Core;

public enum AppEntryKind
{
    /// <summary>An item of shell:AppsFolder (Start menu app, including Store apps). Target is the parsing name / AUMID.</summary>
    AppsFolder,
    /// <summary>A .lnk file or other file path opened through the shell.</summary>
    Shortcut,
    /// <summary>A program name resolved through PATH, e.g. "cmd.exe".</summary>
    Executable,
    /// <summary>A URI such as ms-settings:.</summary>
    Uri,
}

public sealed record AppEntry(string Name, string LaunchTarget, AppEntryKind Kind);

public interface IAppCatalog
{
    IReadOnlyList<AppEntry> Apps { get; }
}

public sealed class StaticAppCatalog(IEnumerable<AppEntry> apps) : IAppCatalog
{
    public IReadOnlyList<AppEntry> Apps { get; } = apps.ToList();
}

/// <summary>Fuzzy matching of a typed name against installed application names.</summary>
public static class AppMatcher
{
    /// <summary>Score above which a plain prompt is treated as "open this app".</summary>
    public const double ImplicitThreshold = 0.8;

    /// <summary>Score above which "open xyz" is accepted.</summary>
    public const double ExplicitThreshold = 0.55;

    public static (AppEntry? App, double Score) FindBest(IEnumerable<AppEntry> apps, string query)
    {
        var q = Normalize(query);
        if (q.Length == 0) return (null, 0);
        var wantsUninstall = q.Contains("uninstall");

        AppEntry? best = null;
        double bestScore = 0;
        foreach (var app in apps)
        {
            var name = Normalize(app.Name);
            if (!wantsUninstall && (name.StartsWith("uninstall") || name.Contains(" uninstall"))) continue;

            var score = Score(q, name);
            if (score <= 0) continue;
            if (app.Kind == AppEntryKind.AppsFolder) score += 0.001; // prefer Start-menu entries on ties

            if (score > bestScore + 1e-9 ||
                (Math.Abs(score - bestScore) < 1e-9 && best is not null && app.Name.Length < best.Name.Length))
            {
                best = app;
                bestScore = score;
            }
        }
        return (best, Math.Min(bestScore, 1.0));
    }

    /// <summary>Scores a normalized query against a normalized name (0 = no match, 1 = identical).</summary>
    internal static double Score(string q, string name)
    {
        if (name.Length == 0) return 0;
        if (q == name) return 1.0;

        var qCompact = q.Replace(" ", "");
        var nCompact = name.Replace(" ", "");
        if (qCompact == nCompact) return 0.98;

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var qWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var ratio = (double)qCompact.Length / nCompact.Length;

        // The name starts with the query on a word boundary ("visual studio" -> "visual studio code").
        if (name.StartsWith(q + " ")) return 0.93 + 0.05 * ratio;

        // A single query word equals a word of the name ("word" -> "microsoft word", "chrome" -> "google chrome").
        if (qWords.Length == 1 && q.Length >= 3 && words.Contains(q)) return 0.9 + 0.05 * ratio;

        // The name starts with the query mid-word ("note" -> "notepad", "calc" -> "calculator").
        if (q.Length >= 3 && name.StartsWith(q)) return 0.85 + 0.05 * ratio;
        if (qCompact.Length >= 3 && nCompact.StartsWith(qCompact)) return 0.83 + 0.05 * ratio;

        // A single query word starts a later word of the name ("chrom" -> "google chrome").
        if (qWords.Length == 1 && q.Length >= 3)
        {
            foreach (var w in words)
            {
                if (w.StartsWith(q)) return 0.7 + 0.15 * q.Length / w.Length;
            }
        }

        // Every query word is a prefix of a distinct name word, in order ("visual studio" -> "visual studio code").
        if (qWords.Length > 1 && WordsPrefixInOrder(qWords, words))
            return 0.82 + 0.1 * ratio;

        // Acronyms and word-prefix abbreviations ("vsc", "vscode" -> "visual studio code").
        if (qWords.Length == 1 && q.Length >= 2 && words.Length >= 2)
        {
            if (IsPrefixConcatenation(q, words, 0)) return 0.8 + 0.05 * ratio;
            var initials = new string(words.Select(w => w[0]).ToArray());
            if (initials.StartsWith(q) && q.Length >= 3) return 0.72;
        }

        // Characters in order (weak, only useful for explicit "open ..." prompts).
        if (qCompact.Length >= 3 && IsSubsequence(qCompact, nCompact))
            return 0.45 + 0.2 * ratio;

        return 0;
    }

    private static bool WordsPrefixInOrder(string[] qWords, string[] words)
    {
        var i = 0;
        foreach (var w in words)
        {
            if (i < qWords.Length && w.StartsWith(qWords[i])) i++;
        }
        return i == qWords.Length;
    }

    /// <summary>True when <paramref name="q"/> is a non-empty prefix of every word, concatenated in order.</summary>
    private static bool IsPrefixConcatenation(string q, string[] words, int wordIndex)
    {
        if (wordIndex == words.Length) return q.Length == 0;
        if (q.Length == 0) return false;
        var word = words[wordIndex];
        for (var len = Math.Min(word.Length, q.Length); len >= 1; len--)
        {
            if (string.CompareOrdinal(q, 0, word, 0, len) == 0 && IsPrefixConcatenation(q[len..], words, wordIndex + 1))
                return true;
        }
        return false;
    }

    private static bool IsSubsequence(string q, string s)
    {
        var i = 0;
        foreach (var c in s)
        {
            if (i < q.Length && q[i] == c) i++;
        }
        return i == q.Length;
    }

    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = true;
        foreach (var c in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '+' || c == '#')
            {
                sb.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }
        return sb.ToString().TrimEnd();
    }
}
