# RedirectManager 2.0: Design

Status: DRAFT for owner review · 2026-10-12 · decisions marked **[confirmed]** were made by the owner on 2026-10-12.
Fits: roadmap `2026-10-10-monetization-roadmap-design.md` (Phase 1 to 2 bridge), Free Forever Promise (`docs/FREE-FOREVER.md`).

## 1. Decisions

- **[confirmed] Scope:** 2.0 is **Umbraco 17+ only** (net10.0), with **one modern dashboard** (the Lit/Web Component
  one). Umbraco 13, net8.0 and the AngularJS dashboard are not part of 2.0.
- **[confirmed] Timing:** release together with **Umbraco 19** (planned 2026-12-10), so 2.0 is advertised as
  "Umbraco 17, 18 and 19". Working window: 2026-10-12 to about 2026-12-07 (8 weeks), release the same week as Umbraco 19.
- **Proposed:** a `release/1.x` branch keeps Umbraco 13 users on security and critical fixes (1.x) until Umbraco 13's
  end of life and best effort afterwards, exactly as `FREE-FOREVER.md` item 4 promises. 1.x never loses a feature.

## 2. Why this shape

| Today (1.x) | Cost | 2.0 |
|---|---|---|
| Two build targets (net8.0 + net10.0), two Umbraco package sets | Every change compiled and reasoned about twice | net10.0 only |
| Two dashboards: AngularJS (~1,900 lines: `dashboard.html`, `modal.html`, `redirect.controller.js`, `redirect.css`, `redirect.resource.js`, `package.manifest`) and Lit (`redirect-dashboard.js`, ~2,750 lines) | Features land in Lit first; the AngularJS one lacks the "Redirect exists" badge, search box, categories parity, extension tabs | one dashboard |
| `#if NET10_0_OR_GREATER` in the migration plan (duplicated, ~580 lines) and in 3 services | Migration drift risk | single code path, migration file roughly halves |
| Modal, filters and tab chrome grew organically | Inconsistent, dated look (raised by the owner) | redesigned |

Dropping Umbraco 13 is allowed by the Promise (it names the support window and 13's December 2026 end of life) and
does not reduce what anyone already has: 1.x stays available and keeps working.

## 3. Scope of 2.0

### In
1. **Single target and cleanup:** remove net8.0, the Umbraco 13 package references, all `#if` branches, the AngularJS
   assets and `package.manifest`; keep `umbraco-package.json` (Lit). Collapse the migration plan to one set (same
   migration GUIDs, so upgrading databases from 1.x runs nothing new and nothing is lost).
2. **Umbraco 18 and 19 compatibility:** compile against 17.x (LTS baseline), run the full live matrix on 17, 18 and
   19 (release candidate before GA, final on release day).
3. **Dashboard redesign (the owner's request):** Add/Edit redirect modal, filters bar, tab chrome, tables, empty
   states, consistent spacing/typography, keyboard and screen-reader basics (focus trap, labels, contrast).
   Behavior stays identical; this is a visual and structural pass, verified in a real backoffice.
4. **Extension API v2 (stable):** the five extension points keep their shape. 2.0 is the one window where breaking
   changes are allowed (see `EXTENDING.md` stability policy), so: remove nothing needlessly, make the optional
   record parameters of `DashboardExtension` the documented contract, document `api.*` of the dashboard host, and
   freeze the surface for all of 2.x. A short migration note lists any change.
5. **Dependencies and warnings:** move to the patched Umbraco 17.x/18.x packages (MailKit and MessagePack advisories
   seen on 17.1.0), clear the nullable warning in `BT.RedirectManager.Premium` `GscStore`.
6. **Telemetry and update banner:** keep as is (opt-in). The update banner explains 1.x vs 2.x for Umbraco 13 sites.
7. **Docs:** README (supported versions table), `docs/UPGRADING-2.0.md`, release notes, Marketplace listing, NuGet
   description. Screenshots retaken (the README ones are stale after the redesign).

### Out (explicitly)
- No new Premium features in 2.0 (they live in the separate package and ship on their own schedule).
- No database schema changes; no change to redirect matching semantics, status codes or API routes.
- No rewrite of the redirect engine, no new redirect types.
- No Umbraco 13 or net8.0 in 2.0 (stay on 1.x).

## 4. Versioning, branches and releases

- `main` becomes 2.0 development as soon as the 1.x line is cut. **Before the cut:** tag the last 1.x release from
  `main`, create `release/1.x` from it, and keep publishing 1.x patches from that branch (tag `v1.x.y` as today).
- **Announce one minor ahead** (the stability policy): a 1.14 release with release notes plus a short note in the
  update banner ("2.0 is coming for Umbraco 17+; Umbraco 13 stays on 1.x"). 1.14 contains no behavior change.
- Pre-releases: `2.0.0-rc.1` once the feature set is complete and live-verified on 17 and 18; `2.0.0-rc.2` against the
  Umbraco 19 RC; `2.0.0` on the Umbraco 19 release week. Pre-releases are published to nuget.org as prerelease so
  design partners and the Premium alpha can test.
- NuGet: the package ID stays `BT.RedirectManager`. The 2.0 dependency on `Umbraco.Cms` is `[17.0.0, 20.0.0)`; consumers
  on Umbraco 13 keep resolving 1.x (their TFM and Umbraco version already exclude 2.x).
- Premium `1.0` (when it leaves alpha) depends on core `>= 2.0`; alphas on 1.x core keep working until then.

## 5. Compatibility and upgrade promise

- Data: **no migration needed** from 1.x. 2.0 runs the same migration plan GUIDs; an existing 1.x database is already
  "up to date".
- Config keys, API routes, CSV formats and the redirect behavior are unchanged.
- URL rewrite semantics and ordering are covered by existing tests; add characterization tests before touching
  anything (the engine is out of scope, tests prove it stayed that way).
- Anyone on Umbraco 17+ upgrades by bumping the package; on Umbraco 13 they stay on 1.x with no loss.

## 6. Work breakdown and schedule (8 weeks)

| Week | Work | Gate |
|---|---|---|
| 1 | Cut `release/1.x`; publish 1.14 (announcement + any pending fixes); inventory tests; add characterization tests for matching, CSV, categories, bulk ops | tests green on both branches |
| 2 | Remove net8.0/Umbraco 13/AngularJS/`#if`; collapse migrations; update CI (single TFM), packing and `.targets` | clean build, 140+ tests green, live smoke on U17 |
| 3 | Design pass: tokens (spacing, type, color), tab chrome, filters bar, tables | design review with screenshots |
| 4 | Redesigned Add/Edit modal and other dialogs (A/B test settings, validity window, regex), accessibility pass | live verification on U17 and U18 |
| 5 | Extension API v2: freeze, docs, conformance run with the sample (throw/hang faults); Premium alpha rebuilt against 2.0 pre-release | `2.0.0-rc.1` |
| 6 | Feedback from design partners; polish; docs, upgrade guide, screenshots | rc feedback triaged |
| 7 | Umbraco 19 RC compatibility; full matrix U17/U18/U19; performance sanity (large 404 log, 5,000 redirects) | `2.0.0-rc.2` |
| 8 | Release week: final matrix on Umbraco 19 GA, publish 2.0.0, Marketplace sync, announcement | `2.0.0` |

Buffer: Umbraco 19 may slip or change backoffice APIs; weeks 7 to 8 absorb that. If Umbraco 19 is late, 2.0 ships
as "17 and 18" and 2.1 adds 19 (no re-planning needed).

## 7. Verification (non-negotiable, per the owner's rule)

Every user-visible change is verified in a **real running Umbraco** with `scripts/dev/cdp.mjs` (real Chrome, shadow-DOM
clicks, screenshots), on 17, 18 and (from the RC) 19, not only in unit tests. Matrix per RC: fresh install, upgrade
from a 1.x database, empty and large datasets, light/dark, narrow viewport, Premium installed and not installed.

## 8. Risks and open questions

| Risk or question | Mitigation or decision needed |
|---|---|
| Unknown share of Umbraco 13 users (telemetry unmeasured) | 1.x stays maintained; announce in 1.14; NuGet downloads per version tracked weekly |
| Redesign regressions in rarely used dialogs | Characterization tests + full manual matrix; keep behavior unchanged |
| Umbraco 19 backoffice API changes late | RC testing from week 7; ship "17 and 18" if needed |
| Extension API frozen too early | Premium alpha + sample are the real consumers; freeze only after week 5 conformance run |
| Marketplace listing text must change (supported versions) | Update `umbraco-marketplace.json` and README in week 8 |
| **Owner decisions pending:** (a) exact 1.x support commitment for Umbraco 13 (suggest: security/critical fixes until 2027-06, then best effort); (b) whether the redesign gets a design reference first (Figma mock or "match Umbraco backoffice") or is built directly in code | Answer before week 2 and week 3 respectively |

## 9. Next step

On approval: write the week-by-week implementation plan (`writing-plans`), starting with the 1.14 announcement release
and the characterization tests, then work in a worktree with local merges as usual.
