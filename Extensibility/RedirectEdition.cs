namespace Umbraco.RedirectManager.Extensibility;

public enum RedirectEdition
{
    Standard = 0,
    Premium = 1,
    Company = 2
}

/// <summary>
/// Reports which edition is active. The core registers a Standard provider by
/// default (via TryAdd); a commercial package replaces it with one that
/// validates its license. Implementations must be cheap and must never throw.
/// </summary>
public interface IRedirectEditionProvider
{
    RedirectEdition Edition { get; }

    /// <summary>Stable feature keys enabled for this installation (empty in Standard).</summary>
    IReadOnlyCollection<string> EnabledFeatures { get; }
}

public sealed class StandardEditionProvider : IRedirectEditionProvider
{
    public RedirectEdition Edition => RedirectEdition.Standard;

    public IReadOnlyCollection<string> EnabledFeatures { get; } = Array.Empty<string>();
}
