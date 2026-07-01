namespace Umbraco.RedirectManager.Services;

public static class DomainNormalizer
{
    /// <summary>
    /// Normalizes a domain/host value: trims, lowercases, and strips a
    /// trailing ":port" suffix. Does NOT strip a "www." prefix -- an
    /// intentional choice, since Umbraco sites typically manage www/apex
    /// redirection as their own binding, and silently merging the two here
    /// could surprise anyone expecting an exact hostname match. Null or
    /// whitespace-only input normalizes to null (meaning "global"), so null
    /// and empty string are never treated as distinct values.
    /// </summary>
    public static string? Normalize(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        var value = domain.Trim().ToLowerInvariant();

        // Strip a trailing ":port" (e.g. "example.com:8080" -> "example.com"),
        // including a bare trailing colon with no digits after it (e.g.
        // "example.com:") -- never a valid hostname component, so there's no
        // legitimate value it could be part of. Guard against IPv6 literals in
        // brackets (e.g. "[::1]:8080"), where taking the substring after the
        // last colon would cut into the address itself rather than removing a
        // port.
        var lastColon = value.LastIndexOf(':');
        if (lastColon > 0 && value.IndexOf(']', lastColon) == -1)
        {
            var portPart = value[(lastColon + 1)..];
            if (portPart.Length == 0 || portPart.All(char.IsDigit))
            {
                value = value[..lastColon];
            }
        }

        return value.Length == 0 ? null : value;
    }
}
