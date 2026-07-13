# Rate Limiting — Design

## Context

This is sub-project 8 of a 9-part roadmap for BT.RedirectManager, drawn from
`docs/GELISTIRME-VE-OZELLIK-FIKIRLERI.md` (excluding the "appsettings
config" item, which the user chose not to pursue):

1. Query string koruma (preserve query string) (done)
2. Geçerlilik tarihleri (valid from / until) (done)
3. Basit wildcard (`*`) eşleşme (done)
4. Audit alanları (CreatedBy / ModifiedBy) (done)
5. Health check endpoint (done)
6. Unit / entegrasyon testleri (done)
7. Çakışma / duplicate uyarısı (done)
8. **Rate limiting** (this spec)
9. Culture / çoklu site kapsamı (multi-site scoping)

Each sub-project gets its own spec → plan → implementation cycle. This
document covers only sub-project 8, corresponding to roadmap item 14
("Rate limiting (isteğe bağlı)").

## Problem

Nothing currently protects the redirect-serving path from a single client
hammering it with a high volume of requests (e.g. abusive scraping,
enumeration of redirect rules, or accidental retry storms). The roadmap
proposes either a hard `429` response or a log-only warning once a single
IP exceeds a configurable request threshold within a time window, scoped
only to requests that actually trigger a redirect — not all site traffic.

## Design

### Scope decision (confirmed with user)

- **Opt-in, disabled by default** — mirrors this codebase's existing
  `RedirectBackupOptions.Enabled` convention (`Models/RedirectBackupOptions.cs`),
  configured via `IOptions<T>` bound to a new `RedirectManager:RateLimit`
  appsettings section (the same binding pattern already used for
  `RedirectManager:Backup`). This is *not* the same "appsettings config"
  roadmap item the user declined earlier (that one was specifically about
  making skip-paths/cache-duration configurable) — this codebase already
  has precedent for feature-specific `IOptions<T>` sections.
- **Default mode when enabled: `LogOnly`** — confirmed with user. Enabling
  the feature never blocks real traffic on its own; an admin must
  additionally set `Mode: "Block"` once they've observed the logs and are
  confident in the threshold.
- **Only counts requests that actually match an active redirect rule**
  (exact, wildcard, or regex tier) — not skip-listed paths, and not
  genuinely-missed requests that fall through to `_next` (those are already
  tracked separately by the existing `IMissedRequestTracker`/404 log
  feature). This matches the roadmap text precisely: "Aynı IP'den çok
  sayıda **redirect isteğinde**" — many *redirect* requests, not all
  traffic.

### New options: `Models/RedirectRateLimitOptions.cs`

```csharp
public class RedirectRateLimitOptions
{
    public bool Enabled { get; set; } = false;
    public int MaxRequestsPerWindow { get; set; } = 30;
    public int WindowSeconds { get; set; } = 60;
    public RateLimitMode Mode { get; set; } = RateLimitMode.LogOnly;
}

public enum RateLimitMode
{
    LogOnly,
    Block
}
```

Bound in the composer via
`builder.Services.Configure<RedirectRateLimitOptions>(builder.Config.GetSection("RedirectManager:RateLimit"))`,
identical in shape to the existing `RedirectBackupOptions` binding.

### New service: `IRedirectRateLimiter`

```csharp
public interface IRedirectRateLimiter
{
    bool ShouldRateLimit(string clientIp, DateTime utcNow);
}
```

Implemented as a singleton, in-memory, per-IP **fixed-window counter**
using a `ConcurrentDictionary<string, WindowCounter>` with an
immutable-record window state, updated via `AddOrUpdate`'s pure
add/update-value functions — thread-safe with no explicit locking. A
window resets (starts a fresh count at 1) once `WindowSeconds` have
elapsed since it started; otherwise the count increments. Returns `true`
once the count for the current window exceeds `MaxRequestsPerWindow`.

`utcNow` is accepted as a parameter (not read internally via
`DateTime.UtcNow`) specifically so this service is deterministically
unit-testable without real sleeps or a injected clock abstraction —
production code passes `DateTime.UtcNow` at the call site; tests pass
controlled, fake timestamps to simulate window expiry. This follows the
same "pure, directly-testable logic" style already established for
`WildcardPatternBuilder`/`DomainNormalizer` in the unit-test sub-project.

Registered as a singleton in the composer, alongside the existing tracker
singletons (`IRedirectHitTracker`, `IMissedRequestTracker`,
`IVariantBHitTracker`).

### Middleware wiring (`RedirectMiddleware`)

A new private helper, called once at the top of each of the three
match-confirmed blocks (exact, wildcard, regex tiers) — i.e. right after
`if (redirect != null && redirect.IsActive)`, `if (wildcardRedirect !=
null)`, and `if (regexRedirect != null)`, and specifically **before** any
existing `_hitTracker.RecordHit(...)` call in that block, so a blocked
request never gets counted in the existing hit-count stats:

```csharp
private bool TryApplyRateLimit(HttpContext context)
{
    if (!_rateLimitOptions.Value.Enabled)
        return false;

    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!_rateLimiter.ShouldRateLimit(clientIp, DateTime.UtcNow))
        return false;

    if (_rateLimitOptions.Value.Mode == RateLimitMode.Block)
    {
        context.Response.StatusCode = 429;
        context.Response.Headers["Retry-After"] = _rateLimitOptions.Value.WindowSeconds.ToString();
        return true;
    }

    _logger.LogWarning(
        "Redirect rate limit exceeded for {ClientIp} (more than {MaxRequestsPerWindow} redirect requests in {WindowSeconds}s)",
        clientIp, _rateLimitOptions.Value.MaxRequestsPerWindow, _rateLimitOptions.Value.WindowSeconds);
    return false;
}
```

Each call site becomes `if (TryApplyRateLimit(context)) return;` right at
the top of the matched block, before the existing status-code switch —
when it returns `true` (blocked), the method returns immediately with the
`429` already written, exactly mirroring how every other terminal branch
in this middleware already returns without calling `_next`.

`RedirectMiddleware`'s constructor gains two new dependencies:
`IOptions<RedirectRateLimitOptions>` and `IRedirectRateLimiter` — both
resolved from DI like its existing tracker dependencies.

### Known, accepted limitation

The counter is per-application-instance, in-memory only — a load-balanced,
multi-instance deployment gets an independent limit *per instance*, not a
single global limit across the whole site. This is a deliberate,
documented trade-off: the roadmap's own suggested approaches were "a
simple IP-based counter in middleware" (what this implements) or pulling
in a full package like `AspNetCoreRateLimit` (a heavier dependency this
avoids). For a package-level protective feature — not a dedicated
infrastructure component — this trade-off is reasonable and consistent
with the roadmap's own framing ("Zorluk: Orta" / medium difficulty, not
"needs distributed state").

## Decisions confirmed with user (2026-07-13)

- Opt-in via appsettings (`RedirectManager:RateLimit`), disabled by
  default — not in conflict with the earlier-declined generic
  "appsettings config" item, since this codebase already has
  feature-specific `IOptions<T>` precedent (`RedirectBackupOptions`).
- Default `Mode` when enabled is `LogOnly` (never blocks on its own);
  `Block` mode is an explicit additional opt-in.
- Only counts requests that actually match an active redirect rule, never
  skip-listed paths or genuinely-missed (404 passthrough) requests.
- In-memory, per-instance counter — no distributed/shared-state rate
  limiting, and no new NuGet dependency.

## Out of scope

- Distributed/shared rate-limiting state across multiple app instances
  (e.g. via a shared cache like Redis).
- Any dashboard UI surfacing rate-limit activity (no "blocked IPs" view,
  no stats card) — this is purely a backend protective/logging feature.
- Rate limiting genuinely-missed (404 passthrough) requests — that's
  already covered by the existing `IMissedRequestTracker`/404-log feature.
- Rate limiting skip-listed paths (backoffice, assets, etc.) — the
  middleware already exits before reaching any match tier for those.
- Per-rule rate limiting (e.g. a separate counter per matched
  `RedirectEntry.Id`) — the roadmap specifies a per-IP counter across all
  redirect matches, not per-rule.
- Sliding-window or token-bucket algorithms — a fixed-window counter is
  the simplest correct implementation of the roadmap's ask and is what's
  implemented here.
- Version bump, git tag, and NuGet publish — happens once, after all 9
  sub-projects in this batch are done, as a separate step.
