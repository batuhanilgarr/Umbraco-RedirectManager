namespace Umbraco.RedirectManager.Models;

public enum MissedRequestCategory
{
    Unclassified,
    MaliciousScanner,
    MissingAsset,
    RedirectNeeded,
    Gone,
    TypoMalformed,
    NeedsInvestigation
}
