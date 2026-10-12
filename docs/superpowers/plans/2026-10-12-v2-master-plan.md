# RedirectManager 2.0: Master Plan

> **For agentic workers:** This is the index. Each sub-plan below is a separate, self-contained plan that produces
> working, testable software. Execute them in order with `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans`. Spec: `docs/superpowers/specs/2026-10-12-redirectmanager-2-0-design.md`.

**Goal:** Ship RedirectManager 2.0 (Umbraco 17+ only, one modern dashboard, redesigned UI, stable extension API) in
the week Umbraco 19 is released (planned 2026-12-10), without breaking anyone on 1.x.

**Architecture:** Cut a `release/1.x` branch first so Umbraco 13 users keep a maintained line; turn `main` into the 2.0
line by deleting the net8.0 target and the AngularJS dashboard, then redesign the Lit dashboard and freeze the
extension API. A **golden behavior suite** (HTTP-level, recorded on 1.x) proves the redirect engine did not change.

**Tech Stack:** .NET 10, Umbraco 17.x baseline (tested on 17/18/19), Lit web components, xUnit, Node 22+ (CDP helper),
GitHub Actions.

## Assumptions made when the owner approved the spec (2026-10-12)

The two open decisions in the spec were not answered separately; this plan adopts the spec's own recommendations.
Say so if you want either changed:

1. **Umbraco 13 support on `release/1.x`:** security and critical fixes until 2027-06-30, best effort afterwards.
2. **Design reference:** no external mock. The redesign matches the Umbraco 17+ backoffice look (Umbraco UI Library tokens
   and patterns) and is reviewed through screenshots at two checkpoints (end of weeks 3 and 4).

## Sub-plans

| # | Plan file | Weeks | Gate to start the next |
|---|---|---|---|
| A | `2026-10-12-v2-plan-a-announce-and-golden-baseline.md` | 1 | 1.14.0 published, `release/1.x` exists, golden baseline recorded and passing on 1.x |
| B | `2026-10-12-v2-plan-b-single-target-cleanup.md` | 2 | net10-only build green, 140+ tests green, golden suite passes unchanged, live smoke on Umbraco 17 |
| C | Design pass and redesigned dialogs (weeks 3 and 4) | 3-4 | Screenshot review approved by the owner; golden suite still green |
| D | Extension API v2 freeze, Premium rebuilt against 2.0 pre-release, `2.0.0-rc.1` (weeks 5 and 6) | 5-6 | Conformance run with the sample (throw/hang) clean; Premium alpha works on `2.0.0-rc.1` |
| E | Umbraco 19 compatibility, full matrix, release (weeks 7 and 8) | 7-8 | Final matrix green on 17, 18 and 19; owner confirms the tag push |

**Plans C, D and E are written when their predecessor finishes**, not now. They depend on things that do not exist
yet (the cleaned-up code base, the screenshots the owner reviews, design-partner feedback, the Umbraco 19 release
candidate). Writing them today would mean guessing; each will be produced with `superpowers:writing-plans` at the
start of its week, using the spec sections quoted here.

### What C, D and E must deliver (from the spec, so nothing is dropped)

- **C:** consistent design tokens (spacing, type, colour, dark mode via Umbraco's CSS variables); redesigned tab chrome,
  filter bar, tables and empty states; redesigned Add/Edit modal and the other dialogs (A/B test, validity window,
  regex); keyboard and screen-reader basics (focus trap, labels, contrast); behavior unchanged; retaken screenshots.
- **D:** `DashboardExtension` optional parameters documented as the contract; host `api.*` documented
  (`authFetch`, `showMessage`, `createRedirectFromMissed(item, {newUrl}?)`, `reloadMissedRequests`, `reloadRedirects`,
  `reloadCapabilities`); surface frozen for 2.x with a short migration note; patched Umbraco 17.x/18.x package
  references (MailKit and MessagePack advisories); Premium (`BT.RedirectManager.Premium`) rebuilt against the
  pre-release; `docs/UPGRADING-2.0.md`; README support table; `2.0.0-rc.1` on nuget.org as a prerelease.
- **E:** Umbraco 19 RC compatibility fixes; matrix per release candidate (fresh install, upgrade from a 1.x database,
  empty and large datasets, light and dark, narrow viewport, Premium installed and not installed); `2.0.0-rc.2`;
  final matrix on Umbraco 19 GA; release notes, Marketplace listing (`umbraco-marketplace.json`) and NuGet
  description updated; `2.0.0` tag after the owner's explicit OK. If Umbraco 19 slips, ship "17 and 18" as 2.0 and add
  19 in 2.1.

## Working rules for every sub-plan

- Work in an isolated worktree per sub-plan (`superpowers:using-git-worktrees`) and merge locally (owner preference).
- Every user-visible change is verified in a **real running Umbraco** with `scripts/dev/cdp.mjs` and screenshots, not
  only unit tests. The test site is `/Users/bhan/Documents/works/sites/RedirectManagerTestSite/RmTestSite` (Umbraco 18
  RC) with its `App_Plugins/RedirectManager` symlinked to this repo.
- Commit messages end with the `Co-Authored-By` line given in the session; never push tags or publish without the
  owner's explicit confirmation in that moment.
- After each sub-plan: update the roadmap memory file and the spec's status line.

## Definition of done for 2.0

- `BT.RedirectManager 2.0.0` on nuget.org, Marketplace synced, `release/1.x` maintained, README and
  `docs/UPGRADING-2.0.md` correct, golden suite green on Umbraco 17, 18 and 19, no known regressions in the matrix.
