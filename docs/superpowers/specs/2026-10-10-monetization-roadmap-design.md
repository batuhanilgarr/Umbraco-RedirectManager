# Monetization Roadmap (Standard / Premium / Company) — Design

## Context

BT.RedirectManager is an MIT-licensed Umbraco 13/17/18 redirect-management
package (NuGet `BT.RedirectManager`, v1.10.2 as of 2026-10-10). The maintainer
wants to grow the user base and build recurring revenue (MRR) through
editions — **Standard, Premium, Company** — without harming current users, who
rely on the existing feature set for free.

Facts established during research (2026-10-10):

- 🟢 All published versions (≤1.10.2) are MIT-licensed. They cannot be
  re-licensed retroactively, and the project's goodwill depends on not trying.
- 🟢 NuGet lifetime downloads: 1,995; roughly 100–225 per recent version.
  Active-site count is **unknown** (see Telemetry below). Estimated active sites:
  100–400 (🔴 assumed).
- 🟢 Free competitors exist: Skybrud.Umbraco.Redirects (MIT) and Umbraco's
  built-in redirect URL tracker. Basic redirect management cannot be sold;
  paid value must be workflow value (triage, migration, monitoring, headless).
- 🟡 The Umbraco Marketplace is a showcase, not a payment/licensing platform.
  Billing, license keys and delivery must be built or bought by the vendor.
- 🟢 Umbraco 13 support ends December 2026; Umbraco 19 (STS) ships
  2026-12-10; Umbraco 21 (LTS) ships 2027-12-09. The v13 end-of-life creates a
  site-migration wave in which URLs change — directly relevant to a redirect tool.
- 🟢 The package ships two parallel dashboards (AngularJS for Umbraco 13, Lit for
  17/18). Every feature is built twice today.
- 🟢 **Telemetry is dead and risky.** The ping endpoint
  `redirectmanager-telemetry.azurewebsites.net` returns NXDOMAIN (app deleted),
  so the maintainer has no usage data. Anyone can register that Azure hostname
  and receive pings (site domain + versions) from opted-in sites; the ingest API
  key is also committed in the public repo
  (`Services/RedirectTelemetryPinger.cs`).

## Goals

1. Build a paid tier structure (Standard / Premium / Company) that produces
   recurring revenue.
2. Zero negative impact on existing users: nothing they use today gets
   restricted, degraded, or nagged.
3. Establish measurement (install base, conversion) before scaling spend.
4. Target a realistic MRR, with explicit decision gates.

## Non-goals (this roadmap)

- Re-licensing or removing any shipped feature.
- Charging for basic redirect management (301/302/404/410, regex, wildcard,
  scoping, CSV, scheduling, A/B, analytics, 404 triage categories).
- Building a payment platform; a merchant-of-record or payment provider is used.
- A non-Umbraco product (kept as a Phase 4 option only).

## Strategy: open-core with a separate commercial package

Chosen over (B) single package + license-key feature flags (trivially bypassed
in MIT code, and a "feature disappeared" perception risk) and (C) hosted-only
SaaS (detaches from in-CMS installs; kept as a Phase 4 option).

- `BT.RedirectManager` — core, MIT, free forever (**Standard** edition).
- `BT.RedirectManager.Premium` — commercial EULA, depends on the core, adds
  **Premium** and **Company** capabilities, unlocked by a signed license key.
- The core gains extension points (suggestion providers, missed-request
  enrichers, dashboard slots, an edition abstraction) so Premium plugs in without
  forking core code. Core remains installable and unchanged on its own.

## Guardrails that protect current users

1. **Free Forever Promise** — every feature in ≤1.10.x stays MIT and free;
   compatibility (Umbraco 17/18/19/21) and security fixes continue. Published in
   the README and docs.
2. **No clawback** — a shipped free feature is never moved behind a paywall.
   Premium capabilities are net-new or an additive layer on top of free ones
   (e.g. free 404 triage stays; Premium adds auto-suggested targets).
3. **Fail-open licensing** — runtime redirect resolution has no license
   dependency. An invalid/expired license only disables Premium features; it never
   returns an error or blocks redirects. Validation is offline (signed key, no
   call-home required). 14-day grace after expiry; premium data becomes read-only
   after that, never deleted.
4. **Zero-touch upgrades** — free users see at most one dismissible "Premium"
   card/tab. No modals, no disabled controls.
5. **Telemetry stays opt-in**, moves to an endpoint on a domain the maintainer
   controls, and carries no secrets in the public repo.
6. **Premium targets Umbraco 17+ (Lit dashboard only)** — removes AngularJS
   dual-maintenance cost. Umbraco 13 users stay on the free edition, which enters
   maintenance mode after Umbraco 13 end of life.
7. **Licenses are asymmetric-signed** (private key never in the repo); a
   determined user can bypass any license — price and value, not DRM, are the
   protection.

## Editions

| | **Standard** | **Premium** | **Company** |
|---|---|---|---|
| Price (🟡 hypothesis) | Free forever | ~$25/mo or $250/yr per production domain (dev/staging free) | ~$99/mo or $990/yr, up to ~25 production domains of one organization |
| License | MIT, no key | Commercial EULA, signed key | Commercial EULA, signed key |
| Includes | Everything in ≤1.10.x | Smart Target Suggestions, 404 Intelligence, Migration Assistant, Health Scanner, Scanner Ruleset Feed | Premium + governance (approval workflow, change history/rollback, role-based permissions, audit export), headless/edge export, environment sync, API/webhooks, priority support |
| Umbraco | 13, 17, 18+ | 17+ | 17+ |

Optional "Migration Pass" (one-off, ~$149, 30 days) to capture the v13→17 wave
without a subscription; converts to Premium. Pricing is a hypothesis to be
validated with design partners (founder-led interviews, n≈10–15), not a survey.

### Premium feature candidates (all net-new)

| # | Feature | Phase | Why it earns recurring revenue |
|---|---|---|---|
| 1 | **Smart Target Suggestions** — for each 404, propose the best live target (fuzzy match vs published URLs/names, URL history), one-click accept, bulk accept of high-confidence matches | 1 | Attacks the real pain customers reported (thousands of 404 rows); builds on shipped triage |
| 2 | **404 Intelligence** — referrer capture (which page links to the broken URL; host+path only, no IPs), priority score (hits × recency × referrer count), spike alerts, weekly digest | 1 | Continuous SEO value |
| 3 | **Migration Assistant** — import old sitemap/crawl/CSV, auto-map to new content, review table, bulk create, dry-run, chain/loop validation | 2 | High one-off value, timed to v13 EOL |
| 4 | **Health Scanner** — scheduled scan for chains, loops, dead targets, expired rules; email/Slack/Teams alerts | 2 | Ongoing monitoring |
| 5 | **Scanner Ruleset Feed** — signed, regularly updated scanner/asset patterns for auto-tagging, optional auto-ignore | 3 | Updates justify a subscription |
| 6 | **Headless/edge export** — Netlify/Cloudflare `_redirects`, `vercel.json`, nginx, IIS/Azure Front Door; Delivery-API resolver for Next/Nuxt middleware | 3 | Differentiator vs free competitors |
| 7 | **Environment sync** — promote redirects staging→prod with diff/preview | 3 | Agency workflow |
| 8 | **Governance** (Company) — maker/checker approval, history + rollback, RBAC per domain/section, audit export | 3 | Org requirement |
| 9 | **API + webhooks, white-label, priority support** (Company) | 3–4 | Agency/enterprise |

## Roadmap

Today is 2026-10-10. Dates are targets, gated as described.

### Phase 0 — Foundations (2026-10-10 → 2026-11-15)

Sub-projects (each gets its own plan):

- **0.1 Telemetry security + measurement.** Stop pinging the dead azurewebsites
  host (patch release), remove the committed API key from the client, stand up a
  new endpoint on a domain the maintainer controls (small Azure Function + Table
  Storage), keep opt-in. Optional free "register for security/compatibility
  notices" email capture. Weekly script recording NuGet/GitHub/Marketplace
  numbers. Outcome: first real active-site count within ~3 weeks of release.
- **0.2 Core extension points (v1.11.0).** Interfaces for suggestion
  providers, missed-request enrichers, dashboard slots, and an edition service;
  no user-visible behavior change. Includes the Umbraco 19 compatibility check
  planned for December.
- **0.3 Trust + legal.** Free Forever Promise in README/docs; EULA, ToS and
  privacy policy drafts; seller structure decision (freelancer) including
  international subscription collection, tax/VAT, and merchant-of-record options
  (Paddle / Lemon Squeezy — Turkey payout support **not yet verified**).
- **0.4 Customer discovery.** 10–15 conversations with existing users; the
  customer who requested 404 categorization (and reported the bulk-apply bug) is
  the first design partner.

**Gate A (end of Phase 0):** measured active sites. If < 50, insert a growth
sprint (distribution plan below) before building Premium features at scale.

### Phase 1 — Premium alpha (2026-11-16 → 2026-12-31)

Smart Target Suggestions + 404 Intelligence; license-key infrastructure v1
(Ed25519-signed, offline verification, domain-bound; issuance by a small
function, manual invoicing acceptable at first); free alpha for 5–10 design
partners in exchange for feedback and testimonials; Umbraco 19 compatibility
release.

**Gate B:** ≥ 3 design partners state they would pay at the proposed price.

### Phase 2 — Public launch (Jan → Mar 2027)

Migration Assistant + Health Scanner GA; pricing page (reuse `landing/`);
"founding customer" 30% first-year discount; Marketplace listing links to the
commercial edition; v13→17 redirect-migration guide as launch content.

**Gate C:** ≥ 5 paying customers and monthly churn < 5%.

### Phase 3 — Company tier and expansion (Apr → Aug 2027)

Company edition (multi-site license + governance), headless/edge export,
Scanner Ruleset Feed, environment sync, agency partner program, annual plans.
Target 3–5 agencies on Company.

### Phase 4 — Scale options (Sep 2027 →)

API/webhooks, white-label, hosted "404 Intelligence Cloud" (cross-site rollup,
recurring SaaS), Umbraco 21 LTS readiness (2027-12-09), evaluate non-Umbraco
expansion only if the installed base requires it.

## Realistic MRR (🟡/🔴 — active-site count unknown)

| Scenario | Active sites | Paying | 12-month MRR |
|---|---|---|---|
| Conservative | ~150 | 5 Premium + 1 Company | ~$180 |
| **Base** | ~300 | 12 Premium + 3 Company | **~$500** |
| Optimistic | ~600 | 30 Premium + 6 Company | ~$1,100 |

(Premium counted at the annual-plan monthly equivalent ≈ $21/mo; Company ≈ $82/mo.)

**Targets:** 12 months — $500 base, $1,000 stretch. 24 months — $2,000–3,000,
only if the installed base grows ~5× and the agency channel works. $2–3k MRR
within 12 months is **not** realistic from the current base.

## Distribution plan (needed alongside features)

Umbraco community channels (Discord, Our Umbraco, Reddit); a v13→17
redirect-migration guide; agency outreach (Turkey and EU); Codegarden 2027
submission; polished README/NuGet/Marketplace listing with the new screenshots;
demo videos.

## Risks

- **Free alternatives** (Skybrud, built-in tracker): compete on workflow value,
  never on basic redirect features.
- **Solo maintainer support load:** define a support policy per edition; Company
  gets the SLA, Premium gets best-effort email.
- **Per-major-version maintenance** (Umbraco 19 in Dec 2026, 21 in Dec 2027):
  revenue must fund it; Premium stays on the Lit/17+ track only.
- **Payments/tax as a freelancer selling internationally:** unresolved; blocks
  public launch (Phase 2), not alpha.
- **Privacy:** referrer capture stores host+path only, never IPs; configurable.
- **License server availability:** mitigated by offline-first validation and
  fail-open behavior.

## Decisions confirmed with user (2026-10-10)

- Standard edition = everything in ≤1.10.x, free forever, no license key.
- Seller: the maintainer as an individual (freelancer).
- MRR target: assistant to recommend a realistic one (done above).
- Telemetry data: none exists (endpoint is dead); measurement is a Phase 0 task.
- Phase 0 starts immediately after spec approval.

## Open questions

- Payment/merchant-of-record provider and its Turkey eligibility.
- Final prices and the Company site cap (validate with design partners).
- Whether the optional free "register for notices" capture is acceptable to
  the community (keep strictly optional).
- Hosting for the new telemetry/license endpoints (domain + Azure subscription).
