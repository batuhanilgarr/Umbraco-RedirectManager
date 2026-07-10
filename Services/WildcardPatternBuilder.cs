using System.Text.RegularExpressions;

namespace Umbraco.RedirectManager.Services;

public static class WildcardPatternBuilder
{
    /// <summary>
    /// Splits <paramref name="oldUrl"/> at its first '*' into a prefix and
    /// suffix, escapes each side (so a literal "." or "+" in the URL is
    /// treated as a literal character, not a regex metacharacter -- the
    /// whole point of this being usable without regex knowledge), and joins
    /// them with a capturing "(.*)", anchored at both ends so this behaves
    /// as a whole-path match rather than a substring match (an unanchored
    /// pattern would also match a path that merely contains the prefix and
    /// suffix somewhere in the middle). If <paramref name="oldUrl"/> has no
    /// '*' at all, it's escaped and anchored as a literal exact-match
    /// pattern -- a defensive fallback, since callers are only expected to
    /// pass values already confirmed to contain '*'.
    /// </summary>
    public static string BuildRegexPattern(string oldUrl)
    {
        var starIndex = oldUrl.IndexOf('*');
        if (starIndex < 0)
            return "^" + Regex.Escape(oldUrl) + "$";

        var prefix = oldUrl[..starIndex];
        var suffix = oldUrl[(starIndex + 1)..];
        return "^" + Regex.Escape(prefix) + "(.*)" + Regex.Escape(suffix) + "$";
    }
}
