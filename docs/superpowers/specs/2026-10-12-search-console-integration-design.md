# Search Console Integration (404 Intelligence) — Design

Status: DRAFT for owner review · 2026-10-12 · Phase 1 flagship candidate · Premium package only
Roadmap: `2026-10-10-monetization-roadmap-design.md` (Phase 1). Extension seams: `docs/EXTENDING.md`.

## 1. Goal

Show, for every logged 404, **how much search traffic it is costing**, and rank the 404 log by that, so a
user fixes the 10 URLs that matter instead of scrolling hundreds of bot hits. Combined with Smart Target
Suggestions this gives "fix these first, here is the likely target".

Non-goals: replacing Search Console; submitting sitemaps or reindex requests; writing anything to Google
(read-only); showing data for non-404 URLs; anything in the free package beyond the existing seams.

## 2. Guardrails (from the Free Forever Promise)

- Lives only in `BT.RedirectManager.Premium`. Core gets no new behavior. Umbraco 17+ only.
- **Fail-open:** if Google is unreachable, credentials expire, quota is exhausted or the license lapses, the
  404 log works exactly as in the free edition; GSC columns simply show "n/a". Redirect resolution never
  touches this module.
- **No data through the maintainer's servers.** The site talks to Google directly. Credentials are stored
  only in the customer's own database/App_Data. The telemetry endpoint never receives GSC data.
- Read-only scope only.

## 3. Verified facts about the API (Google docs, checked 2026-10-12)

- `searchanalytics.query`: `rowLimit` 1–25,000 (default 1,000), `startRow` paging, `dimensions` incl. `page`,
  `dimensionFilterGroups` on `page` (operators `equals`, `contains`, `includingRegex`…), `dataState`
  `final|all`. Dates inclusive, Pacific time. Results sorted by clicks descending. Returns clicks, impressions,
  ctr, position per row.
- **Only the top rows are returned** (the docs say not every row is guaranteed). Consequence: low-traffic
  404 URLs may be missing from a bulk page dump, so the design also does targeted per-URL lookups.
- Scopes: `webmasters.readonly` (read-only) or `webmasters`.
- Quotas: Search Analytics 1,200 QPM per site and per user; load quota also measured over 10-minute and
  1-day windows (cost grows with page/query grouping and wide date ranges). URL Inspection: 2,000/day and
  600 QPM per site.
- 🟡 Not confirmed from sources fetched: data retention window (commonly 16 months), whether
  `webmasters.readonly` counts as a sensitive scope for OAuth verification (search results did not name it),
  and that the old crawl-errors API no longer exists. **The design does not depend on any of these.** Confirm
  before committing the auth option in 4.2.

## 4. Architecture

### 4.1 Components (all inside the Premium package)

| Component | Role | Core seam |
|---|---|---|
| `SearchConsoleClient` | Typed HTTP client for `searchanalytics.query` (+ optional URL Inspection); retry with backoff, quota-aware | none |
| `SearchConsoleSyncService` | `BackgroundService`: daily sync of page performance into a Premium-owned table | none |
| `SearchConsoleStore` | NPoco table `rmPremiumGscPage` (+ migration, `#if NET10_0_OR_GREATER` only; Premium is 17+) | none |
| `GscMissedRequestEnricher` | Adds `clicks90d`, `impressions90d`, `lastSeenInGoogle`, `impactScore` to 404 rows | `IMissedRequestEnricher` (staging copies, 2 s timeout) |
| `GscExtensionProvider` | Declares a "Search Console" tab (setup + status) and the 404-row panel | `IDashboardExtensionProvider` |
| `GscSuggestionBoost` | Uses top queries of the lost page to rank suggestion candidates | `IRedirectSuggestionProvider` |
| `WeeklyDigestService` | Email of top lost-traffic 404s (reuses core mail settings) | `IMissedRequestObserver` not needed; timer-based |
| `PremiumEditionProvider` | Reports edition `Premium`, feature `search-console` | `IRedirectEditionProvider` |

Enrichment must never block the 404 list: the enricher reads only the local table, never calls Google inline.

### 4.2 Authentication — options and recommendation

Because the plugin is self-hosted, "whose OAuth client?" decides setup friction, privacy and Google review.

| Option | How | For | Against |
|---|---|---|---|
| **A. Service account (BYO)** | Customer creates a Google Cloud service account, downloads the JSON key, adds the service account email as a user on the Search Console property, pastes the key into the Settings tab | No Google app verification, no consent screen, no maintainer involvement, simplest server-side code | Most setup steps; non-technical editors may struggle |
| **B. BYO OAuth client** | Customer creates their own OAuth client; plugin runs the consent flow | Their app, their consent screen | As many steps as A, plus redirect-URI wiring |
| **C. Maintainer's OAuth app** | "Sign in with Google" button using one app owned by the maintainer | Best UX, one click | Needs Google OAuth verification (and a privacy policy, demo video, domain); until verified Google caps test users and shows an "unverified app" warning; client secret cannot be truly secret in distributed code; maintainer becomes an OAuth operator |

**Recommendation:** ship **A** in the alpha (target audience is developers and agencies, matches design-partner
profile, zero Google review), with a step-by-step in-dashboard wizard and a "Test connection" button. Evaluate
**C** after Gate B, only if interviews show setup friction is the blocker, and only after confirming the
verification requirements. Tokens/keys: encrypt with ASP.NET Core Data Protection before storing; never log.

### 4.3 Data model

`rmPremiumGscPage` (one row per normalized URL per sync window):
`Id, PropertyId, NormalizedPath, Clicks90d, Impressions90d, Ctr, AvgPosition, TopQueriesJson (top 5), LastClickDate, SyncedUtc`.
`rmPremiumGscSettings`: property identifier (`sc-domain:example.com` or URL-prefix), encrypted credential blob,
last sync status/error, lookback days (default 90).

### 4.4 Sync algorithm

1. Daily (and on "Sync now"): one bulk query, `dimensions=[page]`, last 90 days, `rowLimit=25000`, paginate with
   `startRow` until empty or a cap (default 100k rows).
2. **Targeted lookups:** for the top-N unresolved 404 paths by hit count not found in the bulk dump, query
   `page equals <url>` (and `query` dimension for the top 5 queries). Capped per run (default 200) to stay far
   below 1,200 QPM and the daily load quota.
3. Normalize both sides before joining: lowercase host, strip scheme and fragment, drop tracking params,
   trailing slash policy equal to the core matcher's, percent-decoding consistent. A domain property
   (`sc-domain:`) matches any host of that domain; multi-domain Umbraco sites map each configured domain.
4. Impact score = `clicks90d` weighted most, then `impressions90d`, then 404 hit count; ties broken by recency
   of last click. Pure function, unit-tested.
5. Honest labeling: a URL that already 404s for weeks shows decaying clicks. UI says "clicks in the 90 days
   before/around the 404" and shows `LastClickDate` so users do not misread it as current traffic.

### 4.5 UI (Lit, Umbraco 17+)

- **Search Console tab:** setup wizard (property, key paste, Test connection), sync status, last error,
  quota hint, "Sync now".
- **404 log (via row-panel + extra columns):** Clicks, Impressions, Impact; default sort option "Lost traffic".
  Rows without data show "–", not 0.
- **Row detail:** top queries that used to reach this URL + suggested targets (boosted by query overlap).
- Premium info section in free dashboard is a separate Phase 1 item (see roadmap addendum).

### 4.6 Weekly digest

Top 10 lost-traffic 404s, new 404 spikes, redirects created last week, one-click deep link into the dashboard.
Opt-in per site, uses the core email settings. Retains subscribers because it is useful even when nobody opens
the backoffice.

## 5. Privacy and security

- Data stored locally only. Customer's key can be revoked at Google at any time; plugin must handle 401/403
  by disabling sync and showing the error, not retrying forever.
- Update `docs/legal/PRIVACY.DRAFT.md`: Premium reads Search Console data inside the customer's site; the
  maintainer receives none of it.
- Credential blob encrypted at rest; excluded from CSV exports and from any support bundle.
- Least privilege: instruct customers to add the service account as **Restricted** user (read-only).

## 6. Testing

- Unit: URL normalization, impact score, quota/backoff, paging, per-URL fallback (fake `HttpMessageHandler`).
- Contract: recorded sample responses for `searchanalytics.query` including empty, partial and 403 cases.
- Extension conformance: reuse `samples/RedirectManager.SampleExtension` fault modes (throw, hang) against
  the GSC enricher; 404 list must stay identical when GSC is down.
- **Live verification** (per the owner's rule): needs a real Search Console property with data. Use the
  owner's own site or a design partner's, with a throw-away service account. Verify in a real Umbraco 17/18
  backoffice via `scripts/dev/cdp.mjs`: setup wizard, sync, columns, sort, failure states (revoked key,
  offline).
- A fake Google server mode (env flag) for demos and screenshots without real data.

## 7. Milestones

1. Package skeleton, edition provider, license verification stub (Ed25519, offline) — shared with other Premium features.
2. GSC client + store + sync + normalization, fully unit-tested.
3. Enricher + 404-log columns + sort; fail-open verified with fault modes.
4. Setup wizard + status tab; live verification against a real property.
5. Suggestion boost + weekly digest.
6. Alpha to design partners; collect feedback; decide on option C.

## 8. Risks and open questions

| Risk / question | Mitigation or decision needed |
|---|---|
| Setup friction (service account) kills adoption | Wizard, screenshots, video; measure in interviews; consider option C after Gate B |
| Top-rows-only API hides low-traffic 404s | Targeted per-URL lookups; honest "no data" state |
| 404 URLs lose clicks after Google drops them | Show last-click date and lookback; label clearly |
| Google API quotas on large sites | Capped lookups, daily schedule, backoff; surface quota errors in UI |
| Customers with domain vs URL-prefix properties | Support both; mapping covered by tests |
| Option C verification requirements unconfirmed | Confirm sensitivity of `webmasters.readonly` before choosing C |
| Is GSC data covered by the customer's own privacy obligations (agencies, clients' data)? | Note in docs; data stays in customer infrastructure |
| Owner decision: alpha auth = service account only? | Recommended yes |
| Owner decision: digest in alpha or after? | Recommended after the GSC columns ship |

## 9. Sources

- Search Analytics query reference: <https://developers.google.com/webmaster-tools/v1/searchanalytics/query>
- Search Console API limits: <https://developers.google.com/webmaster-tools/limits>
- Google sensitive scope verification: <https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification>
