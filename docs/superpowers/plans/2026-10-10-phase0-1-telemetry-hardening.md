# Phase 0.1 — Telemetry Hardening Implementation Plan

> **For agentic workers:** executed inline (single small file + one test project, sequential tasks); use superpowers:executing-plans or subagent-driven-development if re-run.

**Goal:** Close the telemetry privacy/takeover risk and restore measurement, without changing the plugin's public behavior or leaving users on old versions exposed.

**Architecture:** Fix the *backend* (`/Users/bhan/Documents/works/sites/RedirectManagerTelemetry`, own git repo) instead of forcing a plugin release. The key already baked into every shipped plugin version (and the public landing page) currently doubles as the **read** secret. Split it into a write-only **ingest key** (stays public by nature, accepted for `POST /api/ping` only) and a private **admin key** (never shipped, required for `GET /api/pings`). Then redeploy under the **same hostname** (`redirectmanager-telemetry.azurewebsites.net`) so old plugin versions ping a service we own again (removes the dangling-hostname takeover risk and restores data), and remove the secret/reader from the public landing page.

**Tech Stack:** ASP.NET Core minimal API (net10.0), SQLite, xUnit + `Microsoft.AspNetCore.Mvc.Testing`, Azure App Service (F1, Linux).

**Spec:** `docs/superpowers/specs/2026-10-10-monetization-roadmap-design.md` (Phase 0, sub-project 0.1)

---

## Task 1 — Backend: key split, validation, rate limit (TDD)

**Files (repo `RedirectManagerTelemetry`):**
- Create: `RedirectManagerTelemetry.Tests/RedirectManagerTelemetry.Tests.csproj`, `RedirectManagerTelemetry.Tests/TelemetryFactory.cs`, `RedirectManagerTelemetry.Tests/AuthAndValidationTests.cs`
- Modify: `Program.cs`, `RedirectManagerTelemetry.csproj` (exclude the test folder from the web project's globs)

Required behavior (each is a test, written first and seen failing):

| # | Request | Expected |
|---|---|---|
| 1 | `POST /api/ping` with ingest key | 200 |
| 2 | `POST /api/ping` with no key | 401 |
| 3 | `POST /api/ping` with wrong key | 401 |
| 4 | `GET /api/pings` with **ingest** key | 401 (leaked key cannot read) |
| 5 | `GET /api/pings` with admin key | 200 + the pinged row |
| 6 | `GET /api/pings` when `AdminKey` unconfigured (Production) | 503 (fail closed) |
| 7 | `POST /api/ping` when no keys configured (Production) | 503 |
| 8 | `POST` with non-GUID `siteId` | 400 |
| 9 | `POST` with `domain` > 253 chars | 400 |
| 10 | `GET /health` with no key | 200 |
| 11 | Legacy `Telemetry:ApiKey` (old name) is honored as the ingest key | 200 on POST, 401 on GET `/api/pings` |
| 12 | Development environment with no keys | open (local Docker convenience) |

Implementation notes: constant-time key comparison (hash both with SHA-256, `CryptographicOperations.FixedTimeEquals`); remove `AddCors`; `UseForwardedHeaders` + fixed-window rate limiter partitioned by client IP (60/min); `Kestrel.Limits.MaxRequestBodySize = 8 KB`; expose `public partial class Program` for the test factory; tests set config with `UseSetting` and a unique temp SQLite file per test.

Commands: `dotnet test RedirectManagerTelemetry.Tests` — red, then green.

## Task 2 — Docs + deploy runbook (repo `RedirectManagerTelemetry`)

Update `README.md`: key model (ingest vs admin), required app settings (`Telemetry__IngestKey` = the key already shipped in plugins ≤1.10.2, `Telemetry__AdminKey` = new random secret, `Telemetry__DbPath`), and a post-deploy smoke check. Commit both tasks to that repo.

## Task 3 — Deploy under the same hostname (BLOCKED on user: `az login`; needs explicit confirmation)

1. User runs `az login --tenant f6aaadc9-d425-44bb-b632-ec72950ebfa4` (refresh token expired after 90 days of inactivity).
2. Read-only inspection: does `redirectmanager-telemetry-rg` / the app still exist?
3. **With explicit user confirmation** (creates/changes cloud resources): create or restore the F1 app with the same name, set app settings, deploy the new build.
4. Smoke checks: `/health` 200; ping with ingest key 200; read with ingest key 401; read with admin key 200.
5. Store the admin key outside the repo (local file mode 600 and the user's password manager).

## Task 4 — Plugin repo cleanup (repo `RedirectManager`)

- `landing/script.js` + `landing/index.html`: remove the "who's running it" reader section and the embedded key (the public page must never carry a read secret).
- `Services/RedirectTelemetryPinger.cs`: comment that the baked key is **ingest-only by design**, not a secret (no behavior change; ships with the next release).
- No version bump / publish for this task alone.

## Acceptance

- Reading pings with the key found in the public repo/NuGet package returns 401.
- The old hostname resolves to a service we control.
- Backend tests (≥12 cases) pass; plugin tests unaffected.
